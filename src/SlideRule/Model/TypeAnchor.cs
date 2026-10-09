using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     How a rule names a single type (GRAMMAR §5.2–§5.3): either a reflected <see cref="System.Type" />
///     (<c>typeof(ApiControllerAttribute)</c>, <c>typeof(IHandler&lt;&gt;)</c>) or the type
///     definition's fully-qualified name as a verbatim string
///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>, <c>"MyApp.Web.IHandler&lt;T&gt;"</c>).
///     Exactly one arm is populated — <see cref="Type" /> xor <see cref="DefinitionFullName" />.
/// </summary>
/// <remarks>
///     The string arm is the escape hatch for a type the spec project cannot compile against:
///     without it, naming someone else's attribute or interface forces a package reference on the
///     spec just to write the <c>typeof</c>.
///     <para>
///         String matching is <em>definition-level, exact, and verbatim</em>: the name is the type
///         definition's FQN in extraction format — <c>Attribute</c> suffix included for an attribute,
///         declared type-parameter names for a generic (<c>"MyApp.Web.IHandler&lt;T&gt;"</c>, exactly as
///         a report prints it) — and it matches any construction of that definition, the same semantics
///         an open-generic <c>typeof</c> anchor carries. A <em>constructed</em> spelling
///         (<c>"N.MarkAttribute&lt;System.Int32&gt;"</c>,
///         <c>"MyApp.Web.IHandler&lt;MyApp.Web.InvoiceCreated&gt;"</c>) names no definition and so never
///         matches; that is the stated honesty boundary of the hatch, not a defect. Nothing is inferred
///         from the shape of the string: a dotless or suffix-less name is a legal spelling that simply
///         never matches.
///     </para>
///     <para>
///         <see cref="PathSegments" />, <see cref="FullName" />, <see cref="OnDefinition" />,
///         <see cref="IsConstructed" /> and <see cref="Unrepresentable" /> are computed once, in the
///         constructor: every reflection an anchor ever needs happens while the spec is being built, so
///         nothing after spec build reaches for the reflected type. <see cref="TryDefinitionKey" /> and
///         <see cref="TryConstructionKey" /> answer out of those facts and are the only way a checker
///         position obtains a key from an anchor — which is what lets a position compare names rather than
///         a <see cref="System.Type" />.
///     </para>
/// </remarks>
internal sealed class TypeAnchor
{
    private TypeAnchor(Type? type, string? definitionFullName)
    {
        Type = type;
        DefinitionFullName = definitionFullName;
        PathSegments = type is not null ? TypeName.PathSegments(type) : SplitPath(definitionFullName!);

        // A string anchor IS a definition FQN and nothing is inferred from its shape, so it is neither a
        // construction nor unrepresentable: it supplies its key verbatim to either position.
        if (type is null)
        {
            FullName = definitionFullName;
            OnDefinition = true;
            return;
        }

        OnDefinition = type.IsGenericTypeDefinition;
        IsConstructed = Generics.IsConstructed(type);
        try
        {
            FullName = TypeName.FullDisplay(type);
        }
        catch (UnrepresentableTypeException ex)
        {
            // The message verbatim, so the type it names survives: FullDisplay recurses into an array's
            // element type before refusing, and `int*[]` therefore reports on `System.Int32*`.
            Unrepresentable = ex.Message;
        }
    }

    // A parsed anchor: every fact is supplied, none is derived, and no reflection happens. It populates
    // neither arm — there is no Type to hold and no verbatim author-written name to attribute — so the
    // xor above reads as "neither" here. The two arm properties are read only while a spec is being
    // validated, which a parsed model never is.
    private TypeAnchor(string name, bool onDefinition, IReadOnlyList<string> path)
    {
        FullName = name;
        OnDefinition = onDefinition;
        PathSegments = path;
    }

    /// <summary>The reflected type of a <c>typeof</c> anchor; <c>null</c> on a string anchor.</summary>
    internal Type? Type { get; }

    /// <summary>The verbatim definition FQN of a string anchor; <c>null</c> on a <c>typeof</c> anchor.</summary>
    internal string? DefinitionFullName { get; }

    /// <summary>
    ///     The anchor's dot-separated path, whichever arm it carries — the input the
    ///     colliding-simple-name rule widens outward along (GRAMMAR §6). Because both arms produce it, a
    ///     string anchor renders byte-identically to its <c>typeof</c> twin.
    /// </summary>
    internal IReadOnlyList<string> PathSegments { get; }

    /// <summary>The unqualified display name: the anchor's last path segment.</summary>
    internal string SimpleDisplay => PathSegments[PathSegments.Count - 1];

    /// <summary>
    ///     The anchor's name in extraction format — a string anchor's own verbatim name, or the
    ///     <c>typeof</c> arm's rendered one — and <c>null</c> only for a type that has no such form, which
    ///     is exactly when <see cref="Unrepresentable" /> is set.
    /// </summary>
    internal string? FullName { get; }

    /// <summary>
    ///     Whether the anchor names a type <em>definition</em> rather than one construction of it, and so
    ///     matches every construction: true for a string anchor (a name is a definition name) and for an
    ///     open-generic <c>typeof</c>.
    /// </summary>
    internal bool OnDefinition { get; }

    /// <summary>
    ///     Whether the anchor is a constructed generic — <c>typeof(Task&lt;int&gt;)</c>, or a partially-open
    ///     construction — which names no definition-level node and so cannot key a definition-level position.
    /// </summary>
    internal bool IsConstructed { get; }

    /// <summary>
    ///     The refusal message for a type with no source-level display form (a pointer, a by-ref, a
    ///     partially-open construction), or <c>null</c> for every anchor that has one.
    /// </summary>
    internal string? Unrepresentable { get; }

    // A definition-level position: the anchor must name a definition. Refuses a closed generic
    // construction FIRST, then an unrepresentable type. That order is observable and must be kept: a
    // partially-open construction is both, and it reports as a closed generic.
    internal bool TryDefinitionKey(out string key, out AnchorRefusal refusal)
    {
        key = string.Empty;
        if (IsConstructed)
        {
            refusal = AnchorRefusal.ClosedGeneric;
            return false;
        }

        return TryConstructionKey(out key, out _, out refusal);
    }

    // A construction-level position: an open-generic typeof and a string anchor compare on the
    // definition name, anything else on the constructed name. Admits a closed generic. Refuses an
    // unrepresentable type alone.
    internal bool TryConstructionKey(out string key, out bool onDefinition, out AnchorRefusal refusal)
    {
        key = string.Empty;
        onDefinition = false;
        if (FullName is not { } name)
        {
            refusal = AnchorRefusal.Unrepresentable;
            return false;
        }

        key = name;
        onDefinition = OnDefinition;
        refusal = AnchorRefusal.None;
        return true;
    }

    /// <summary>Anchors on a reflected type — the <c>typeof</c> form.</summary>
    internal static TypeAnchor FromType(Type type)
    {
        return new TypeAnchor(Guard.NotNull(type, nameof(type)), null);
    }

    /// <summary>
    ///     Anchors on the type definition's fully-qualified name — the string form. Only null is
    ///     refused here; a blank name is a spec-author mistake, collected by the validation catalog
    ///     (GRAMMAR §8 item 15) so it reports beside every other error rather than throwing first.
    /// </summary>
    internal static TypeAnchor FromName(string definitionFullName)
    {
        return new TypeAnchor(null, Guard.NotNull(definitionFullName, nameof(definitionFullName)));
    }

    /// <summary>
    ///     Anchors on the names a model file carries, for a reader with no spec assembly: the name the
    ///     anchor compares on, whether it names a definition, and the path the colliding-simple-name rule
    ///     widens along. A null <paramref name="path" /> is derived from the name the way a string anchor's
    ///     is, which is the right answer for every name whose dots are its own path.
    /// </summary>
    /// <remarks>
    ///     A parsed anchor never refuses either key: it is neither a construction nor unrepresentable. That
    ///     is sound rather than optimistic — an anchor that would have refused the accessor its position
    ///     asks was never written, so no file carries one.
    /// </remarks>
    internal static TypeAnchor FromParsed(string name, bool onDefinition, IReadOnlyList<string>? path)
    {
        Guard.NotNull(name, nameof(name));
        IReadOnlyList<string> segments = path ?? SplitPath(name);
        return new TypeAnchor(name, onDefinition, segments);
    }

    /// <summary>
    ///     The <c>(first, params more)</c> anchor list of a negative hierarchy verb, each operand a
    ///     <c>typeof</c> anchor — the hierarchy-verb shape (GRAMMAR §10), stored on the node as anchors
    ///     and never wrapped as selections.
    /// </summary>
    internal static IReadOnlyList<TypeAnchor> FromTypes(Type first, Type[] more)
    {
        return OperandList.OneOrMore(first, more, FromType);
    }

    /// <summary>
    ///     The string twin of <see cref="FromTypes" />: the same shape over type-definition names, each
    ///     minted through <see cref="FromName" />.
    /// </summary>
    internal static IReadOnlyList<TypeAnchor> FromNames(string first, string[] more)
    {
        return OperandList.OneOrMore(first, more, FromName);
    }

    // Splits a fully-qualified name on the dots OUTSIDE any <...>: "N.Sub.MarkAttribute<T>" is
    // ["N", "Sub", "MarkAttribute<T>"] and "N.IHandler<System.Int32>" is ["N", "IHandler<System.Int32>"],
    // because the dots inside a type-argument list belong to the ARGUMENT's path, not this name's. Depth
    // tracking (rather than a first-'<' scan) is what keeps a nested argument list honest.
    private static IReadOnlyList<string> SplitPath(string fullName)
    {
        var segments = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < fullName.Length; i++)
            switch (fullName[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    if (depth > 0) depth--;

                    break;
                case '.' when depth == 0:
                    segments.Add(fullName.Substring(start, i - start));
                    start = i + 1;
                    break;
            }

        // The tail is always a segment, so a dotless name is one segment and a blank name is one blank
        // segment — the path is never empty, which is what lets SimpleDisplay index it unguarded.
        segments.Add(fullName.Substring(start));
        return segments;
    }
}
