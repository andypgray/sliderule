using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using SlideRule.ModelFile;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Analyzers.Parity;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     One <c>SR</c> line a real build printed, read back off the console logger's own format.
/// </summary>
/// <param name="Id">The diagnostic ID that led the line.</param>
/// <param name="Rule">The rule ID the message opens with, or the empty string for a line that carries none.</param>
/// <param name="File">Where the line put the warning, relative to the solution directory and '/'-separated.</param>
/// <param name="Line">The one-based line the warning sits on, or zero for a line with no place.</param>
/// <param name="Column">The one-based column, which is the offending line's first non-blank character.</param>
/// <param name="Message">Everything after the ID, with the project suffix taken off.</param>
/// <param name="Project">The project whose compilation printed it, by name.</param>
internal sealed record PrintedWarning(
    string Id,
    string Rule,
    string File,
    int Line,
    int Column,
    string Message,
    string Project)
{
    /// <summary>The one line a failure prints for this warning.</summary>
    internal string Describe()
    {
        string place = Line == 0 ? Project : $"{File}({Line},{Column.ToString(CultureInfo.InvariantCulture)})";

        return $"{place}: {Id}: {Message}";
    }
}

/// <summary>
///     The demonstration the whole build tier exists for, over a real solution: the analyzer package's three
///     assemblies attached to an ordinary <c>dotnet build</c>, and the warnings that build prints held to
///     what a check over the whole solution gives.
/// </summary>
/// <remarks>
///     <para>
///         The analyzer is attached from a copy inside the bed rather than from the repository's build
///         output, because a compiler holds every analyzer assembly open for as long as it runs and a build
///         of this repository must never find one of its own outputs locked by a test.
///         <c>UseSharedCompilation=false</c> is the other half: a compiler server would outlive the build
///         and keep holding the copy.
///     </para>
///     <para>
///         The expectation is the parity pin's own, computed over the bed the copy is taken from rather than
///         written down here, so the demonstration cannot drift from the pin it demonstrates.
///     </para>
/// </remarks>
public sealed class BuildTierDemoE2ETests
{
    /// <summary>The spec the bed is built under, which is the one that breaks rules of every kind.</summary>
    private const string SpecName = "MyAppViolatedSpec";

    /// <summary>The ratcheted rule whose captured baseline grandfathers one of the two pairs it finds.</summary>
    private const string GrandfatheredRule = "data-access/no-inline-sql";

    /// <summary>The file holding the one pair of that rule the baseline does not hold.</summary>
    private const string UngrandfatheredSite = "MyApp.Web/HomeController.cs";

    /// <summary>The line that names a ratcheted rule the build left out for want of a captured baseline.</summary>
    private const string WithheldId = "SR2092";

    /// <summary>Where inside the copy the analyzer is attached from.</summary>
    private const string AnalyzerDirectory = "analyzer";

    /// <summary>The package's own MSBuild file, which hands the analyzer the model file and the baselines.</summary>
    private const string AnalyzerTargets = "SlideRule.Analyzers.targets";

    // The console logger's format for a diagnostic: an origin, the severity and ID, the message, and the
    // project in brackets. The message is lazy so the bracketed project comes off it rather than staying in.
    private static readonly Regex WarningLine = new(
        @"^(?<origin>.*?): warning (?<id>SR\d{4}): (?<message>.*?)(?: \[(?<project>[^\]]*\.[a-z]{2}proj[^\]]*)\])?$",
        RegexOptions.Compiled);

    private static readonly Regex Site = new(@"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\)$", RegexOptions.Compiled);

    // SR2092 names its rule in single quotes, which is the only place that diagnostic carries it.
    private static readonly Regex Quoted = new(@"'(?<rule>[^']+)'", RegexOptions.Compiled);

    /// <summary>The three assemblies the compiler loads together, which the package ships in one folder.</summary>
    private static readonly string[] AnalyzerAssemblies =
    [
        "SlideRule.Analyzers.dll",
        "SlideRule.Extraction.dll",
        "SlideRule.dll"
    ];

    /// <summary>The two findings a broken rule is reported as; everything else the tier says is not one.</summary>
    private static readonly string[] FindingIds = ["SR2001", "SR2002"];

    [Fact]
    public async Task ARealBuild_OfABedTheAnalyzerGoverns_PrintsTheWarningsTheWholeSolutionCheckGives()
    {
        // Arrange
        using var workspace = new TempFixtureWorkspace(restore: false);
        string root = workspace.PathOf();
        ParitySubject subject = await ParityHarness.ForSolutionAsync(
            SpecName, CliRunner.ViolatedSpecDll, CliRunner.MyAppSolution, Ct);
        File.WriteAllText(Path.Combine(root, ModelDocument.FileName), subject.ModelText);
        AttachTheAnalyzer(root, AppContext.BaseDirectory);

        // Act
        ChildProcess.ProcessResult build = Build(workspace);

        // Assert
        string transcript = Transcript(build);
        build.ExitCode.ShouldBe(0, transcript);
        LinesOf(build)
            .Where(line => line.Contains("AD0001", StringComparison.Ordinal)
                           || line.Contains("CS8032", StringComparison.Ordinal))
            .ShouldBeEmpty($"the compiler could not load or could not run the analyzer.{transcript}");

        IReadOnlyList<PrintedWarning> printed = SlideRuleWarningsIn(build, root);
        Describe(printed.Where(warning => !FindingIds.Contains(warning.Id) && warning.Id != WithheldId))
            .ShouldBeEmpty($"line(s) the build tier printed that are neither a finding nor a withheld "
                           + $"rule — a missing model file, a file it could not read, or its own fault.{transcript}");

        // Both sides are computed, so both are asserted non-empty: two empty lists agree about nothing.
        string bed = Path.GetDirectoryName(CliRunner.MyAppSolution)!;
        IReadOnlyList<string> expected = ExpectedKeys(subject, bed);
        IReadOnlyList<string> entitled = Pairs(subject.Expectation.UncheckedRules(subject.Compilations));
        expected.ShouldNotBeEmpty("findings the whole-solution check gives over this bed");
        entitled.ShouldNotBeEmpty("ratcheted rules with no captured baseline that would report here");

        List<PrintedWarning> findings = printed
            .Where(warning => FindingIds.Contains(warning.Id))
            .ToList();
        Keys(findings)
            .ShouldBe(expected, $"which rule the build reported broken, and where.{transcript}");

        ShouldReadAsTheReportReadsThem(findings, subject, transcript);

        IReadOnlyList<string> ratchetedSites = Sorted(findings
            .Where(warning => string.Equals(warning.Rule, GrandfatheredRule, StringComparison.Ordinal))
            .Select(warning => warning.File));
        ratchetedSites.ShouldBe(
            [UngrandfatheredSite],
            $"where the build reported '{GrandfatheredRule}': the pair its captured baseline holds is the "
            + $"one site it must stay silent about.{transcript}");

        UncheckedPairs(printed)
            .ShouldBe(
                entitled,
                $"which ratcheted rules the build named as unchecked, one line per project and rule.{transcript}");
    }

    // ── the copy the build runs over ──────────────────────────────────────────────────────────────────────

    // The whole package attach, hand-written: the three assemblies as Analyzer items and the package's own
    // targets file imported, both out of a folder inside the copy. The targets file removes the analyzer
    // below the Roslyn floor by its NuGetPackageId, which an item added here does not carry — so that target
    // is inert and the rest of the file, which is what hands over the model and the baselines, is what runs.
    private static void AttachTheAnalyzer(string root, string built)
    {
        string attached = Path.Combine(root, AnalyzerDirectory);
        Directory.CreateDirectory(attached);
        foreach (string name in AnalyzerAssemblies)
            File.Copy(Path.Combine(built, name), Path.Combine(attached, name), true);

        string packagedTargets = Path.Combine(
            RepoRoot.Absolute("src/SlideRule.Analyzers/buildTransitive"), AnalyzerTargets);
        File.Copy(packagedTargets, Path.Combine(attached, AnalyzerTargets), true);

        IEnumerable<string> items = AnalyzerAssemblies.Select(name =>
            $"        <Analyzer Include=\"$(MSBuildThisFileDirectory){AnalyzerDirectory}/{name}\" />");
        var content = $"""
                       <Project>

                           <ItemGroup>
                       {string.Join(Environment.NewLine, items)}
                           </ItemGroup>

                           <Import Project="$(MSBuildThisFileDirectory){AnalyzerDirectory}/{AnalyzerTargets}" />

                       </Project>

                       """;
        File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), content);
    }

    // Nothing incremental and no compiler server: the copy is fresh, every project has to reach CoreCompile
    // for the analyzer to say anything, and a server would keep holding the copied assemblies afterwards.
    // NoSummary asks the console logger not to list every warning a second time under "Build succeeded";
    // measured, this SDK's `dotnet build` prints the list either way, which is why the reader below
    // de-duplicates rather than relying on it.
    private static ChildProcess.ProcessResult Build(TempFixtureWorkspace workspace)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(workspace.SolutionPath)!
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(workspace.SolutionPath);
        startInfo.ArgumentList.Add("--no-incremental");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.ArgumentList.Add("-clp:NoSummary");
        DotnetCli.ApplyCleanSdkEnvironment(startInfo);

        return ChildProcess.Run(startInfo);
    }

    // ── what the build printed ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Asserts every finding reads the way the human report reads it: the reason always, and the fix
    ///     wherever the rule carries one.
    /// </summary>
    /// <param name="findings">The findings the build printed.</param>
    /// <param name="subject">The solution and spec under test, whose document says which rules carry a fix.</param>
    /// <param name="transcript">The build's output, so a red carries it.</param>
    private static void ShouldReadAsTheReportReadsThem(
        IReadOnlyList<PrintedWarning> findings, ParitySubject subject, string transcript)
    {
        HashSet<string> carryAFix = subject.Document.Rules
            .Where(rule => rule.Fix is not null)
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.Ordinal);

        Describe(findings.Where(warning => !warning.Message.Contains("Because: ", StringComparison.Ordinal)))
            .ShouldBeEmpty($"finding(s) the build printed without the reason the rule exists.{transcript}");
        Describe(findings.Where(warning =>
                carryAFix.Contains(warning.Rule) && !warning.Message.Contains("Fix: ", StringComparison.Ordinal)))
            .ShouldBeEmpty($"finding(s) whose rule carries a fix that the build printed without it.{transcript}");
    }

    // Every SR line the build printed, de-duplicated on the whole line: a console logger prints a warning
    // once per project that raised it and again in its summary, and neither repeat is a second finding.
    private static IReadOnlyList<PrintedWarning> SlideRuleWarningsIn(ChildProcess.ProcessResult build, string root)
    {
        return LinesOf(build)
            .Distinct(StringComparer.Ordinal)
            .Select(line => WarningLine.Match(line))
            .Where(match => match.Success)
            .Select(match => WarningFrom(match, root))
            .ToList();
    }

    private static PrintedWarning WarningFrom(Match match, string root)
    {
        // A multi-targeted project's suffix carries its framework after a double colon; the path is the head.
        string project = match.Groups["project"].Value.Split("::")[0];
        string projectDirectory = DirectoryOf(project) ?? root;
        string message = match.Groups["message"].Value;
        Match site = Site.Match(match.Groups["origin"].Value);

        var file = "";
        var line = 0;
        var column = 0;
        if (site.Success)
        {
            // The compiler reports a source path as the build handed it over, which is relative to the
            // project rather than to the solution; both sides of the pin are read solution-relative.
            string absolute = Path.GetFullPath(site.Groups["file"].Value, projectDirectory);
            file = Path.GetRelativePath(root, absolute)
                .Replace('\\', '/');
            line = int.Parse(site.Groups["line"].Value, CultureInfo.InvariantCulture);
            column = int.Parse(site.Groups["column"].Value, CultureInfo.InvariantCulture);
        }

        string id = match.Groups["id"].Value;
        string owner = Path.GetFileNameWithoutExtension(project);

        return new PrintedWarning(id, RuleOpening(message), file, line, column, message, owner);
    }

    // Where the project that printed a line sits, or null for a line the logger named no project for.
    private static string? DirectoryOf(string projectPath)
    {
        if (projectPath.Length == 0) return null;

        string absolute = Path.GetFullPath(projectPath);

        return Path.GetDirectoryName(absolute);
    }

    // A finding's message opens with the rule ID and a colon; a warning about the build itself does not.
    private static string RuleOpening(string message)
    {
        int colon = message.IndexOf(": ", StringComparison.Ordinal);

        return colon < 0 ? "" : message.Substring(0, colon);
    }

    private static IReadOnlyList<string> LinesOf(ChildProcess.ProcessResult build)
    {
        return (build.StandardOutput + build.StandardError)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();
    }

    private static string Transcript(ChildProcess.ProcessResult build)
    {
        return $"\n--- exit {build.ExitCode.ToString(CultureInfo.InvariantCulture)} ---\n"
               + $"{build.StandardOutput}\n{build.StandardError}";
    }

    // ── the two sides, keyed the same way ─────────────────────────────────────────────────────────────────

    // File, line and rule: what the build prints as a place and what the check records as one are the same
    // site, and the message beside it is pinned separately.
    private static IReadOnlyList<string> Keys(IEnumerable<PrintedWarning> findings)
    {
        return Sorted(findings.Select(warning =>
            $"{warning.File}:{warning.Line.ToString(CultureInfo.InvariantCulture)} {warning.Rule}"));
    }

    private static IReadOnlyList<string> ExpectedKeys(ParitySubject subject, string root)
    {
        return Sorted(subject.Expectation.Keys.Select(key =>
        {
            string file = Path.GetRelativePath(root, key.File)
                .Replace('\\', '/');

            return $"{file}:{key.Line.ToString(CultureInfo.InvariantCulture)} {key.Rule}";
        }));
    }

    private static IReadOnlyList<string> UncheckedPairs(IEnumerable<PrintedWarning> printed)
    {
        return Sorted(printed
            .Where(warning => string.Equals(warning.Id, WithheldId, StringComparison.Ordinal))
            .Select(warning => $"{warning.Project}: {Quoted.Match(warning.Message).Groups["rule"].Value}"));
    }

    private static IReadOnlyList<string> Pairs(IEnumerable<(string Compilation, string Rule)> pairs)
    {
        return Sorted(pairs.Select(pair => $"{pair.Compilation}: {pair.Rule}"));
    }

    private static IReadOnlyList<string> Describe(IEnumerable<PrintedWarning> warnings)
    {
        return Sorted(warnings.Select(warning => warning.Describe()));
    }

    // Distinct and ordinally sorted: a set comparison whose red reads as two sorted lists.
    private static IReadOnlyList<string> Sorted(IEnumerable<string> lines)
    {
        return lines
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
