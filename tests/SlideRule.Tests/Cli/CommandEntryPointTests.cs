using Shouldly;
using SlideRule.Cli;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The outer shell's answer to a cancelled run, unit-tested without a workspace: a run the operator
///     cancelled prints one line and exits 2, and a cancellation nobody asked for is still a bug, which
///     <see cref="CliErrorMapper" /> prints with its stack trace.
/// </summary>
public sealed class CommandEntryPointTests
{
    [Fact]
    public async Task RunAsync_RunCancelledByTheOperator_PrintsOneLineAndExitsTwo()
    {
        var cancelled = new CancellationToken(canceled: true);

        CliResult result = await CliResult.CapturedAsync((_, error) =>
            CommandEntryPoint.RunAsync(() => Task.FromCanceled<int>(cancelled), error, cancelled));

        result.ShouldRefuseWith();
        result.Err.ShouldBe("Cancelled." + Environment.NewLine, result.Transcript());
    }

    [Fact]
    public async Task RunAsync_CancellationNobodyAskedFor_PrintsTheStackTraceAndExitsTwo()
    {
        CliResult result = await CliResult.CapturedAsync((_, error) =>
            CommandEntryPoint.RunAsync(() => throw new OperationCanceledException(), error, CancellationToken.None));

        result.ShouldRefuseWith("System.OperationCanceledException");
        result.Err.ShouldNotContain("Cancelled.", result.Transcript());
    }
}
