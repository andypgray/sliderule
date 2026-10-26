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
    ///     built, <c>MyApp.Unbuilt\MyApp.Unbuilt.csproj</c>, as an absolute path; no file exists there.
    /// </summary>
    internal string UnbuiltProject => PathOf(UnbuiltName, $"{UnbuiltName}.csproj");

    /// <summary>
    ///     Writes a second solution file beside <see cref="SolutionPath" /> declaring its members plus
    ///     <see cref="UnbuiltProject" />, and returns it as a scope that deletes it on dispose. It carries the
    ///     original's write time, so a capture's ingest reads it as no newer than the build rather than refusing
    ///     the binlog as stale.
    /// </summary>
    internal ScopedFile WriteSolutionWithUnbuiltMember()
    {
        string declared = File.ReadAllText(SolutionPath);
        int global = declared.IndexOf("Global", StringComparison.Ordinal);
        string member =
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{UnbuiltName}\", "
            + $"\"{UnbuiltName}\\{UnbuiltName}.csproj\", \"{{44444444-4444-4444-4444-444444444444}}\"{Environment.NewLine}"
            + $"EndProject{Environment.NewLine}";

        string widerPath = PathOf("MyApp.Wider.sln");
        File.WriteAllText(widerPath, declared.Insert(global, member));
        File.SetLastWriteTimeUtc(widerPath, File.GetLastWriteTimeUtc(SolutionPath));
        return ScopedFile.Planted(widerPath);
    }
}
