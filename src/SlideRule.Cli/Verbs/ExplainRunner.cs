using SlideRule.Checking;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.SpecLoading;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>explain</c> pipeline in its two forms: a rule ID dumps that rule's fields, and a type name or a path
///     answers who claims the type, or each type the path declares, and why.
/// </summary>
/// <remarks>
///     <para>
///         The rule form: load the model (the DLL fast path needs no workspace; convention or a csproj loads one
///         for resolution only, never extraction) → find the rule by ID → dump its fields
///         (<see cref="ExplainFormatter" />) → exit 0. An unknown ID is a <see cref="UserErrorException" />
///         listing every available (post-desugar) ID, ordinal-sorted → exit 2 — unless the ID is a JSON array
///         written as text, which is named as that instead of rostered (<see cref="Refusals" />). A missing ID
///         argument never reaches here — the surfaces refuse a request that names none of a rule ID, a type and a
///         path, or more than one.
///     </para>
///     <para>
///         The type and path forms: load and extract the codebase, resolve the type by name or the types a path
///         declares, and write each one's claims
///         (<see cref="TypeClaims" />, <see cref="ClaimsFormatter" />). An unknown type name is a
///         <see cref="UserErrorException" /> naming the closest full names; a path declaring no type, and a type
///         nothing claims, answer successfully saying so.
///     </para>
///     <para>
///         <b>Never a gate.</b> A workspace opened for resolution can fail partially; the rule form renders the
///         composed diagnostics to stderr and still answers, because the model it dumps comes from the spec,
///         not the codebase — a load failure cannot make the answer wrong. The DLL fast path never opens a
///         workspace, so it stays silent by construction. The type and path forms answer from the codebase, which
///         a load failure can leave short, so they also announce an incomplete or narrowed model at the top of
///         the answer.
///     </para>
///     <para>
///         <b>It fronts the persisted extraction cache</b> (<c>--no-cache</c> opts out), and the rule form is
///         what the cache does the most for: it never extracts at all, so on a hit the recorded resolution
///         replays and the run opens <em>no workspace</em> — the whole of what a convention or csproj
///         <c>--spec</c> costs. Nothing in that form reads absence as evidence: an unknown rule ID is a refusal
///         against the spec's own rule list, which no cache state can shorten. The type and path forms extract,
///         and front the cache as <c>check</c> does.
///     </para>
/// </remarks>
internal sealed class ExplainRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
    : CacheWiredRunner(source, environment)
{
    // Enough to recognise a near miss by, few enough that a short fragment does not roster the solution.
    private const int ClosestNameLimit = 10;

    public async Task<int> RunAsync(ExplainRequest request, CancellationToken ct)
    {
        if (request.RuleId is not { } ruleId) return await ExplainClaimsAsync(request, ct);

        ArchitectureModel model = await LoadModelAsync(request, ct);

        ArchRule? rule = model.Rules.FirstOrDefault(candidate => candidate.Id == ruleId);
        if (rule is null) throw new UserErrorException(UnknownRuleMessage(ruleId, model));

        foreach (string line in ExplainFormatter.Lines(rule)) await output.WriteLineAsync(line);
        return ExitCodes.Clean;
    }

    private async Task<ArchitectureModel> LoadModelAsync(ExplainRequest request, CancellationToken ct)
    {
        // Fast path: a built-DLL --spec resolves without ever opening the solution. The model still comes
        // through the source, so a warm host serves its cached one rather than reloading the DLL per call.
        SpecResolution? withoutSolution = SpecResolver.TryResolveWithoutSolution(request.Spec);
        if (withoutSolution is not null) return SolutionSource.LoadSpecModel(withoutSolution.DllPath);

        // Convention or csproj --spec: load the workspace for resolution only; never extract. The acquisition
        // seam is the shared one, so explain's model is the model every other verb would have loaded — its
        // lazy ExtractAsync is never called. On a cache hit the recorded resolution replays and no
        // workspace opens either, which is this verb's entire cost.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            request.NoCache, ct);
        RecordCacheOutcome(source);

        // Composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
        // Nothing gates on them here: the rule being dumped is the spec's, so a project that failed to load
        // cannot change the answer — only the visibility of the failure.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics);

        return source.Model;
    }

    // The type and path forms. Unlike the rule form this extracts, because who claims a type is a question about
    // the codebase as much as the spec, so it fronts the cache exactly as check does and inherits check's model.
    // Never a gate, for context's reason: an incomplete or narrowed model is announced in the body, which is
    // arch_explain's only channel, ahead of an answer the partial model still supports.
    private async Task<int> ExplainClaimsAsync(ExplainRequest request, CancellationToken ct)
    {
        RefuseAStringifiedArray(request);

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            request.NoCache, ct);

        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics);
        IncompleteModelNotices.Caveat(output, diagnostics, IncompleteModelGate.ExplainCaveat);

        NarrowingNotices.Stamp(output, source, NarrowedUniverseNotice.ExplainStamp);

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);
        RecordCacheOutcome(source);

        IReadOnlyList<TypeNode> types = request.Type is { } name
            ? NamedTypes(codebase, name, diagnostics.IsIncomplete)
            : DeclaredUnder(codebase, ContextRunner.ResolveQueryPath(request.Path!, source.SolutionDirectory));

        if (types.Count == 0)
        {
            await output.WriteLineAsync($"No type the solution declares is in '{request.Path}'.");
            return ExitCodes.Clean;
        }

        var tracer = new ClaimTracer(codebase, new SolutionRoot(source.SolutionDirectory));
        var first = true;
        foreach (TypeNode type in types)
        {
            if (!first) await output.WriteLineAsync();
            first = false;

            TypeClaims claims = TypeClaims.Of(source.Model, tracer, type);
            foreach (string line in ClaimsFormatter.Lines(claims, source.SolutionDirectory))
                await output.WriteLineAsync(line);
        }

        return ExitCodes.Clean;
    }

    // Ahead of the load, as context refuses its path, so a bracketed value comes back named as the shape it is
    // rather than as an unknown type or a path declaring nothing, a proven negative to a question never asked.
    private static void RefuseAStringifiedArray(ExplainRequest request)
    {
        (string value, string lead, string advice) = request.Type is { } type
            ? (type, $"Cannot explain type '{type}'", "pass one type name")
            : (request.Path!, $"Cannot explain path '{request.Path}'", "pass one path");
        if (Refusals.SingleValueRefusal(value, lead, advice) is { } refusal) throw new UserErrorException(refusal);
    }

    // By full name first, the spelling check reports; then by simple name, every match, since a simple name is
    // what an author writes in a Named list. A type the solution declares outranks an external one of the same
    // full name, and an external is still an answer, because it tells the reader the type is outside every subject.
    private static IReadOnlyList<TypeNode> NamedTypes(CodebaseModel codebase, string name, bool incomplete)
    {
        List<TypeNode> byFullName = codebase.Types.Where(type => type.FullName == name).ToList();
        List<TypeNode> declared = byFullName.Where(type => !type.IsExternal).ToList();
        if (declared.Count > 0) return declared;
        if (byFullName.Count > 0) return byFullName;

        List<TypeNode> bySimpleName = codebase.Types
            .Where(type => !type.IsExternal && type.Name == name)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
        if (bySimpleName.Count > 0) return bySimpleName;

        throw new UserErrorException(UnknownTypeMessage(codebase, name, incomplete));
    }

    private static string UnknownTypeMessage(CodebaseModel codebase, string name, bool incomplete)
    {
        string lead = incomplete
            ? $"Unknown type '{name}' (the model is incomplete, so a project that failed to load may declare it)"
            : $"Unknown type '{name}'";

        List<string> closest = codebase.Types
            .Where(type => !type.IsExternal && type.FullName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fullName => fullName, StringComparer.Ordinal)
            .Take(ClosestNameLimit)
            .ToList();

        return closest.Count > 0
            ? Refusals.NotFoundMessage(lead, "Closest full names", closest)
            : $"{lead}. Pass a type's full name, as check reports it, or its simple name.";
    }

    // Every type the solution declares with a declaration site at or under the path: one for a file, several for
    // a file holding several types or for a directory. The path is canonicalized as the declaration sites are, so
    // a spelling through a symlinked root still finds them.
    private static IReadOnlyList<TypeNode> DeclaredUnder(CodebaseModel codebase, string queryFullPath)
    {
        string[] querySegments = PathFormat.Segments(queryFullPath);
        return codebase.Types
            .Where(type => !type.IsExternal)
            .Where(type => type.DeclarationSites.Any(site =>
                PathFormat.IsWithin(querySegments, PathFormat.Segments(site.FilePath))))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ThenBy(type => type.ProjectName, StringComparer.Ordinal)
            .ToList();
    }

    // A JSON array a client wrote into the string parameter is answered on its shape rather than with the
    // roster: every ID the reader could want is inside their own brackets already.
    private static string UnknownRuleMessage(string ruleId, ArchitectureModel model)
    {
        var lead = $"Unknown rule ID '{ruleId}'";

        return Refusals.SingleValueRefusal(ruleId, lead, "pass one rule ID")
               ?? Refusals.RuleNotFound(lead, model);
    }
}
