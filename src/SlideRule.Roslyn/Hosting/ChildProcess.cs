using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SlideRule.Roslyn.Hosting;

/// <summary>
///     The one child-process launcher in this repository. It starts a process with all three
///     standard streams redirected, hands it a stdin it closes immediately, captures stdout and stderr,
///     and bounds the whole thing: on expiry it kills the process tree rather than waiting on.
/// </summary>
/// <remarks>
///     <para>
///         Stdin is always redirected and then closed. A child started with <c>UseShellExecute=false</c> and no
///         stdin redirection inherits a duplicate of the parent's stdin handle. Inside the MCP server that handle
///         is the client's live JSON-RPC pipe, and the stdio transport is permanently parked on it in a
///         synchronous read; Git for Windows probes its standard handles at startup, and that probe blocks forever
///         against a pipe with outstanding synchronous I/O. The child then never reaches its own entry point, the
///         caller waits out the whole timeout, and the tool call fails every time, invisibly to any test that runs
///         the same code in-process. A stdin of its own also makes a child that asks "is stdin a terminal?" answer
///         the same way under a test runner as under a real client.
///     </para>
///     <para>
///         The bound does not bound the reads. On Windows <see cref="Process.StandardOutput" /> wraps a
///         synchronous <c>FileStream</c>, so a <see cref="CancellationToken" /> handed to <c>ReadToEndAsync</c>
///         cannot interrupt a read already in flight; the token bounds the wait for exit and nothing else. What
///         keeps a caller from hanging is the closed stdin, so the child cannot wedge at startup; the bounded wait
///         for exit; and the kill-tree on expiry, which closes the child's write ends so the blocked reads
///         complete with EOF.
///     </para>
/// </remarks>
internal static class ChildProcess
{
    /// <summary>The ceiling for any single child, generous so it only trips on a wedge.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    // How long the expiry path waits for the two reads to finish once the tree is dead. A grace period,
    // not a second unbounded wait: a kill that somehow failed must not turn a bounded call into an
    // indefinite one.
    private static readonly TimeSpan KilledReadGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Starts <paramref name="startInfo" />, overwriting its redirection flags, writes
    ///     <paramref name="standardInput" /> if any and closes stdin, drains both output streams, and returns the
    ///     result. Throws <see cref="TimeoutException" />, after killing the process tree, when the child outlives
    ///     <paramref name="timeout" />, and <see cref="OperationCanceledException" /> when <paramref name="ct" /> is
    ///     cancelled first, so a cancelled call aborts its child rather than leaving it to run out the clock.
    /// </summary>
    internal static async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan? timeout = null,
        string? standardInput = null,
        CancellationToken ct = default)
    {
        TimeSpan bound = timeout ?? DefaultTimeout;

        using Process process = Start(startInfo);
        await WriteAndCloseInputAsync(process, standardInput).ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(bound);

        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string standardOutput = await output.ConfigureAwait(false);
            string standardError = await error.ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, standardOutput, standardError);
        }
        catch (OperationCanceledException)
        {
            TryKillTree(process);
            await ObserveKilledReadsAsync(output, error).ConfigureAwait(false);

            // A caller cancel is a cancel, not a timeout: only the ceiling produces a TimeoutException.
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(TimeoutMessage(startInfo, bound));
        }
    }

    /// <summary>
    ///     The synchronous arm, for a caller that cannot await. It drains through the event-based readers and
    ///     blocks on the process handle, never on a <see cref="Task" />, so its capture is line-oriented and
    ///     LF-normalized, one trailing newline per line read, where <see cref="RunAsync" /> captures whole. Same
    ///     failure contract as <see cref="RunAsync" /> minus cancellation.
    /// </summary>
    internal static ProcessResult Run(
        ProcessStartInfo startInfo, TimeSpan? timeout = null, string? standardInput = null)
    {
        TimeSpan bound = timeout ?? DefaultTimeout;

        using Process process = Start(startInfo);

        StringBuilder output = new();
        StringBuilder error = new();
        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(error, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!string.IsNullOrEmpty(standardInput)) process.StandardInput.Write(standardInput);
        process.StandardInput.Close();

        if (!process.WaitForExit((int)bound.TotalMilliseconds))
        {
            TryKillTree(process);
            throw new TimeoutException(TimeoutMessage(startInfo, bound));
        }

        // The documented quirk, and the reason the bounded overload alone is not enough: only the
        // parameterless WaitForExit also waits for the event-based readers to reach EOF, so this is what
        // makes the two buffers complete before they are read.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        // A missing or non-executable file surfaces as Win32Exception from Process.Start; callers that
        // can say something useful about it (git off PATH) catch it themselves.
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");
    }

    private static async Task WriteAndCloseInputAsync(Process process, string? standardInput)
    {
        if (!string.IsNullOrEmpty(standardInput)) await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
        process.StandardInput.Close();
    }

    // The tree, not the process: git on Windows is a wrapper that execs the real binary as a grandchild,
    // and killing only the wrapper would leave the grandchild holding the pipes open.
    private static void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // Already gone or inaccessible — nothing left to clean up.
        }
    }

    // Awaiting the killed reads keeps neither faulted nor cancelled against the Process this call is about to
    // dispose; the outcome itself is moot, because the caller is already getting a timeout.
    private static async Task ObserveKilledReadsAsync(Task<string> output, Task<string> error)
    {
        Task observed = Task
            .WhenAll(output, error)
            .ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

        await Task.WhenAny(observed, Task.Delay(KilledReadGrace, CancellationToken.None)).ConfigureAwait(false);
    }

    // '\n' rather than Environment.NewLine, so the sync arm's capture reads the same on every OS.
    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (line is not null) builder.Append(line).Append('\n');
    }

    private static string TimeoutMessage(ProcessStartInfo startInfo, TimeSpan bound)
    {
        string arguments = startInfo.ArgumentList.Count > 0
            ? string.Join(" ", startInfo.ArgumentList)
            : startInfo.Arguments;
        string command = $"{startInfo.FileName} {arguments}".Trim();

        return $"'{command}' did not complete within {bound.TotalSeconds:0}s; its process tree was killed.";
    }

    /// <summary>A completed child process's exit code and fully-drained standard streams.</summary>
    internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
