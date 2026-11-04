using System.Reflection;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     <see cref="OperandRoles" /> against the checker itself: for every constraint kind that carries selection
///     operands, the role and position the table gives an operand are the ones the checker's dead-operand warning
///     reports for it.
/// </summary>
public sealed class OperandRolesTests
{
    private const string Shop = """
                                namespace Shop.Domain
                                {
                                    [System.Serializable] public class Order {}
                                }
                                namespace Shop.Web
                                {
                                    public class HomeController { private Shop.Domain.Order _order = new(); }
                                }
                                """;

    private static readonly CodebaseModel ShopModel = CompilationFactory.Extract(Shop);

    // The injection verb never reports a dead operand: an empty Registered operand means no such registration
    // exists, which is the rule holding. Its rows skip, and the fact below holds it to reporting none.
    private static readonly string[] NeverWarns = [nameof(MustNotInjectConstraint)];

    // Every concrete kind with a selection-operand list, found rather than listed, so a new verb adds a row.
    private static readonly IReadOnlyList<Type> Kinds = typeof(OperandConstraint).Assembly.GetTypes()
        .Where(type => type is { IsAbstract: false } && type.IsSubclassOf(typeof(OperandConstraint)))
        .ToArray();

    // Each kind by name, so a row reads as the kind it constructs.
    public static TheoryData<string> OperandKinds()
    {
        IEnumerable<string> names = Kinds
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);
        return new TheoryData<string>(names);
    }

    [Fact]
    public void TheVerbsListedAsNeverWarning_ReportNoDeadOperand()
    {
        var arch = new Arch();
        foreach (string kind in NeverWarns)
        {
            Constraint constraint = Construct(
                kind, arch.Namespace("Shop.Web"), [arch.Namespace("Shop.Domain"), arch.Namespace("Nowhere")]);

            DeadOperandWarnings(constraint).ShouldBeEmpty($"{kind} is listed as never warning, so it should report no dead operand");
        }
    }

    [Theory]
    [MemberData(nameof(OperandKinds))]
    public void ADeadOperand_IsReportedInTheRoleTheTableGivesIt(string kind)
    {
        // Arrange: a live operand beside one that names nothing, so the checker reports the dead one by itself.
        var arch = new Arch();
        SkipWhereTheVerbNeverWarns(kind);
        Constraint constraint = Construct(kind, arch.Namespace("Shop.Web"), [arch.Namespace("Shop.Domain"), arch.Namespace("Nowhere")]);

        // Act
        OperandRole dead = OperandRoles.Of(constraint)[1];
        CheckWarning warning = ShouldReportOneDeadOperand(constraint);

        // Assert
        warning.Role.ShouldBe(dead.Role, $"{kind}: the checker's dead-operand warning names the operand's role");
    }

    [Theory]
    [MemberData(nameof(OperandKinds))]
    public void AnOperandNamingOnlyAnExternalType_IsDeadExactlyWhereTheTableResolvesItInSubjectPosition(string kind)
    {
        // Arrange: `System` holds only types the solution references, which target position admits and subject
        // position never does.
        var arch = new Arch();
        SkipWhereTheVerbNeverWarns(kind);
        Constraint constraint = Construct(kind, arch.Namespace("Shop.Web"), [arch.Namespace("Shop.Domain"), arch.Namespace("System")]);

        // Act
        OperandRole external = OperandRoles.Of(constraint)[1];
        bool reportedDead = DeadOperandWarnings(constraint).Count > 0;

        // Assert
        reportedDead.ShouldBe(
            external.Position == SelectionPosition.Subject,
            $"{kind}: the table resolves the operand in {external.Position} position");
    }

    private static void SkipWhereTheVerbNeverWarns(string kind)
    {
        Assert.SkipWhen(
            NeverWarns.Contains(kind, StringComparer.Ordinal),
            $"{kind} never reports a dead operand, so its warnings cannot show the role its operands play.");
    }

    private static CheckWarning ShouldReportOneDeadOperand(Constraint constraint)
    {
        IReadOnlyList<CheckWarning> warnings = DeadOperandWarnings(constraint);
        return warnings.ShouldHaveSingleItem(
            $"{constraint.GetType().Name} should report exactly one dead operand; it reported: "
            + string.Join("; ", warnings.Select(warning => warning.Message)));
    }

    private static IReadOnlyList<CheckWarning> DeadOperandWarnings(Constraint constraint)
    {
        ArchitectureModel model = Checker.HandBuilt("area/rule", constraint);
        RuleResult result = Harness.Check(model, ShopModel)
            .Single();
        return result.Warnings.Where(warning => warning.Kind == CheckWarningKind.DeadOperand).ToList();
    }

    // Every operand kind takes the subject and its operand list first; the counterpart verb also takes the name
    // template, which here derives a name no type carries.
    private static Constraint Construct(string kind, Selection subject, IReadOnlyList<Selection> operands)
    {
        Type type = Kinds.Single(candidate => candidate.Name == kind);
        ConstructorInfo constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single();
        object[] arguments = constructor.GetParameters().Length == 3 ? [subject, operands, "{Name}Twin"] : [subject, operands];
        return (Constraint)constructor.Invoke(arguments);
    }
}
