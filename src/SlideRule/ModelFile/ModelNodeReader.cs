using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Validation;

namespace SlideRule.ModelFile;

/// <summary>
///     Reads a parsed model file into model nodes, minting them on one <see cref="Arch" /> of its own.
/// </summary>
/// <remarks>
///     <para>
///         Every method returns null once a refusal is recorded and every caller returns straight back
///         up, which is what makes "the first refusal wins" true of the whole read rather than of one
///         method. Nothing here throws for content: a text that is not a model file is refused, whatever
///         it says.
///     </para>
///     <para>
///         It validates before it mints, everywhere a node constructor or the evaluator would otherwise
///         throw on bad content — globs, rule ids, enum spellings, layer names. A node minted from
///         unchecked content would be a model whose first check crashes, which is a worse report than a
///         refusal naming the line.
///     </para>
///     <para>
///         An unknown key is refused rather than ignored. A reader that skipped what it did not recognise
///         would read a narrowed selection as a wide one — silently enforcing less than the file says —
///         and in an allow-list position a widened set is enforcement that cannot fail.
///     </para>
/// </remarks>
internal sealed class ModelNodeReader
{
    private const string AnchorWhere = "an anchor";
    private const string MemberWhere = "a member";
    private const string RootWhere = "the root";

    private readonly Arch _arch = new();
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ruleIds = new(StringComparer.Ordinal);

    internal ModelRefusal? Refusal { get; private set; }

    private bool Failed => Refusal is not null;

    internal ModelDocument? ReadDocument(StrictJsonValue root)
    {
        StrictJsonValue? obj = AsObject(root, RootWhere);
        if (obj is null) return null;

        // The version is read before any other key is judged: a file from a different version of this
        // format is not malformed, and saying so is more use than complaining about its first odd key.
        if (!ReadSchemaVersion(obj)) return null;

        if (!OnlyKeys(obj, RootWhere, "schemaVersion", "spec", "projects", "layers", "rules", "omitted")) return null;

        string? spec = RequiredText(obj, RootWhere, "spec");
        if (spec is null) return null;

        IReadOnlyList<ModelDocumentProject>? projects = ReadProjects(obj);
        if (projects is null) return null;

        IReadOnlyList<LayerDefinition>? layers = ReadLayers(obj);
        if (layers is null) return null;

        IReadOnlyList<ArchRule>? rules = ReadRules(obj);
        if (rules is null) return null;

        IReadOnlyList<OmittedRule>? omitted = ReadOmitted(obj);
        if (omitted is null) return null;

        return new ModelDocument(spec, projects, layers, rules, omitted);
    }

    private bool ReadSchemaVersion(StrictJsonValue root)
    {
        StrictJsonValue? value = Required(root, RootWhere, "schemaVersion");
        if (value is null) return false;

        // A number too large for an int is refused the same way a string would be: no version this reader
        // could ever grow into is one it cannot hold, so the value is not a version.
        var version = 0;
        bool isInteger = value.ValueKind == StrictJsonKind.Number && value.TryGetInt32(out version);
        if (!isInteger)
        {
            RefuseAt(value, Field("schemaVersion") + " must be an integer.");
            return false;
        }

        if (version == ModelDocument.SchemaVersion) return true;

        string declared = JsonText.Digits(version);
        string understood = JsonText.Digits(ModelDocument.SchemaVersion);
        ModelRefusalKind kind = version > ModelDocument.SchemaVersion
            ? ModelRefusalKind.Newer
            : ModelRefusalKind.Older;
        string direction = kind == ModelRefusalKind.Newer ? "newer" : "older";
        Refuse(kind, "schemaVersion " + declared + " is " + direction + " than this reader understands ("
                     + understood + ").", value.Position);
        return false;
    }

    private IReadOnlyList<ModelDocumentProject>? ReadProjects(StrictJsonValue root)
    {
        StrictJsonValue? array = RequiredArray(root, RootWhere, "projects");
        return array is null ? null : Each(array, ReadProject);
    }

    private ModelDocumentProject? ReadProject(StrictJsonValue element)
    {
        StrictJsonValue? entry = AsObject(element, EntryOf("projects"));
        if (entry is null) return null;
        if (!OnlyKeys(entry, "a project", "name", "assemblies")) return null;

        string? name = RequiredText(entry, "a project", "name");
        if (name is null) return null;

        IReadOnlyList<string>? assemblies = RequiredTexts(entry, "a project", "assemblies");
        if (assemblies is null) return null;

        return new ModelDocumentProject(name, assemblies);
    }

    private IReadOnlyList<LayerDefinition>? ReadLayers(StrictJsonValue root)
    {
        StrictJsonValue? array = RequiredArray(root, RootWhere, "layers");
        return array is null ? null : Each(array, ReadLayer);
    }

    private LayerDefinition? ReadLayer(StrictJsonValue element)
    {
        StrictJsonValue? entry = AsObject(element, EntryOf("layers"));
        if (entry is null) return null;
        if (!OnlyKeys(entry, "a layer", "name", "globs", "definition", "purpose")) return null;

        StrictJsonValue? nameValue = Required(entry, "a layer", "name");
        if (nameValue is null) return null;

        string? name = AsText(nameValue, Field("name"));
        if (name is null) return null;
        if (_layers.ContainsKey(name))
        {
            RefuseAt(nameValue, "layer " + JsonText.Quote(name) + " is declared twice.");
            return null;
        }

        StrictJsonValue? globs = Optional(entry, "globs");
        StrictJsonValue? definition = Optional(entry, "definition");
        if (globs is null == definition is null)
        {
            RefuseAt(entry, "a layer takes exactly one of " + Field("globs") + " and " + Field("definition") + ".");
            return null;
        }

        Layer? minted = definition is null ? MintGlobLayer(entry, name, globs!) : MintDefinedLayer(name, definition);
        if (minted is null) return null;

        string? purpose = OptionalText(entry, "purpose");
        if (Failed) return null;
        if (purpose is not null) minted.Purpose(purpose);

        _layers.Add(name, minted);

        return ArchModelBuilder.ProjectLayer((LayerNoun)minted.Noun, purpose);
    }

    private Layer? MintGlobLayer(StrictJsonValue entry, string name, StrictJsonValue globs)
    {
        IReadOnlyList<string>? patterns = Texts(globs, "globs");
        if (patterns is null) return null;

        // A layer with no glob at all has neither of the two things a layer is defined by.
        if (patterns.Count == 0)
        {
            RefuseAt(entry, "a layer takes exactly one of " + Field("globs") + " and " + Field("definition") + ".");
            return null;
        }

        List<StrictJsonValue> elements = globs.EnumerateArray().ToList();
        for (var i = 0; i < patterns.Count; i++)
            if (!CheckGlob(elements[i], patterns[i], "namespace pattern", NamespacePattern.Validate))
                return null;

        string[] more = patterns.Skip(1).ToArray();
        return _arch.Layer(name, patterns[0], more);
    }

    private Layer? MintDefinedLayer(string name, StrictJsonValue definition)
    {
        Selection? selection = ReadSelection(definition, Field("definition"));
        if (selection is null) return null;

        return _arch.Layer(name, selection);
    }

    private IReadOnlyList<ArchRule>? ReadRules(StrictJsonValue root)
    {
        StrictJsonValue? array = RequiredArray(root, RootWhere, "rules");
        return array is null ? null : Each(array, ReadRule);
    }

    private ArchRule? ReadRule(StrictJsonValue element)
    {
        StrictJsonValue? entry = AsObject(element, EntryOf("rules"));
        if (entry is null) return null;
        if (!OnlyKeys(entry, "a rule", "id", "posture", "sentence", "because", "fix", "citation", "exemplar",
                "constraint", "migrate", "scope")) return null;

        string? id = ReadRuleId(entry, "a rule");
        if (id is null) return null;

        if (!RequiredEnum(entry, "a rule", "posture", "a posture", out Posture posture)) return null;

        string? sentence = RequiredText(entry, "a rule", "sentence");
        if (sentence is null) return null;

        string? because = RequiredText(entry, "a rule", "because");
        if (because is null) return null;

        string? fix = OptionalText(entry, "fix");
        if (Failed) return null;

        string? citation = OptionalText(entry, "citation");
        if (Failed) return null;

        Selection? exemplar = null;
        if (Optional(entry, "exemplar") is { } exemplarValue)
        {
            exemplar = ReadSelection(exemplarValue, Field("exemplar"));
            if (exemplar is null) return null;
        }

        StrictJsonValue? constraintValue = Optional(entry, "constraint");
        Constraint? constraint = null;
        if (constraintValue is not null)
        {
            constraint = ReadConstraint(constraintValue, Field("constraint"));
            if (constraint is null) return null;
        }

        MigrateData? migrate = null;
        if (Optional(entry, "migrate") is { } migrateValue)
        {
            migrate = ReadMigrate(migrateValue);
            if (migrate is null) return null;
        }

        ScopeData? scope = null;
        if (Optional(entry, "scope") is { } scopeValue)
        {
            scope = ReadScope(scopeValue);
            if (scope is null) return null;
        }

        // A tripwire is the one rule shape with nothing to check: it reports on whatever its scoped
        // selection reaches. Every other rule is its constraint, so one without one would check nothing
        // and read as passing.
        bool isTripwire = scope is { Role: ScopeRole.Tripwire };
        if (isTripwire == constraint is not null)
        {
            RefuseAt(entry, "rule " + JsonText.Quote(id) + " carries a " + Field("constraint")
                            + " unless its scope is a tripwire, and a tripwire carries none.");
            return null;
        }

        return new ArchRule(id, posture, because, fix, sentence, constraint, migrate, scope, citation, exemplar);
    }

    private IReadOnlyList<OmittedRule>? ReadOmitted(StrictJsonValue root)
    {
        StrictJsonValue? array = RequiredArray(root, RootWhere, "omitted");
        return array is null ? null : Each(array, ReadOmittedRule);
    }

    private OmittedRule? ReadOmittedRule(StrictJsonValue element)
    {
        StrictJsonValue? entry = AsObject(element, EntryOf("omitted"));
        if (entry is null) return null;
        if (!OnlyKeys(entry, "an omitted rule", "id", "reason")) return null;

        string? id = ReadRuleId(entry, "an omitted rule");
        if (id is null) return null;

        string? reason = RequiredText(entry, "an omitted rule", "reason");
        if (reason is null) return null;

        return new OmittedRule(id, reason);
    }

    private string? ReadRuleId(StrictJsonValue entry, string where)
    {
        StrictJsonValue? value = Required(entry, where, "id");
        if (value is null) return null;

        string? id = AsText(value, Field("id"));
        if (id is null) return null;

        if (!RuleIdSyntax.IsValid(id))
        {
            RefuseAt(value, JsonText.Quote(id) + " is not a rule id.");
            return null;
        }

        if (_ruleIds.Add(id)) return id;

        RefuseAt(value, "rule " + JsonText.Quote(id) + " appears twice.");
        return null;
    }

    private MigrateData? ReadMigrate(StrictJsonValue value)
    {
        StrictJsonValue? obj = AsObject(value, Field("migrate"));
        if (obj is null) return null;
        if (!OnlyKeys(obj, "the migration", "from", "toSentence", "baselinePath", "policy")) return null;

        string? from = RequiredText(obj, "the migration", "from");
        if (from is null) return null;

        string? toSentence = RequiredText(obj, "the migration", "toSentence");
        if (toSentence is null) return null;

        string? baselinePath = RequiredText(obj, "the migration", "baselinePath");
        if (baselinePath is null) return null;

        if (!RequiredEnum(obj, "the migration", "policy", "a migration policy", out MigrationPolicy policy)) return null;

        return new MigrateData(from, toSentence, baselinePath, policy);
    }

    private ScopeData? ReadScope(StrictJsonValue value)
    {
        StrictJsonValue? obj = AsObject(value, Field("scope"));
        if (obj is null) return null;
        if (!OnlyKeys(obj, "the scope", "role", "scopeId", "scoped", "surface", "baselinePath", "dragons",
                "dragonsDoc")) return null;

        if (!RequiredEnum(obj, "the scope", "role", "a scope role", out ScopeRole role)) return null;

        string? scopeId = RequiredText(obj, "the scope", "scopeId");
        if (scopeId is null) return null;

        Selection? scoped = RequiredSelection(obj, "the scope", "scoped");
        if (scoped is null) return null;

        IReadOnlyList<string>? surface = RequiredTexts(obj, "the scope", "surface");
        if (surface is null) return null;

        string? baselinePath = OptionalText(obj, "baselinePath");
        if (Failed) return null;

        string? dragons = OptionalText(obj, "dragons");
        if (Failed) return null;

        string? dragonsDoc = OptionalText(obj, "dragonsDoc");
        if (Failed) return null;

        return new ScopeData(role, surface, baselinePath, dragons, dragonsDoc, scopeId, scoped);
    }

    private Selection? ReadSelection(StrictJsonValue value, string what)
    {
        StrictJsonValue? obj = AsObject(value, what);
        if (obj is null) return null;
        if (!OnlyKeys(obj, "a selection", "noun", "union", "adjectives")) return null;

        StrictJsonValue? noun = Optional(obj, "noun");
        StrictJsonValue? union = Optional(obj, "union");
        if (noun is null == union is null)
        {
            RefuseAt(obj, "a selection takes exactly one of " + Field("noun") + " and " + Field("union") + ".");
            return null;
        }

        if (union is not null) return ReadUnion(obj, union);

        SelectionNoun? head = ReadNoun(noun!);
        if (head is null) return null;

        IReadOnlyList<SelectionAdjective>? adjectives =
            ReadAdjectives(obj, NodeReaders.AdjectiveWhere, NodeReaders.AdjectiveArms);
        if (adjectives is null) return null;

        // A bare layer reference mints the registered layer itself, so a family's cells are the same
        // objects the spec's own would be; a refined one is an ordinary selection over the same noun. The
        // lookup cannot miss: the noun arm resolved the name against the layers declared above, or refused.
        if (head is LayerNoun layerNoun && adjectives.Count == 0) return _layers[layerNoun.Name];

        return new RefinedSelection(_arch, head, adjectives);
    }

    private Selection? ReadUnion(StrictJsonValue obj, StrictJsonValue union)
    {
        StrictJsonValue? array = AsArray(union, Field("union"));
        if (array is null) return null;

        List<Selection>? read = Each(array, element => ReadSelection(element, EntryOf("union")));
        IReadOnlyList<Selection>? parts = NonEmpty(read, obj, "union");
        if (parts is null) return null;

        IReadOnlyList<SelectionAdjective>? adjectives =
            ReadAdjectives(obj, NodeReaders.AdjectiveWhere, NodeReaders.AdjectiveArms);
        if (adjectives is null) return null;

        var built = UnionSelection.Create(_arch, parts);
        return adjectives.Count == 0 ? built : built.WithAdjectives(adjectives);
    }

    private MemberSelection? ReadMemberSelection(StrictJsonValue value, string what)
    {
        StrictJsonValue? obj = AsObject(value, what);
        if (obj is null) return null;
        if (!OnlyKeys(obj, "a member selection", "source", "kind", "adjectives")) return null;

        Selection? source = RequiredSelection(obj, "a member selection", "source");
        if (source is null) return null;

        if (!RequiredEnum(obj, "a member selection", "kind", "a member kind", out MemberKindFilter kind)) return null;

        IReadOnlyList<MemberAdjective>? adjectives =
            ReadAdjectives(obj, NodeReaders.MemberAdjectiveWhere, NodeReaders.MemberAdjectiveArms);
        if (adjectives is null) return null;

        return MemberSelections.Of(source, kind, adjectives);
    }

    private ProjectSelection? ReadProjectSelection(StrictJsonValue value, string what)
    {
        StrictJsonValue? obj = AsObject(value, what);
        if (obj is null) return null;
        if (!OnlyKeys(obj, "a project selection", "adjectives")) return null;

        IReadOnlyList<ProjectAdjective>? adjectives =
            ReadAdjectives(obj, NodeReaders.ProjectAdjectiveWhere, NodeReaders.ProjectAdjectiveArms);
        if (adjectives is null) return null;

        return new RefinedProjectSelection(_arch, adjectives);
    }

    private Constraint? ReadConstraint(StrictJsonValue value, string what)
    {
        return ReadNode(value, what, NodeReaders.ConstraintWhere, NodeReaders.ConstraintArms);
    }

    private SelectionNoun? ReadNoun(StrictJsonValue value)
    {
        return ReadNode(value, Field("noun"), NodeReaders.NounWhere, NodeReaders.NounArms);
    }

    /// <summary>One node of a family: an object whose discriminator picks the arm that reads the rest.</summary>
    private TNode? ReadNode<TNode>(
        StrictJsonValue value, string what, string family,
        IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, TNode?>> arms)
        where TNode : class
    {
        StrictJsonValue? node = AsObject(value, what);
        if (node is null) return null;

        string? discriminator = Discriminator(node, family);
        if (discriminator is null) return null;

        if (arms.TryGetValue(discriminator, out Func<ModelNodeReader, StrictJsonValue, TNode?> arm))
            return arm(this, node);

        NotA(node, discriminator, family);
        return null;
    }

    private IReadOnlyList<TNode>? ReadAdjectives<TNode>(
        StrictJsonValue obj, string family,
        IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, TNode?>> arms)
        where TNode : class
    {
        StrictJsonValue? array = Adjectives(obj);
        if (array is null) return Failed ? null : Array.Empty<TNode>();

        return Each(array, element => ReadNode(element, EntryOf("adjectives"), family, arms));
    }

    // The adjectives array, or null for "there is none" — absence reads as empty, and an empty array is
    // accepted too, because a hand-edited file may carry one.
    private StrictJsonValue? Adjectives(StrictJsonValue obj)
    {
        StrictJsonValue? value = Optional(obj, "adjectives");
        if (value is null) return null;

        return AsArray(value, Field("adjectives"));
    }

    private string? Discriminator(StrictJsonValue node, string where)
    {
        return RequiredText(node, where, "node");
    }

    // Positioned at the value that named the discriminator rather than at the node, because that word is what
    // a reader has to change.
    private void NotA(StrictJsonValue node, string discriminator, string family)
    {
        RefuseAt(node.GetProperty("node"), JsonText.Quote(discriminator) + " is not " + family + ".");
    }

    internal Selection? RequiredSelection(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : ReadSelection(value, Field(key));
    }

    internal MemberSelection? RequiredMemberSelection(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : ReadMemberSelection(value, Field(key));
    }

    internal IReadOnlyList<MemberSelection>? RequiredMemberSelections(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? array = RequiredArray(obj, where, key);
        return array is null ? null : Each(array, element => ReadMemberSelection(element, EntryOf(key)));
    }

    internal ProjectSelection? RequiredProjectSelection(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : ReadProjectSelection(value, Field(key));
    }

    internal IReadOnlyList<Selection>? RequiredOperands(StrictJsonValue obj, string where)
    {
        StrictJsonValue? array = RequiredArray(obj, where, "operands");
        return array is null ? null : Each(array, element => ReadSelection(element, EntryOf("operands")));
    }

    internal IReadOnlyList<Member>? RequiredMembers(StrictJsonValue obj, string where)
    {
        StrictJsonValue? array = RequiredArray(obj, where, "members");
        return array is null ? null : Each(array, ReadMember);
    }

    internal TypeAnchor? RequiredAnchor(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : ReadAnchor(value, Field(key));
    }

    internal IReadOnlyList<TypeAnchor>? RequiredAnchors(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? array = RequiredArray(obj, where, key);
        return array is null ? null : Each(array, element => ReadAnchor(element, EntryOf(key)));
    }

    internal Layer? RequiredLayer(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        if (value is null) return null;

        string? name = AsText(value, Field(key));
        return name is null ? null : ResolveLayer(value, name);
    }

    internal IReadOnlyList<Layer>? RequiredLayers(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? array = RequiredArray(obj, where, key);
        if (array is null) return null;

        return Each(array, element => AsText(element, EntryOf(key)) is { } name ? ResolveLayer(element, name) : null);
    }

    internal string? RequiredNamespaceGlob(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        if (value is null) return null;

        string? glob = AsText(value, Field(key));
        if (glob is null) return null;

        return CheckGlob(value, glob, "namespace pattern", NamespacePattern.Validate) ? glob : null;
    }

    /// <summary>
    ///     A list of path globs, each refused on <see cref="PathGlob.Validate" />'s terms, so a glob spec-load
    ///     validation would have refused never reaches the matcher, which throws on one.
    /// </summary>
    internal IReadOnlyList<string>? RequiredPathGlobs(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? array = RequiredArray(obj, where, key);
        if (array is null) return null;

        return Each(array, element =>
        {
            string? glob = AsText(element, EntryOf(key));
            return glob is not null && CheckGlob(element, glob, "path glob", PathGlob.Validate) ? glob : null;
        });
    }

    internal string? RequiredTypeNameGlob(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        if (value is null) return null;

        string? glob = AsText(value, Field(key));
        if (glob is null) return null;

        if (!string.IsNullOrWhiteSpace(glob)) return glob;

        RefuseAt(value, "type-name pattern " + JsonText.Quote(glob) + " is blank.");
        return null;
    }

    internal string? RequiredText(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : AsText(value, Field(key));
    }

    internal IReadOnlyList<string>? RequiredTexts(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : Texts(value, key);
    }

    /// <summary>
    ///     Refuses an empty <paramref name="list" /> at <paramref name="obj" />'s <paramref name="key" />. For the
    ///     lists the spec refuses empty when it loads (GRAMMAR §8 item 36): a file holding one was not written
    ///     from a spec that loaded, and reading it would mint a model the spec could not.
    /// </summary>
    internal IReadOnlyList<T>? NonEmpty<T>(IReadOnlyList<T>? list, StrictJsonValue obj, string key)
    {
        if (list is not { Count: 0 }) return list;

        RefuseAt(Optional(obj, key)!, Field(key) + " must not be empty.");
        return null;
    }

    internal bool RequiredEnum<TEnum>(StrictJsonValue obj, string where, string key, string family, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        StrictJsonValue? value = Required(obj, where, key);
        if (value is null) return false;

        string? text = AsText(value, Field(key));
        if (text is null) return false;

        if (TryParseEnum(text, out parsed)) return true;

        RefuseAt(value, JsonText.Quote(text) + " is not " + family + ".");
        return false;
    }

    private bool RequiredFlag(StrictJsonValue obj, string where, string key, out bool flag)
    {
        flag = false;
        StrictJsonValue? value = Required(obj, where, key);
        if (value is null) return false;

        return AsFlag(value, key, out flag);
    }

    /// <summary>An enum that is left out when it means "any", so absence is a value of its own.</summary>
    internal bool OptionalEnum<TEnum>(StrictJsonValue obj, string key, string family, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        StrictJsonValue? value = Optional(obj, key);
        if (value is null) return true;

        string? text = AsText(value, Field(key));
        if (text is null) return false;

        if (TryParseEnum(text, out TEnum read))
        {
            parsed = read;
            return true;
        }

        RefuseAt(value, JsonText.Quote(text) + " is not " + family + ".");
        return false;
    }

    private bool OptionalFlag(StrictJsonValue obj, string key, out bool flag)
    {
        flag = false;
        StrictJsonValue? value = Optional(obj, key);
        if (value is null) return true;

        return AsFlag(value, key, out flag);
    }

    internal bool OnlyKeys(StrictJsonValue obj, string where, params string[] allowed)
    {
        foreach (StrictJsonProperty member in obj.EnumerateObject())
        {
            if (allowed.Contains(member.Name, StringComparer.Ordinal)) continue;

            Refuse(ModelRefusalKind.Malformed, where + " does not take " + JsonText.Quote(member.Name) + ".",
                member.Position);
            return false;
        }

        return true;
    }

    private Member? ReadMember(StrictJsonValue element)
    {
        StrictJsonValue? obj = AsObject(element, EntryOf("members"));
        if (obj is null) return null;
        if (!OnlyKeys(obj, MemberWhere, "declaring", "name", "isMethod")) return null;

        TypeAnchor? declaring = RequiredAnchor(obj, MemberWhere, "declaring");
        if (declaring is null) return null;

        string? name = RequiredText(obj, MemberWhere, "name");
        if (name is null) return null;

        if (!RequiredFlag(obj, MemberWhere, "isMethod", out bool isMethod)) return null;

        return new Member(_arch, declaring, name, isMethod);
    }

    private TypeAnchor? ReadAnchor(StrictJsonValue value, string what)
    {
        StrictJsonValue? obj = AsObject(value, what);
        if (obj is null) return null;
        if (!OnlyKeys(obj, AnchorWhere, "name", "onDefinition", "path")) return null;

        string? name = RequiredText(obj, AnchorWhere, "name");
        if (name is null) return null;

        if (!OptionalFlag(obj, "onDefinition", out bool onDefinition)) return null;

        IReadOnlyList<string>? path = null;
        if (Optional(obj, "path") is { } pathValue)
        {
            path = Texts(pathValue, "path");
            if (path is null) return null;
        }

        return TypeAnchor.FromParsed(name, onDefinition, path);
    }

    private Layer? ResolveLayer(StrictJsonValue at, string name)
    {
        if (_layers.TryGetValue(name, out Layer layer)) return layer;

        RefuseAt(at, "layer " + JsonText.Quote(name) + " is not declared above this point.");
        return null;
    }

    private bool CheckGlob(StrictJsonValue at, string glob, string noun, Func<string, string?> validate)
    {
        string? reason = validate(glob);
        if (reason is null) return true;

        RefuseAt(at, noun + " " + JsonText.Quote(glob) + " " + reason + ".");
        return false;
    }

    private IReadOnlyList<string>? Texts(StrictJsonValue value, string key)
    {
        StrictJsonValue? array = AsArray(value, Field(key));
        if (array is null) return null;

        return Each(array, element => AsText(element, EntryOf(key)));
    }

    private string? OptionalText(StrictJsonValue obj, string key)
    {
        StrictJsonValue? value = Optional(obj, key);
        return value is null ? null : AsText(value, Field(key));
    }

    private StrictJsonValue? AsObject(StrictJsonValue value, string what)
    {
        if (value.ValueKind == StrictJsonKind.Object) return value;

        RefuseAt(value, what + " must be an object.");
        return null;
    }

    private StrictJsonValue? AsArray(StrictJsonValue value, string what)
    {
        if (value.ValueKind == StrictJsonKind.Array) return value;

        RefuseAt(value, what + " must be an array.");
        return null;
    }

    private string? AsText(StrictJsonValue value, string what)
    {
        if (value.ValueKind == StrictJsonKind.String) return value.GetString();

        RefuseAt(value, what + " must be a string.");
        return null;
    }

    private bool AsFlag(StrictJsonValue value, string key, out bool flag)
    {
        flag = false;
        bool isLiteral = value.ValueKind == StrictJsonKind.True || value.ValueKind == StrictJsonKind.False;
        if (!isLiteral)
        {
            RefuseAt(value, Field(key) + " must be true or false.");
            return false;
        }

        flag = value.GetBoolean();
        return true;
    }

    private StrictJsonValue? Required(StrictJsonValue obj, string where, string key)
    {
        if (obj.TryGetProperty(key, out StrictJsonValue? value)) return value;

        RefuseAt(obj, where + " has no " + JsonText.Quote(key) + ".");
        return null;
    }

    private StrictJsonValue? RequiredArray(StrictJsonValue obj, string where, string key)
    {
        StrictJsonValue? value = Required(obj, where, key);
        return value is null ? null : AsArray(value, Field(key));
    }

    private static List<T>? Each<T>(StrictJsonValue array, Func<StrictJsonValue, T?> read)
        where T : class
    {
        var items = new List<T>();
        foreach (StrictJsonValue element in array.EnumerateArray())
        {
            T? item = read(element);
            if (item is null) return null;

            items.Add(item);
        }

        return items;
    }

    internal StrictJsonValue? Optional(StrictJsonValue obj, string key)
    {
        obj.TryGetProperty(key, out StrictJsonValue? value);
        return value;
    }

    internal void RefuseAt(StrictJsonValue at, string message)
    {
        Refuse(ModelRefusalKind.Malformed, message, at.Position);
    }

    private void Refuse(ModelRefusalKind kind, string message, JsonPosition at)
    {
        Refusal ??= new ModelRefusal(kind, message, at);
    }

    private static string Field(string key)
    {
        return JsonText.Quote(key);
    }

    private static string EntryOf(string key)
    {
        return "an entry of " + JsonText.Quote(key);
    }

    // The enum spellings are the writer's own rule read backwards, so a member renamed in one place
    // cannot keep reading in the other.
    private static bool TryParseEnum<TEnum>(string text, out TEnum parsed)
        where TEnum : struct, Enum
    {
        foreach (string name in Enum.GetNames(typeof(TEnum)))
        {
            if (!string.Equals(NodeWriters.Camel(name), text, StringComparison.Ordinal)) continue;

            parsed = (TEnum)Enum.Parse(typeof(TEnum), name);
            return true;
        }

        parsed = default;
        return false;
    }
}
