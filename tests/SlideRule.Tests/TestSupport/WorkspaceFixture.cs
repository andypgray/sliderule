using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A class fixture over the run's one model of the checked-in <c>MyApp</c> fixture solution: the first
///     consumer class to start pays the load and the extraction through <see cref="FixtureCodebase" />, and
///     every later one awaits the same task. Tests only read <see cref="Model" />, so one shared instance is
///     safe.
/// </summary>
public sealed class WorkspaceFixture : IAsyncLifetime
{
    /// <summary>Absolute path to the fixture solution in the test output directory.</summary>
    public string SolutionPath { get; } = FixtureBeds.MyAppSolution;

    /// <summary>The extracted model. Set during <see cref="InitializeAsync" />.</summary>
    internal CodebaseModel Model { get; private set; } = null!;

    /// <summary>Opens the fixture solution and extracts <see cref="Model" /> from it.</summary>
    public async ValueTask InitializeAsync()
    {
        Model = await FixtureCodebase.Of(SolutionPath);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>Solution-relative, forward-slash rendering of a location's file path.</summary>
    internal string RelativePath(SourceLocation location)
    {
        return RelativePath(location.FilePath);
    }

    /// <summary>Solution-relative, forward-slash rendering of a file path.</summary>
    internal string RelativePath(string filePath)
    {
        return Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, filePath)
            .Replace('\\', '/');
    }

    /// <summary>Renders an edge as the pinned agent-facing form: <c>src -&gt; tgt @ file:line, ...</c>.</summary>
    internal string RenderEdge(ReferenceEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Target.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders a member-use edge as <c>src -&gt; member SymbolId @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    internal string RenderMemberEdge(MemberEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Member.SymbolId} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders a construction edge as <c>src -&gt; constructed @ file:line, ...</c> (GRAMMAR §4.5).</summary>
    internal string RenderConstructorEdge(ConstructorEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Constructed.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders an injection edge as <c>src -&gt; injected @ file:line, ...</c> (GRAMMAR §4.7).</summary>
    internal string RenderInjectionEdge(InjectionEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Injected.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>Renders an exposure edge as <c>src -&gt; exposed @ file:line, ...</c> (GRAMMAR §4.9).</summary>
    internal string RenderExposureEdge(ExposureEdge edge)
    {
        return $"{edge.Source.FullName} -> {edge.Exposed.FullName} @ {Sites(edge.Sites)}";
    }

    /// <summary>
    ///     Renders a registration fact as <c>lifetime service -&gt; impl @ file:line, ...</c> (GRAMMAR §4.7);
    ///     the implementation renders as <c>(none)</c> when the registration names no distinct implementation.
    /// </summary>
    internal string RenderRegistration(ServiceRegistration registration)
    {
        string implementation = registration.ImplementationFullName ?? "(none)";
        return $"{registration.Lifetime} {registration.ServiceFullName} -> {implementation} @ {Sites(registration.Sites)}";
    }

    /// <summary>The site list every render above ends in: <c>file:line</c>, comma-separated, in model order.</summary>
    private string Sites(IEnumerable<SourceLocation> sites)
    {
        return string.Join(", ", sites.Select(s => $"{RelativePath(s)}:{s.Line}"));
    }
}
