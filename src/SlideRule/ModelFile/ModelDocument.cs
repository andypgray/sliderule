using SlideRule.Hosting;

namespace SlideRule.ModelFile;

/// <summary>
///     A built architecture model as a file can carry it: the layers and rules whose every node is data,
///     the projects of the checked universe, and the rules that had to be left behind with the reason for
///     each.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="From" /> is the only thing that decides what is carried, and it decides by trying to
///         write: a rule that writes clean is carried and one that does not is omitted, so the partition
///         and the text can never disagree about a rule. Nothing else may classify a node.
///     </para>
///     <para>
///         A document read back from a file holds the same members, and <see cref="ToModel" /> turns it into
///         a model the checker runs unchanged.
///     </para>
/// </remarks>
internal sealed class ModelDocument
{
    /// <summary>The version of the format this writes and reads.</summary>
    internal const int SchemaVersion = 2;

    internal const string FileName = "sliderule.model.json";

    internal ModelDocument(
        string spec,
        IReadOnlyList<ModelDocumentProject> projects,
        IReadOnlyList<LayerDefinition> layers,
        IReadOnlyList<ArchRule> rules,
        IReadOnlyList<OmittedRule> omitted)
    {
        Spec = spec;
        Projects = projects;
        Layers = layers;
        Rules = rules;
        Omitted = omitted;
    }

    /// <summary>The spec's name, as the tool that wrote the file knew it.</summary>
    internal string Spec { get; }

    /// <summary>The projects of the checked universe, ordinally sorted by name.</summary>
    internal IReadOnlyList<ModelDocumentProject> Projects { get; }

    /// <summary>The layers the file carries, in declaration order. A layer may name only one above it.</summary>
    internal IReadOnlyList<LayerDefinition> Layers { get; }

    /// <summary>The rules the file carries, in model order.</summary>
    internal IReadOnlyList<ArchRule> Rules { get; }

    /// <summary>The rules the file names but does not carry, in model order, each with its reason.</summary>
    internal IReadOnlyList<OmittedRule> Omitted { get; }

    /// <summary>
    ///     Partitions <paramref name="model" /> by attempting to write each layer, then each rule. A layer
    ///     that cannot be written is left out, and every rule naming it joins the omitted list carrying
    ///     that layer's reason — which is why the layers are walked first and in declaration order.
    /// </summary>
    internal static ModelDocument From(
        ArchitectureModel model, string spec, IReadOnlyList<ModelDocumentProject> projects)
    {
        var unwritableLayers = new Dictionary<string, string>(StringComparer.Ordinal);
        var layers = new List<LayerDefinition>();
        foreach (LayerDefinition layer in model.Layers)
        {
            var writer = new NodeWriter(unwritableLayers);
            writer.WriteLayer(layer);
            if (writer.Refusal is { } layerReason) unwritableLayers[layer.Name] = layerReason;
            else layers.Add(layer);
        }

        var rules = new List<ArchRule>();
        var omitted = new List<OmittedRule>();
        foreach (ArchRule rule in model.Rules)
        {
            var writer = new NodeWriter(unwritableLayers);
            writer.WriteRule(rule);
            if (writer.Refusal is { } ruleReason) omitted.Add(new OmittedRule(rule.Id, ruleReason));
            else rules.Add(rule);
        }

        List<ModelDocumentProject> sorted = projects
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToList();

        return new ModelDocument(spec, sorted, layers, rules, omitted);
    }

    internal ArchitectureModel ToModel()
    {
        return new ArchitectureModel(Rules, Layers);
    }
}
