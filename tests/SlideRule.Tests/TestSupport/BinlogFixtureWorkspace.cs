namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A binlog of a private restored copy of the MyApp fixture, built once per test class that takes it as
///     a class fixture — the substrate for the binlog-replay tests. Producing a binlog needs a real
///     <c>dotnet build -bl</c>, which costs seconds, so the copy → restore → build runs once per class and
///     every test in it reads the same <see cref="SolutionPath" /> / <see cref="BinlogPath" />.
/// </summary>
/// <remarks>
///     <para>
///         The binlog bakes in the copy's absolute source paths, so replay reads exactly those files from
///         disk — which is why a freshness test mutates a source file in place under <see cref="PathOf" />
///         rather than relocating the tree, and why a class cannot be handed a copy of another class's
///         binlog: it would replay the other class's tree.
///     </para>
///     <para>
///         One tree per class is what lets the replay classes run in parallel: a class's tests run one at a
///         time, so a test that edits the tree and restores it through a <see cref="ScopedFile" /> is never
///         observed by another reader mid-edit.
///     </para>
/// </remarks>
public sealed class BinlogFixtureWorkspace : IDisposable
{
    private const string UnbuiltName = "MyApp.Unbuilt";

    private readonly TempFixtureWorkspace _copy;

    /// <summary>Copies, restores and builds the fixture with the binary logger.</summary>
    public BinlogFixtureWorkspace()
    {
        // Dedicated, not leased: the binlog bakes in this copy's absolute paths and a freshness test mutates
        // a source file in place, so this tree must survive the whole class rather than be reset between
        // tests the way a leased copy is.
        _copy = TempFixtureWorkspace.Dedicated();

        DotnetCli.Run(
            $"build \"{_copy.SolutionPath}\" -bl:LogFile=\"{BinlogPath}\"",
            Path.GetDirectoryName(_copy.SolutionPath)!);
    }

    /// <summary>Absolute path to the copied solution file (the tree the binlog was built from).</summary>
    internal string SolutionPath => _copy.SolutionPath;

    /// <summary>Absolute path to the binlog produced by building <see cref="SolutionPath" />.</summary>
    internal string BinlogPath => _copy.PathOf("MyApp.binlog");

    /// <summary>Drops any pooled session over the copy, then deletes it.</summary>
    public void Dispose()
    {
        _copy.Dispose();
    }

    /// <summary>Absolute path to a file or directory inside the copy, from solution-relative segments.</summary>
    internal string PathOf(params string[] relativeSegments)
    {
        return _copy.PathOf(relativeSegments);
    }

    /// <summary>
    ///     The project <see cref="WriteSolutionWithUnbuiltMember" />'s wider solution declares and the binlog never
    ///     built, <c>MyApp.Unbuilt\MyApp.Unbuilt.csproj</c>, as an absolute path.
    /// </summary>
    internal string UnbuiltProject => PathOf(UnbuiltName, $"{UnbuiltName}.csproj");

    /// <summary>
    ///     Writes a second solution file beside <see cref="SolutionPath" /> declaring its members plus
    ///     <see cref="UnbuiltProject" />, and returns a scope that deletes what it wrote on dispose. The member's
    ///     project file is written too unless <paramref name="memberOnDisk" /> is false, which makes the member a
    ///     missing file rather than an unbuilt project. Both files carry the original solution's write time, so a
    ///     capture's ingest reads them as no newer than the build rather than refusing the binlog as stale.
    /// </summary>
    internal WiderSolution WriteSolutionWithUnbuiltMember(bool memberOnDisk = true)
    {
        string widerPath = PathOf("MyApp.Wider.sln");
        var scope = new WiderSolution(
            ScopedFile.Hold(widerPath), memberOnDisk ? ScopedFile.Hold(UnbuiltProject) : null);
        if (memberOnDisk)
        {
            Directory.CreateDirectory(PathOf(UnbuiltName));
            File.WriteAllText(UnbuiltProject, "<Project />");
            File.SetLastWriteTimeUtc(UnbuiltProject, File.GetLastWriteTimeUtc(SolutionPath));
        }

        string declared = File.ReadAllText(SolutionPath);
        int global = declared.IndexOf("Global", StringComparison.Ordinal);
        string member = ClassicSolution.Entry(UnbuiltName, "{44444444-4444-4444-4444-444444444444}");

        File.WriteAllText(widerPath, declared.Insert(global, member));
        File.SetLastWriteTimeUtc(widerPath, File.GetLastWriteTimeUtc(SolutionPath));
        return scope;
    }
}

/// <summary>
///     The wider solution <see cref="BinlogFixtureWorkspace.WriteSolutionWithUnbuiltMember" /> wrote, and the
///     member's project file when it wrote one; disposing deletes both.
/// </summary>
internal sealed class WiderSolution(ScopedFile solution, ScopedFile? member) : IDisposable
{
    /// <summary>The wider solution file.</summary>
    internal string Path => solution.Path;

    public void Dispose()
    {
        member?.Dispose();
        solution.Dispose();
    }
}
