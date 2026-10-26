using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     Which slot a literal fills and whether that slot completes: the seven that do, the ones that do not,
///     and the written shapes an argument can arrive in.
/// </summary>
/// <remarks>
///     Every silent case here is a claim that nothing was offered, and a body that failed to compile would
///     make every one of them pass on the strength of the typo — so each goes through the entry point that
///     holds the source to compiling first. Two do not: the call that cannot bind, whose comment says why,
///     and the project with no contract, which is asked through an entry point of its own.
/// </remarks>
public sealed class SpecSlotBindingTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Fact]
    public async Task AnExactNameSlot_OffersTheProductsSimpleNames()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Types.Named("$$");""", Ct);

        // Assert: a simple name, never a dotted one, and never a generic spelling — that is what the slot
        // compares against.
        offered.ShouldOffer("Order", "IHandler", "Line", "Entry");
        offered.ShouldNotOffer("IHandler<T>", "MyApp.Domain.Order");
    }

    [Fact]
    public async Task AnInterfaceSlot_OffersInterfacesAndTheNamespacesBelowIt()
    {
        // Act: a spec anchors on the internal types of the project it governs as readily as on the public
        // ones, and hiding them would hide exactly the rules worth writing.
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Types.Implementing("MyApp.Domain.$$");""", Ct);

        // Assert: a generic is spelled with its declared type parameters, separated by a comma and a space,
        // which is what the extractor renders and therefore what a rule's anchor has to be spelled as.
        offered.ShouldOfferExactly(
            "Orders", "IHandler<T>", "IMap<TKey, TValue>", "IInternalRepository");
    }

    [Fact]
    public async Task ABaseTypeSlot_OffersOnlyTheClassesSomethingCanDeriveFrom()
    {
        // Act
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Types.DerivedFrom("MyApp.Domain.$$");""", Ct);

        // Assert: a sealed class and a static one are both names a subject could never reach, and an
        // interface is not a base type at all — the base-type chain and the interface list are two
        // different facts about a type.
        offered.ShouldOfferExactly("Orders", "EntityBase", "Container<T>");
    }

    [Fact]
    public async Task AnAttributeSlot_OffersOnlyTheClassesThatReachSystemAttribute()
    {
        // Act
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Types.AttributedWith("MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOfferExactly("Orders", "AuditedAttribute");
    }

    [Fact]
    public async Task AReturnTypeSlot_OffersEveryType()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.Returning("MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOffer(
            "Orders", "EntityBase", "Container<T>", "InvoiceEntity", "Registry", "AuditedAttribute",
            "IHandler<T>");
    }

    [Fact]
    public async Task AParameterTypeSlot_OffersEveryType()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.MustAcceptParameter("MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOffer("Orders", "EntityBase", "InvoiceEntity", "IHandler<T>");
    }

    [Theory]
    // A name-pattern slot: a name glob is written over names that may not exist yet, so a list of the ones
    // that do would be a list of the wrong answers.
    [InlineData("""arch.Types.WithNameMatching("$$");""")]
    // A prose slot.
    [InlineData("""arch.Layer("Domain", "MyApp.Domain.*").Purpose("$$");""")]
    // A parameter carrying no marker: a call that binds perfectly well and has nothing to do with the
    // authoring surface.
    [InlineData("""_ = string.IsNullOrEmpty("MyApp.$$");""")]
    public async Task ASlotThatDoesNotComplete_OffersNothing(string body)
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(body, Ct);

        // Assert
        offered.ShouldOfferNothing();
    }

    [Fact]
    public async Task AProjectThatNeverReferencesTheContract_OffersNothing()
    {
        // Act: the marker is not in the compilation, so there is no slot to be in. This is what keeps the
        // provider free for every project in an estate that is not a spec project.
        OfferedList offered = await fixture.OfferedWithoutTheContractAsync(
            """
            namespace MyApp.Reporting;

            public static class Report
            {
                public static bool Empty() => string.IsNullOrEmpty("MyApp.$$");
            }
            """, Ct);

        // Assert
        offered.ShouldOfferNothing();
    }

    [Fact]
    public async Task ACallThatDoesNotBind_OffersNothing()
    {
        // Act: deliberately not held to compiling, because the whole shape is a call to a verb that does
        // not exist. Nothing binds, so nothing says which slot this is, and guessing would offer names for
        // a slot the author is not in.
        OfferedList offered = await fixture.OfferedAsync("""arch.NoSuchVerb("MyApp.$$");""", Ct);

        // Assert
        offered.ShouldOfferNothing();
    }

    [Theory]
    // An element after the first of a params call: expanded at the call site, it has no argument operation
    // of its own per element, which is why the binding walks up from the literal's own operation, through
    // the array the compiler gathers the elements into, to the one argument carrying them all.
    [InlineData("""arch.Types.MustNotImplement("MyApp.Domain.IHandler<T>", "MyApp.Domain.$$");""")]
    // An element of an explicit array.
    [InlineData("""arch.Types.MustNotImplement("MyApp.Domain.IHandler<T>", new[] { "MyApp.Domain.$$" });""")]
    // An element of a collection expression.
    [InlineData("""arch.Types.MustNotImplement("MyApp.Domain.IHandler<T>", ["MyApp.Domain.$$"]);""")]
    public async Task AnElementOfAnArrayArgument_OffersTheSameNames(string body)
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(body, Ct);

        // Assert
        offered.ShouldOffer("IMap<TKey, TValue>");
    }

    [Theory]
    // A named argument: the ordinal comes off the parameter the argument bound to, not off its position.
    [InlineData("""arch.Namespace(glob: "MyApp.$$");""")]
    // A literal in parentheses: parentheses have no operation of their own, so the literal inside them is
    // the value the slot receives, which is where the spec-literal analyzer reads it too.
    [InlineData("""arch.Namespace(("MyApp.$$"));""")]
    // A null-forgiven literal: the null-forgiving operator has no operation of its own either, so the
    // literal it follows is the value the slot receives.
    [InlineData("""arch.Namespace("MyApp.$$"!);""")]
    public async Task ANamedOrWrappedArgument_OffersTheSameNames(string body)
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(body, Ct);

        // Assert
        offered.ShouldOffer("Domain", "Domain.*");
    }

    [Fact]
    public async Task AnExtensionCalledAsAStaticMethod_OffersTheSameNames()
    {
        // Act: written unreduced, the receiver is an ordinary first argument and every later parameter's
        // ordinal moves by one — which is exactly the case the equal-parameter-count guard is for.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """SelectionAdjectives.Implementing(arch.Types, "MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOffer("IHandler<T>", "IMap<TKey, TValue>");
    }
}
