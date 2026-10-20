using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     Defensive shutdown watcher: when the MCP host process dies abnormally (Task Manager kill,
///     host crash, force-close) without cleanly closing this server's stdin pipe, the standard
///     stdin-EOF detection in the MCP SDK can miss the exit and orphan the server. This watcher
///     resolves the parent PID at startup and exits through <see cref="ServerShutdown" /> when that
///     parent process terminates.
/// </summary>
/// <remarks>
///     Windows-only. Other hosts skip silently because the Unix MCP SDK reliably observes stdin EOF
///     and the parent-PID lookup uses a Windows-specific NT API. Set
///     <c>SLIDERULE_DISABLE_PARENT_WATCH=true</c> to opt out (for debugging or detached runs).
/// </remarks>
internal static class ParentProcessWatcher
{
    /// <summary>
    ///     Starts the watcher. When <paramref name="disabled" />, off Windows, or when the parent PID cannot be
    ///     resolved, it logs why and attaches nothing.
    /// </summary>
    public static void Start(bool disabled)
    {
        Start(
            GetParentProcessId,
            () => ServerShutdown.ExitWith("parent process exited"),
            disabled);
    }

    internal static void Start(Func<int?> parentLookup, Action onParentExited, bool disabled)
    {
        if (disabled)
        {
            Log.Information("Parent process watcher disabled via SLIDERULE_DISABLE_PARENT_WATCH");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Log.Debug("Parent process watcher: non-Windows host, skipping");
            return;
        }

        int? parentPid = parentLookup();
        if (parentPid is null)
        {
            Log.Warning("Parent process watcher: could not resolve parent PID, skipping");
            return;
        }

        Process parent;
        try
        {
            parent = Process.GetProcessById(parentPid.Value);
        }
        catch (ArgumentException)
        {
            Log.Warning("Parent process {Pid} already exited at startup, exiting now", parentPid);
            onParentExited();
            return;
        }

        // Logged at Warning so post-mortem at the default min-level can confirm the watcher attached.
        Log.Warning("Parent process watcher attached to PID {Pid}", parentPid);
        // Detached, never awaited: the wait-for-exit loop runs for the life of the server, and Start must
        // return so the transport can begin serving.
        _ = Task.Run(() => WatchAsync(parent, onParentExited));
    }

    /// <summary>The parent PID, read with <c>NtQueryInformationProcess</c>, or null when it cannot be read.</summary>
    internal static int? GetParentProcessId()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var current = Process.GetCurrentProcess();
            ProcessBasicInformation info = default;
            int status = NtQueryInformationProcess(
                current.Handle,
                0, // PROCESSINFOCLASS.ProcessBasicInformation
                ref info,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);

            if (status != 0)
            {
                Log.Debug("NtQueryInformationProcess returned non-zero status 0x{Status:X8}", status);
                return null;
            }

            return info.InheritedFromUniqueProcessId.ToInt32();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Parent PID lookup failed");
            return null;
        }
    }

    private static async Task WatchAsync(Process parent, Action onParentExited)
    {
        try
        {
            using (parent)
            {
                await parent.WaitForExitAsync();
            }

            Log.Warning("Parent process exited; shutting down server");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Parent-process watcher failed; exiting defensively");
        }
        finally
        {
            onParentExited();
        }
    }

    // The search path is pinned to System32 so the loader resolves the real ntdll and cannot bind a copy
    // planted beside the executable or anywhere else on the default probing path (CA5392).
    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
