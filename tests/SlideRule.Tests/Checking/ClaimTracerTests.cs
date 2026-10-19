using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Which layers, rules and scopes claim one type, and the step each trace names. Each test pins the claims
///     as <c>explain --type</c> prints them below the type's own header lines.
/// </summary>
public sealed class ClaimTracerTests
{
    private const string Shop = """
                                using Microsoft.Extensions.DependencyInjection;
                                using Shop.Domain;
                                namespace Shop.Domain
                                {
                                    public interface IRepository {}
                                    [System.Serializable] public class Order {}
                                    public class OrderRepository : IRepository {}
                                    internal class LegacyOrderRepository : IRepository {}
                                }
                                namespace Shop.Web
                                {
                                    public class HomeController {}
                                    public static class Wiring
                                    {
                                        public static void Configure(IServiceCollection services)
                                        {
                                            services.AddScoped<IRepository, OrderRepository>();
                                        }
                                    }
                                }
                                """;

    private static readonly CodebaseModel ShopModel = CompilationFactory.ExtractWithDi(("Shop.cs", Shop));

    [Fact]
    public void AGlobLayerAndAnAdjectiveChain_NameTheGlobAndEachAdjective()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.OrderRepository", arch =>
        {
            arch.Layer("Domain", "Shop.Domain.*");
            arch.Rule("naming/repositories")
                .Enforce(arch.Types.WithSuffix("Repository").InNamespace("Shop.*").MustBePublic())
                .Because("b");
        });

        claims.ShouldReadClaims(
            """
            layers:
              Domain
                namespace `Shop.Domain` matches `Shop.Domain.*`
            rules:
              naming/repositories: Types named `*Repository` in `Shop.*`
                its name `OrderRepository` ends with `Repository`
                namespace `Shop.Domain` matches `Shop.*`
            """);
    }

    [Fact]
    public void AProjectLayer_NamesTheDeclaringProject_AndARuleOverItNamesTheLayerFirst()
    {
        TypeClaims claims = ClaimsOn("Shop.Web.HomeController", arch =>
        {
            Layer app = arch.Layer("App", arch.Project("TestProject"));
            arch.Rule("app/sealed")
                .Enforce(app.MustBeSealed())
                .Because("b");
        });

        claims.ShouldReadClaims(
            """
            layers:
              App
                declared by project `TestProject`
            rules:
              app/sealed: The App layer
                in the App layer
                  declared by project `TestProject`
            """);
    }

    [Fact]
    public void ABareTypesSubject_IsItsOwnReason()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.Order", arch =>
            arch.Rule("all/public")
                .Enforce(arch.Types.MustBePublic())
                .Because("b"));

        claims.ShouldReadClaims(
            """
            rules:
              all/public: Types
                every type the solution declares
            """);
    }

    [Fact]
    public void AnExceptThatRemovesTheType_IsListedWithThePayloadsOwnTrace()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.LegacyOrderRepository", arch =>
        {
            Layer domain = arch.Layer("Domain", "Shop.Domain.*");
            arch.Rule("domain/public")
                .Enforce(domain.Except(arch.Types.Named("LegacyOrderRepository")).MustBePublic())
                .Because("b");
        });

        claims.ShouldReadClaims(
            """
            layers:
              Domain
                namespace `Shop.Domain` matches `Shop.Domain.*`
            excepted by:
              rule domain/public: Types in the Domain layer, except types named `LegacyOrderRepository`
                in the Domain layer
                  namespace `Shop.Domain` matches `Shop.Domain.*`
                removed by the except: types named `LegacyOrderRepository`
                  it is named `LegacyOrderRepository`
            """);
    }

    [Fact]
    public void AnAdjectiveOtherThanExcept_DropsTheTypeWithoutListingIt()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.Order", arch =>
            arch.Rule("naming/interfaces")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).MustHavePrefix("I"))
                .Because("b"));

        claims.ShouldReadClaims("claimed by: no layer, rule or scope");
    }

    [Fact]
    public void AUnionOperandWithAdjectives_TracesUnderTheOperand()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.OrderRepository", arch =>
            arch.Rule("mixed/public")
                .Enforce(arch.AnyOf(arch.Namespace("Shop.Web"), arch.Types.WithSuffix("Repository")).MustBePublic())
                .Because("b"));

        claims.ShouldReadClaims(
            """
            rules:
              mixed/public: Types in `Shop.Web` or types named `*Repository`
                through types named `*Repository`
                  its name `OrderRepository` ends with `Repository`
            """);
    }

    [Fact]
    public void AFamily_NamesTheCellThatAdmittedTheType()
    {
        TypeClaims claims = ClaimsOn("Shop.Web.HomeController", arch =>
        {
            Layer domain = arch.Layer("Domain", "Shop.Domain.*");
            Layer web = arch.Layer("Web", "Shop.Web.*");
            arch.Rule("layering/independent")
                .Enforce(arch.Each(domain, web).MustNotReferenceEachOther())
                .Because("b");
        });

        claims.ShouldReadClaims(
            """
            layers:
              Web
                namespace `Shop.Web` matches `Shop.Web.*`
            rules:
              layering/independent: Each of the Domain and Web layers
                in the Web layer
                  namespace `Shop.Web` matches `Shop.Web.*`
            """);
    }

    [Theory]
    [InlineData("Shop.Domain.IRepository", "service")]
    [InlineData("Shop.Domain.OrderRepository", "implementation")]
    public void ARegisteredSubject_NamesTheRoleAndTheLifetime(string fullName, string role)
    {
        TypeClaims claims = ClaimsOn(fullName, arch =>
            arch.Rule("di/scoped-public")
                .Enforce(arch.Registered(Lifetime.Scoped).MustBePublic())
                .Because("b"));

        claims.ShouldReadClaims(
            $"""
             rules:
               di/scoped-public: Scoped-registered types
                 registered as the {role} of a scoped registration
             """);
    }

    [Fact]
    public void AMemberRule_IsClaimedThroughItsTypeSubject()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.Order", arch =>
            arch.Rule("domain/methods-public")
                .Enforce(arch.Namespace("Shop.Domain").Methods.MustBePublic())
                .Because("b"));

        claims.ShouldReadClaims(
            """
            rules:
              domain/methods-public: Methods of types in `Shop.Domain`
                namespace `Shop.Domain` matches `Shop.Domain`
            """);
    }

    [Fact]
    public void AQuarantine_ClaimsItsSurface_WhileItsContainmentRuleExceptsIt()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.OrderRepository", arch =>
            arch.Scope("domain/legacy")
                .Quarantine(arch.Namespace("Shop.Domain"))
                .BoundaryOnlyVia(arch.Types.Named("OrderRepository"))
                .Dragons("d")
                .Because("b"));

        claims.ShouldReadClaims(
            """
            scopes:
              domain/legacy (quarantine): Types in `Shop.Domain`
                namespace `Shop.Domain` matches `Shop.Domain`
            excepted by:
              rule domain/legacy/containment: Types in `Shop.Domain`, except types named `OrderRepository`
                namespace `Shop.Domain` matches `Shop.Domain`
                removed by the except: types named `OrderRepository`
                  it is named `OrderRepository`
            """);
    }

    [Fact]
    public void APredicateThatThrows_IsNotEvaluated_AndTheOtherClaimantsStillAnswer()
    {
        TypeClaims claims = ClaimsOn("Shop.Domain.Order", arch =>
        {
            arch.Layer("Domain", "Shop.Domain.*");
            arch.Rule("broken/where")
                .Enforce(arch.Types.Where(_ => throw new InvalidOperationException("boom"), "that explode").MustBePublic())
                .Because("b");
        });

        claims.ShouldReadClaims(
            """
            layers:
              Domain
                namespace `Shop.Domain` matches `Shop.Domain.*`
            not evaluated:
              rule broken/where: Types that explode
                could not be evaluated: the `Where` predicate threw InvalidOperationException on `Shop.Domain.Order`: boom
            """);
    }

    [Fact]
    public void AnExternalType_IsClaimedByNothing()
    {
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("all/public")
                .Enforce(arch.Types.MustBePublic())
                .Because("b"));
        TypeNode external = ShopModel.Types.First(type => type.IsExternal);

        TypeClaims claims = TypeClaims.Of(model, new ClaimTracer(ShopModel), external);

        claims.ClaimedByAnything.ShouldBeFalse();
        ClaimsFormatter.Lines(claims, Directory.GetCurrentDirectory())
            .ShouldBe([$"{external.FullName} (external)", ClaimsFormatter.ExternalLine]);
    }

    // Each adjective's two wordings, read straight off the trace: a dropped type is never listed by the answer
    // unless an Except dropped it, so the trace is the only place the other drops can be pinned.
    [Theory]
    [InlineData("prefix", "Shop.Domain.OrderRepository", true, "its name `OrderRepository` starts with `Order`")]
    [InlineData("prefix", "Shop.Domain.IRepository", false, "its name `IRepository` does not start with `Order`")]
    [InlineData("suffix", "Shop.Domain.Order", false, "its name `Order` does not end with `Repository`")]
    [InlineData("matching", "Shop.Domain.OrderRepository", true, "its name `OrderRepository` matches `*Repo*`")]
    [InlineData("matching", "Shop.Domain.Order", false, "its name `Order` does not match `*Repo*`")]
    [InlineData("named", "Shop.Domain.Order", false, "its name `Order` is not `Ledger`")]
    [InlineData("namespace", "Shop.Web.HomeController", false, "namespace `Shop.Web` does not match `Shop.Domain`")]
    [InlineData("kind", "Shop.Domain.IRepository", true, "it is an interface")]
    [InlineData("kind", "Shop.Domain.Order", false, "it is a class, not an interface")]
    [InlineData("implementing", "Shop.Domain.OrderRepository", true, "it implements `IRepository`")]
    [InlineData("implementing", "Shop.Domain.Order", false, "it does not implement `IRepository`")]
    [InlineData("derived", "Shop.Domain.Order", true, "it derives from `Object`")]
    [InlineData("derived", "Shop.Domain.IRepository", false, "it does not derive from `Object`")]
    [InlineData("attributed", "Shop.Domain.Order", true, "it is attributed with `[Serializable]`")]
    [InlineData("attributed", "Shop.Domain.OrderRepository", false, "it is not attributed with `[Serializable]`")]
    [InlineData("authored", "Shop.Domain.Order", true, "it is authored, not generated")]
    [InlineData("public", "Shop.Domain.Order", true, "it is declared public")]
    [InlineData("public", "Shop.Domain.LegacyOrderRepository", false, "it is declared internal, not public")]
    [InlineData("where-true", "Shop.Domain.Order", true, "it passes the predicate \"that pass\"")]
    [InlineData("where-false", "Shop.Domain.Order", false, "it fails the predicate \"that fail\"")]
    public void EachAdjective_NamesTheFactThatKeptOrDroppedTheType(string adjective, string fullName, bool kept, string step)
    {
        var arch = new Arch();
        Selection selection = adjective switch
        {
            "prefix" => arch.Types.WithPrefix("Order"),
            "suffix" => arch.Types.WithSuffix("Repository"),
            "matching" => arch.Types.WithNameMatching("*Repo*"),
            "named" => arch.Types.Named("Ledger"),
            "namespace" => arch.Types.InNamespace("Shop.Domain"),
            "kind" => arch.Types.OfKind(TypeKind.Interface),
            "implementing" => arch.Types.Implementing("Shop.Domain.IRepository"),
            "derived" => arch.Types.DerivedFrom("System.Object"),
            "attributed" => arch.Types.AttributedWith("System.SerializableAttribute"),
            "authored" => arch.Types.Authored(),
            "public" => arch.Types.ThatArePublic(),
            "where-true" => arch.Types.Where(_ => true, "that pass"),
            _ => arch.Types.Where(_ => false, "that fail")
        };
        TypeNode type = ShopModel.Type(fullName);

        ClaimTrace trace = new ClaimTracer(ShopModel).Trace(selection, type);

        trace.Verdict.ShouldBe(kept ? ClaimVerdict.Claimed : ClaimVerdict.Narrowed);
        trace.Steps.Select(candidate => candidate.Text).ShouldBe([step]);
    }

    [Fact]
    public void AnUnderAdjective_NamesTheFileAndTheGlobThatKeptTheType()
    {
        // The first declaring file under a glob, relative to the solution directory: a partial type answers
        // from whichever of its files the glob reaches.
        var tracer = new ClaimTracer(Sources.FolderModel, Sources.FolderRoot);
        var arch = new Arch();

        ClaimTrace kept = tracer.Trace(arch.Types.Under("src/Widgets", "vendor"), Sources.FolderModel.Type("App.Split"));

        kept.Verdict.ShouldBe(ClaimVerdict.Claimed);
        kept.Steps.Select(step => step.Text).ShouldBe(["it is declared in `vendor/Split.Vendored.cs`, under `vendor`"]);
    }

    [Fact]
    public void AnUnderAdjective_SaysWhyNoFilePutTheTypeUnderTheGlobs()
    {
        var tracer = new ClaimTracer(Sources.FolderModel, Sources.FolderRoot);
        var arch = new Arch();

        ClaimTrace outside = tracer.Trace(arch.Types.Under("src/Widgets", "src/Console"), Sources.FolderModel.Type("App.Shared"));

        // A declared type the model records no file for, which no glob can reach either.
        TypeNode bare = SyntheticNodes.Type("App.Bare");
        ClaimTrace fileless = new ClaimTracer(SyntheticNodes.Referencing([bare]), Sources.FolderRoot)
            .Trace(arch.Types.Under("src"), bare);

        outside.Verdict.ShouldBe(ClaimVerdict.Narrowed);
        outside.Steps.Select(step => step.Text).ShouldBe(["none of the files declaring it is under `src/Widgets` or `src/Console`"]);
        fileless.Steps.Select(step => step.Text).ShouldBe(["it has no source file in the solution"]);
    }

    private static TypeClaims ClaimsOn(string fullName, Action<Arch> define)
    {
        ArchitectureModel model = Checker.Model(define);
        TypeNode type = ShopModel.Type(fullName);
        return TypeClaims.Of(model, new ClaimTracer(ShopModel), type);
    }
}

file static class ClaimTracerAssertions
{
    // The header (the type line and its project and file lines) is the formatter's own subject, pinned end to end
    // by the CLI tests, so this compares from the first group on, one indent shallower.
    internal static void ShouldReadClaims(this TypeClaims claims, string expected)
    {
        IReadOnlyList<string> lines = ClaimsFormatter.Lines(claims, Directory.GetCurrentDirectory());
        string actual = string.Join(
            "\n",
            lines
                .Skip(1)
                .SkipWhile(line => line.StartsWith("  project", StringComparison.Ordinal)
                                   || line.StartsWith("  file", StringComparison.Ordinal))
                .Select(line => line.Substring(2)));

        actual.ShouldBe(expected.NormalizedLines(), customMessage: string.Join("\n", lines));
    }
}
