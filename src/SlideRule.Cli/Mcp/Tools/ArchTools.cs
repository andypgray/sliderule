using System.ComponentModel;
using ModelContextProtocol.Server;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Mcp.Tools;

/// <summary>
///     The MCP tool surface: the five <c>arch_*</c> tools, each a thin shell that runs the
///     same internal runner its CLI verb uses against the bound solution + spec, captures stdout into a
///     <see cref="StringWriter" />, and returns the text — so CLI and MCP output are identical by
///     construction (pinned by <c>CliMcpParityTests</c>).
/// </summary>
/// <remarks>
///     Violations are data, never tool errors. Tool methods never <c>try/catch</c>: they throw, and
///     <see cref="GlobalCallToolFilter" /> shapes any <see cref="Roslyn.UserErrorException" /> or
///     spec-validation failure into an error result.
///     Reaching a Roslyn workspace type only through the runners keeps the MSBuildLocator JIT quarantine
///     intact — these methods are first JITted at the first tool call, after registration has run. Every
///     runner is handed the injected <see cref="ISolutionSource" />, so every tool call acquires the solution
///     through the one source the server registered. The injected <see cref="IResponseFitter" /> is how a
///     document-shaped tool answers a client whose channel is smaller than the answer: the runner offers its
///     coarsening ladder and the fitter takes the first rung that fits, rather than a tool reading process
///     state to decide.
/// </remarks>
[McpServerToolType]
internal sealed class ArchTools(McpServerBinding binding, ISolutionSource source, IResponseFitter fitter)
{
    private const string CheckDescription =
        "Run the architecture spec against the bound solution and return the JSON check report " +
        "(schemaVersion 3): rules[] keyed by id, plus summary counts. Violations are data — a red rule is a " +
        "finding, not an error. The rules parameter narrows what is evaluated, and the report then covers " +
        "only those. " +
        "Narrow with grain (overview, skeleton or index) or rules (fewer rules); an over-budget " +
        "report coarsens its own grain, as far as index, rather than being cut — and index lists every rule " +
        "ID, which is what rules globs match. " +
        "subjectTypes/subjectGeneratedTypes appear together on a rule whose subject contained generator " +
        "output, pass or fail: how many types it swept and how many nobody wrote. Both absent means none " +
        "were. Narrowing the rule with .Authored() removes them. " +
        "If projects fail to load, or their NuGet packages did not resolve, the report still " +
        "returns, stamped modelIncomplete: true and failedProjects/restoreFailedProjects — a verdict reached " +
        "against a partial model; report that, never plain green. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never " +
        "checked — a clean report then covers a subset; say so. " +
        "unsupportedProjects names the declared projects this product cannot read at all (C# only), each " +
        "with its reason — no rule can be violated in one, so a clean report over a polyglot solution is " +
        "not a clean solution either. " +
        "multiTargetedProjects names the projects that compiled for several frameworks, each with the " +
        "first framework its shared types' members and hierarchy follow (a type that framework does not " +
        "declare follows the next that does); their references come from every framework, so a clean " +
        "report over such a project covers one framework's members and hierarchy per type and every " +
        "framework's references.";

    private const string StatusDescription =
        "Return the JSON migration burndown (schemaVersion 2): per-rule grandfathered/stale counts and " +
        "promotion suggestions. If projects fail to load, or their NuGet packages did not resolve, the " +
        "burndown still returns, stamped modelIncomplete: true and failedProjects/restoreFailedProjects — " +
        "counts from a partial model; report that rather than quoting them as whole. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never checked; " +
        "they contribute no violations, so every count reads low. unsupportedProjects names the declared " +
        "projects this product cannot read at all (C# only): their zero is permanent, not burndown. " +
        "orphans lists baseline sections no rule in the spec names, such as a renamed rule's old section, and " +
        "baseline files that could not be read.";

    private const string ExplainDescription =
        "Return one rule's because, fix, citation, exemplar, posture payload, and linked prose as text. Given a " +
        "type or a path instead of a rule ID, return which layers, rules and scopes claim each type, the step that " +
        "admitted it to each, and the rules whose selection reached it and then left it out: call it when a rule " +
        "fires on a type you did not expect it to govern. Pass exactly one of ruleId, type and path. If projects " +
        "fail to load or to restore, a type or path answer opens with a caveat naming them.";

    private const string ContextDescription =
        "Return the architecture scope card(s) covering a path — a quarantined scope's dragons + sanctioned surface, " +
        "a cautioned scope's dragons, or a layer's local rules — or a pointer line when none apply. If projects " +
        "fail to load or to restore, the answer opens with a caveat naming them: cards from unloaded projects " +
        "cannot be placed, and a rule about a package the restore never fetched was never measured, so treat a " +
        "no-coverage answer as unproven there. A .slnf solution filter gets the same caveat for the declared " +
        "projects it left unchecked: treat a no-coverage answer as unproven under them as well.";

    private const string GraphDescription =
        "Return the JSON codebase survey (schemaVersion 1): projects[] with namespace inventories, " +
        "projectEdges[] (source/target, declared vs observed dependencies), and externalEdges[] grouped by " +
        "namespace root. " +
        "Every project the workspace loaded is here, which is wider than the solution: solutionMember false " +
        "marks a passenger a ProjectReference dragged in, and an absent key means membership was unreadable. " +
        "undeclaredProjectReferences (absent when empty) names the projects a project compiles against that " +
        "no declared projectReferences entry reaches: its declared list is short, so trust its edges. " +
        "projectEdges[] carry only references the code declares: where several projects declare one type, a " +
        "project referencing its own copy is not an edge to the declarer the type was attributed to, nor is a " +
        "reference one copy makes an edge from the others, and multiplyDeclaredTypes[] names those types with " +
        "every declaring project and the one whose facts won — arch.Project() on any declarer selects them, " +
        "with shape and member rules answering from the winner's facts (the key is " +
        "absent when the solution has none). shadowedTypes[] is the other coverage key: a full name a project " +
        "declares that a referenced assembly also supplies means two types, so a rule naming it reaches both " +
        "while arch.Project() over the declaring project reaches only the declaration. " +
        "generated qualifies the types count on a project and on each namespace (absent when zero): how many " +
        "of them a generator emitted, which is what arch.Project() would sweep and .Authored() would drop. A " +
        "namespace whose two counts are equal is wholly generator output — never make it a layer glob. " +
        "targetFrameworks rides every project's row and names what it declares, normalized (a project " +
        "predating the SDK reads net48); factsFollow rides only a multi-targeted one and names the " +
        "first framework its shared types' members and hierarchy came from (a type it does not declare " +
        "takes them from the next that does); their references come from every framework. isPackable, " +
        "locksPackages and packageReferences are the artifact facts, evaluated rather " +
        "than read off the project file, so a value an SDK defaults or a props file above the solution sets " +
        "is the one reported; packageReferences is what the project declares, never the transitive closure, " +
        "and privatePackageReferences (absent when empty) names the ones whose PrivateAssets is all — " +
        "analyzers and build tools that ship to no consumer, which MustReferenceNoPackages passes — and " +
        "packageFrameworks (absent when empty) the ones declared for only some of the project's frameworks, " +
        "with those frameworks. " +
        "Needs no spec — call it before one exists to plan layers and rules. Needs the solution restored, " +
        "not built (the code is read from source): if projects fail to load, or their NuGet packages did " +
        "not resolve, it returns an error naming them rather than a survey missing them or missing their " +
        "external edges. " +
        "Narrow with grain (overview, skeleton or index) or projects (fewer projects); an " +
        "over-budget survey coarsens its own grain, as far as index, rather than being cut — and index lists " +
        "every project name, which is what projects globs match. " +
        "Under a .slnf solution filter, uncheckedProjects names the declared projects the run never loaded — " +
        "a project absent from the survey may simply be out of view. unsupportedProjects is the third " +
        "coverage key and the one that bounds the survey's subject: the declared projects this product " +
        "cannot read at all (C# only), each with its reason, so projects[] shorter than the solution is a " +
        "stated fact rather than something to infer by opening the solution file.";

    [McpServerTool(
        Name = ArchToolNames.Check,
        Title = "Architecture Check",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(CheckDescription)]
    public async Task<string> CheckAsync(
        [Description(
            "Git ref as one string, not an array; files changed since it that fall in a quarantined or "
            + "cautioned scope raise a tripwire warning.")]
        string? diffBase = null,
        [Description(
            "Rule-ID globs as one string, not an array, semicolon-separated ('*' spans '/'). Only matching "
            + "rules run, so rules[] and summary cover that subset alone. Matching no rule is an error "
            + "listing the available IDs.")]
        string? rules = null,
        [Description(
            "One grain as one string, not an array: full (the default), overview, skeleton or index, each coarser "
            + "than the one before and never a narrower subject. overview elides each violation's sites "
            + "(siteCount still says how many); "
            + "skeleton elides the violations too, keeping every rule with its verdict, prose, baseline and "
            + "warnings (violationCount still says how many); index elides the rule prose (sentence, because, "
            + "fix, citation, exemplar and each warning's hint) and the packageAudit and workspaceDiagnostics arrays "
            + "(their counts kept), keeping every "
            + "rule's id, posture, status, baseline, warnings and violationCount and every trust stamp — the "
            + "menu the rules parameter picks from, and arch_explain returns any of these ids whole.")]
        string? grain = null,
        CancellationToken cancellationToken = default)
    {
        // Exit code and error writer deliberately discarded — everything they would carry is in the document.
        // Violations ride in rules[]; which projects failed to load or to restore rides in failedProjects and
        // restoreFailedProjects, the NuGet audit advisories ride in packageAudit, and the load's
        // own diagnostics ride in workspaceDiagnostics along with the MSBuild-selection note, which
        // WorkspaceDiagnostics puts in the list both surfaces read rather than appending at write time
        // (TextWriter.Null would otherwise swallow that one line, and "which MSBuild opened it" is the next
        // question after any load failure). The gate verdict the exit code would have expressed rides in
        // modelIncomplete, and an unmatched --rules filter surfaces as an error result rather than exit 2.
        return await CaptureAsync(output => new CheckRunner(output, TextWriter.Null, source, fitter: fitter)
            .RunAsync(
                binding.CheckRequest(diffBase, rules, DocumentGrains.Parse(grain)),
                cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Status,
        Title = "Architecture Status",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(StatusDescription)]
    public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
    {
        return await CaptureAsync(output => new StatusRunner(output, TextWriter.Null, source)
            .RunAsync(binding.StatusRequest(), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Explain,
        Title = "Architecture Explain",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(ExplainDescription)]
    public async Task<string> ExplainAsync(
        [Description(
            "A post-desugar rule ID as one string, not an array — e.g. layering/domain-independent or "
            + "legacy/billing/containment.")]
        string? ruleId = null,
        [Description(
            "A type to explain instead, as one string, not an array: a full name as arch_check reports it, or a "
            + "simple name, which explains every type wearing it. An unknown name is an error listing the closest "
            + "full names.")]
        string? type = null,
        [Description(
            "A file or directory to explain instead, absolute or solution-relative, as one string, not an array: "
            + "every type declared there is explained. A path declaring no type answers successfully saying so.")]
        string? path = null,
        CancellationToken cancellationToken = default)
    {
        // Error writer deliberately discarded. A rule answer is spec-derived and cannot be made wrong by a load
        // failure; a type or path answer carries its partial-model caveat in the body, as arch_context does.
        return await CaptureAsync(output => new ExplainRunner(output, TextWriter.Null, source)
            .RunAsync(binding.ExplainRequest(ruleId, type, path), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Context,
        Title = "Architecture Context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(ContextDescription)]
    public async Task<string> ContextAsync(
        [Description(
            "A file or directory path (absolute or solution-relative) as one string, not an array, to find "
            + "architecture scope cards for. A path no card covers — including one the codebase does not "
            + "contain yet — answers successfully saying so; a JSON array written as text is an error naming "
            + "that shape rather than a no-coverage answer that was never asked the question.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        // No error writer to discard: context's body is its only channel, so its runner takes stdout alone.
        return await CaptureAsync(output => new ContextRunner(output, source)
            .RunAsync(binding.ContextRequest(path), cancellationToken));
    }

    [McpServerTool(
        Name = ArchToolNames.Graph,
        Title = "Architecture Graph",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Description(GraphDescription)]
    public async Task<string> GraphAsync(
        [Description(
            "Survey the partial model even when some projects fail to load; otherwise the call returns an "
            + "error naming what failed, because a survey missing whole projects is a wrong map, not a smaller one.")]
        bool allowWorkspaceDiagnostics = false,
        [Description(
            "One grain as one string, not an array: full (the default), overview, skeleton or index, each coarser "
            + "than the one before and never a narrower subject. overview elides every project's namespace "
            + "inventory; skeleton also elides each project's packageReferences, privatePackageReferences and "
            + "packageFrameworks, "
            + "and reports the external-reference rows, the multiply-declared types and the shadowed types as "
            + "counts (externalEdgeCount, multiplyDeclaredTypeCount, shadowedTypeCount), keeping every project "
            + "with its targetFrameworks, factsFollow, isPackable, locksPackages, projectReferences, "
            + "undeclaredProjectReferences and type count, and the observed project edges; index also elides each "
            + "project's targetFrameworks, factsFollow, isPackable, locksPackages, projectReferences and "
            + "undeclaredProjectReferences, and reports the observed project "
            + "edges and the packageAudit and workspaceDiagnostics arrays as counts (projectEdgeCount, "
            + "packageAuditCount, workspaceDiagnosticCount), keeping every project's name, solution membership "
            + "and type count and every trust stamp — the menu the projects parameter picks from.")]
        string? grain = null,
        [Description(
            "Project-name globs as one string, not an array, semicolon-separated ('*' allowed). References "
            + "in both directions are kept, so an edge can name a project outside the scope. Matching no "
            + "project is an error listing the available names.")]
        string? projects = null,
        CancellationToken cancellationToken = default)
    {
        // Unlike arch_check and arch_status, the incomplete-model verdict cannot ride this document by
        // default — graph refuses before there is one — so the refusal throws and GlobalCallToolFilter
        // returns it as a clean un-logged error result.
        return await CaptureAsync(output => new GraphRunner(output, TextWriter.Null, source, fitter: fitter)
            .RunAsync(
                binding.GraphRequest(
                    allowWorkspaceDiagnostics, DocumentGrains.Parse(grain), projects),
                cancellationToken));
    }

    // Every tool's shell: give the run a stdout to write into, discard the exit code it returns, and answer
    // with what it wrote. Only BCL types appear here, so a runner is reached solely from inside a tool
    // method's own body, which keeps the MSBuildLocator JIT quarantine intact.
    private static async Task<string> CaptureAsync(Func<TextWriter, Task<int>> run)
    {
        var output = new StringWriter();
        await run(output);
        return output.ToString();
    }
}
