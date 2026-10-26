using Shouldly;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The headline sentence pins: the canonical sample's rules (GRAMMAR §12) and the flagship rules
///     rendered beside them. These strings are the spec: each is the deterministic law rendered from a
///     reified rule.
/// </summary>
public class ProseSentenceTests
{
    private static string SentenceFor(string id)
    {
        return Checker.Canonical
            .Rule(id)
            .Sentence;
    }

    [Fact]
    public void Enforce_Layering_RendersCollectiveLayerVoice()
    {
        // Bare layer subject → collective voice; layer reference target is not backticked.
        SentenceFor("layering/domain-independent")
            .ShouldBe("The Domain layer must not reference the Web layer.");
    }

    [Fact]
    public void Enforce_InterfaceNaming_RendersOfKindHeadAndPrefix()
    {
        // OfKind substitutes the head plural ("interfaces"); MustHavePrefix → "named `I*`".
        SentenceFor("naming/interfaces")
            .ShouldBe("Interfaces in `MyApp.*` must be named `I*`.");
    }

    [Fact]
    public void Enforce_HandlerNaming_RendersOpenGenericAndSuffix()
    {
        // Implementing an open generic renders declared type-parameter names: `IHandler<T>`.
        SentenceFor("naming/handlers")
            .ShouldBe("Types implementing `IHandler<T>` must be named `*Handler`.");
    }

    [Fact]
    public void Enforce_DiHandlersViaRegistry_RendersExceptSubjectAndConstructVerb()
    {
        // The DI construction ban (GRAMMAR §5.3): .Except carves the sanctioned root out of the subject as a
        // sentence-final parenthetical ("except `HandlerRegistry`") that the verb closes with a comma, and
        // the open-generic target renders as "types implementing `IHandler<T>`".
        SentenceFor("di/handlers-via-registry")
            .ShouldBe("Types, except `HandlerRegistry`, must not construct types implementing `IHandler<T>`.");
    }

    [Fact]
    public void Enforce_TypeNameLength_RendersMustEscapeHatchDescription()
    {
        // The `.Must(...)` escape-hatch description completes "must …" verbatim.
        SentenceFor("style/type-name-length")
            .ShouldBe("Types in `MyApp.*` must keep type names at or under 40 characters.");
    }

    [Fact]
    public void Enforce_AsyncSuffix_RendersMemberSubjectFlagship()
    {
        // The flagship member-subject rule (GRAMMAR §4.6, §6). The member
        // subject is "{kind-plural} {Returning} declared in {selection-reference}"; single anchor.
        Checker.Model(arch =>
            {
                Selection web = arch.Namespace("MyApp.Web.*");
                arch.Rule("naming/async-suffix")
                    .Enforce(web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async"))
                    .Because("Async methods are discovered by suffix; agents grep by *Async.");
            })
            .Rule("naming/async-suffix")
            .Sentence
            .ShouldBe("Methods returning `Task` declared in types in `MyApp.Web.*` must be named `*Async`.");
    }

    [Fact]
    public void Enforce_AcceptCancellation_RendersMemberSubjectAcceptParameterFlagship()
    {
        // The parameter-facts flagship in Layer voice (GRAMMAR §4.6, §5.7): Web-layer methods returning a
        // Task must accept a CancellationToken. Two Returning anchors join with "or"; the verb renders the
        // article-safe "a parameter of type `X`" fragment.
        Checker.Model(arch =>
            {
                Layer web = arch.Layer("Web", "MyApp.Web.*");
                arch.Rule("async/accept-cancellation")
                    .Enforce(web.Methods.Returning(typeof(Task), typeof(Task<>))
                        .MustAcceptParameter(typeof(CancellationToken)))
                    .Because("Async Web methods must honor cancellation; agents thread the token through.");
            })
            .Rule("async/accept-cancellation")
            .Sentence
            .ShouldBe("Methods returning `Task` or `Task<TResult>` declared in the Web layer must accept a parameter of type `CancellationToken`.");
    }

    [Fact]
    public void MustNotUse_ExpressionMintedProperty_RendersNoParens()
    {
        // An expression-minted property anchor renders with no parens — identical to the typeof form.
        Checker.Sentence(arch => arch.Types.MustNotUse(arch.Member(() => DateTime.Now)))
            .ShouldBe("Types must not use `DateTime.Now`.");
    }

    [Fact]
    public void MustNotUse_VerbStaticVoidMethod_RendersParens()
    {
        // The static-form verb sugar: a bare () => GC.Collect() lambda desugars through
        // arch.Member(() => …) to the same method leaf, so the sentence is byte-identical — parens for a method.
        Checker.Sentence(arch => arch.Types.MustNotUse(() => GC.Collect()))
            .ShouldBe("Types must not use `GC.Collect()`.");
    }
}
