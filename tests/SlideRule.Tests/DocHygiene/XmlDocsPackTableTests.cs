using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the table that says which packages ship XML documentation. The release gate unzips
///     each package it is about to push and fails unless the documentation file is inside it, and the
///     table naming those packages and their paths is hand-written — so a library added to the release
///     without a row is a package whose editor experience nobody checks, and a framework bump that moves
///     a path is a row that asserts an entry no package carries.
/// </summary>
/// <remarks>
///     <para>
///         <b>Which projects the table has to name.</b> Every project that packs, minus two kinds that
///         have no <c>lib/</c> folder for a row to name: the one that packs as a tool, which ships a
///         command rather than an assembly, and any that ships no build output, which puts its assemblies
///         somewhere a consumer's editor never looks. That set is derived from the solution file, which is
///         what makes an unlisted library a failure here rather than a silent gap in a release.
///     </para>
///     <para>
///         <b>Committed bytes only.</b> The script is read as text; nothing here unzips a package or runs
///         a release. What is held is the agreement between two declarations — the table's, and each
///         project file's — which is the half that can go wrong months before a release would notice.
///     </para>
/// </remarks>
public sealed class XmlDocsPackTableTests
{
    private const string Script = ".github/scripts/verify-xml-docs-packed.sh";

    /// <summary>
    ///     The one package that reads as a library by every other measure and still has no <c>lib/</c>
    ///     folder, which is what the fact below is written around.
    /// </summary>
    private const string AnalyzerProject = "SlideRule.Analyzers";

    // The table sits in a quoted heredoc, so the rows are literal text rather than anything the shell
    // expands: what is read here is what the loop reads.
    private const string TableStart = "<<'EOF'";

    private const string TableEnd = "EOF";

    /// <summary>
    ///     A row's package glob, less the version part and the extension. The Core row's glob requires a
    ///     digit where the others take anything, because <c>SlideRule.*</c> would also match every
    ///     other package's file and the row would then assert Core's entry against whichever one unzipped
    ///     first — so both spellings have to resolve to the same package id here.
    /// </summary>
    private static readonly Regex PackageGlob =
        new(@"^(?<id>.+)\.(?:\[0-9\]\*|\*)\.nupkg$", RegexOptions.CultureInvariant);

    private const string Header =
        "The table naming the packages that ship XML documentation no longer describes what this "
        + "repository packs. It is hand-written, and every project that packs as a library needs exactly "
        + "one row naming its target framework and its assembly name";

    [Fact]
    public void TheTable_CarriesOneRowPerLibraryPackage()
    {
        // Arrange
        IReadOnlyList<TableRow> rows = Rows();
        IReadOnlyList<SolutionProject> libraries = LibraryPackages();
        List<string> findings = new();

        // Act
        foreach (SolutionProject library in libraries)
        {
            IReadOnlyList<TableRow> claimed = RowsFor(rows, library);
            if (claimed.Count == 1) continue;

            findings.Add($"{Script} carries {claimed.Count} rows for {library.PackageId}, not one.");
        }

        IEnumerable<TableRow> unclaimed = rows
            .Where(row => libraries.All(library => library.PackageId != row.PackageId));
        findings.AddRange(unclaimed.Select(row =>
            $"{Script} carries a row for {row.PackageId}, which is not a library this solution packs."));

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void EveryRow_NamesItsProjectsFrameworkAndAssembly()
    {
        // Arrange
        IReadOnlyList<TableRow> rows = Rows();
        List<string> findings = new();

        // Act: the documentation file packs beside the assembly, so its entry is the framework the
        // project targets and the name the assembly carries — two facts the project file already states.
        foreach (SolutionProject library in LibraryPackages())
        {
            IReadOnlyList<TableRow> claimed = RowsFor(rows, library);
            if (claimed.Count != 1) continue;

            string asserted = claimed[0]
                .Entry;
            string packed = EntryFor(library);
            if (asserted == packed) continue;

            findings.Add($"{Script} asserts {asserted} for {library.PackageId}, but "
                         + $"{library.CsprojPath} packs {packed}.");
        }

        // Assert
        findings.ShouldReportNothing(Header);
    }

    [Fact]
    public void TheLibrarySet_LeavesOutTheAnalyzerAndNothingElse()
    {
        // Arrange: the analyzer packs and is not a tool, so deciding this set by those two facts alone
        // would put it in — and the table would then demand a lib/netstandard2.0 entry from a package
        // whose assemblies all sit under analyzers/dotnet/cs. Reading whether a package ships its build
        // output at all is the third fact, and this is where it is stated rather than inferred.
        IReadOnlyList<SolutionProject> packableLibraries = SolutionInventory.PackableProjects
            .Where(static project => !project.IsTool)
            .ToArray();
        packableLibraries.ShouldContain(
            static project => project.Name == AnalyzerProject,
            $"{AnalyzerProject} is not among the packable non-tool projects, so this fact is about nothing.");

        // Act
        string[] leftOut = packableLibraries
            .Except(LibraryPackages())
            .Select(static project => project.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        leftOut.ShouldBe(
            [AnalyzerProject],
            Case.Sensitive,
            "Which packable projects have no lib/ folder is read from IncludeBuildOutput. A name that "
            + "appeared here ships its assemblies somewhere a consumer never compiles against and needs no "
            + "row in the table; one that disappeared now packs a lib/ folder and needs one.");
    }

    private static string EntryFor(SolutionProject library)
    {
        return $"lib/{library.TargetFramework}/{library.AssemblyName}.xml";
    }

    private static IReadOnlyList<TableRow> RowsFor(IEnumerable<TableRow> rows, SolutionProject library)
    {
        return rows.Where(row => row.PackageId == library.PackageId)
            .ToArray();
    }

    // What packs, minus the tool and minus anything that ships no build output: the set whose consumers
    // compile against an assembly in lib/ and read its documentation in an editor.
    private static IReadOnlyList<SolutionProject> LibraryPackages()
    {
        return SolutionInventory.PackableProjects
            .Where(static project => !project.IsTool)
            .Where(static project => project.IncludeBuildOutput)
            .ToArray();
    }

    // The heredoc's lines, from the line that opens it to the line that closes it. A row is a glob and an
    // entry separated by a space, which is how the loop that reads it splits them.
    private static IReadOnlyList<TableRow> Rows()
    {
        string[] lines = RepoRoot.ReadLines(Script);
        int start = Array.FindIndex(lines, static line => line.Contains(TableStart, StringComparison.Ordinal));
        if (start < 0)
            throw new InvalidOperationException(
                $"{Script} carries no {TableStart} table, so nothing names the packages that ship XML documentation.");

        return lines
            .Skip(start + 1)
            .TakeWhile(static line => line != TableEnd)
            .Select(Row)
            .ToArray();
    }

    private static TableRow Row(string line)
    {
        string[] columns = line.Split(' ', 2);
        Match glob = PackageGlob.Match(columns[0]);

        return new TableRow(glob.Success ? glob.Groups["id"].Value : columns[0], columns.ElementAtOrDefault(1) ?? "");
    }

    /// <summary>
    ///     One row of the table: the <paramref name="PackageId" /> its glob resolves to, and the
    ///     <paramref name="Entry" /> that package is required to carry.
    /// </summary>
    private sealed record TableRow(string PackageId, string Entry);
}
