using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Validation;

namespace SlideRule;

/// <summary>
///     The object a spec's <see cref="IArchitectureSpec.Define" /> receives, on which everything is
///     declared. Build a selection from <see cref="Types" />, <c>Layer</c>, <see cref="Namespace" />,
///     <see cref="Project" />, <c>Type</c>, <c>AnyOf</c>, <c>Each</c> or <c>Registered</c>, or a project
///     selection from <see cref="Projects" />; declare a rule with <see cref="Rule" /> and a scope with
///     <see cref="Scope" />. One instance is shared by every spec class in a build, so rule and scope
///     IDs must be unique across all of them.
///     <para>
///         Mistakes divide in two. A blank or malformed value (an empty glob, a malformed rule ID, a
///         missing reason) is collected and reported together when the spec is loaded, so one pass fixes
///         them all. A null argument or an undefined enum value is refused at the call itself, with the
///         offending parameter named.
///     </para>
/// </summary>
// Rule and Scope register their node at the call, before any posture verb runs, so a dangling
// arch.Rule("x") reaches validation (GRAMMAR §8 item 2) instead of vanishing; the builders they return
// mutate that registered node. Every selection and member is stamped with this instance as its owner,
// which is what the foreign-selection checks read (§8 items 10, 13 and 22).
public sealed class Arch
{
    private readonly List<LayerRegistration> _layers = [];
    private readonly List<Registration> _registrations = [];

    internal Arch()
    {
    }

    /// <summary>
    ///     Gets every type the solution declares, generated types included. Narrow it with adjectives such
    ///     as <c>InNamespace</c>, <c>Implementing</c> or <c>Named</c>, leave generated code out with
    ///     <c>Authored</c>, or use it as it is.
    /// </summary>
    public Selection Types => new RefinedSelection(this, TypesNoun.Instance);

    /// <summary>
    ///     Gets every project the solution declares, as build artifacts rather than as the types they
    ///     contain. Narrow it with <c>Named</c>, <c>Matching</c> or <c>Packable</c> and finish with a
    ///     project verb such as <c>MustOnlyTarget</c>, <c>MustReferenceNoPackages</c> or
    ///     <c>MustLockPackages</c>. <c>arch.Projects.Named("MyApp.Web")</c> is the project itself, where
    ///     <see cref="Project" /> is the types it declares.
    /// </summary>
    public ProjectSelection Projects => new RefinedProjectSelection(this, Array.Empty<ProjectAdjective>());

    /// <summary>The registered rule and scope anchors, in authoring order.</summary>
    internal IReadOnlyList<Registration> Registrations => _registrations;

    /// <summary>The declared layers with their purposes, in authoring order.</summary>
    internal IReadOnlyList<LayerRegistration> Layers => _layers;

    /// <summary>
    ///     Defines a named layer as the types whose namespace matches any of the globs, such as
    ///     <c>arch.Layer("Domain", "MyApp.Domain.*")</c>; a trailing <c>.*</c> covers the namespace
    ///     itself and everything beneath it. At least one glob is required, and a blank name or glob is
    ///     reported when the spec is loaded. The layer is a <see cref="Selection" />, usable as a rule subject or
    ///     target, and <see cref="SlideRule.Layer.Purpose" /> says what it is for. Layer names
    ///     must be unique within the spec.
    /// </summary>
    public Layer Layer(
        [SpecSlot(nameof(SpecSlot.LayerName))] string name,
        [SpecSlot(nameof(SpecSlot.NamespacePattern))]
        string glob,
        [SpecSlot(nameof(SpecSlot.NamespacePattern))]
        params string[] more)
    {
        Guard.NotNull(name, nameof(name));

        IReadOnlyList<string> globs = OperandList.OneOrMore(glob, more, nameof(glob));
        return Register(new LayerNoun(name, globs));
    }

    /// <summary>
    ///     Defines a named layer as exactly the types a selection names, such as
    ///     <c>arch.Layer("Core", arch.Project("MyApp.Core"))</c> or
    ///     <c>arch.Layer("Model", core.InNamespace("MyApp.Core.Model.*"))</c>; adjectives applied to the
    ///     layer afterwards narrow it further. A project selection (<see cref="Projects" />) is not
    ///     accepted: a layer is a set of types, not of projects. The layer is a <see cref="Selection" />,
    ///     usable as a rule subject or target, and <see cref="SlideRule.Layer.Purpose" /> says
    ///     what it is for. Layer names must be unique within the spec, and a blank one is reported when
    ///     the spec is loaded.
    /// </summary>
    public Layer Layer([SpecSlot(nameof(SpecSlot.LayerName))] string name, Selection definition)
    {
        Guard.NotNull(name, nameof(name));
        Guard.NotNull(definition, nameof(definition));

        return Register(new LayerNoun(name, definition));
    }


    /// <summary>
    ///     Selects the types whose namespace matches a glob, such as
    ///     <c>arch.Namespace("MyApp.Legacy.Billing.*")</c>. Matching is by dot-separated segment and
    ///     case-sensitive: a trailing <c>.*</c> covers the namespace itself and everything beneath it, a
    ///     <c>*</c> standing alone as a segment matches exactly one segment, a <c>*</c> inside a segment
    ///     (<c>MyApp.Legacy*</c>) matches within that segment only, and a lone <c>*</c> matches every
    ///     namespace. A blank glob, or one whose text before a trailing <c>.*</c> itself contains a
    ///     <c>*</c> (which could never match), is reported when the spec is loaded.
    /// </summary>
    public Selection Namespace([SpecSlot(nameof(SpecSlot.NamespacePattern))] string glob)
    {
        Guard.NotNull(glob, nameof(glob));

        return new RefinedSelection(this, new NamespaceNoun(glob));
    }

    /// <summary>
    ///     Selects the types a project declares, by the project's name as the solution lists it, such as
    ///     <c>arch.Project("MyApp.Web")</c>. Generated types count as declared, and a type several projects
    ///     declare, whether from one linked source file or a copy in each, is a member of each. For the
    ///     project itself as a build artifact use <see cref="Projects" />. A blank name is reported when the
    ///     spec is loaded.
    /// </summary>
    public Selection Project([SpecSlot(nameof(SpecSlot.ProjectName))] string name)
    {
        Guard.NotNull(name, nameof(name));

        return new RefinedSelection(this, new ProjectNoun(name));
    }

    /// <summary>
    ///     Selects a single type, such as <c>arch.Type(typeof(SqlConnection))</c>; types from referenced
    ///     packages and the framework are accepted. For a generic type pass the open definition,
    ///     <c>typeof(Task&lt;&gt;)</c>: a constructed type such as <c>typeof(Task&lt;int&gt;)</c> cannot
    ///     be matched, and a rule naming one reports an error at check time that says which definition to
    ///     name instead.
    /// </summary>
    public Selection Type(Type type)
    {
        Guard.NotNull(type, nameof(type));

        return new RefinedSelection(this, new TypeNoun(TypeAnchor.FromType(type)));
    }

    /// <summary>
    ///     Selects the single type <typeparamref name="T" />, such as
    ///     <c>arch.Type&lt;SqlConnection&gt;()</c>; types from referenced packages and the framework are
    ///     accepted, and the selection is the one <c>arch.Type(typeof(T))</c> makes. For a generic type use
    ///     the <see cref="System.Type" /> overload with the open definition, <c>typeof(Task&lt;&gt;)</c>:
    ///     a constructed type argument cannot be matched, and a rule naming one reports an error at check
    ///     time that says which definition to name instead.
    /// </summary>
    public Selection Type<T>()
    {
        return Type(typeof(T));
    }

    /// <summary>
    ///     Selects every type any of the given selections names, such as
    ///     <c>arch.AnyOf(arch.Project("A"), arch.Project("B"))</c>. The operands may be layers, narrowed
    ///     selections or other unions; one operand alone is allowed and selects just that. Adjectives
    ///     applied afterwards narrow the combined set: <c>arch.AnyOf(a, b).Except(c)</c> removes
    ///     <c>c</c>'s types from the union. As a rule subject, every operand must match at least one type,
    ///     or the rule fails naming the empty one.
    /// </summary>
    public Selection AnyOf(Selection first, params Selection[] more)
    {
        IReadOnlyList<Selection> parts = OperandList.OneOrMore(first, more);
        return UnionSelection.Create(this, parts);
    }

    /// <summary>
    ///     Selects every type any selection in a list names, such as <c>arch.AnyOf(layers)</c> over a list
    ///     a helper method built. The selections may be layers, narrowed selections or other unions; a
    ///     list of one selects just that. Adjectives applied afterwards narrow the combined set. The list
    ///     is read once, when this is called. As a rule subject, every selection must match at least one
    ///     type, or the rule fails naming the empty one. An empty list is reported when the spec is
    ///     loaded, and a null list or a null selection in it throws.
    /// </summary>
    public Selection AnyOf(IEnumerable<Selection> selections)
    {
        IReadOnlyList<Selection> parts = OperandList.ZeroOrMore(selections, nameof(selections));
        return UnionSelection.Create(this, parts);
    }

    /// <summary>
    ///     Selects every listed type, such as <c>arch.AnyOf(typeof(A), typeof(B))</c>; types from
    ///     referenced packages and the framework are accepted, and a generic type is given as its open
    ///     definition. Adjectives applied afterwards narrow the combined set. To mix types with other
    ///     selections, wrap each type in <c>arch.Type</c> and use the selection overload.
    /// </summary>
    public Selection AnyOf(Type first, params Type[] more)
    {
        return UnionSelection.Create(this, TypeSelections(first, more));
    }

    /// <summary>
    ///     Selects every type in a list, such as <c>arch.AnyOf(handlerTypes)</c> over a list a helper
    ///     method built; types from referenced packages and the framework are accepted, and a generic type
    ///     is given as its open definition. Adjectives applied afterwards narrow the combined set. The list
    ///     is read once, when this is called. An empty list is reported when the spec is loaded, and a null
    ///     list or a null type in it throws.
    /// </summary>
    public Selection AnyOf(IEnumerable<Type> types)
    {
        // As in TypeSelections: a method group would read as the bare word Type.
        // ReSharper disable once ConvertClosureToMethodGroup
        IReadOnlyList<Selection> parts = OperandList.ZeroOrMore(types, type => Type(type), nameof(types));
        return UnionSelection.Create(this, parts);
    }

    internal IReadOnlyList<Selection> TypeSelections(Type first, Type[] more)
    {
        // A method group here would read as the bare word Type, which at this call site is far more
        // naturally the System.Type the operands already are. The lambda names the call.
        // ReSharper disable once ConvertClosureToMethodGroup
        return OperandList.OneOrMore(first, more, type => Type(type));
    }

    /// <summary>
    ///     Declares a family of layers, such as <c>arch.Each(dispatch, tracking, invoicing)</c>: one rule
    ///     over every member layer (its cells) instead of one rule per layer. As a subject it names every
    ///     type in any cell, and the family verbs <c>MustNotReferenceEachOther</c>,
    ///     <c>MustOnlyBeReferencedByItself</c> and <c>MustNotHaveCircularReferences</c> judge the cells
    ///     against one another; adjectives applied afterwards narrow every cell alike. Cells must be plain
    ///     layers, and no type may belong to two of them as they are defined, whatever adjectives the family
    ///     carries: the rule fails naming the type and both cells. A type that several projects each compile,
    ///     as from a source-only package, counts once per project, so layers defined by different projects do
    ///     not overlap on it. A family may stand only as a rule subject, directly or through a member
    ///     projection such as <c>.Methods</c>; anywhere else it is reported when the spec is loaded.
    /// </summary>
    public Selection Each(Layer first, params Layer[] more)
    {
        IReadOnlyList<Layer> cells = OperandList.OneOrMore(first, more);
        return new RefinedSelection(this, new EachNoun(cells));
    }

    /// <summary>
    ///     Declares a family of projects, such as
    ///     <c>arch.Each(arch.Projects.Matching("Nop.Plugin.*"))</c>: one rule over every project the
    ///     selection names (its cells), each cell being the types that project declares. As a subject it
    ///     names every type in any cell, and the family verbs <c>MustNotReferenceEachOther</c> and
    ///     <c>MustOnlyBeReferencedByItself</c> judge the cells against one another; adjectives applied
    ///     afterwards narrow every cell alike. A family that names no project fails the rule as an empty
    ///     subject. A family may stand only as a rule subject, directly or through a member projection
    ///     such as <c>.Methods</c>; anywhere else it is reported when the spec is loaded.
    /// </summary>
    public Selection Each(ProjectSelection projects)
    {
        Guard.NotNull(projects, nameof(projects));

        return new RefinedSelection(this, new EachNoun(projects));
    }

    /// <summary>
    ///     Selects the types a container registration in the source names with the given lifetime,
    ///     service and implementation types alike, such as <c>arch.Registered(Lifetime.Singleton)</c>.
    ///     Registrations are recognized from <c>AddSingleton</c>, <c>AddScoped</c>, <c>AddTransient</c>,
    ///     their <c>TryAdd</c> twins, <c>AddHostedService</c>, <c>AddDbContext</c>, <c>AddDbContextPool</c> and
    ///     <c>AddHttpClient</c> on an <c>IServiceCollection</c>; anything registered another way
    ///     (assembly scanning, keyed services, raw descriptors, code compiled into a package) is not seen.
    ///     The natural operand of <c>MustNotInject</c>; as a subject, a selection that matches nothing
    ///     fails the rule. An undefined <see cref="Lifetime" /> value is reported when the spec is loaded.
    /// </summary>
    public Selection Registered(Lifetime lifetime)
    {
        return new RefinedSelection(this, new RegisteredNoun(lifetime));
    }

    /// <summary>
    ///     Selects the types a container registration in the source names, with any lifetime, service
    ///     and implementation types alike. Registrations are recognized from <c>AddSingleton</c>,
    ///     <c>AddScoped</c>, <c>AddTransient</c>, their <c>TryAdd</c> twins, <c>AddHostedService</c>,
    ///     <c>AddDbContext</c>, <c>AddDbContextPool</c> and <c>AddHttpClient</c> on an
    ///     <c>IServiceCollection</c>; anything registered another way (assembly scanning, keyed services,
    ///     raw descriptors, code compiled into a package) is not seen. The natural operand of
    ///     <c>MustNotInject</c>; as a subject, a selection that matches nothing fails the rule.
    /// </summary>
    public Selection Registered()
    {
        return new RefinedSelection(this, new RegisteredNoun(null));
    }

    /// <summary>
    ///     Names a member to ban with <c>MustNotUse</c>, by declaring type and member name:
    ///     <c>arch.Member(typeof(DateTime), nameof(DateTime.Now))</c>. Every overload of that name is
    ///     covered. The member must be declared on the given type itself, not inherited from a base (for
    ///     <c>Wait()</c> use <c>typeof(Task)</c>, not <c>typeof(Task&lt;&gt;)</c>), and a generic type is
    ///     given as its open definition. A <see cref="SlideRule.Member" /> takes no adjectives
    ///     and no verbs. A blank name, or a name the type does not declare, is reported when the spec is
    ///     loaded.
    /// </summary>
    public Member Member(Type type, [SpecSlot(nameof(SpecSlot.MemberName))] string name,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        Guard.NotNull(type, nameof(type));

        return new Member(this, type, name, SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     Names an instance property, field or value-returning method to ban with <c>MustNotUse</c>,
    ///     from a lambda over its declaring type: <c>arch.Member&lt;Task&lt;int&gt;&gt;(t =&gt; t.Result)</c>
    ///     names <c>Task&lt;T&gt;.Result</c>. Every overload of that name is covered. The lambda must read
    ///     or invoke the member directly on its parameter; the compiler checks that the member belongs to
    ///     <typeparamref name="T" />, an inherited member is recorded against the type that declares it,
    ///     and a generic declaring type is recorded as its open definition. A lambda of any other shape,
    ///     such as a chained access (<c>t =&gt; t.A.B</c>), a method group without parentheses, a static
    ///     member, a constant or an object creation, is reported when the spec is loaded, with the form to
    ///     use instead.
    /// </summary>
    public Member Member<T>(Expression<Func<T, object?>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     Names an instance method that returns nothing to ban with <c>MustNotUse</c>, from a lambda
    ///     over its declaring type: <c>arch.Member&lt;Task&gt;(t =&gt; t.Wait())</c> names
    ///     <c>Task.Wait</c>. Every overload of that name is covered. The lambda must invoke the method
    ///     directly on its parameter; the compiler checks that the method belongs to
    ///     <typeparamref name="T" />, an inherited method is recorded against the type that declares it,
    ///     and a generic declaring type is recorded as its open definition. A lambda of any other shape,
    ///     such as a chained access (<c>t =&gt; t.A.B()</c>), a static method or an object creation, is
    ///     reported when the spec is loaded, with the form to use instead.
    /// </summary>
    public Member Member<T>(Expression<Action<T>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     Names a static property, field or value-returning method to ban with <c>MustNotUse</c>, from a
    ///     parameterless lambda: <c>arch.Member(() =&gt; DateTime.Now)</c> names <c>DateTime.Now</c>.
    ///     Every overload of that name is covered, and a generic declaring type is recorded as its open
    ///     definition. The lambda must read or invoke one static member; a lambda of any other shape, such
    ///     as an instance member, a method group without parentheses, a constant or an object creation, is
    ///     reported when the spec is loaded, with the form to use instead.
    /// </summary>
    public Member Member(Expression<Func<object?>> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     Names a static method that returns nothing to ban with <c>MustNotUse</c>, from a parameterless
    ///     lambda: <c>arch.Member(() =&gt; GC.Collect())</c> names <c>GC.Collect</c>. Every overload of
    ///     that name is covered, and a generic declaring type is recorded as its open definition. The
    ///     lambda must invoke one static method; a lambda of any other shape, such as an instance method
    ///     or an object creation, is reported when the spec is loaded, with the form to use instead.
    /// </summary>
    public Member Member(Expression<Action> member,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        return MemberExpressionResolver.Resolve(this, Guard.NotNull(member, nameof(member)), SpecSourceLocation.Capture(filePath, lineNumber));
    }

    /// <summary>
    ///     Declares a rule and returns the step that takes its posture: call <c>Enforce</c> or
    ///     <c>Migrate</c> next, then <c>Because</c>. An ID is lowercase letters, digits and hyphens, in
    ///     segments joined by <c>/</c>, conventionally <c>area/rule-name</c>; it must be unique across
    ///     every spec class in the build. A malformed or duplicate ID, or a rule given no posture, is
    ///     reported when the spec is loaded.
    /// </summary>
    public IRuleBuilder Rule([SpecSlot(nameof(SpecSlot.RuleId))] string id,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        Guard.NotNull(id, nameof(id));

        var registration = new RuleRegistration(id) { Location = SpecSourceLocation.Capture(filePath, lineNumber) };
        _registrations.Add(registration);
        return new RuleBuilder(registration);
    }

    /// <summary>
    ///     Declares a scope and returns the step that takes its posture: call <c>Quarantine</c> or
    ///     <c>Caution</c> next, then <c>Dragons</c> or <c>DragonsDoc</c>, and <c>Because</c>. The ID takes
    ///     the same form as a rule ID and must be unique across every spec class in the build; the scope's
    ///     own rules are <c>{id}/containment</c> and <c>{id}/tripwire</c>, so no declared rule may use an
    ///     ID beneath it. A malformed or duplicate ID, or a scope given no posture, is reported when the
    ///     spec is loaded.
    /// </summary>
    public IScopeBuilder Scope([SpecSlot(nameof(SpecSlot.ScopeId))] string id,
        [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        Guard.NotNull(id, nameof(id));

        var registration = new ScopeRegistration(id) { Location = SpecSourceLocation.Capture(filePath, lineNumber) };
        _registrations.Add(registration);
        return new ScopeBuilder(registration);
    }

    private Layer Register(LayerNoun noun)
    {
        var registration = new LayerRegistration(noun);
        _layers.Add(registration);
        return new Layer(this, registration);
    }
}
