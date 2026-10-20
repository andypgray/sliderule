using System.ComponentModel;
using ModelContextProtocol.Server;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Rendering;

namespace SlideRule.Cli.Mcp.Prompts;

/// <summary>
///     The MCP prompt surface. One prompt, <c>derive_spec</c>, whose body is the embedded
///     <c>derive-spec.md</c> recipe for deriving a SlideRule architecture spec from an existing codebase,
///     validating every claim with <c>arch_graph</c> and <c>arch_check</c>.
/// </summary>
/// <remarks>
///     The class is deliberately non-static: <c>WithPrompts&lt;ArchPrompts&gt;()</c> takes it as a type
///     argument, and static classes cannot be type arguments. The prompt method is <c>static</c>, so no
///     instance is ever constructed. A <c>string</c>-returning method maps to a single <c>Role.User</c>
///     <c>PromptMessage</c> carrying the markdown body.
/// </remarks>
[McpServerPromptType]
internal sealed class ArchPrompts
{
    internal const string DeriveSpecName = "derive_spec";

    private const string DeriveSpecDescription =
        "Derive an architecture spec (Enforce/Migrate/Quarantine/Caution postures) from an existing codebase. The server "
        + "does not infer the spec; this recipe guides you: survey with arch_graph, draft candidate rules, "
        + "validate with arch_check, the human curates and baselines.";

    /// <summary>
    ///     The version placeholder as the raw scaffold spells it. The embedded markdown stays a
    ///     readable template; serving replaces this token with the running build's version, which
    ///     lockstep package versioning and the CI version pin make the right contract version to
    ///     reference.
    /// </summary>
    internal const string ScaffoldVersionPlaceholder = "Version=\"...\"";

    /// <summary>
    ///     The package placeholder as the raw recipe spells it, in the <c>dnx</c> prefix a session with no
    ///     installed <c>sliderule</c> command has to put in front of every CLI verb. Serving pins it to
    ///     the running build, so the recipe's CLI half names the same engine its tool half is.
    /// </summary>
    internal const string CliPackagePlaceholder = "SlideRule.Cli@...";

    // The served body, composed once: both halves are fixed for the process's life (an embedded resource
    // and the running build's own version), so a prompts/get is a field read rather than a resource
    // read plus a scan of the whole recipe.
    private static readonly string DeriveSpecBody = ComposeDeriveSpecBody();

    [McpServerPrompt(Name = DeriveSpecName, Title = "Derive an architecture spec")]
    [Description(DeriveSpecDescription)]
    internal static string DeriveSpec()
    {
        return DeriveSpecBody;
    }

    private static string ComposeDeriveSpecBody()
    {
        string template = EmbeddedResourceText.Load("derive-spec.md");

        // The served recipe carries the generated vocabulary table but never the two marker lines around it:
        // the markers are for the gate that holds the committed file, and one managed block per file is a
        // rule about files, which the prompt is not. Only the two marker lines go, each with its line break.
        IEnumerable<string> unmarkedLines = template.Split('\n')
            .Where(line => line.Trim() is not (ManagedBlock.BeginMarker
                or ManagedBlock.EndMarker));
        string unmarked = string.Join("\n", unmarkedLines);

        return unmarked
            .Replace(ScaffoldVersionPlaceholder, $"Version=\"{ServerVersion.SemVer}\"")
            .Replace(CliPackagePlaceholder, $"SlideRule.Cli@{ServerVersion.SemVer}");
    }
}
