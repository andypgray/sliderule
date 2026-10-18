using Shouldly;
using SlideRule.Fluent;
using Xunit;

// Each null argument is cast to the parameter type of the overload its guard is tested through, including
// where overload resolution would pick that overload without the cast.
// ReSharper disable RedundantCast

namespace SlideRule.Tests;

/// <summary>
///     The argument guards on the authoring surface: the nouns and anchors on <see cref="Arch" />, the
///     operand lists behind every <c>(first, params more)</c> verb, the trailers each posture stage
///     carries, and the enum-valued arguments. A null argument, a null <c>params</c> array and an
///     undefined enum value are programmer errors and throw at the call naming the parameter the caller
///     actually wrote; a blank or malformed string is a spec-author mistake and is collected instead, so
///     the whole spec reports at once when it is loaded (GRAMMAR §8). Two tests pin that split from the
///     other side — the blank that must not throw, and the one undefined enum value the catalog keeps.
/// </summary>
public sealed class AuthoringGuardTests
{
    private static readonly Arch Arch = new();

    [Fact]
    public void Namespace_NullGlob_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Namespace(null!))
            .ParamName.ShouldBe("glob");
    }

    [Fact]
    public void Project_NullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Project(null!))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Type_NullType_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Type(null!))
            .ParamName.ShouldBe("type");
    }

    [Fact]
    public void Rule_NullId_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Rule(null!))
            .ParamName.ShouldBe("id");
    }

    [Fact]
    public void Scope_NullId_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Scope(null!))
            .ParamName.ShouldBe("id");
    }

    [Fact]
    public void Member_NullType_NamesTheParameterTheCallerWrote()
    {
        // The cast picks the arm: a bare `null!` is ambiguous between the typeof and expression overloads.
        // The claim is the NAME — the Member constructor spells this argument `declaringType`, which is not
        // a parameter of anything the caller can call.
        Should.Throw<ArgumentNullException>(() => Arch.Member((Type)null!, "Now"))
            .ParamName.ShouldBe("type");
    }

    [Fact]
    public void Member_NullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Member(typeof(DateTime), null!))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Layer_NullName_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Layer(null!, "MyApp.*"))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Layer_NullNameOnTheSelectionForm_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Layer(null!, Arch.Types))
            .ParamName.ShouldBe("name");
    }

    [Fact]
    public void Layer_NullDefinition_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Layer("Domain", (Selection)null!))
            .ParamName.ShouldBe("definition");
    }

    [Fact]
    public void Layer_NullGlob_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Layer("Domain", (string)null!))
            .ParamName.ShouldBe("glob");
    }

    [Fact]
    public void Layer_NullGlobInsideMore_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Layer("Domain", "MyApp.*", [null!]))
            .ParamName.ShouldBe("more");
    }

    [Fact]
    public void AnyOf_NullFirst_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.AnyOf((Selection)null!))
            .ParamName.ShouldBe("first");
    }

    [Fact]
    public void Named_NullNameInsideMore_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Named("Order", [null!]))
            .ParamName.ShouldBe("more");
    }

    [Fact]
    public void ProjectsMatching_NullGlob_NamesTheParameterTheCallerWrote()
    {
        // The glob is handed to OperandList as its `first`, together with this overload's name for it: without
        // that the caller would be told about a parameter named `first` that this overload does not have.
        Should.Throw<ArgumentNullException>(() => Arch.Projects.Matching((string)null!))
            .ParamName.ShouldBe("glob");
    }

    [Fact]
    public void Purpose_NullProse_ThrowsArgumentNullException()
    {
        Layer domain = Arch.Layer("PurposeGuard", "MyApp.*");

        Should.Throw<ArgumentNullException>(() => domain.Purpose(null!))
            .ParamName.ShouldBe("prose");
    }

    [Fact]
    public void Because_NullProse_ThrowsArgumentNullException()
    {
        IEnforceRule rule = Arch.Rule("guard/because").Enforce(Arch.Types.MustBeSealed());

        Should.Throw<ArgumentNullException>(() => rule.Because(null!))
            .ParamName.ShouldBe("because");
    }

    [Fact]
    public void Fix_NullProse_ThrowsArgumentNullException()
    {
        IEnforceRule rule = Arch.Rule("guard/fix").Enforce(Arch.Types.MustBeSealed());

        Should.Throw<ArgumentNullException>(() => rule.Fix(null!))
            .ParamName.ShouldBe("fix");
    }

    [Fact]
    public void Citation_NullUri_ThrowsArgumentNullException()
    {
        IEnforceRule rule = Arch.Rule("guard/citation").Enforce(Arch.Types.MustBeSealed());

        Should.Throw<ArgumentNullException>(() => rule.Citation(null!))
            .ParamName.ShouldBe("uri");
    }

    [Fact]
    public void Exemplar_NullSelection_ThrowsArgumentNullException()
    {
        IEnforceRule enforce = Arch.Rule("guard/exemplar").Enforce(Arch.Types.MustBeSealed());
        IMigrateRule migrate = Arch.Rule("guard/migrate-exemplar").Migrate("Some debt.", Arch.Types.MustBeSealed());

        enforce.ShouldSatisfyAllConditions(
            () => Should.Throw<ArgumentNullException>(() => enforce.Exemplar(null!))
                .ParamName.ShouldBe("exemplar"),
            () => Should.Throw<ArgumentNullException>(() => migrate.Exemplar(null!))
                .ParamName.ShouldBe("exemplar"));
    }

    [Fact]
    public void Migrate_NullFrom_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Rule("guard/migrate").Migrate(null!, Arch.Types.MustBeSealed()))
            .ParamName.ShouldBe("from");
    }

    [Fact]
    public void Baseline_NullPath_ThrowsArgumentNullException()
    {
        IMigrateRule rule = Arch.Rule("guard/baseline").Migrate("Some debt.", Arch.Types.MustBeSealed());

        Should.Throw<ArgumentNullException>(() => rule.Baseline(null!))
            .ParamName.ShouldBe("path");
    }

    [Fact]
    public void Dragons_NullProse_ThrowsArgumentNullException()
    {
        ICautionedScope scope = Arch.Scope("guard/dragons").Caution(Arch.Types);

        Should.Throw<ArgumentNullException>(() => scope.Dragons(null!))
            .ParamName.ShouldBe("prose");
    }

    [Fact]
    public void DragonsDoc_NullPath_ThrowsArgumentNullException()
    {
        ICautionedScope scope = Arch.Scope("guard/dragons-doc").Caution(Arch.Types);

        Should.Throw<ArgumentNullException>(() => scope.DragonsDoc(null!))
            .ParamName.ShouldBe("path");
    }

    [Fact]
    public void Where_NullDescription_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Where(type => type.IsRecord, null!))
            .ParamName.ShouldBe("description");
    }

    [Fact]
    public void Must_NullDescription_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Arch.Types.Must(type => type.IsRecord, null!))
            .ParamName.ShouldBe("description");
    }

    [Fact]
    public void OfKind_UndefinedKind_ThrowsArgumentOutOfRangeException()
    {
        var thrown =
            Should.Throw<ArgumentOutOfRangeException>(() => Arch.Types.OfKind((TypeKind)9));

        thrown.ParamName.ShouldBe("kind");
        thrown.Message.ShouldContain("'(TypeKind)9' is not a defined TypeKind");
        thrown.Message.ShouldContain(
            "the defined values are TypeKind.Class, TypeKind.Interface, TypeKind.Struct, TypeKind.Enum, "
            + "TypeKind.Delegate.");
    }

    [Fact]
    public void WhileYoureThere_UndefinedPolicy_ThrowsArgumentOutOfRangeException()
    {
        IMigrateRule rule = Arch.Rule("guard/policy").Migrate("Some debt.", Arch.Types.MustBeSealed());

        var thrown =
            Should.Throw<ArgumentOutOfRangeException>(() => rule.WhileYoureThere((MigrationPolicy)9));

        thrown.ParamName.ShouldBe("policy");
        thrown.Message.ShouldContain("'(MigrationPolicy)9' is not a defined MigrationPolicy");
        thrown.Message.ShouldContain(
            "the defined values are MigrationPolicy.MigrateIfSmall, MigrationPolicy.AlwaysMigrate, "
            + "MigrationPolicy.NeverMigrate.");
    }

    [Fact]
    public void BlankArguments_AreCollectedByTheCatalog_NotThrownAtTheCall()
    {
        // The other half of the split, and the reason none of the guards above reaches for
        // NotNullOrWhiteSpace: a blank is an authoring mistake, and reporting it here would take it out of
        // the one all-errors-at-once report that carries its file:line.
        Should.NotThrow(() => Arch.Namespace(""));
        Should.NotThrow(() => Arch.Project("   "));
        Should.NotThrow(() => Arch.Rule("").Enforce(Arch.Types.MustBeSealed()).Because(" "));
    }

    [Fact]
    public void UndefinedLifetime_IsCollectedByTheCatalog_NotThrownAtTheCall()
    {
        // The recorded asymmetry with OfKind and WhileYoureThere above: the catalog's message for this one
        // also names the rule that used the value, which a throw at this call could not. Guard's own remarks
        // say so; this is the pin that keeps someone from tidying the three into agreement.
        Should.NotThrow(() => Arch.Registered((Lifetime)7));
    }
}
