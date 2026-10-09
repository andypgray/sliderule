using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using SlideRule.Cli.Settings;
using SlideRule.Internal;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the project list the committed drawing is scoped by. The list has one home, the
///     <c>render.diagram.only</c> array of this repository's <c>sliderule.json</c>, which the workflow's
///     <c>render --check</c> and the plain <c>sliderule render</c> that ARCHITECTURE.md publishes both read. It
///     has to name every project the solution declares outside the tests and their fixtures, in the order the
///     drawing is composed in.
/// </summary>
/// <remarks>
///     <para>
///         The allow-list the dogfood diagram gate composes with is a projection of the solution file, so a
///         project added to <c>src/</c> or <c>arch/</c> joins that drawing on its own. The settings file cannot
///         be projected, because it is what a person's render reads, so this gate reds on the day the
///         projection moves, instead of the file silently scoping every redraw to a list that has gone short.
///     </para>
///     <para>
///         It also holds that the workflow and the page, which each carried the argument by hand before the
///         settings file existed, do not spell a scope again.
///     </para>
/// </remarks>
public sealed class DiagramScopeSyncTests
{
    private const string Workflow = ".github/workflows/ci.yml";

    /// <summary>The files that passed the drawing's scope by hand before the settings file held it.</summary>
    private static readonly string[] FormerSites = [Workflow, DocPaths.Architecture];

    /// <summary>
    ///     A scoping flag given an argument on a <c>render</c> command line: double-quoted (the form both former
    ///     sites spelled their scope in), single-quoted, unquoted, or joined by <c>=</c>. A bare mention in prose
    ///     is not an invocation.
    /// </summary>
    private static readonly Regex ScopeArgument =
        new("render[^\n]*--diagram-(only|exclude)(=|[ \t]+)[\"'A-Za-z0-9_.*]", RegexOptions.CultureInvariant);

    [Fact]
    public void TheSettingsFile_DrawsIntoArchitectureMdAndNamesEveryDrawnProjectInOrder()
    {
        // Arrange
        ProjectSettings settings = ProjectSettingsReader.Read(RepoRoot.Settings);
        List<string> drawn = SolutionInventory.DrawnProjects
            .Select(static project => project.Name)
            .ToList();
        List<string> findings = [];

        // Act
        if (settings.Render.Diagram is not { } diagram)
        {
            findings.Add("sliderule.json declares no render.diagram, so a plain render redraws nothing.");
        }
        else
        {
            if (!string.Equals(diagram.Target, RepoRoot.ArchitectureMd, PathComparison.Comparison))
                findings.Add($"sliderule.json draws into {diagram.Target}, not {RepoRoot.ArchitectureMd}.");
            if (!diagram.Only.SequenceEqual(drawn, StringComparer.Ordinal))
                findings.Add($"sliderule.json scopes the drawing to {string.Join(", ", diagram.Only)}, but the "
                             + $"solution declares {string.Join(", ", drawn)} outside the tests and their fixtures.");
            if (diagram.Exclude.Count > 0)
                findings.Add($"sliderule.json excludes {string.Join(", ", diagram.Exclude)}; the scope is the "
                             + "allow-list alone.");
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Theory]
    [InlineData("sliderule render --diagram-only \"Core,Cli\"")]
    [InlineData("sliderule render --diagram-only 'Core,Cli'")]
    [InlineData("sliderule render --diagram-only Core,Cli")]
    [InlineData("sliderule render --diagram-only=Core,Cli")]
    [InlineData("dotnet sliderule-cli.dll render --check --diagram-exclude Tests")]
    public void ScopeArgument_MatchesEveryWayARenderLinePassesAScope(string line)
    {
        ScopeArgument.IsMatch(line)
            .ShouldBeTrue(line);
    }

    [Theory]
    [InlineData("The --diagram-only flag scopes a drawing.")]
    [InlineData("sliderule render --diagram-only")]
    [InlineData("sliderule render --diagram-only --check")]
    public void ScopeArgument_PassesAMentionThatPassesNoScope(string line)
    {
        ScopeArgument.IsMatch(line)
            .ShouldBeFalse(line);
    }

    [Fact]
    public void NeitherFormerSite_SpellsAScopeOfItsOwn()
    {
        // Act
        List<string> findings = FormerSites
            .Where(static site => ScopeArgument.IsMatch(RepoRoot.ReadText(site)))
            .Select(static site => $"{site} passes a diagram scope of its own.")
            .ToList();

        // Assert
        findings.ShouldReportNothing(Header);
    }

    // What a reader of any of these failures has to do, which is the same thing in every case.
    private const string Header =
        "The committed drawing's scope lives in sliderule.json alone and has to name every project the "
        + "solution declares outside the tests and their fixtures, in the same order — so a new src/ or arch/ "
        + "project belongs in its render.diagram.only list, and no command anywhere passes a scope of its own";
}
