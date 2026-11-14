using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The files CI's MSBuild selection step writes, put through <c>graph</c> the way the step runs it.
/// </summary>
/// <remarks>
///     The step learns which MSBuild a runner image selects from one line of graph's refusal, so it holds only while
///     that refusal carries the line. The files, the solution graph is given and the grep and sed that read the line
///     all come from the step itself, so a change to the step or to the refusal fails here instead of on every image
///     after a push.
/// </remarks>
public sealed class MsBuildSelectionStepTests
{
    private const string StepName = "Assert which MSBuild this runner selects";

    // The step's two ways of writing a file under its $root: a quoted heredoc closed by its tag on a line of its own,
    // and a single-quoted echo.
    private static readonly Regex HeredocWrite = new("""^cat > "\$root/(?<path>[^"]+)" <<'(?<tag>\w+)'$""", RegexOptions.CultureInvariant);

    private static readonly Regex EchoWrite = new("""^echo '(?<text>[^']*)' > "\$root/(?<path>[^"]+)"$""", RegexOptions.CultureInvariant);

    private static readonly Regex GraphTarget = new("""\bgraph "\$root/(?<path>[^"]+)"(?=\s)""", RegexOptions.CultureInvariant);

    // The step reads its line with a grep and its selection with a sed chain of empty substitutions over that line.
    private static readonly Regex LineGrep = new("""grep -m1 '(?<pattern>[^']+)' "\$log"(?=\s)""", RegexOptions.CultureInvariant);

    private static readonly Regex SelectionSed = new("""^selection="\$\(sed (?<edits>.+) <<<"\$line"\)"$""", RegexOptions.CultureInvariant);

    private static readonly Regex SedEdit = new("""-e 's/(?<pattern>[^/]+)//'""", RegexOptions.CultureInvariant);

    [Fact]
    public async Task Graph_OverTheFilesTheStepWrites_RefusesWithTheLineItsSedReadsTheSelectionFrom()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh("msbuild-selection-step");
        string workflow = RepoRoot.ReadText(TrackedFiles.CiWorkflow);
        string step = QuoteSyncTests.WorkflowSteps(workflow, StepName)
            .ShouldHaveSingleItem($"{TrackedFiles.CiWorkflow} should have one step named '{StepName}'.");
        string[] run = RunBlock(step);
        string script = string.Join("\n", run);
        string[] written = WriteFiles(run, root);
        string? solution = GraphTargetOf(script);
        Regex? selectionLine = SelectionLineOf(script);
        Regex[] selectionEdits = SelectionEditsOf(run);
        written.ShouldNotBeEmpty($"The step '{StepName}' writes no file this test can read. Its run block:\n{script}");
        solution.ShouldNotBeNull($"The step '{StepName}' runs graph on no file under $root. Its run block:\n{script}");
        selectionLine.ShouldNotBeNull($"The step '{StepName}' reads the selection line with no grep this test can read. Its run block:\n{script}");
        selectionEdits.ShouldNotBeEmpty($"The step '{StepName}' reads the selection with no sed chain this test can read. Its run block:\n{script}");

        // Act
        string solutionPath = root.PathOf(solution.Split('/'));
        CliResult graph = await CliRunner.InvokeColdAsync("graph", solutionPath, "--no-cache");

        // Assert: one line the step's grep reads, from which its sed chain leaves the MSBuild this process selected.
        string line = graph.Err.Split('\n')
            .Where(candidate => selectionLine.IsMatch(candidate))
            .ShouldHaveSingleItem($"graph should answer the step's files with one line its grep reads. Exit {graph.Exit}, stderr:\n{graph.Err}");
        string selection = selectionEdits.Aggregate(line, static (text, edit) => edit.Replace(text, "", 1));
        selection.ShouldBe(MsBuildBootstrap.LastSelection, customMessage: line);
    }

    // The run block's lines as bash receives them, with the YAML block's indentation removed.
    private static string[] RunBlock(string step)
    {
        string[] lines = step.Split('\n');
        int start = Array.FindIndex(lines, static line => line.Trim() == "run: |");
        if (start < 0) return [];

        string[] block = lines.Skip(start + 1)
            .ToArray();
        int indent = block
            .Where(static line => line.Trim().Length > 0)
            .Select(static line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return block
            .Select(line => line.Trim().Length == 0 ? "" : line[indent..])
            .ToArray();
    }

    // Writes each file the run block writes under $root into root, with the bytes bash would write, and returns the
    // paths written.
    private static string[] WriteFiles(string[] run, TempDirectory root)
    {
        List<string> written = [];
        for (var index = 0; index < run.Length; index++)
        {
            Match heredoc = HeredocWrite.Match(run[index]);
            if (heredoc.Success)
            {
                string tag = heredoc.Groups["tag"].Value;
                string[] body = run.Skip(index + 1)
                    .TakeWhile(line => line != tag)
                    .ToArray();
                string[] segments = heredoc.Groups["path"].Value.Split('/');
                string content = string.Join("\n", body) + "\n";
                written.Add(root.WriteFile(segments, content));
                index += body.Length + 1;
                continue;
            }

            Match echo = EchoWrite.Match(run[index]);
            if (!echo.Success) continue;

            string[] echoSegments = echo.Groups["path"].Value.Split('/');
            written.Add(root.WriteFile(echoSegments, echo.Groups["text"].Value + "\n"));
        }

        return written.ToArray();
    }

    private static string? GraphTargetOf(string script)
    {
        Match graph = GraphTarget.Match(script);
        return graph.Success ? graph.Groups["path"].Value : null;
    }

    // The step's grep and sed patterns are basic regular expressions written in the subset whose meaning .NET shares:
    // anchors, escaped dots, starred atoms and literal text.
    private static Regex? SelectionLineOf(string script)
    {
        Match grep = LineGrep.Match(script);
        return grep.Success ? new Regex(grep.Groups["pattern"].Value, RegexOptions.CultureInvariant) : null;
    }

    // The sed chain's substitutions, in the order sed applies them. None carries the g flag, so each replaces only
    // its first match.
    private static Regex[] SelectionEditsOf(string[] run)
    {
        Match sed = run
            .Select(static line => SelectionSed.Match(line))
            .FirstOrDefault(static match => match.Success) ?? Match.Empty;
        return SedEdit.Matches(sed.Groups["edits"].Value)
            .Select(static edit => new Regex(edit.Groups["pattern"].Value, RegexOptions.CultureInvariant))
            .ToArray();
    }
}
