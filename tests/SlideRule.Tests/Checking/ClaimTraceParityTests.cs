using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The claim trace and the checker agree on every type: a trace claims a type exactly when
///     <see cref="SelectionEvaluator.Evaluate" /> names it, for every rule subject, rule operand, scope and layer of
///     the canonical sample over the MyApp fixture and of this repository's own spec over this repository. An
///     operand is compared in the position <see cref="OperandRoles" /> gives it, over external types as well.
/// </summary>
/// <remarks>
///     The trace asks the evaluator each noun and adjective of one type and restates only the compound fold, so
///     this is the gate on the fold. An adjective needs none: the trace asks it through the per-type test a
///     narrowing filters with.
/// </remarks>
public sealed class ClaimTraceParityTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    [Fact]
    public void TheCanonicalSample_OverMyApp_TracesWhatItEvaluates()
    {
        ShouldTraceWhatItEvaluates(Checker.Canonical, fixture.Model);
    }

    [Fact]
    public async Task TheSelfSpec_OverThisRepository_TracesWhatItEvaluates()
    {
        CodebaseModel codebase = await SelfSpecTests.WholeCodebase.Value;

        ShouldTraceWhatItEvaluates(SelfSpecTests.SelfModel, codebase);
    }

    [Fact]
    public void AFolderSpec_OverOneNamespaceInFolders_TracesWhatItEvaluates()
    {
        // The source-location adjective reads files through the run's root, so the trace and the evaluator are
        // handed the same one.
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("folders/widgets")
                .Enforce(arch.Types.Under("src/Widgets", "vendor").MustBeSealed())
                .Because("b");
            arch.Rule("folders/console")
                .Enforce(arch.Types.Under("src").Except(arch.Types.Under("src/Console/Internal")).MustBeSealed())
                .Because("b");
        });

        ShouldTraceWhatItEvaluates(model, Sources.FolderModel, Sources.FolderRoot);
    }

    private static void ShouldTraceWhatItEvaluates(
        ArchitectureModel model, CodebaseModel codebase, SolutionRoot? root = null)
    {
        var evaluator = new SelectionEvaluator(codebase, root);
        var tracer = new ClaimTracer(codebase, root);
        List<TypeNode> declared = codebase.Types.Where(type => !type.IsExternal).ToList();
        var disagreements = new List<string>();
        var compared = 0;

        foreach ((string name, Func<TypeNode, ClaimTrace> trace, HashSet<TypeNode> members, SelectionPosition position) in
                 Subjects(model, evaluator, tracer))
        {
            compared++;
            IReadOnlyList<TypeNode> universe = position == SelectionPosition.Target ? codebase.Types : declared;
            foreach (TypeNode type in universe)
            {
                bool traced = trace(type).Verdict == ClaimVerdict.Claimed;
                if (traced != members.Contains(type))
                    disagreements.Add($"{name} on {type.FullName}: traced {traced}, evaluated {!traced}");
            }
        }

        compared.ShouldBeGreaterThan(0);
        disagreements.ShouldBeEmpty();
    }

    // Every selection the explain answer traces, beside what the checker's evaluator names for it. A subject the
    // evaluator refuses is skipped, as the answer reports it under "not evaluated" rather than tracing it.
    private static IEnumerable<(string Name, Func<TypeNode, ClaimTrace> Trace, HashSet<TypeNode> Members,
        SelectionPosition Position)> Subjects(ArchitectureModel model, SelectionEvaluator evaluator, ClaimTracer tracer)
    {
        const SelectionPosition subjectPosition = SelectionPosition.Subject;
        foreach (LayerDefinition layer in model.Layers)
        {
            HashSet<TypeNode> members = LayerMembers(layer, evaluator);
            yield return ($"layer {layer.Name}", type => tracer.Trace(layer, type), members, subjectPosition);
        }

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Scope?.Scoped is { } scoped && Evaluated(evaluator, scoped, subjectPosition) is { } scopedMembers)
                yield return ($"scope of {rule.Id}", type => tracer.Trace(scoped, type), scopedMembers, subjectPosition);

            if (rule.Constraint is not { } constraint) continue;

            if (constraint.Subject is { } subject && Evaluated(evaluator, subject, subjectPosition) is { } subjectMembers)
                yield return ($"rule {rule.Id}", type => tracer.Trace(subject, type), subjectMembers, subjectPosition);

            IReadOnlyList<OperandRole> operands = OperandRoles.Of(constraint);
            for (var i = 0; i < operands.Count; i++)
            {
                OperandRole operand = operands[i];
                if (Evaluated(evaluator, operand.Operand, operand.Position) is not { } operandMembers) continue;

                yield return ($"operand {i} of {rule.Id}", type => tracer.Trace(operand.Operand, type, operand.Position),
                    operandMembers, operand.Position);
            }
        }
    }

    // A glob layer has no selection to evaluate, so its members are the namespace scan its noun performs.
    private static HashSet<TypeNode> LayerMembers(LayerDefinition layer, SelectionEvaluator evaluator)
    {
        if (layer.Definition is { } definition) return evaluator.Evaluate(definition, SelectionPosition.Subject);

        List<NamespacePattern> globs = layer.Globs.Select(glob => new NamespacePattern(glob)).ToList();
        return evaluator.Evaluate(new Arch().Types, SelectionPosition.Subject)
            .Where(type => globs.Any(glob => glob.Matches(type.Namespace)))
            .ToHashSet();
    }

    private static HashSet<TypeNode>? Evaluated(
        SelectionEvaluator evaluator, Selection selection, SelectionPosition position)
    {
        try
        {
            return evaluator.Evaluate(selection, position);
        }
        catch (RuleEvaluationException)
        {
            return null;
        }
    }
}
