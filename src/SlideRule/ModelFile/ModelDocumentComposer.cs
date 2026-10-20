using System.Text;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.ModelFile;

/// <summary>Composes a model document as the file's bytes: UTF-8, LF, two-space indent, one trailing newline.</summary>
/// <remarks>
///     <para>
///         Nothing here decides anything. The document arrived already partitioned — either from
///         <see cref="ModelDocument.From" /> or from a file that was read — so every layer and every rule
///         in it is known to write clean, and a refusal met here is a caller handing over a document it
///         built some other way. That throws rather than silently dropping the rule, because a file that
///         lost a rule enforces less than it claims.
///     </para>
///     <para>
///         The layout is fixed so two runs over one model produce one byte sequence: no timestamp, no tool
///         version, and nothing sorted here, since the order is the model's, which is the spec author's.
///     </para>
/// </remarks>
internal static class ModelDocumentComposer
{
    private static readonly IReadOnlyDictionary<string, string> NoUnwritableLayers =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal static string Compose(ModelDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        Property(builder, "schemaVersion", JsonText.Digits(ModelDocument.SchemaVersion));
        Property(builder, "spec", JsonText.Quote(document.Spec));

        List<string> projects = document.Projects
            .Select(project => Entry("a project", writer => writer.WriteProject(project)))
            .ToList();
        List<string> layers = document.Layers
            .Select(layer => Entry("a layer", writer => writer.WriteLayer(layer)))
            .ToList();
        List<string> rules = document.Rules
            .Select(rule => Entry("rule " + ProseFormat.Backtick(rule.Id), writer => writer.WriteRule(rule)))
            .ToList();
        List<string> omitted = document.Omitted
            .Select(entry => Entry("an omitted rule", writer => writer.WriteOmitted(entry)))
            .ToList();

        ArrayProperty(builder, "projects", projects, last: false);
        ArrayProperty(builder, "layers", layers, last: false);
        ArrayProperty(builder, "rules", rules, last: false);
        ArrayProperty(builder, "omitted", omitted, last: true);
        builder.Append("}\n");
        return builder.ToString();
    }

    private static void Property(StringBuilder builder, string key, string value)
    {
        builder.Append("  ")
            .AppendQuoted(key)
            .Append(": ")
            .Append(value)
            .Append(",\n");
    }

    private static void ArrayProperty(StringBuilder builder, string key, IReadOnlyList<string> entries, bool last)
    {
        builder.Append("  ")
            .AppendQuoted(key)
            .Append(": ");
        if (entries.Count == 0)
        {
            builder.Append("[]");
        }
        else
        {
            string joined = string.Join(",\n", entries);
            builder.Append("[\n")
                .Append(joined)
                .Append("\n  ]");
        }

        builder.Append(last ? "\n" : ",\n");
    }

    private static string Entry(string what, Action<NodeWriter> write)
    {
        var writer = new NodeWriter(NoUnwritableLayers);
        write(writer);
        if (writer.Refusal is { } reason)
            throw new InvalidOperationException(
                $"The document carries {what}, which cannot be written: {reason}");

        return writer.Text;
    }
}
