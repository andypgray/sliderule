using SlideRule.Internal;
using SlideRule.Model;
using Code = SlideRule.Validation.SpecValidationErrorCode;
using NamespaceGlob = SlideRule.Fluent.NamespacePattern;

namespace SlideRule.Validation;

/// <summary>
///     One string slot of the authoring surface: which call it is, the noun its messages name the value
///     by, and the verdict a literal written there draws (GRAMMAR §8).
/// </summary>
/// <remarks>
///     One instance per slot, so each label literal is written once and the noun a value is named by
///     cannot fork between the walks that reach it. <see cref="Decide" /> owns the whole precedence for
///     its slot — blank first, then the slot's own structural check — which is what lets a slot be held
///     by more than one caller and still be reported once.
///     <para>
///         Two slots carry the label "project name" under different codes. <c>arch.Project(...)</c> and
///         <c>MustResideInProject(...)</c> report a blank as
///         <see cref="SpecValidationErrorCode.BlankPattern" />, while <c>arch.Projects.Named(...)</c>
///         reports it as <see cref="SpecValidationErrorCode.BlankProjectPattern" />, because a blank
///         project name and a blank project glob fail in opposite directions and the catalog entries have
///         to be able to say so. A verdict carries the code, so those need an instance each.
///     </para>
///     <para>
///         "member name" is shared the same way. <c>arch.Member(type, name)</c> reports a blank as
///         <see cref="SpecValidationErrorCode.BlankMemberName" />, because a blank anchor name never resolves
///         to a member, while a member selection's <c>Named(...)</c> reports it as
///         <see cref="SpecValidationErrorCode.BlankPattern" /> beside the type side's exact names, because a
///         blank exact name is the same authoring slip on either stratum.
///     </para>
/// </remarks>
internal sealed class SpecSlot
{
    /// <summary>A namespace glob: <c>arch.Namespace</c>, <c>InNamespace</c>, <c>MustResideInNamespace</c>, a layer's region.</summary>
    internal static readonly SpecSlot NamespacePattern = new(nameof(NamespacePattern), "namespace pattern", SlotCheck.NamespaceStructure);

    /// <summary>A type-name glob: <c>WithNameMatching</c>, <c>MustHaveNameMatching</c>.</summary>
    internal static readonly SpecSlot NamePattern = new(nameof(NamePattern), "name pattern", SlotCheck.Pattern);

    /// <summary>One exact type name on <c>Named</c>.</summary>
    internal static readonly SpecSlot ExactTypeName = new(nameof(ExactTypeName), "type name", SlotCheck.Pattern);

    /// <summary>A type-name suffix: <c>WithSuffix</c>, <c>MustHaveSuffix</c>.</summary>
    internal static readonly SpecSlot Suffix = new(nameof(Suffix), "suffix", SlotCheck.Pattern);

    /// <summary>A type-name prefix: <c>WithPrefix</c>, <c>MustHavePrefix</c>.</summary>
    internal static readonly SpecSlot Prefix = new(nameof(Prefix), "prefix", SlotCheck.Pattern);

    /// <summary>A member-name glob, on the member axis.</summary>
    internal static readonly SpecSlot MemberNamePattern = new(nameof(MemberNamePattern), "member name pattern", SlotCheck.Pattern);

    /// <summary>A member-name suffix.</summary>
    internal static readonly SpecSlot MemberSuffix = new(nameof(MemberSuffix), "member suffix", SlotCheck.Pattern);

    /// <summary>A member-name prefix.</summary>
    internal static readonly SpecSlot MemberPrefix = new(nameof(MemberPrefix), "member prefix", SlotCheck.Pattern);

    /// <summary>
    ///     One exact member name on a member selection's <c>Named</c>, which reports a blank as
    ///     <see cref="Code.BlankPattern" />.
    /// </summary>
    internal static readonly SpecSlot ExactMemberName = new(nameof(ExactMemberName), "member name", SlotCheck.Pattern);

    /// <summary>An attribute anchor spelled as a definition FQN, on either axis and either polarity.</summary>
    internal static readonly SpecSlot AttributeName = new(nameof(AttributeName), "attribute name", SlotCheck.Pattern);

    /// <summary>An interface anchor spelled as a definition FQN.</summary>
    internal static readonly SpecSlot InterfaceName = new(nameof(InterfaceName), "interface name", SlotCheck.Pattern);

    /// <summary>A base-type anchor spelled as a definition FQN.</summary>
    internal static readonly SpecSlot BaseTypeName = new(nameof(BaseTypeName), "base type name", SlotCheck.Pattern);

    /// <summary>A member <c>Returning</c> anchor spelled as a definition FQN.</summary>
    internal static readonly SpecSlot ReturnTypeName = new(nameof(ReturnTypeName), "return type name", SlotCheck.Pattern);

    /// <summary>A <c>MustAcceptParameter</c> anchor spelled as a definition FQN.</summary>
    internal static readonly SpecSlot ParameterTypeName = new(nameof(ParameterTypeName), "parameter type name", SlotCheck.Pattern);

    /// <summary>The name template of <c>MustHaveExactlyOneCounterpart</c>.</summary>
    internal static readonly SpecSlot CounterpartTemplate = new(nameof(CounterpartTemplate), "counterpart name template", SlotCheck.CounterpartTemplate);

    /// <summary>
    ///     A project named as a noun or by <c>MustResideInProject</c>, which reports a blank as
    ///     <see cref="Code.BlankPattern" />.
    /// </summary>
    internal static readonly SpecSlot ProjectName = new(nameof(ProjectName), "project name", SlotCheck.Pattern);

    /// <summary>A glob on <c>arch.Projects.Matching</c>.</summary>
    internal static readonly SpecSlot ProjectNamePattern = new(nameof(ProjectNamePattern), "project name pattern", SlotCheck.ProjectOperand);

    /// <summary>
    ///     A name on <c>arch.Projects.Named</c>: the same noun as <see cref="ProjectName" /> under the project stratum's
    ///     own code.
    /// </summary>
    internal static readonly SpecSlot ProjectNameOperand = new(nameof(ProjectNameOperand), "project name", SlotCheck.ProjectOperand);

    /// <summary>The prose of <c>Because</c>, on a rule or a scope.</summary>
    internal static readonly SpecSlot Because = new(nameof(Because), "Because", SlotCheck.Prose);

    /// <summary>The prose of <c>Fix</c>.</summary>
    internal static readonly SpecSlot Fix = new(nameof(Fix), "Fix", SlotCheck.Prose);

    /// <summary>The URL of <c>Citation</c>, which is prose and a link both.</summary>
    internal static readonly SpecSlot Citation = new(nameof(Citation), "Citation", SlotCheck.Citation);

    /// <summary>The <c>from</c> argument of <c>Migrate</c>.</summary>
    internal static readonly SpecSlot MigrateFrom = new(nameof(MigrateFrom), "Migrate from", SlotCheck.Prose);

    /// <summary>
    ///     The path of <c>Baseline</c>, walked as prose: blank and multi-line are the two ways to write one no later
    ///     stage can report.
    /// </summary>
    internal static readonly SpecSlot Baseline = new(nameof(Baseline), "Baseline", SlotCheck.Prose);

    /// <summary>The prose of <c>Dragons</c>.</summary>
    internal static readonly SpecSlot Dragons = new(nameof(Dragons), "Dragons", SlotCheck.Prose);

    /// <summary>The path of <c>DragonsDoc</c>, walked as prose on <see cref="Baseline" />'s terms.</summary>
    internal static readonly SpecSlot DragonsDoc = new(nameof(DragonsDoc), "DragonsDoc", SlotCheck.Prose);

    /// <summary>The description an escape hatch carries: a <c>Where</c> or a custom <c>Must</c>, on any axis.</summary>
    internal static readonly SpecSlot Description = new(nameof(Description), "description", SlotCheck.Prose);

    /// <summary>The prose of a layer's <c>Purpose</c>.</summary>
    internal static readonly SpecSlot Purpose = new(nameof(Purpose), "purpose", SlotCheck.Prose);

    /// <summary>One framework moniker on <c>MustOnlyTarget</c> or <c>MustTarget</c>.</summary>
    internal static readonly SpecSlot TargetFramework = new(nameof(TargetFramework), "target framework", SlotCheck.TargetFramework);

    /// <summary>A package identifier glob on <c>MustOnlyReferencePackages</c> or <c>MustNotReferencePackages</c>.</summary>
    internal static readonly SpecSlot PackagePattern = new(nameof(PackagePattern), "package pattern", SlotCheck.PackagePattern);

    /// <summary>The MSBuild property name on <c>MustSetProperty</c>.</summary>
    internal static readonly SpecSlot PropertyName = new(nameof(PropertyName), "property name", SlotCheck.Property);

    /// <summary>The value <c>MustSetProperty</c> requires.</summary>
    internal static readonly SpecSlot PropertyValue = new(nameof(PropertyValue), "property value", SlotCheck.Property);

    /// <summary>The name of <c>arch.Member(type, name)</c>.</summary>
    internal static readonly SpecSlot MemberName = new(nameof(MemberName), "member name", SlotCheck.MemberName);

    /// <summary>The ID of <c>arch.Rule</c>.</summary>
    internal static readonly SpecSlot RuleId = new(nameof(RuleId), "rule ID", SlotCheck.Id);

    /// <summary>The ID of <c>arch.Scope</c>, which is held to the same grammar and reported in the same sentence.</summary>
    internal static readonly SpecSlot ScopeId = new(nameof(ScopeId), "scope ID", SlotCheck.Id);

    /// <summary>The name of <c>arch.Layer</c>.</summary>
    internal static readonly SpecSlot LayerName = new(nameof(LayerName), "layer name", SlotCheck.LayerName);

    /// <summary>Every slot, in declaration order.</summary>
    internal static readonly IReadOnlyList<SpecSlot> All =
    [
        NamespacePattern,
        NamePattern,
        ExactTypeName,
        Suffix,
        Prefix,
        MemberNamePattern,
        MemberSuffix,
        MemberPrefix,
        ExactMemberName,
        AttributeName,
        InterfaceName,
        BaseTypeName,
        ReturnTypeName,
        ParameterTypeName,
        CounterpartTemplate,
        ProjectName,
        ProjectNamePattern,
        ProjectNameOperand,
        Because,
        Fix,
        Citation,
        MigrateFrom,
        Baseline,
        Dragons,
        DragonsDoc,
        Description,
        Purpose,
        TargetFramework,
        PackagePattern,
        PropertyName,
        PropertyValue,
        MemberName,
        RuleId,
        ScopeId,
        LayerName
    ];

    private static readonly Dictionary<string, SpecSlot> Index = All.ToDictionary(slot => slot.Name, StringComparer.Ordinal);

    private readonly SlotCheck _check;

    private SpecSlot(string name, string label, SlotCheck check)
    {
        Name = name;
        Label = label;
        _check = check;
    }

    /// <summary>
    ///     The slot's own identifier, which is the name of the field holding it. It is what a caller with
    ///     no reference to the instance names the slot by.
    /// </summary>
    internal string Name { get; }

    /// <summary>The noun this slot's messages name the value by ("Blank interface name on 'rule/id'.").</summary>
    internal string Label { get; }

    /// <summary>The slot of this name, or null when nothing declares it.</summary>
    internal static SpecSlot? ByName(string name)
    {
        return Index.TryGetValue(name, out SpecSlot? slot) ? slot : null;
    }

    /// <summary>
    ///     What a literal written in this slot decides, or null when it draws no verdict one literal can
    ///     reach on its own.
    /// </summary>
    /// <remarks>
    ///     The whole precedence for the slot, in the order the catalog reports it: blank first, so every
    ///     kind shares one blank code and one blank sentence, then whatever structure the slot carries of
    ///     its own. A namespace glob's second verdict is asked of <c>NamespacePattern.Validate</c> rather
    ///     than reproduced here, so what the matcher treats as literal and what the catalog refuses to
    ///     strand a <c>*</c> in cannot drift; that method keeps its own blank branch for the readers that
    ///     call it with no blank check in front.
    ///     <para>
    ///         A null is read as the empty string. The model holds a value that was never written as null
    ///         (a <c>Migrate</c> with no <c>from</c>), every arm below opens on a blank check, and a blank
    ///         and an absent value draw the same sentence.
    ///     </para>
    /// </remarks>
    internal LiteralVerdict? Decide(string? value)
    {
        string literal = value ?? string.Empty;

        return _check switch
        {
            SlotCheck.Pattern => Blank(literal, Code.BlankPattern),
            SlotCheck.NamespaceStructure => Blank(literal, Code.BlankPattern) ?? Unanchored(literal),
            SlotCheck.CounterpartTemplate => Blank(literal, Code.BlankPattern) ?? PlaceholderFree(literal),
            SlotCheck.ProjectOperand => Blank(literal, Code.BlankProjectPattern),
            SlotCheck.Prose => Blank(literal, Code.BlankProse) ?? MultiLine(literal),
            SlotCheck.Citation => Blank(literal, Code.BlankProse) ?? MultiLine(literal) ?? NotAnHttpUrl(literal),
            SlotCheck.TargetFramework => Blank(literal, Code.BlankTargetFramework),
            SlotCheck.PackagePattern => Blank(literal, Code.BlankPackagePattern),
            SlotCheck.Property => Blank(literal, Code.BlankProperty),
            SlotCheck.MemberName => Blank(literal, Code.BlankMemberName),
            SlotCheck.Id => MalformedId(literal),
            SlotCheck.LayerName => BlankLayerName(literal),
            _ => null
        };
    }

    // The one blank-operand sentence — "Blank {label} on {subject}." — every stratum's blank check
    // renders. The catalog parts them by CODE, not by wording: they fail in different directions and each
    // entry has to be able to say so, while a reader who has met one blank-operand error has met them all.
    private LiteralVerdict? Blank(string literal, Code code)
    {
        return string.IsNullOrWhiteSpace(literal) ? LiteralVerdict.About(code, $"Blank {Label}", ".") : null;
    }

    // GRAMMAR §8 item 6: a line break in a value every consuming surface prints on one line.
    private LiteralVerdict? MultiLine(string literal)
    {
        return SingleLineProse.IsMultiLine(literal)
            ? LiteralVerdict.About(Code.MultiLineProse, $"Multi-line {Label}", "; prose fields are single-line.")
            : null;
    }

    // GRAMMAR §8 item 16: a glob whose trailing `.*` sits behind a literal prefix carrying a `*` can never
    // match. The clause comes back from the matcher; the frame is this catalog's.
    private LiteralVerdict? Unanchored(string literal)
    {
        string? reason = NamespaceGlob.Validate(literal);
        if (reason is null) return null;

        return LiteralVerdict.About(Code.UnanchoredSubtreePattern, $"The {Label} '{literal}'", $" {reason}.");
    }

    // GRAMMAR §8 item 26: a template with no {Name} in it is a constant predicate — every subject derives
    // the same fixed name, so the rule is a cardinality law wearing a correspondence law's clothes, and it
    // reds or greens the whole subject set together. The match is ordinal, which is what makes the
    // '{name}' typo fail here rather than at check time.
    private LiteralVerdict? PlaceholderFree(string literal)
    {
        int placeholder = literal.IndexOf(MustHaveExactlyOneCounterpartConstraint.NamePlaceholder, StringComparison.Ordinal);
        if (placeholder >= 0) return null;

        return LiteralVerdict.About(Code.CounterpartTemplateWithoutPlaceholder, $"The {Label} '{literal}'",
            " contains no '{Name}' placeholder, so every subject derives the same fixed name — a cardinality "
            + "claim, not a correspondence; use a template such as 'I{Name}' (substitution is case-sensitive).");
    }

    // GRAMMAR §8 item 30: every consuming surface treats a citation as a link — the agent block autolinks
    // it, SARIF hands it to a scanning service as helpUri — and none of them can report a value that is not
    // one. The scheme allow-list is what makes a pasted page title or a repo-relative path fail at spec
    // build rather than downstream.
    private static LiteralVerdict? NotAnHttpUrl(string literal)
    {
        bool absolute = Uri.TryCreate(literal, UriKind.Absolute, out Uri parsed);
        if (absolute && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)) return null;

        return LiteralVerdict.About(Code.MalformedCitation, "Malformed citation",
            $": '{literal}' is not an absolute http(s) URL.");
    }

    // GRAMMAR §8 item 7. The sentence names no subject because the ID is the subject: quoting it twice
    // would say the same thing in two places.
    private static LiteralVerdict? MalformedId(string literal)
    {
        if (RuleIdSyntax.IsValid(literal)) return null;

        return LiteralVerdict.Whole(Code.MalformedId,
            $"Malformed ID '{literal}'; expected `area/rule-name` matching {RuleIdSyntax.Pattern}.");
    }

    // A blank layer name is the one blank the shared sentence cannot render: the name is what is missing,
    // so there is nothing to name it by. The steer points at the argument to write instead.
    private static LiteralVerdict? BlankLayerName(string literal)
    {
        if (!string.IsNullOrWhiteSpace(literal)) return null;

        return LiteralVerdict.Whole(Code.BlankLayerName,
            "Blank layer name; name the layer in the first argument to arch.Layer(...).");
    }

    /// <summary>Which verdict procedure a slot runs, and so which codes a literal written there can draw.</summary>
    private enum SlotCheck
    {
        Pattern,
        NamespaceStructure,
        CounterpartTemplate,
        ProjectOperand,
        Prose,
        Citation,
        TargetFramework,
        PackagePattern,
        Property,
        MemberName,
        Id,
        LayerName
    }
}
