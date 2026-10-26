using System.Text.Json;
using SlideRule.Extraction.Fragments;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Tests.Extraction;

/// <summary>The fragment-side twin of <see cref="ModelQuery" />, plus a bed reaching every extraction pass.</summary>
internal static class FragmentQuery
{
    // Two types, a reference edge, a construction, a catch clause and a public signature position, so the
    // bed reaches every pass rather than only the declaration one — over a single bare class an equality
    // row would compare two nearly empty fragments and hold whatever the passes did.
    public const string EveryPassSource =
        """
        namespace N;

        public class Widget
        {
            public Part Made;

            public Part Build()
            {
                try
                {
                    return new Part();
                }
                catch (System.Exception)
                {
                    return Made;
                }
            }
        }

        public class Part {}
        """;

    // The seven edge families, named rather than counted so a red says which one moved, and in the order
    // OneProjectExtractionTests' FamiliesHolding reports them.
    public static readonly IReadOnlyList<string> EdgeFamilies =
    [
        "Edges", "MemberEdges", "ConstructorEdges", "InjectionEdges", "CatchEdges", "ThrowEdges", "ExposureEdges"
    ];

    public static FragmentType DeclaredType(this CodebaseFragment fragment, string fullName)
    {
        return fragment.DeclaredTypes.Single(type => type.Facts.FullName == fullName);
    }

    // Fragments compare the way the cache compares them — through the serializer that writes them to disk,
    // whose output is the byte identity the round-trip rows pin. The DTO's own record equality compares its
    // lists by reference, so two fragments of one compilation are never equal by it.
    public static string Serialized(this CodebaseFragment fragment)
    {
        return JsonSerializer.Serialize<IReadOnlyList<CodebaseFragment>>([fragment], ManifestJson.Options);
    }

    public static string Serialized(this IReadOnlyList<CodebaseFragment> fragments)
    {
        return JsonSerializer.Serialize(fragments, ManifestJson.Options);
    }

    // The fragments as a cache hit replays them: written and read back through the serializer the cache persists with.
    public static IReadOnlyList<CodebaseFragment> Persisted(this IReadOnlyList<CodebaseFragment> fragments)
    {
        string json = fragments.Serialized();

        return JsonSerializer.Deserialize<IReadOnlyList<CodebaseFragment>>(json, ManifestJson.Options)!;
    }

    // The fragment as the cache reads it back: through the cache's own serializer, which stores no span.
    public static CodebaseFragment Persisted(this CodebaseFragment fragment)
    {
        string json = JsonSerializer.Serialize(fragment, ManifestJson.Options);

        return JsonSerializer.Deserialize<CodebaseFragment>(json, ManifestJson.Options)!;
    }

    public static string Serialized(this FragmentType type)
    {
        return JsonSerializer.Serialize(type, ManifestJson.Options);
    }
}
