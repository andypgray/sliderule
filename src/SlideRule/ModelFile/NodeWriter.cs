using System.Text;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.ModelFile;

/// <summary>
///     Writes one entry of a model file — a project, a layer, a rule, an omission — and reports the first
///     thing it met that it could not write.
/// </summary>
/// <remarks>
///     <para>
///         This is both the writer and the walk that decides what a file can carry: there is no separate
///         pass that asks "is this writable", because two walks can disagree and one cannot. A node it
///         cannot write records a reason and every method above it returns without writing, so the reason
///         is the first thing met in file order and the half-written text is discarded.
///     </para>
///     <para>
///         Nothing here throws for a node it cannot write, and nothing here validates: the model it is
///         given was built and validated, so what it writes is whatever that model holds.
///     </para>
///     <para>
///         Two shapes of text come out of one builder, and the split is the file's whole layout: entries and
///         the values inside them are compact, one line each, while a rule is an envelope whose properties sit
///         one per line with a compact value on each.
///     </para>
/// </remarks>
internal sealed class NodeWriter
{
    private const string EntryIndent = "    ";
    private const string RulePropertyIndent = "      ";

    private readonly StringBuilder _builder = new();

    // A stack of "nothing has been written into the container that is open here yet", which is the whole
    // of what deciding between a separator and a space needs to know.
    private readonly List<bool> _empty = new();
    private readonly IReadOnlyDictionary<string, string> _unwritableLayers;
    private bool _firstRuleProperty = true;

    /// <summary>
    ///     <paramref name="unwritableLayers" /> maps each layer already found unwritable, by name, to its own
    ///     reason.
    /// </summary>
    internal NodeWriter(IReadOnlyDictionary<string, string> unwritableLayers)
    {
        _unwritableLayers = unwritableLayers;
    }

    internal string? Refusal { get; private set; }

    /// <summary>Meaningless once <see cref="Refusal" /> is set.</summary>
    internal string Text => _builder.ToString();

    private bool Refused => Refusal is not null;

    internal void Refuse(string reason)
    {
        Refusal ??= reason;
    }

    internal void WriteProject(ModelDocumentProject project)
    {
        _builder.Append(EntryIndent);
        BeginObject();
        Field("name", project.Name);
        StringsField("assemblies", project.Assemblies);
        EndObject();
    }

    internal void WriteOmitted(OmittedRule omitted)
    {
        _builder.Append(EntryIndent);
        BeginObject();
        Field("id", omitted.Id);
        Field("reason", omitted.Reason);
        EndObject();
    }

    /// <summary>
    ///     A layer defined by a selection writes that selection and no globs, because its globs are derived
    ///     from it and would be a second, staler copy.
    /// </summary>
    internal void WriteLayer(LayerDefinition layer)
    {
        _builder.Append(EntryIndent);
        BeginObject();
        Field("name", layer.Name);
        if (layer.Definition is { } definition)
        {
            Key("definition");
            WriteSelection(definition);
        }
        else
        {
            StringsField("globs", layer.Globs);
        }

        OptionalField("purpose", layer.Purpose);
        EndObject();
    }

    internal void WriteRule(ArchRule rule)
    {
        _builder.Append(EntryIndent)
            .Append("{\n");
        RuleProperty("id");
        Str(rule.Id);
        RuleProperty("posture");
        EnumValue(rule.Posture);
        RuleProperty("sentence");
        Str(rule.Sentence);
        RuleProperty("because");
        Str(rule.Because);

        if (rule.Fix is { } fix)
        {
            RuleProperty("fix");
            Str(fix);
        }

        if (rule.Citation is { } citation)
        {
            RuleProperty("citation");
            Str(citation);
        }

        if (rule.ExemplarSelection is { } exemplar)
        {
            RuleProperty("exemplar");
            WriteSelection(exemplar);
        }

        if (rule.Constraint is { } constraint)
        {
            RuleProperty("constraint");
            WriteConstraint(constraint);
        }

        if (rule.Migrate is { } migrate)
        {
            RuleProperty("migrate");
            WriteMigrate(migrate);
        }

        if (rule.Scope is { } scope)
        {
            RuleProperty("scope");
            WriteScope(scope);
        }

        _builder.Append('\n')
            .Append(EntryIndent)
            .Append('}');
    }

    internal void WriteSelection(Selection selection)
    {
        if (Refused) return;

        BeginObject();
        if (selection is UnionSelection union)
        {
            Key("union");
            Elements(union.Parts, WriteSelection);
        }
        else if (selection.Noun is { } noun)
        {
            Key("noun");
            NodeWriters.Write(this, noun);
        }

        WriteAdjectives(selection.Adjectives);
        EndObject();
    }

    private void WriteMemberSelection(MemberSelection selection)
    {
        if (Refused) return;

        BeginObject();
        Key("source");
        WriteSelection(selection.Source);
        EnumField("kind", selection.Kind);
        WriteAdjectives(selection.Adjectives);
        EndObject();
    }

    internal void WriteProjectSelection(ProjectSelection selection)
    {
        if (Refused) return;

        BeginObject();
        WriteAdjectives(selection.Adjectives);
        EndObject();
    }

    /// <summary>
    ///     A predicate verb is met after its subject: a rule whose subject narrows by a <c>Where</c> and whose
    ///     verb is a <c>Must</c> is left out for the <c>Where</c>, because that is the first thing a reader
    ///     walking the file in order would reach.
    /// </summary>
    internal void WriteConstraint(Constraint constraint)
    {
        if (Refused) return;

        if (NodeWriters.IsDelegateNode(constraint.GetType())) WriteDelegateSubject(constraint);
        NodeWriters.Write(this, constraint);
    }

    internal void Subject(Selection selection)
    {
        Key("subject");
        WriteSelection(selection);
    }

    internal void MemberSubject(MemberSelection selection)
    {
        Key("memberSubject");
        WriteMemberSelection(selection);
    }

    internal void ProjectSubject(ProjectSelection selection)
    {
        Key("projectSubject");
        WriteProjectSelection(selection);
    }

    internal void MemberSelections(string key, IReadOnlyList<MemberSelection> selections)
    {
        if (Refused) return;

        Key(key);
        Elements(selections, WriteMemberSelection);
    }

    internal void Operands(IReadOnlyList<Selection> operands)
    {
        if (Refused) return;

        Key("operands");
        Elements(operands, WriteSelection);
    }

    internal void Members(IReadOnlyList<Member> members)
    {
        if (Refused) return;

        Key("members");
        Elements(members, WriteMember);
    }

    /// <summary>An anchor in a position that matches on the definition, which refuses a construction.</summary>
    internal void DefinitionAnchor(string key, TypeAnchor anchor)
    {
        if (Refused) return;

        Key(key);
        Anchor(anchor, definitionPosition: true);
    }

    /// <summary>An anchor in a position that matches on the construction, which admits one.</summary>
    internal void ConstructionAnchor(string key, TypeAnchor anchor)
    {
        if (Refused) return;

        Key(key);
        Anchor(anchor, definitionPosition: false);
    }

    internal void DefinitionAnchors(string key, IReadOnlyList<TypeAnchor> anchors)
    {
        AnchorList(key, anchors, definitionPosition: true);
    }

    internal void ConstructionAnchors(string key, IReadOnlyList<TypeAnchor> anchors)
    {
        AnchorList(key, anchors, definitionPosition: false);
    }

    internal void LayerName(string key, string name)
    {
        if (Refused) return;

        Key(key);
        LayerReference(name);
    }

    internal void LayerCells(IReadOnlyList<Layer> cells)
    {
        if (Refused) return;

        Key("layers");
        Elements(cells, cell => LayerReference(cell.Name));
    }

    internal void ProjectCells(ProjectSelection projects)
    {
        if (Refused) return;

        Key("projects");
        WriteProjectSelection(projects);
    }

    /// <summary>Opens a discriminated node and writes its discriminator, which is always its first key.</summary>
    internal void BeginNode(string discriminator)
    {
        if (Refused) return;

        BeginObject();
        Field("node", discriminator);
    }

    internal void EndNode()
    {
        if (Refused) return;

        EndObject();
    }

    internal void Key(string name)
    {
        if (Refused) return;

        Separate(spaced: true);
        _builder.AppendQuoted(name)
            .Append(": ");
    }

    private void Str(string value)
    {
        if (Refused) return;

        _builder.AppendQuoted(value);
    }

    private void Bool(bool value)
    {
        if (Refused) return;

        _builder.Append(value ? "true" : "false");
    }

    private void EnumValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        Str(NodeWriters.Camel(value.ToString()));
    }

    private void Strings(IReadOnlyList<string> values)
    {
        if (Refused) return;

        Elements(values, Str);
    }

    internal void Field(string key, string value)
    {
        Key(key);
        Str(value);
    }

    private void OptionalField(string key, string? value)
    {
        if (value is null) return;

        Key(key);
        Str(value);
    }

    internal void StringsField(string key, IReadOnlyList<string> values)
    {
        Key(key);
        Strings(values);
    }

    internal void EnumField<TEnum>(string key, TEnum value)
        where TEnum : struct, Enum
    {
        Key(key);
        EnumValue(value);
    }

    private void Elements<T>(IReadOnlyList<T> items, Action<T> write)
    {
        BeginArray();
        foreach (T item in items)
        {
            Element();
            write(item);
        }

        EndArray();
    }

    private void WriteAdjectives(IReadOnlyList<object> adjectives)
    {
        if (Refused || adjectives.Count == 0) return;

        Key("adjectives");
        Elements(adjectives, adjective => NodeWriters.Write(this, adjective));
    }

    // The subject of a predicate verb, walked but not written: the Must refuses the entry after it.
    private void WriteDelegateSubject(Constraint constraint)
    {
        switch (constraint)
        {
            case MemberConstraint member:
                WriteMemberSelection(member.MemberSubject);
                break;
            case ProjectConstraint project:
                WriteProjectSelection(project.ProjectSubject);
                break;
            default:
                WriteSelection(constraint.Subject!);
                break;
        }
    }

    private void WriteMember(Member member)
    {
        if (Refused) return;

        BeginObject();
        DefinitionAnchor("declaring", member.DeclaringAnchor);
        Field("name", member.Name);
        Key("isMethod");
        Bool(member.IsMethod);
        EndObject();
    }

    // The name and whether it names a definition; the path only when splitting the name would not yield
    // it, which is what a nested type and a closed generic do. The reader re-derives it otherwise, so the
    // question is asked of the reader's own derivation rather than of a second copy of its rule.
    private void WriteAnchor(TypeAnchor anchor)
    {
        string name = anchor.FullName!;
        BeginObject();
        Field("name", name);
        if (anchor.OnDefinition)
        {
            Key("onDefinition");
            Bool(true);
        }

        TypeAnchor derived = TypeAnchor.FromParsed(name, anchor.OnDefinition, null);
        if (!anchor.PathSegments.SequenceEqual(derived.PathSegments, StringComparer.Ordinal))
            StringsField("path", anchor.PathSegments);

        EndObject();
    }

    private void AnchorList(string key, IReadOnlyList<TypeAnchor> anchors, bool definitionPosition)
    {
        if (Refused) return;

        Key(key);
        Elements(anchors, anchor => Anchor(anchor, definitionPosition));
    }

    private void Anchor(TypeAnchor anchor, bool definitionPosition)
    {
        if (Refused) return;

        bool usable = definitionPosition
            ? anchor.TryDefinitionKey(out _, out AnchorRefusal refusal)
            : anchor.TryConstructionKey(out _, out _, out refusal);
        if (!usable)
        {
            Refuse(OmissionReasons.ForAnchor(anchor, refusal));
            return;
        }

        WriteAnchor(anchor);
    }

    private void LayerReference(string name)
    {
        if (Refused) return;

        if (_unwritableLayers.TryGetValue(name, out string layerReason))
        {
            Refuse(OmissionReasons.UnwritableLayer(name, layerReason));
            return;
        }

        Str(name);
    }

    private void WriteMigrate(MigrateData migrate)
    {
        if (Refused) return;

        BeginObject();
        Field("from", migrate.From);
        Field("toSentence", migrate.ToSentence);
        Field("baselinePath", migrate.BaselinePath);
        EnumField("policy", migrate.Policy);
        EndObject();
    }

    private void WriteScope(ScopeData scope)
    {
        if (Refused) return;

        BeginObject();
        EnumField("role", scope.Role);
        Field("scopeId", scope.ScopeId);
        Key("scoped");
        WriteSelection(scope.Scoped);
        StringsField("surface", scope.Surface);
        OptionalField("baselinePath", scope.BaselinePath);
        OptionalField("dragons", scope.Dragons);
        OptionalField("dragonsDoc", scope.DragonsDoc);
        EndObject();
    }

    private void RuleProperty(string key)
    {
        if (Refused) return;

        if (!_firstRuleProperty) _builder.Append(",\n");
        _firstRuleProperty = false;
        _builder.Append(RulePropertyIndent)
            .AppendQuoted(key)
            .Append(": ");
    }

    // An object breathes — `{ "k": v }` — and an array does not — `[a, b]`. An empty one of either is
    // written tight, `{}` and `[]`, which is the one place the two agree.
    private void BeginObject()
    {
        Open('{');
    }

    private void EndObject()
    {
        Close('}', spaced: true);
    }

    private void BeginArray()
    {
        Open('[');
    }

    private void EndArray()
    {
        Close(']', spaced: false);
    }

    private void Element()
    {
        Separate(spaced: false);
    }

    private void Open(char open)
    {
        if (Refused) return;

        _builder.Append(open);
        _empty.Add(true);
    }

    private void Separate(bool spaced)
    {
        if (Refused) return;

        int top = _empty.Count - 1;
        bool first = _empty[top];
        _empty[top] = false;
        if (!first) _builder.Append(", ");
        else if (spaced) _builder.Append(' ');
    }

    private void Close(char close, bool spaced)
    {
        if (Refused) return;

        int top = _empty.Count - 1;
        bool empty = _empty[top];
        _empty.RemoveAt(top);
        if (!empty && spaced) _builder.Append(' ');
        _builder.Append(close);
    }
}
