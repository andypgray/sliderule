using System.ComponentModel;
using System.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Covers <see cref="ChildProcess" />'s expiry path on both arms: a real tree kill ends the child before the
///     caller gets its timeout, and a refused kill still leaves the caller its timeout, or its own cancellation,
///     rather than the kill's failure.
/// </summary>
/// <remarks>
///     The refused kill is injected because a real tree kill refuses only when a descendant cannot be ended, which no
///     test can arrange.
/// </remarks>
public sealed class ChildProcessTests
{
    // Long enough that the child has almost always recorded its PID before the expiry kills it.
    private static readonly TimeSpan RecordedBound = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Run_Timeout_KillsTheChildAndThrowsTheTimeout()
    {
        using RecordedChild child = RecordedChild.Fresh();

        Should.Throw<TimeoutException>(() => ChildProcess.Run(child.StartInfo(), RecordedBound));

        await child.ShouldHaveEndedAsync();
    }

    [Fact]
    public async Task RunAsync_TimeoutWithNoInjectedKill_KillsTheTreeAndThrowsTheTimeout()
    {
        using RecordedChild child = RecordedChild.Fresh();

        await Should.ThrowAsync<TimeoutException>(ChildProcess.RunAsync(child.StartInfo(), RecordedBound, ct: Ct));

        await child.ShouldHaveEndedAsync();
    }

    [Fact]
    public async Task RunAsync_TimeoutWhoseTreeKillIsRefused_StillThrowsTheTimeout()
    {
        RefusedKill kill = new();

        await Should.ThrowAsync<TimeoutException>(
            ChildProcess.RunAsync(Sleeper(), TimeSpan.FromMilliseconds(500), killTree: kill.Run, ct: Ct));

        kill.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_CancelWhoseTreeKillIsRefused_StillThrowsTheCancellation()
    {
        RefusedKill kill = new();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Should.ThrowAsync<OperationCanceledException>(
            ChildProcess.RunAsync(Sleeper(), TimeSpan.FromMinutes(1), killTree: kill.Run, ct: cancel.Token));

        kill.Calls.ShouldBe(1);
    }

    // A child that outlives any bound here and ends on its own if no kill comes. With recordsPid it first writes
    // its own PID to the file named by RecordedChild.PidFileVariable.
    private static ProcessStartInfo Sleeper(bool recordsPid = false)
    {
        if (OperatingSystem.IsWindows())
        {
            string powershell = Path.Combine(
                Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            string record = recordsPid
                ? $"Set-Content -LiteralPath $env:{RecordedChild.PidFileVariable} -Value $PID; "
                : "";
            return new ProcessStartInfo(powershell, $"-NoProfile -Command {record}Start-Sleep -Seconds 30")
            {
                CreateNoWindow = true
            };
        }

        if (!recordsPid) return new ProcessStartInfo("sleep", "30");

        // exec keeps the recorded PID the sleeping process's own.
        return new ProcessStartInfo("sh")
        {
            ArgumentList = { "-c", $"echo $$ > \"${RecordedChild.PidFileVariable}\"; exec sleep 30" }
        };
    }

    // Ends the tree as Process.Kill(true) does, then refuses as that call does when a descendant survives.
    private sealed class RefusedKill
    {
        internal int Calls { get; private set; }

        internal void Run(Process process)
        {
            Calls++;
            process.Kill(true);
            throw new AggregateException(new Win32Exception());
        }
    }

    // A recording sleeper and the directory its PID file lives in. Dispose kills a child the expiry left running,
    // so a red leaves no sleeper behind.
    private sealed class RecordedChild : IDisposable
    {
        internal const string PidFileVariable = "SLIDERULE_TEST_CHILD_PID_FILE";

        // How long a child the kill missed is given to show itself, by recording its PID or by exiting.
        private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

        private static readonly string[] SleeperNames = ["powershell", "sh", "sleep"];

        private readonly TempDirectory _directory;
        private readonly string _pidFile;

        private RecordedChild(TempDirectory directory)
        {
            _directory = directory;
            _pidFile = directory.PathOf("child.pid");
        }

        public void Dispose()
        {
            try
            {
                using Process? survivor = TryReadPid() is { } pid ? TryGetProcess(pid) : null;
                if (survivor is not null && SleeperNames.Contains(survivor.ProcessName) && !survivor.HasExited)
                    survivor.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited between the check and the kill.
            }
            finally
            {
                _directory.Dispose();
            }
        }

        internal static RecordedChild Fresh()
        {
            return new RecordedChild(TestTempRoot.Fresh("child-process"));
        }

        internal ProcessStartInfo StartInfo()
        {
            ProcessStartInfo startInfo = Sleeper(recordsPid: true);
            startInfo.Environment[PidFileVariable] = _pidFile;
            return startInfo;
        }

        // A child that never records its PID was killed before it could, since unkilled it writes the file and
        // then sleeps for longer than Settle.
        internal async Task ShouldHaveEndedAsync()
        {
            int? pid = await WaitForPidAsync();
            if (pid is null) return;

            using Process? process = TryGetProcess(pid.Value);
            if (process is null) return;

            process
                .WaitForExit(Settle)
                .ShouldBeTrue(
                    $"the expiry should have killed the child (PID {pid}), but it was still running "
                    + $"{Settle.TotalSeconds:0}s after the timeout");
        }

        private async Task<int?> WaitForPidAsync()
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < Settle)
            {
                if (TryReadPid() is { } pid) return pid;
                await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
            }

            return TryReadPid();
        }

        // Null while the file is absent, locked by the child's write, or not yet a whole number.
        private int? TryReadPid()
        {
            try
            {
                return int.TryParse(File.ReadAllText(_pidFile).Trim(), out int pid) ? pid : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static Process? TryGetProcess(int pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
