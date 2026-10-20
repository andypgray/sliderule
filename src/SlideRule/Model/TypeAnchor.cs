using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     How a rule names a single type (GRAMMAR §5.2–§5.3): either a reflected <see cref="System.Type" />
///     (<c>typeof(ApiControllerAttribute)</c>, <c>typeof(IHandler&lt;&gt;)</c>) or the type
///     definition's fully-qualified name as a verbatim string
///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>, <c>"MyApp.Web.IHandler&lt;T&gt;"</c>).
///     A spec-built anchor populates exactly one arm, <see cref="Type" /> or <see cref="DefinitionFullName" />;
///     an anchor parsed from a model file populates neither.
/// </summary>
/// <remarks>
///     A string anchor matches definition-level, exactly and verbatim: the name is the definition's FQN in
///     extraction format (<c>Attribute</c> suffix included, declared type-parameter names for a generic, as a
///     report prints it), and it matches any construction of that definition, as an open-generic <c>typeof</c>
///     does. A constructed spelling (<c>"MyApp.Web.IHandler&lt;MyApp.Web.InvoiceCreated&gt;"</c>) names no
///     definition and never matches, and nothing is inferred from the string's shape: a dotless or
///     suffix-less name is legal and never matches.
///     <para>
///         Every derived fact is computed in the constructor, so all the reflection an anchor needs happens
///         while the spec is built and nothing afterwards reaches for the reflected type. A checker position
///         obtains a key only through <see cref="TryDefinitionKey" /> or <see cref="TryConstructionKey" />,
///         which is what lets it compare names rather than a <see cref="System.Type" />.
///     </para>
/// </remarks>
internal sealed class TypeAnchor
{
    private TypeAnchor(Type? type, string? definitionFullName)
    {
        Type = type;
        DefinitionFullName = definitionFullName;
        PathSegments = type is not null ? TypeName.PathSegments(type) : SplitPath(definitionFullName!);

        // A string anchor is a definition FQN taken verbatim: neither a construction nor unrepresentable.
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

    // A parsed anchor: every fact is supplied and nothing is reflected. Its arms stay null; they are read
    // only while a spec is being validated, which a parsed model never is.
    private TypeAnchor(string name, bool onDefinition, IReadOnlyList<string> path)
    {
        FullName = name;
        OnDefinition = onDefinition;
        PathSegments = path;
    }

    internal Type? Type { get; }

    internal string? DefinitionFullName { get; }

    /// <summary>
    ///     The anchor's dot-separated path, whichever arm it carries — the input the
    ///     colliding-simple-name rule widens outward along (GRAMMAR §6). Because both arms produce it, a
    ///     string anchor renders byte-identically to its <c>typeof</c> twin.
    /// </summary>
    internal IReadOnlyList<string> PathSegments { get; }

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

    internal static TypeAnchor FromType(Type type)
    {
        return new TypeAnchor(Guard.NotNull(type, nameof(type)), null);
    }

    /// <summary>
    ///     Anchors on the type definition's fully-qualified name. Only null is refused here: a blank name is
    ///     collected by validation (GRAMMAR §8 item 15) and reported beside every other error.
    /// </summary>
    internal static TypeAnchor FromName(string definitionFullName)
    {
        return new TypeAnchor(null, Guard.NotNull(definitionFullName, nameof(definitionFullName)));
    }

    /// <summary>
    ///     Anchors on the names a model file carries, for a reader with no spec assembly. A null
    ///     <paramref name="path" /> is derived from the name the way a string anchor's is, which is right for
    ///     every name whose dots are its own path.
    /// </summary>
    /// <remarks>
    ///     A parsed anchor never refuses either key: an anchor that would have refused the key its position asks
    ///     for was never written, so no file carries one.
    /// </remarks>
    internal static TypeAnchor FromParsed(string name, bool onDefinition, IReadOnlyList<string>? path)
    {
        Guard.NotNull(name, nameof(name));
        IReadOnlyList<string> segments = path ?? SplitPath(name);
        return new TypeAnchor(name, onDefinition, segments);
    }

    /// <summary>
    ///     A <c>(first, params more)</c> list of <c>typeof</c> anchors (GRAMMAR §10), stored on the node as
    ///     anchors and never wrapped as selections.
    /// </summary>
    internal static IReadOnlyList<TypeAnchor> FromTypes(Type first, Type[] more)
    {
        return OperandList.OneOrMore(first, more, FromType);
    }

    /// <summary>
    ///     The string twin of <see cref="FromTypes" />, each name minted through <see cref="FromName" />.
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
