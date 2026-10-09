using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Shouldly;
using Xunit;
using SlideRule.SpecCompletion;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The route a reader actually gets: the provider reached only through an analyzer reference, the way
///     the contract package delivers it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this is worth a whole workspace of its own.</b> Every other fact here composes the
///         provider into the host as a built-in part, which is fast, deterministic and not how anyone gets
///         it. What a spec author has is one <c>PackageReference</c>, which puts an assembly under
///         <c>analyzers/dotnet/cs</c> and leaves an editor to find the provider in it by reflection — and
///         an editor does that inside catch blocks that report nothing. A type it cannot construct, an
///         export attribute it cannot read and an assembly it cannot load all produce the same thing: no
///         list, no message. This is the only fact here that would notice.
///     </para>
///     <para>
///         <b>The poll and the one document.</b> A host loads a project's extensions on a background batch
///         and caches them against the analyzer-reference list it was given, so the first ask after the
///         reference appears can come back without them. One document is made and asked repeatedly, rather
///         than a fresh one per attempt, because a new document would be a new list instance and the cache
///         would start again each time.
///     </para>
/// </remarks>
public sealed class ProviderDeliveryTests
{
    [Fact]
    public void TheProviderType_HasTheShapeAReflectingLoaderCanConstruct()
    {
        // Arrange
        Type provider = typeof(SpecSlotCompletionProvider);

        // Act
        ConstructorInfo[] constructors = provider.GetConstructors();

        // Assert: internal keeps it off any surface a consumer compiles against, and the implicit
        // constructor of a non-abstract class is public, which is what a loader creating it by reflection
        // needs. Declaring one explicitly, or making it take an argument, is what would break the route
        // above — silently, because the loader reports nothing.
        provider.IsPublic.ShouldBeFalse("The provider is reached by reflection and never by a reference.");
        provider.IsSealed.ShouldBeTrue();
        ConstructorInfo only = constructors.ShouldHaveSingleItem(
            "A loader constructs this type with no arguments, so one public parameterless constructor is "
            + "the whole of what it may have.");
        only.GetParameters()
            .ShouldBeEmpty();
        only.IsPublic.ShouldBeTrue();
    }

    [Fact]
    public async Task ThroughAnAnalyzerReferenceAlone_TheProviderStillOffersNames()
    {
        // Arrange: host services without the provider assembly, so the analyzer reference is the only way
        // in. The same composition an editor has before any package is referenced.
        AnalyzerFileReference reference =
            new(typeof(SpecSlotCompletionProvider).Assembly.Location, new PathLoader());
        using SpecCompletionFixture delivered = new(MefHostServices.DefaultHost, reference);
        (Document document, int caret) = delivered.SpecDocument("""arch.Namespace("MyApp.$$");""");
        CompletionService service = CompletionService.GetService(document)!;

        // Act
        bool offered = await OfferedWithinAsync(service, document, caret, TimeSpan.FromSeconds(30));

        // Assert
        offered.ShouldBeTrue(
            "The provider never arrived through the analyzer reference. That is exactly what a spec author "
            + "gets from the contract package, and an editor finds providers there by reflection inside "
            + "catch blocks that report nothing — so a type it cannot construct, an export attribute it "
            + "cannot read and an assembly it cannot load are all silence.");
    }

    // Asked repeatedly on one document until the names appear or the deadline passes, because the
    // extensions of a project load on a background batch and the first ask can beat them.
    private static async Task<bool> OfferedWithinAsync(
        CompletionService service, Document document, int caret, TimeSpan within)
    {
        DateTime deadline = DateTime.UtcNow + within;
        while (true)
        {
            CompletionList list = await service.GetCompletionsAsync(
                document, caret, CompletionTrigger.Invoke, roles: null, options: null, Ct);
            bool found = list.ItemsList.Any(item => string.Equals(item.DisplayText, "Domain", StringComparison.Ordinal));
            if (found) return true;
            if (DateTime.UtcNow >= deadline) return false;

            await Task.Delay(TimeSpan.FromMilliseconds(250), Ct);
        }
    }

    // The four lines a host's own loader comes down to for a reference already on disk beside the tests.
    private sealed class PathLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath)
        {
            return Assembly.LoadFrom(fullPath);
        }
    }
}
