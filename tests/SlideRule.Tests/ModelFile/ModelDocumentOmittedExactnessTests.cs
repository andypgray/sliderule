using Shouldly;
using Xunit;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.Stubs;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Holds the omitted list to being exactly right rather than merely non-empty: for every real spec
///     this assembly can build, and for the shapes written to be refused, the rules a model file leaves
///     behind are the ones an independent walk over the model says cannot be data.
/// </summary>
/// <remarks>
///     <para>
///         The walk is <see cref="ModelNodeWalk" />, which reads the model and never the writer. Two
///         halves of one claim need it: that nothing carriable is quietly dropped — a file that silently
///         enforced less than the spec is the failure this format exists to avoid — and that nothing
///         un-carriable is carried, which would be a rule the reader mints wrong or refuses.
///     </para>
///     <para>
///         The rule for an anchor is its <em>position</em>, not its spelling: a position that matches on a
///         definition refuses a closed generic, and one that matches on a construction admits it. Both
///         refuse a type with no source-level name.
///     </para>
/// </remarks>
public sealed class ModelDocumentOmittedExactnessTests
{
    /// <summary>
    ///     The anchor slots the checker resolves through the definition-level path, read off the evaluator
    ///     rather than off the writer: the type noun
    ///     (<c>SelectionEvaluator.TypeNounFullName</c>), a member selection's <c>.Returning</c> set
    ///     (<c>MemberSelectionEvaluator.ReturningAnchors</c>), a banned member's declaring type
    ///     (<c>ConstraintEvaluator.ForbiddenMemberUse</c>) and the parameter type
    ///     <c>MustAcceptParameter</c> matches on (<c>ConstraintEvaluator</c>'s member-verb switch). Every
    ///     one of the four reaches <c>SelectionEvaluator.DefinitionFullName</c>.
    /// </summary>
    /// <remarks>
    ///     Every other anchor slot goes through <c>SelectionEvaluator.AnchorKey</c> and so matches on the
    ///     construction. To extend this list, find the new slot's evaluator arm and ask which of those two
    ///     helpers it resolves the anchor through — the name of the slot says nothing, and the writer is
    ///     the thing under test here and so cannot be consulted.
    /// </remarks>
    private static readonly (Type Owner, string Property)[] DefinitionLevelAnchors =
    [
        (typeof(TypeNoun), nameof(TypeNoun.Anchor)),
        (typeof(ReturningAdjective), nameof(ReturningAdjective.Anchors)),
        (typeof(Member), nameof(Member.DeclaringAnchor)),
        (typeof(MemberMustAcceptParameterConstraint), nameof(MemberMustAcceptParameterConstraint.Anchor))
    ];

    private static readonly (string Shape, Action<Arch> Define)[] Shapes =
    [
        ("closed-generic type noun", arch => arch.Rule("refusing/closed-generic-noun")
            .Enforce(arch.Type(typeof(IHandler<string>))
                .MustHaveSuffix("Handler"))
            .Because("b")),
        ("unrepresentable operand", arch => arch.Rule("refusing/unrepresentable-operand")
            .Enforce(arch.Namespace("Refusing.*")
                .MustNotReference(typeof(int*)))
            .Because("b")),
        ("unrepresentable hierarchy adjective", arch => arch.Rule("refusing/unrepresentable-adjective")
            .Enforce(arch.Types.Implementing(typeof(int*))
                .MustBeSealed())
            .Because("b")),
        ("predicate inside an Except payload", arch => arch.Rule("refusing/predicate-in-except")
            .Enforce(arch.Types.Except(arch.Types.Where(t => t.IsSealed, description: "that are sealed"))
                .MustBeSealed())
            .Because("b")),
        ("predicate inside a union part", arch => arch.Rule("refusing/predicate-in-union")
            .Enforce(arch.AnyOf(
                    arch.Namespace("Refusing.*"),
                    arch.Types.Where(t => t.IsSealed, description: "that are sealed"))
                .MustBeSealed())
            .Because("b")),
        ("predicate on a member subject", arch => arch.Rule("refusing/predicate-on-members")
            .Enforce(arch.Types.Methods.Where(m => m.IsStatic, description: "that are static")
                .MustBePublic())
            .Because("b")),
        ("predicate on a project subject", arch => arch.Rule("refusing/predicate-on-projects")
            .Enforce(arch.Projects.Where(p => p.IsPackable == true, description: "that are packable")
                .MustLockPackages())
            .Because("b")),
        ("layer defined by a predicate", arch =>
        {
            Layer odd = arch.Layer("Odd", arch.Types.Where(t => t.IsSealed, description: "that are sealed"));
            arch.Rule("refusing/names-an-odd-layer")
                .Enforce(odd.MustBeSealed())
                .Because("b");
        }),
        ("family cell defined by a predicate", arch =>
        {
            Layer odd = arch.Layer("Odd", arch.Types.Where(t => t.IsSealed, description: "that are sealed"));
            Layer plain = arch.Layer("Plain", "Refusing.Plain.*");
            arch.Rule("refusing/names-an-odd-cell")
                .Enforce(arch.Each(odd, plain)
                    .MustNotReferenceEachOther())
                .Because("b");
        })
    ];

    public static TheoryData<string> RefusingShapes => [.. Shapes.Select(row => row.Shape)];

    [Fact]
    public void From_EveryCorpusSpec_OmitsExactlyTheRulesAnIndependentWalkCannotCarry()
    {
        // Act
        List<string> drift = ModelFileCorpus.All
            .SelectMany(spec => Drift(spec.Name, spec.Model))
            .ToList();

        // Assert
        drift.ShouldReportNothing("The omitted list and an independent walk over the model disagree");
    }

    [Theory]
    [MemberData(nameof(RefusingShapes))]
    public void From_ASpecBuiltToBeRefused_OmitsExactlyWhatTheWalkCannotCarry(string shape)
    {
        // Arrange
        Action<Arch> define = Shapes.Single(row => string.Equals(row.Shape, shape, StringComparison.Ordinal))
            .Define;
        ArchitectureModel model = Checker.Model(define);

        // Act
        List<string> drift = Drift(shape, model)
            .ToList();

        // Assert: the rule really is left behind — a shape that stopped refusing would agree with the walk
        // trivially and prove nothing.
        ModelDocument.From(model, shape, ModelFileCorpus.Projects)
            .Omitted.ShouldNotBeEmpty();
        drift.ShouldReportNothing($"'{shape}' is partitioned differently by the writer and by the walk");
    }

    [Fact]
    public void From_ThisRepositorysOwnSpec_OmitsExactlyTheTwoWheres()
    {
        // Arrange
        const string predicateAdjective = "A Where predicate is code, and this file carries data only.";

        // Act
        ModelDocument document = ModelDocument.From(
            SelfSpecTests.SelfModel, "SlideRule.Architecture", ModelFileCorpus.Projects);

        // Assert: in the order the spec declares them, each with the whole sentence a reader of the file
        // gets. Each narrows its subject by a Where before the verb, so that is what the file reports.
        // All four front doors name their lists as data and are carried: api/extraction-front-door
        // subtracts the private nested types it has to leave out with ThatArePrivate, not a predicate.
        document.Omitted.Select(entry => (entry.Id, entry.Reason))
            .ShouldBe(
            [
                ("state/no-static-mutable", predicateAdjective),
                ("naming/async-suffix", predicateAdjective)
            ]);
    }

    // Where the writer's partition and the walk's disagree, as one line per rule.
    private static IEnumerable<string> Drift(string specName, ArchitectureModel model)
    {
        ModelDocument document = ModelDocument.From(model, specName, ModelFileCorpus.Projects);
        List<string> omitted = document.Omitted.Select(entry => entry.Id)
            .ToList();
        List<string> unwalkable = model.Rules.Where(rule => !IsCarriable(rule))
            .Select(rule => rule.Id)
            .ToList();

        if (omitted.SequenceEqual(unwalkable, StringComparer.Ordinal)) yield break;

        yield return $"{specName}: the file omits [{string.Join(", ", omitted)}]; "
                     + $"the walk says [{string.Join(", ", unwalkable)}] cannot be data.";
    }

    // A rule is data exactly when nothing it reaches is code and every anchor it reaches is usable in the
    // position it sits in.
    private static bool IsCarriable(ArchRule rule)
    {
        ModelNodeWalk.Walked walked = ModelNodeWalk.From(rule);
        if (walked.HoldsAPredicate) return false;

        return walked.Anchors.All(IsUsable);
    }

    private static bool IsUsable(ModelNodeWalk.WalkedAnchor walked)
    {
        if (DefinitionLevelAnchors.Contains((walked.Owner, walked.Property)))
            return walked.Anchor.TryDefinitionKey(out _, out _);

        return walked.Anchor.TryConstructionKey(out _, out _, out _);
    }
}
