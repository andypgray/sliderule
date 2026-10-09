using SlideRule.Model;

namespace SlideRule.Hosting;

/// <summary>
///     A layer the spec declared, in the finished model: its name, the namespaces it covers, what it is
///     for, and the line the generated agent context prints for it.
/// </summary>
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

    /// <summary>Gets the layer's name as the spec declared it.</summary>
    public string Name => Noun.Name;

    /// <summary>
    ///     Gets the namespace globs that define the layer: the globs the spec listed, or (for a layer
    ///     defined by a selection instead) the one namespace that selection names, or empty where it names
    ///     no single namespace.
    /// </summary>
    public IReadOnlyList<string> Globs => Noun.Globs;

    /// <summary>
    ///     The selection that defines the layer, or null for the glob form — what the law drawing reads to
    ///     place the layer and to collapse it onto what it is defined as.
    /// </summary>
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
