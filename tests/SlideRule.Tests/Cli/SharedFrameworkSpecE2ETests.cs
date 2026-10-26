using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The shared-framework spec-load failure end to end: a spec whose <c>typeof()</c> anchor names an
///     ASP.NET Core type, driven through the real CLI, refused with exit 2 on stderr — and handed the
///     string overload that makes the same rule work.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ModelPipelineSpecLoadFailureTests" /> drives this fixture as far as
///         <c>ModelPipeline.LoadModel</c> and asserts the thrown <c>UserErrorException</c>; this class
///         carries it the last step, to the exit code and the channel a user actually reads.
///     </para>
///     <para>
///         The <c>explain</c> DLL fast path short-circuits ahead of the workspace, so no solution is
///         loaded and the call is sub-second — the same shape <see cref="SpecContractSkewE2ETests" /> uses
///         for its refusals. The rule ID is the fixture's own, though nothing reaches a rule lookup:
///         <c>Define()</c> throws on the first anchor.
///     </para>
/// </remarks>
public sealed class SharedFrameworkSpecE2ETests
{
    private const string RuleId = "shared-framework/controllers-suffixed";

    [Fact]
    public async Task Explain_OverASharedFrameworkSpec_RefusesWithTheFrameworkNamedAndTheStringHatchOffered()
    {
        // Arrange: guard the precondition rather than leave a bare "expected exit 2".
        CliRunner.ShouldNotLoadTheSharedFrameworkAnchor();

        // Act
        CliResult result = await CliRunner.InvokeAsync("explain", RuleId, "--spec", CliRunner.SharedFrameworkSpecDll);

        // Assert: the world named, the packaging remedy ruled out, and the escape hatch spelled on the very
        // type that failed — the three things a reader needs to keep the rules rather than delete them.
        result.ShouldRefuseWith(
            "The spec assembly 'SlideRule.SharedFrameworkSpec' failed to load its dependency",
            "'Microsoft.AspNetCore.Mvc.Core,",
            "a .NET shared framework pulled in by <FrameworkReference>",
            "CopyLocalLockFileAssemblies has nothing to copy",
            ".DerivedFrom(\"Microsoft.AspNetCore.Mvc.ControllerBase\") renders identically to the typeof() form");
    }
}
