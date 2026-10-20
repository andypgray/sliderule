namespace SlideRule.Validation;

/// <summary>Which mistake in a spec was found, one per <see cref="SpecValidationError" />.</summary>
// A new code is APPENDED, never slotted in beside a related one: the numbers are published as the
// spec analyzer's diagnostic IDs (SR and then 1000 plus the code), so an insertion renumbers every ID
// below it and breaks any suppression that names one. Read the set by name.
internal enum SpecValidationErrorCode
{
    /// <summary>
    ///     Two rules or scopes share an ID across the spec classes of a build, counting the
    ///     <c>{scope-id}/containment</c> and <c>{scope-id}/tripwire</c> rules each scope contributes.
    /// </summary>
    DuplicateId,

    /// <summary>
    ///     A rule ID equal to a scope's ID or beneath it, inside the <c>{scope-id}/</c> prefix the scope reserves
    ///     for its own rules.
    /// </summary>
    IdExtendsScope,

    /// <summary>
    ///     An <c>arch.Rule(id)</c> that never reached <c>Enforce</c> or <c>Migrate</c>, or an
    ///     <c>arch.Scope(id)</c> that never reached <c>Quarantine</c> or <c>Caution</c>.
    /// </summary>
    MissingPosture,

    /// <summary>A rule or scope, whatever its posture, with no <c>Because</c>.</summary>
    MissingBecause,

    /// <summary>A scope with neither <c>Dragons</c> nor <c>DragonsDoc</c>.</summary>
    MissingDragons,

    /// <summary>
    ///     A prose value that is empty or only whitespace: a <c>Because</c>, a <c>Fix</c>, a <c>Citation</c>, a
    ///     migration's <c>from</c> description, a <c>Baseline</c> path, a scope's <c>Dragons</c> or <c>DragonsDoc</c>,
    ///     a layer's <c>Purpose</c>, or the description a <c>Where</c> or a custom <c>Must</c> requires.
    /// </summary>
    BlankProse,

    /// <summary>A prose value carrying a line break.</summary>
    MultiLineProse,

    /// <summary>
    ///     The same call made twice where it is allowed at most once: two <c>Because</c>s, two
    ///     <c>Baseline</c>s, two <c>Citation</c>s on one rule, a layer's <c>Purpose</c> twice.
    /// </summary>
    RepeatedCall,

    /// <summary>
    ///     A rule or scope ID that <see cref="RuleIdSyntax.Pattern" /> refuses: anything but lowercase letters,
    ///     digits and hyphens in segments joined by <c>/</c>.
    /// </summary>
    MalformedId,

    /// <summary><c>BoundaryOnlyVia()</c> called with no types.</summary>
    EmptyBoundary,

    /// <summary>Two layers declared with the same name. Layer names are unique within the spec.</summary>
    DuplicateLayerName,

    /// <summary>
    ///     A rule or scope used a selection built on a different <see cref="Arch" />. A layer's selection
    ///     definition is checked too, and reported against the layer whether or not any rule names it.
    /// </summary>
    ForeignSelection,

    /// <summary>An <c>arch.Member(typeof(T), name)</c> used by a rule was given an empty member name.</summary>
    BlankMemberName,

    /// <summary>
    ///     The type named in an <c>arch.Member(typeof(T), name)</c> does not itself declare that member; an
    ///     inherited one does not count.
    /// </summary>
    MemberNotDeclared,

    /// <summary>A rule used an <c>arch.Member(...)</c> built on a different <see cref="Arch" />.</summary>
    ForeignMember,

    /// <summary>
    ///     A <c>Returning</c> given a closed generic such as <c>typeof(Task&lt;int&gt;)</c>: return types are
    ///     matched by definition, so only the open one, <c>typeof(Task&lt;&gt;)</c>, can match.
    /// </summary>
    MemberReturningClosedGeneric,

    /// <summary>
    ///     A glob, name or affix left empty, on the type and member sides alike, layer globs included: a namespace
    ///     or type-name glob, a path glob on <c>Under</c>, an exact name on <c>Named</c>, a project name on
    ///     <c>arch.Project</c> or <c>MustResideInProject</c>, a suffix or prefix, a
    ///     <c>MustHaveExactlyOneCounterpart</c> name template, or a string-named attribute, interface, base type,
    ///     return type or parameter type. A blank affix is true of every name, and a blank glob fails at check
    ///     time.
    /// </summary>
    BlankPattern,

    /// <summary>
    ///     A namespace glob ending in <c>.*</c> whose text before that ending also contains a <c>*</c>, such
    ///     as <c>MyApp.*.Controllers.*</c>: everything before a trailing <c>.*</c> is matched literally, so it
    ///     can never match. Type-name globs and affixes have no subtree form, so this never reports them.
    /// </summary>
    UnanchoredSubtreePattern,

    /// <summary>
    ///     A rule or scope given a posture twice, which only an <c>arch.Rule(id)</c> or <c>arch.Scope(id)</c>
    ///     value held and called again can do; the second call silently replaces the first.
    /// </summary>
    RepeatedPosture,

    /// <summary>
    ///     An <c>arch.Member&lt;T&gt;(x =&gt; ...)</c> or <c>arch.Member(() =&gt; ...)</c> lambda, or one passed
    ///     to <c>MustNotUse</c>, that does not name one declared member.
    /// </summary>
    MemberExpressionUnresolvable,

    /// <summary>
    ///     An <c>arch.Registered</c> given a lifetime outside the defined set, such as a cast
    ///     <c>(Lifetime)7</c>.
    /// </summary>
    UndefinedLifetime,

    /// <summary>
    ///     A <c>MustAcceptParameter</c> given a closed generic such as <c>typeof(IProgress&lt;int&gt;)</c>:
    ///     parameter types are matched by definition, so only the open one can match.
    /// </summary>
    MemberAcceptParameterClosedGeneric,

    /// <summary>
    ///     A hierarchy verb given a type of the wrong category, which never matches, so the positive verb always
    ///     fails and the negative always passes: <c>MustImplement</c> and <c>MustNotImplement</c> take an
    ///     interface, <c>MustDeriveFrom</c> and <c>MustNotDeriveFrom</c> a non-interface, and
    ///     <c>MustBeAttributedWith</c> and <c>MustNotBeAttributedWith</c> a type deriving from
    ///     <see cref="System.Attribute" />, bare <c>typeof(Attribute)</c> excluded. Reported for <c>typeof</c>
    ///     only: a name given as a string carries no type to categorize.
    /// </summary>
    HierarchyAnchorWrongCategory,

    /// <summary>
    ///     A rule or scope used a project selection built on a different <see cref="Arch" />, including one
    ///     nested inside an <c>Except</c>.
    /// </summary>
    ForeignProjectSelection,

    /// <summary>
    ///     An empty project name on <c>arch.Projects.Named</c>, which would match no project, or an empty glob on
    ///     <c>arch.Projects.Matching</c>, which would match every one.
    /// </summary>
    BlankProjectPattern,

    /// <summary>
    ///     An empty target framework on <c>MustOnlyTarget</c> or <c>MustTarget</c>. A blank moniker matches
    ///     nothing, so <c>MustOnlyTarget</c> would permit less than written and <c>MustTarget</c> would fail
    ///     every project it reads.
    /// </summary>
    BlankTargetFramework,

    /// <summary>
    ///     A <c>MustHaveExactlyOneCounterpart</c> name template with no <c>{Name}</c> placeholder, such as
    ///     <c>IService</c> or the case-mismatched <c>I{name}</c>: every subject would derive the same fixed name.
    /// </summary>
    CounterpartTemplateWithoutPlaceholder,

    /// <summary>
    ///     An <c>arch.Each</c> family used anywhere but as a rule subject, where a plain-set position would read
    ///     it as everything in it and lose its cells. A family reached through a member projection such as
    ///     <c>.Methods</c> is still a subject and is accepted.
    /// </summary>
    FamilyMisplaced,

    /// <summary><c>MustNotReferenceEachOther</c> over a subject that is not an <c>arch.Each</c> family.</summary>
    EachOtherWithoutFamily,

    /// <summary>
    ///     <c>MustNotHaveCircularReferences</c> over a subject that is not an <c>arch.Each</c> family of layers:
    ///     a plain selection has no cells, and the build already refuses circular project references.
    /// </summary>
    CircularReferencesNeedLayerFamily,

    /// <summary>
    ///     A <c>Citation</c> that is not an absolute <c>http</c> or <c>https</c> URL, such as a page title or a
    ///     relative path; every surface passes the value on as a link unread, so nothing downstream reports it.
    /// </summary>
    MalformedCitation,

    /// <summary>A layer declared with a name that is empty or only whitespace.</summary>
    BlankLayerName,

    /// <summary>
    ///     An empty package pattern on <c>MustOnlyReferencePackages</c>, where it permits nothing, or on
    ///     <c>MustNotReferencePackages</c>, where it forbids nothing: no package has an empty identifier.
    /// </summary>
    BlankPackagePattern,

    /// <summary>
    ///     An empty property name or value on <c>MustSetProperty</c>, which no project can declare (MSBuild treats
    ///     a property set to nothing as unset), so the rule would fail every project it reads.
    /// </summary>
    BlankProperty,

    /// <summary>
    ///     A member selection's <c>Except</c> given a payload of another single kind, such as fields taken from a
    ///     method selection, which removes nothing; a <c>.Members</c> payload holds every kind.
    /// </summary>
    MemberExceptKindDisjoint,

    /// <summary>
    ///     A path glob on <c>Under</c> written in a shape no declaring file can match: rooted, with a backslash, or
    ///     with a <c>.</c>, <c>..</c> or empty segment or a <c>**</c> inside a longer segment.
    /// </summary>
    MalformedPathGlob,

    /// <summary>
    ///     A call that takes a list given an empty one: <c>arch.AnyOf</c>, project <c>Named</c> or <c>Matching</c>,
    ///     member <c>Named</c> or <c>Except</c>, or <c>MustNotUse</c>.
    /// </summary>
    EmptyOperandList
}
