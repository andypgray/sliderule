using Shouldly;
using SlideRule.Codebase;
using SlideRule.Tests.Dogfood;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The case that makes the assembly name worth recording at all: a project whose assembly is not named
///     after it. The model reports the assembly's name, not the project's.
/// </summary>
/// <remarks>
///     <para>
///         The smallest instance to hand is this repository itself — project
///         <c>SlideRule.Cli</c>, assembly <c>sliderule-cli</c>, because the project is the package and the
///         assembly must differ from the contract's <c>SlideRule</c> by more than case. The row holds only
///         while the tool's own name does not move.
///     </para>
///     <para>
///         It reads the whole-codebase extraction the self-spec gates already pay for, so the fact costs
///         nothing beyond the assertion.
///     </para>
/// </remarks>
public sealed class RenamedAssemblyExtractionTests
{
    [Fact]
    public async Task ExtractAsync_AProjectWhoseAssemblyIsRenamed_ReportsTheAssemblysName()
    {
        CodebaseModel codebase = await SelfSpecTests.WholeCodebase.Value;

        ProjectNode cli = codebase.Project("SlideRule.Cli");

        cli.AssemblyNames.ShouldBe(["sliderule-cli"]);
    }
}
