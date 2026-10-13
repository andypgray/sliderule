using SlideRule.Roslyn;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     One <see cref="WorkspaceSession" /> over one private copy of a fixture solution, shared by every case of
///     the class that owns it: the delta-measuring cases of <c>WorkspaceSessionTests</c> over MyApp, and
///     <c>SessionFragmentStoreTests</c> over MyApp and MultiTfm.
/// </summary>
/// <remarks>
///     <para>
///         The copy is <see cref="TempFixtureWorkspace.Dedicated" />, not the leased one: a session is bound
///         to the path it loaded, so the tree has to outlive the test that mutated it — and taking the class
///         lease here would push every case that mints its own session onto a private copy plus a restore.
///     </para>
///     <para>
///         Cases therefore inherit each other's edits, which is safe precisely because each one re-baselines
///         after its own first acquisition: that call absorbs whatever the previous case left on disk, and
///         the assertion that follows only ever reads the delta across the mutation under test.
///     </para>
/// </remarks>
public sealed class SharedWorkspaceSession : IAsyncDisposable
{
    private readonly TempFixtureWorkspace _fixture;

    /// <summary>A session over a copy of the MyApp fixture.</summary>
    public SharedWorkspaceSession()
        : this(TempFixtureWorkspace.Dedicated())
    {
    }

    /// <summary>
    ///     A session over a copy of the fixture solution <paramref name="solutionFileName" /> in
    ///     <paramref name="fixtureDirectory" />, '/'-separated and relative to the test output's <c>Fixtures/</c> root.
    /// </summary>
    internal SharedWorkspaceSession(string fixtureDirectory, string solutionFileName)
        : this(TempFixtureWorkspace.Dedicated(fixtureDirectory, solutionFileName))
    {
    }

    private SharedWorkspaceSession(TempFixtureWorkspace fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The session every case reconciles through.</summary>
    internal WorkspaceSession Session { get; } = new();

    /// <summary>The copied solution the session is bound to.</summary>
    public string SolutionPath => _fixture.SolutionPath;

    /// <summary>Disposes the session first, so the copy is unheld when it is deleted.</summary>
    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        _fixture.Dispose();
    }

    /// <summary>Absolute path to a file inside the copy, from solution-relative segments.</summary>
    public string PathOf(params string[] relativeSegments)
    {
        return _fixture.PathOf(relativeSegments);
    }
}
