using Microsoft.CodeAnalysis.Tags;
using SlideRule.Tests.DocHygiene;
using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The edges of what is offered: whose names are in, whose are out, and which come first.
/// </summary>
/// <remarks>
///     A spec project declares rules and nothing a rule anchors on, so its own names are the one part of
///     the compilation an author never writes into a slot, and the contract's are the other. Everything
///     else the compilation can see is in, because a rule anchored on a framework or package type is an
///     ordinary rule — it is simply not what an author reaches for first, which is what the ranking says.
/// </remarks>
public sealed class UniverseBoundaryTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Fact]
    public async Task TheSpecProjectsOwnNamespaces_AreNotOffered()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);

        // Assert: the spec declares MyApp.Architecture and nothing a rule could anchor on inside it.
        offered.ShouldNotOffer("Architecture", "Architecture.*");
    }

    [Fact]
    public async Task TheContractsOwnNamespace_IsNotOffered()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("$$");""", Ct);

        // Assert: every spec project references the contract, so offering its namespace would put a name
        // in every list that no spec ever writes.
        offered.ShouldNotOffer("SlideRule", "SlideRule.*");
    }

    [Fact]
    public async Task TheFrameworksNamespaces_AreOffered()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("$$");""", Ct);

        // Assert: a rule over a framework namespace is an ordinary rule, and leaving it out would make the
        // list a claim about which rules are allowed.
        offered.ShouldOffer("System", "System.*");
    }

    [Fact]
    public async Task TheProductsNames_ComeBeforeTheFrameworks()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("$$");""", Ct);

        // Assert
        offered.ShouldOfferInThisOrder("MyApp", "System");
    }

    [Fact]
    public async Task ANamespaceTheSpecAlsoDeclares_IsStillOfferedAndStillRanksAsProduct()
    {
        // Act: MyApp is one namespace built from two assemblies — the product's and the spec's own. One
        // excluded half must not take the whole name out of the list, nor drop it behind the framework.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("$$");""", Ct);

        // Assert
        offered.ShouldOffer("MyApp");
        offered.ShouldOfferInThisOrder("MyApp", "System");
    }

    [Fact]
    public async Task AnInternalProductInterface_IsOffered()
    {
        // Act: a spec anchors on the internal types of the project it governs as readily as on the public
        // ones, and hiding them would hide exactly the rules worth writing.
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Types.Implementing("MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOffer("IInternalRepository");
    }

    [Fact]
    public async Task CompilerGeneratedNames_AreNotOffered()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.Returning("$$");""", Ct);

        // Assert: every name a compiler mints for itself is spelled with a character no author can type,
        // which is what the referenceable-by-name filter keeps out.
        List<string> unwritable = offered.Texts
            .Where(text => text.StartsWith("<", StringComparison.Ordinal))
            .ToList();
        unwritable.ShouldReportNothing("Name(s) no author could write were offered");
    }

    [Fact]
    public async Task AnExactNameSlot_OffersNoFrameworkNames()
    {
        // Act: the exact-name slot is flat, so the whole framework would arrive as one undifferentiated
        // list of thousands — and a rule named after a framework type reaches every declaration of that
        // name in the solution rather than the framework's own.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Types.Named("$$");""", Ct);

        // Assert
        offered.ShouldNotOffer("String", "Object", "Task", "Attribute");
    }

    [Fact]
    public async Task EachRow_CarriesTheTagItsKindDraws()
    {
        // Act
        OfferedList namespaces = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);
        OfferedList types = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.Returning("MyApp.Domain.$$");""", Ct);

        // Assert
        namespaces.ShouldOfferWithTag("Domain", WellKnownTags.Namespace);
        namespaces.ShouldOfferWithTag("Domain.*", WellKnownTags.Namespace);
        namespaces.ShouldOfferWithTag("*", WellKnownTags.Namespace);
        types.ShouldOfferWithTag("IHandler<T>", WellKnownTags.Interface);
        types.ShouldOfferWithTag("EntityBase", WellKnownTags.Class);
        types.ShouldOfferWithTag("Standing", WellKnownTags.Enum);
        types.ShouldOfferWithTag("Notify", WellKnownTags.Delegate);
    }
}
