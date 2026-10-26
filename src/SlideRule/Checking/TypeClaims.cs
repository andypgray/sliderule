using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Checking;

/// <summary>
///     What a claimant is: a declared layer, a rule through its subject, or a scope through its scoped selection.
/// </summary>
internal enum ClaimantKind
{
    Layer,
    Rule,
    Scope
}

/// <summary>One layer, rule or scope, and how its selection stands toward the type being explained.</summary>
internal sealed class Claimant
{
    internal Claimant(ClaimantKind kind, string name, string? subject, ClaimTrace trace)
    {
        Kind = kind;
        Name = name;
        Subject = subject;
        Trace = trace;
    }

    internal ClaimantKind Kind { get; }

    // The layer's name, the rule's ID, or the scope's ID with its posture, as the answer lists it.
    internal string Name { get; }

    // The selection as the spec's sentence reads it; null for a layer, whose name is the selection.
    internal string? Subject { get; }

    internal ClaimTrace Trace { get; }
}

/// <summary>
///     Every layer, rule and scope that claims one type, each with the trace that admitted it, and the layers,
///     rules and scopes whose selection reached the type and then excepted it.
/// </summary>
/// <remarks>
///     A rule is a claimant through its constraint's type subject, so a member rule is listed by the types
///     whose members it reads, and a project rule, which has no type subject, is never listed. A scope is
///     judged on its scoped selection; its containment rule is a rule like any other and is listed on its own
///     subject, which leaves the sanctioned surface out. A tripwire carries no constraint and is answered by
///     its scope.
/// </remarks>
internal sealed class TypeClaims
{
    private TypeClaims(
        TypeNode type, IReadOnlyList<Claimant> layers, IReadOnlyList<Claimant> rules, IReadOnlyList<Claimant> scopes,
        IReadOnlyList<Claimant> exceptedBy, IReadOnlyList<Claimant> unevaluated)
    {
        Type = type;
        Layers = layers;
        Rules = rules;
        Scopes = scopes;
        ExceptedBy = exceptedBy;
        Unevaluated = unevaluated;
    }

    internal TypeNode Type { get; }

    internal IReadOnlyList<Claimant> Layers { get; }

    internal IReadOnlyList<Claimant> Rules { get; }

    internal IReadOnlyList<Claimant> Scopes { get; }

    // Layers, rules and scopes in spec order whose selection's head admitted the type and whose own Except then
    // removed it. A type another adjective drops is not listed: under an arch.Types head that is nearly every
    // rule, and the removal an author did not expect is an Except's.
    internal IReadOnlyList<Claimant> ExceptedBy { get; }

    // Layers, rules and scopes whose selection could not be evaluated, each trace holding the reason, exactly as a
    // check would report the rule as a rule error.
    internal IReadOnlyList<Claimant> Unevaluated { get; }

    internal bool ClaimedByAnything => Layers.Count > 0 || Rules.Count > 0 || Scopes.Count > 0;

    /// <summary>
    ///     The claims on <paramref name="type" />, in the order the spec declares its layers and rules. An
    ///     external type is claimed by nothing, since every subject ranges over the types the solution declares.
    /// </summary>
    internal static TypeClaims Of(ArchitectureModel model, ClaimTracer tracer, TypeNode type)
    {
        var layers = new List<Claimant>();
        var rules = new List<Claimant>();
        var scopes = new List<Claimant>();
        var exceptedBy = new List<Claimant>();
        var unevaluated = new List<Claimant>();
        if (type.IsExternal) return new TypeClaims(type, layers, rules, scopes, exceptedBy, unevaluated);

        foreach (LayerDefinition layer in model.Layers)
        {
            ClaimTrace layerTrace = tracer.Trace(layer, type);
            ListFor(layerTrace, layers)?.Add(new Claimant(ClaimantKind.Layer, layer.Name, null, layerTrace));
        }

        var seenScopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Scope is { } scope && seenScopes.Add(scope.ScopeId))
            {
                ClaimTrace scopeTrace = tracer.Trace(scope.Scoped, type);
                if (ListFor(scopeTrace, scopes) is { } scopeList)
                {
                    var name = $"{scope.ScopeId} ({rule.Posture.Word()})";
                    string scoped = SentenceRenderer.Subject(scope.Scoped);
                    scopeList.Add(new Claimant(ClaimantKind.Scope, name, scoped, scopeTrace));
                }
            }

            if (rule.Constraint?.Subject is not { } subject) continue;

            ClaimTrace ruleTrace = tracer.Trace(subject, type);
            if (ListFor(ruleTrace, rules) is not { } ruleList) continue;

            string phrase = SentenceRenderer.SubjectOf(rule.Constraint).Subject;
            ruleList.Add(new Claimant(ClaimantKind.Rule, rule.Id, phrase, ruleTrace));
        }

        return new TypeClaims(type, layers, rules, scopes, exceptedBy, unevaluated);

        // The list a claimant with this trace belongs in, its own kind's when claimed, or null for a verdict the answer
        // leaves out, decided before any of the claimant's text is composed.
        List<Claimant>? ListFor(ClaimTrace trace, List<Claimant> claimed)
        {
            return trace.Verdict switch
            {
                ClaimVerdict.Claimed => claimed,
                ClaimVerdict.Excepted => exceptedBy,
                ClaimVerdict.Failed => unevaluated,
                _ => null
            };
        }
    }
}
