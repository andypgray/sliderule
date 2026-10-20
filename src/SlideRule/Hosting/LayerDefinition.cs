using SlideRule.Model;

namespace SlideRule.Hosting;

/// <summary>A layer the spec declared, in the finished model.</summary>
internal sealed class LayerDefinition
{
    internal LayerDefinition(LayerNoun noun, string definitionFragment, string? purpose)
    {
        Noun = noun;
        DefinitionFragment = definitionFragment;
        Purpose = purpose;
    }

    /// <summary>The noun the spec minted for the layer: the instance its rules select through.</summary>
    internal LayerNoun Noun { get; }

    public string Name => Noun.Name;

    public IReadOnlyList<string> Globs => Noun.Globs;

    internal Selection? Definition => Noun.Definition;

    /// <summary>
    ///     Gets the line the generated agent context prints for the layer, such as
    ///     <c>**Domain** — `MyApp.Domain.*`</c>, with the purpose appended as its own sentence where the
    ///     layer has one.
    /// </summary>
    public string DefinitionFragment { get; }

    /// <summary>Gets what the layer is for, in the spec's own words, or null where the spec gave none.</summary>
    public string? Purpose { get; }
}
