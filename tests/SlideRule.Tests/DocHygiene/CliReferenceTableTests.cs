using System.Globalization;
using System.Reflection;
using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Cli;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;
using SlideRule.ModelFile;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the command-line reference's tables, each held to the facts the code names.
/// </summary>
/// <remarks>
///     Each fact compares both ways, so a row the code no longer backs reds as surely as a fact the page leaves
///     out. The codes, the documents, the variables and the events are compared as sets, because the page orders
///     its rows for a reader, and the code's order is only declaration order. The grains are compared as a
///     sequence, because each rung is defined by the one before it, and the page's order is that meaning.
/// </remarks>
public sealed class CliReferenceTableTests
{
    private const string ExitCodesHeading = "Exit codes";
    private const string DocumentsHeading = "Documents and versions";
    private const string EnvironmentHeading = "Environment variables";
    private const string GrainsHeading = "Grains";

    private const string YouSetIt = "you";
    private const string ProductPrefix = "SLIDERULE_";
    private const string Unversioned = "unversioned";

    private static readonly string[] Hosts = ["Claude Code", "the operating system"];

    // Each document row the page carries, keyed by its first cell, with the version its owner declares; the hook
    // document's shape is Claude Code's, so it has no version to state.
    private static readonly IReadOnlyDictionary<string, (Type? Owner, string Version)> Documents =
        new Dictionary<string, (Type?, string)>(StringComparer.Ordinal)
        {
            ["check report"] = Versioned(typeof(JsonReportRenderer), JsonReportRenderer.SchemaVersion),
            ["status burndown"] = Versioned(typeof(StatusJsonRenderer), StatusJsonRenderer.SchemaVersion),
            ["graph survey"] = Versioned(typeof(GraphJsonRenderer), GraphJsonRenderer.SchemaVersion),
            ["baseline file"] = Versioned(typeof(BaselineFormat), BaselineFormat.SchemaVersion),
            ["model file"] = Versioned(typeof(ModelDocument), ModelDocument.SchemaVersion),
            ["hook document"] = (null, Unversioned)
        };

    [Fact]
    public void ExitCodeTable_StatesTheExitCodesClass()
    {
        // Arrange
        List<string> declared = DeclaredConstants.Of<int>(typeof(ExitCodes))
            .Select(static code => code.ToString(CultureInfo.InvariantCulture))
            .ToList();

        // Act
        List<string> stated = Rows(ExitCodesHeading, "Code")
            .Select(static row => Unticked(row[0]))
            .ToList();

        // Assert
        stated.ShouldBe(
            declared,
            ignoreOrder: true,
            $"The exit-code table under '## {ExitCodesHeading}' states {string.Join(", ", stated)}; "
            + $"{nameof(ExitCodes)} declares {string.Join(", ", declared)}.");
    }

    [Fact]
    public void DocumentsTable_StatesEachSchemaVersion()
    {
        // Arrange
        List<string[]> rows = Rows(DocumentsHeading, "Document");

        // Act
        List<string> drift = rows
            .Where(static row => Documents.TryGetValue(row[0], out (Type? Owner, string Version) document)
                                 && Unticked(row[^1]) != document.Version)
            .Select(static row => $"'{row[0]}' states {row[^1]}, and its owner declares {Documents[row[0]].Version}.")
            .ToList();

        // Assert
        rows.Select(static row => row[0])
            .ShouldBe(
                Documents.Keys,
                ignoreOrder: true,
                $"The documents table under '## {DocumentsHeading}' must carry exactly these rows.");
        drift.ShouldReportNothing("These document rows state a schemaVersion their owner does not declare");
    }

    [Fact]
    public void DocumentsTable_CoversEveryTypeDeclaringASchemaVersion()
    {
        // Arrange
        HashSet<Type> tabled = Documents.Values
            .Select(static document => document.Owner)
            .OfType<Type>()
            .ToHashSet();

        // Act
        List<string> untabled = new[] { typeof(Arch).Assembly, typeof(CommandFactory).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type.GetField(
                    "SchemaVersion",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                is { IsLiteral: true })
            .Where(type => !tabled.Contains(type))
            .Select(static type => $"{type.FullName} declares a SchemaVersion and has no row.")
            .ToList();

        // Assert
        untabled.ShouldReportNothing(
            $"Every document a command writes with a version belongs in the table under '## {DocumentsHeading}'");
    }

    [Fact]
    public void EnvironmentTable_StatesEveryVariableTheProductReads()
    {
        // Arrange
        IReadOnlyList<string> declared = DeclaredConstants.Of<string>(typeof(SlideRuleEnvVars));
        List<string[]> rows = Rows(EnvironmentHeading, "Variable");
        var idleDefault = $"{IdleTimeoutWatchdog.DefaultTimeoutMinutes} by default";

        // Act: the product's own variables are the ones the user sets, and every other one names its host.
        List<string> findings = rows
            .Select(static row => (Name: Unticked(row[0]), SetBy: row[1]))
            .Where(static row => row.Name.StartsWith(ProductPrefix, StringComparison.Ordinal)
                ? row.SetBy != YouSetIt
                : !Hosts.Contains(row.SetBy))
            .Select(static row => $"{row.Name} is set by '{row.SetBy}'.")
            .ToList();
        string idleRow = string.Join(
            " | ",
            rows.SingleOrDefault(static row => Unticked(row[0]) == SlideRuleEnvVars.IdleTimeoutMinutes) ?? []);
        if (!idleRow.Contains(idleDefault, StringComparison.Ordinal))
            findings.Add($"The {SlideRuleEnvVars.IdleTimeoutMinutes} row does not say '{idleDefault}': {idleRow}");

        // Assert
        rows.Select(static row => Unticked(row[0]))
            .ShouldBe(
                declared,
                ignoreOrder: true,
                $"The table under '## {EnvironmentHeading}' must name exactly the variables "
                + $"{nameof(SlideRuleEnvVars)} declares.");
        findings.ShouldReportNothing(
            $"Every {ProductPrefix} variable is set by '{YouSetIt}', and every other by one of "
            + $"{string.Join(" or ", Hosts)}");
    }

    [Fact]
    public void GrainTable_StatesTheRungsInOrder()
    {
        // Act
        List<string> stated = Rows(GrainsHeading, "Grain")
            .Select(static row => Unticked(row[0]))
            .ToList();

        // Assert
        stated.ShouldBe(
            DocumentGrains.Names,
            $"The grain table under '## {GrainsHeading}' states {string.Join(", ", stated)}, in that order.");
    }

    [Fact]
    public void HookEventTable_StatesTheEventsTheHookDocumentNames()
    {
        // Act
        List<string> stated = Rows(DocumentsHeading, "Event")
            .Select(static row => Unticked(row[0]))
            .ToList();

        // Assert
        stated.ShouldBe(
            HookReportRenderer.Events,
            ignoreOrder: true,
            $"The event table under '## {DocumentsHeading}' states {string.Join(", ", stated)}.");
    }

    private static (Type?, string) Versioned(Type owner, int version)
    {
        return (owner, version.ToString(CultureInfo.InvariantCulture));
    }

    // The body rows of the table under "## heading" whose header row opens with firstHeaderCell, each split into its
    // trimmed cells. The page's prose alone is read, so a line of help that happens to start with a pipe is never
    // a row.
    private static List<string[]> Rows(string heading, string firstHeaderCell)
    {
        IReadOnlyList<(string Text, int Number)> prose = SourceAnchors.ProseLines(RepoRoot.ReadText(DocPaths.Cli));
        List<string> section = prose
            .Select(static line => line.Text)
            .SkipWhile(line => line != $"## {heading}")
            .Skip(1)
            .TakeWhile(static line => !line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        int header = section.FindIndex(line => line.StartsWith('|') && Cells(line)[0] == firstHeaderCell);
        List<string[]> rows = header < 0
            ? []
            : section.Skip(header + 2) // past the header row and the delimiter row under it
                .TakeWhile(static line => line.StartsWith('|'))
                .Select(Cells)
                .ToList();

        rows.ShouldNotBeEmpty(
            $"{DocPaths.Cli} has no table under '## {heading}' whose header opens with '{firstHeaderCell}'.");
        return rows;
    }

    private static string[] Cells(string line)
    {
        return line.Trim()
            .Trim('|')
            .Split('|')
            .Select(static cell => cell.Trim())
            .ToArray();
    }

    private static string Unticked(string cell)
    {
        return cell.Trim('`');
    }
}
