namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The repo-relative paths of the reader-facing docs, and of the other files carrying reader-facing text,
///     that more than one gate in this folder holds, in the forward-slash spelling <c>RepoRoot.ReadText</c>
///     takes and the tracked set names them by. It also defines the held-page set the fence gates share.
/// </summary>
/// <remarks>
///     Named here rather than on each gate because a page that moves has to move for every gate at once,
///     and <c>SELF-CHECK.md</c>'s path alone is read by several of them. A doc only one gate registers stays a
///     constant on that gate, beside its registration.
/// </remarks>
internal static class DocPaths
{
    /// <summary>The landing page.</summary>
    internal const string RootReadme = "README.md";

    /// <summary>The self-check walkthrough beside it.</summary>
    internal const string SelfCheck = "SELF-CHECK.md";

    /// <summary>The first run: a spec project made from the package, and the two paths to a first spec.</summary>
    internal const string GettingStarted = "GETTING-STARTED.md";

    /// <summary>The page for whoever sets coding agents up: the context files, the MCP server and the hook.</summary>
    internal const string CodingAgents = "CODING-AGENTS.md";

    /// <summary>The command-line reference: each command's help as it prints, and what every command shares.</summary>
    internal const string Cli = "CLI.md";

    /// <summary>The page carrying the rendered diagram, and the command that redraws it.</summary>
    internal const string Architecture = "ARCHITECTURE.md";

    /// <summary>The language spec, whose canonical sample is held to the compiled one.</summary>
    internal const string Grammar = "GRAMMAR.md";

    /// <summary>The release history, which records each release's commands as they shipped.</summary>
    internal const string Changelog = "CHANGELOG.md";

    /// <summary>The flagship example's walkthrough.</summary>
    internal const string MeridianReadme = "examples/Meridian/README.md";

    /// <summary>The walk that derives the flagship example's spec from its code.</summary>
    internal const string MeridianAdopting = "examples/Meridian/ADOPTING.md";

    /// <summary>The flagship example's agent-loop storyboard.</summary>
    internal const string MeridianStoryboard = "examples/Meridian/hooks/storyboard.md";

    /// <summary>The clean-architecture example's walkthrough.</summary>
    internal const string QuotingReadme = "examples/Meridian.Quoting/README.md";

    /// <summary>The module-isolation example's walkthrough.</summary>
    internal const string OperationsReadme = "examples/Meridian.Operations/README.md";

    /// <summary>The cited-guidance example's walkthrough.</summary>
    internal const string InterchangeReadme = "examples/Meridian.Interchange/README.md";

    /// <summary>
    ///     The hook verb's page: its exit-code contract, its options, and the MCP registration a clone pastes.
    /// </summary>
    internal const string HooksReadme = "hooks/README.md";

    /// <summary>The contributor guide.</summary>
    internal const string Contributing = "CONTRIBUTING.md";

    /// <summary>The security policy, which also says how to verify a release.</summary>
    internal const string Security = "SECURITY.md";

    /// <summary>The xUnit adapter package's README, its landing page on nuget.org.</summary>
    internal const string AdapterReadme = "src/SlideRule.Xunit/README.md";

    /// <summary>The contract package's README, its landing page on nuget.org.</summary>
    internal const string ContractReadme = "src/SlideRule/README.md";

    /// <summary>The onboarding prompt the MCP server serves.</summary>
    internal const string DeriveSpecPrompt = "src/SlideRule.Cli/Mcp/Prompts/derive-spec.md";

    /// <summary>The tool's project, whose package description is the first text nuget.org shows for the tool.</summary>
    internal const string CliProject = "src/SlideRule.Cli/SlideRule.Cli.csproj";

    /// <summary>
    ///     The xUnit adapter's project, whose package description states the xunit.v3 requirement a consumer
    ///     reads.
    /// </summary>
    internal const string AdapterProject = "src/SlideRule.Xunit/SlideRule.Xunit.csproj";

    /// <summary>The MCP registry manifest the tool's package carries.</summary>
    internal const string McpManifest = ".mcp/server.json";

    /// <summary>
    ///     The tracked root-level docs that are not held pages, each with the reason it is not. Every other
    ///     root-level doc is one of <see cref="HeldPages" />, and every fence on a held page must be
    ///     registered.
    /// </summary>
    internal static readonly (string Doc, string Reason)[] UnheldRootDocs =
    [
        ("AGENTS.md", "render writes its managed block, and it carries no fence"),
        (Architecture,
            "render writes its drawings, and the hand-written redraw command under them is held by "
            + "DiagramScopeSyncTests and parsed by FencedCommandTests"),
        (Changelog, "it records each release's commands as they shipped"),
        ("CODE_OF_CONDUCT.md", "it is adopted verbatim"),
        (Grammar,
            "it is the language spec, and its fences are the definitions other files copy; the canonical sample in "
            + "§12 is registered, and held to the sample that compiles")
    ];

    /// <summary>
    ///     The pages every fence of which must be registered: every tracked root page
    ///     <see cref="UnheldRootDocs" /> does not name, and every page under <c>examples/</c> a reader reads, as
    ///     against the <c>AGENTS.md</c> and <c>ARCHITECTURE.md</c> files <c>render</c> writes there.
    /// </summary>
    /// <remarks>
    ///     A root page is held on arrival unless <see cref="UnheldRootDocs" /> names it with its reason, so a fence
    ///     on a new root page reds in <see cref="QuoteSyncTests.EveryFence_IsRegistered" /> until it is registered
    ///     or the page is named. The example pages are computed, because <c>render</c> decides which files there it
    ///     writes.
    /// </remarks>
    internal static IReadOnlyList<string> HeldPages =>
    [
        .. TrackedFiles.Markdown.Where(static path => !path.Contains('/')
                                                      && UnheldRootDocs.All(entry => entry.Doc != path)),
        .. TrackedFiles.Markdown.Where(static path => path.StartsWith("examples/", StringComparison.Ordinal)
                                                      && Path.GetFileName(path) is not ("AGENTS.md" or "ARCHITECTURE.md"))
    ];
}
