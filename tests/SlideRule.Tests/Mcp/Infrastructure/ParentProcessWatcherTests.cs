using System.Diagnostics;
using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using Xunit;

namespace SlideRule.Tests.Mcp.Infrastructure;

/// <summary>
///     Verifies <see cref="ParentProcessWatcher" /> opt-out, parent-resolution failure paths,
///     immediate-exit on dead parent, and live-parent watch via the internal test seam.
/// </summary>
[Collection("Serial")]
public sealed class ParentProcessWatcherTests
{
    [Fact]
    public void Start_Disabled_DoesNotInvokeOnExit()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The watcher stands down on any OS but Windows before it asks for the parent, so only Windows can "
            + "show the disabled flag stops it first.");

        // The disabled flag short-circuits before the parent lookup, so neither throwing delegate runs.
        Should.NotThrow(() => ParentProcessWatcher.Start(
            () => throw new InvalidOperationException("lookup must not run when disabled"),
            () => throw new InvalidOperationException("onExit must not run when disabled"),
            true));
    }

    [Fact]
    public void Start_ParentLookupReturnsNull_DoesNotInvokeOnExit()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The watcher stands down on any OS but Windows before it asks for the parent, so a null answer is "
            + "reached only there.");

        // A parent that cannot be resolved means stand down, never exit at once, so the throwing delegate never runs.
        Should.NotThrow(() => ParentProcessWatcher.Start(
            () => null,
            () => throw new InvalidOperationException("onExit must not run when PID is null"),
            false));
    }

    [Fact]
    public void Start_ParentAlreadyExited_InvokesOnExitImmediately()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The watcher stands down on any OS but Windows before it reaches the already-exited branch, so this "
            + "scenario exists only there.");

        // A parent already gone when the watcher starts is reported before Start returns, so a server whose client
        // died first exits during startup rather than after it begins serving. The helper has exited, so
        // Process.GetProcessById throws for its PID and the watcher takes that branch.
        int deadPid = ShouldSpawnAndExit();
        var exited = false;

        ParentProcessWatcher.Start(
            () => deadPid,
            () => exited = true,
            false);

        exited.ShouldBeTrue("Start should report a parent that had already exited before it returns");
    }

    [Fact]
    public void Start_ParentExitsLater_InvokesOnExitWithinTimeout()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "The watcher stands down on any OS but Windows before it attaches to a live parent, so this scenario "
            + "exists only there.");

        // Arrange — spawn a helper that lives ~1 second, point watcher at its live PID.
        using Process helper = SpawnLiveHelper();
        int helperPid = helper.Id;
        ManualResetEventSlim exited = new();

        // Act
        ParentProcessWatcher.Start(
            () => helperPid,
            exited.Set,
            false);

        // Assert — the helper exits quickly; allow generous slack for CI/scheduler.
        exited.Wait(TimeSpan.FromSeconds(10), Ct)
            .ShouldBeTrue();
    }

    [Fact]
    public void GetParentProcessId_OnWindows_ReturnsNonNull()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "GetParentProcessId reads the parent through a Windows API and answers null on any other OS.");

        // The test process is a child of whatever launched it, so a parent always exists. A non-null
        // result confirms the P/Invoke shape compiles and dispatches.
        int? parentPid = ParentProcessWatcher.GetParentProcessId();

        parentPid.ShouldNotBeNull();
        parentPid.Value.ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Spawns <c>cmd /c exit 0</c> and returns its PID once it has exited, failing the test when it has not
    ///     exited within five seconds.
    /// </summary>
    private static int ShouldSpawnAndExit()
    {
        ProcessStartInfo psi = new("cmd.exe", "/c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process proc = Process.Start(psi)
                             ?? throw new InvalidOperationException("Failed to start cmd.exe");
        int pid = proc.Id;
        proc.WaitForExit(TimeSpan.FromSeconds(5))
            .ShouldBeTrue();
        return pid;
    }

    /// <summary>
    ///     Spawns a helper process that lives for roughly one second. Returns the live
    ///     <see cref="Process" /> so the caller can resolve its PID before exit.
    /// </summary>
    private static Process SpawnLiveHelper()
    {
        // ping -n 2 127.0.0.1 sleeps ~1 second between pings, so total runtime is ~1s.
        ProcessStartInfo psi = new("cmd.exe", "/c ping -n 2 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start helper");
    }
}
