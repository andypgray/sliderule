using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using SlideRule.Fluent;
using SlideRule.Prose;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Vocabulary;

/// <summary>
///     Composes the body of the generated vocabulary table that <c>GRAMMAR.md</c> and the <c>derive_spec</c>
///     recipe both carry between their managed-block markers: one row per <see cref="VocabularyCatalog" />
///     entry, each name's overloads read from <see cref="ShippedVocabulary" /> and each fragment printed by the
///     sentence renderer.
/// </summary>
/// <remarks>
///     The body is LF throughout and ends on one newline, so it survives a splice and an extract unchanged. It
///     carries no headings, so nothing in it reaches the gates that read a document's anchors.
/// </remarks>
internal static class VocabularyBlockComposer
{
    private const string Provenance =
        "*Generated from the shipped API by the test suite: each name and its overloads by reflection, each "
        + "fragment by the sentence renderer over an example operand. Do not edit by hand.*";

    private const string Delimiter = "|---|---|---|";

    private const string OverloadSeparator = " · ";

    private static readonly (VocabularyStratum Section, string Lead)[] Sections =
    [
        (VocabularyStratum.Types, "**Type subjects**"),
        (VocabularyStratum.Members, "**Member subjects**"),
        (VocabularyStratum.Projects, "**Project subjects**")
    ];

    private static readonly Dictionary<PartOfSpeech, string> Headers = new()
    {
        [PartOfSpeech.Noun] = "| Noun | Overloads | Fragment |",
        [PartOfSpeech.Projection] = "| Projection | Overloads | Fragment |",
        [PartOfSpeech.Adjective] = "| Adjective | Overloads | Fragment |",
        [PartOfSpeech.Verb] = "| Verb | Overloads | Fragment |"
    };

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(object)] = "object",
        [typeof(void)] = "void",
        [typeof(char)] = "char",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal"
    };

    private static readonly Type[] CallerInfoAttributes =
    [
        typeof(CallerFilePathAttribute),
        typeof(CallerLineNumberAttribute),
        typeof(CallerMemberNameAttribute),
        typeof(CallerArgumentExpressionAttribute)
    ];

    /// <summary>The whole block body, LF throughout and ending with a newline.</summary>
    internal static string Compose()
    {
        List<string> parts = [Provenance];

        foreach ((VocabularyStratum section, string lead) in Sections)
        {
            parts.Add(lead);

            IEnumerable<string> tables = VocabularyCatalog.Rows
                .Where(row => SectionOf(row) == section)
                .GroupBy(row => row.PartOfSpeech)
                .OrderBy(group => group.Key)
                .Select(Table);
            parts.AddRange(tables);
        }

        return string.Join("\n\n", parts) + "\n";
    }

    /// <summary>
    ///     One table line for <paramref name="row" />: its name, its overloads and its fragment. Throws an
    ///     <see cref="InvalidOperationException" /> naming the row when the sweep ships nothing under its key,
    ///     or when any cell carries a <c>|</c>, which would end the cell early.
    /// </summary>
    internal static string Row(VocabularyRow row)
    {
        string name = row.PartOfSpeech == PartOfSpeech.Noun ? $"`arch.{row.Name}`" : $"`.{row.Name}`";
        string overloads = Overloads(EntryFor(row));
        object node = row.Build(new Arch());
        string fragment = Fragment(node, row);

        string[] cells = [name, overloads, fragment];
        if (cells.Any(cell => cell.Contains('|')))
            throw new InvalidOperationException(
                $"The vocabulary row {row.Key} carries a '|' in a cell, which would split the table cell: "
                + string.Join(" / ", cells));

        return $"| {name} | {overloads} | {fragment} |";
    }

    // Projections are type-stratum names, since their receiver is a type selection, but what they mint is a
    // member subject, so the table teaches them under the member section.
    private static VocabularyStratum SectionOf(VocabularyRow row)
    {
        return row.PartOfSpeech == PartOfSpeech.Projection ? VocabularyStratum.Members : row.Stratum;
    }

    private static string Table(IGrouping<PartOfSpeech, VocabularyRow> rows)
    {
        IEnumerable<string> lines = rows.Select(Row);

        return string.Join("\n", [Headers[rows.Key], Delimiter, .. lines]);
    }

    private static VocabularyEntry EntryFor(VocabularyRow row)
    {
        VocabularyEntry? entry = ShippedVocabulary.Entries.SingleOrDefault(candidate => candidate.Key == row.Key);

        return entry
               ?? throw new InvalidOperationException(
                   $"The vocabulary row {row.Key} names nothing the shipped-API sweep found.");
    }

    private static string Fragment(object node, VocabularyRow row)
    {
        return node switch
        {
            Constraint constraint => constraint.VerbPhrase,
            Member member => SentenceRenderer.MemberList([member]),
            MemberSelection members => SentenceRenderer.MemberSubject(members),
            ProjectSelection projects => SentenceRenderer.ProjectSubject(projects),
            Selection selection => SentenceRenderer.Subject(selection),
            _ => throw new InvalidOperationException(
                $"The vocabulary row {row.Key} built a {node.GetType().Name}, which no renderer call prints.")
        };
    }

    // Every distinct overload as a backticked parameter list, ordinal-sorted, and the projection the name is
    // reached through when every overload sits on the same one.
    private static string Overloads(VocabularyEntry entry)
    {
        IEnumerable<string> signatures = entry.Members
            .OfType<MethodInfo>()
            .Select(Signature)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(signature => $"`{signature}`");
        string joined = string.Join(OverloadSeparator, signatures);

        return ProjectionReachedThrough(entry) is { } projection ? $"{joined} on `.{projection}`" : joined;
    }

    private static string Signature(MethodInfo method)
    {
        IEnumerable<ParameterInfo> withoutReceiver = ShippedVocabulary.IsExtension(method)
            ? method.GetParameters()
                .Skip(1)
            : method.GetParameters();
        List<ParameterInfo> parameters = withoutReceiver
            .Where(parameter => !CallerInfoAttributes.Any(attribute => parameter.IsDefined(attribute, inherit: false)))
            .ToList();

        List<string> rendered = parameters
            .Select((parameter, index) => IsFoldedParams(parameters, index) ? "…" : TypeName(parameter.ParameterType))
            .ToList();
        string typeParameters = NeedsExplicitTypeArguments(method)
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;

        return $"{typeParameters}({string.Join(", ", rendered)})";
    }

    // A method's type-parameter list is shown only when a caller has to write it: when no parameter, the
    // receiver included, has a type mentioning one of the type parameters, so nothing can infer them. The
    // nullary sugars keep their <T>(); AttributedWith<TSelf>(this TSelf, Type) reads (Type).
    private static bool NeedsExplicitTypeArguments(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition) return false;

        Type[] typeParameters = method.GetGenericArguments();

        return !method.GetParameters()
            .Any(parameter => Mentions(parameter.ParameterType, typeParameters));
    }

    private static bool Mentions(Type type, IReadOnlyCollection<Type> typeParameters)
    {
        if (type.IsGenericParameter) return typeParameters.Contains(type);

        if (type.HasElementType) return Mentions(type.GetElementType()!, typeParameters);

        return type.IsGenericType && type.GetGenericArguments()
            .Any(argument => Mentions(argument, typeParameters));
    }

    // The trailing `params T[] more` after a `T first`, which the overloads cell spells as "T, …".
    private static bool IsFoldedParams(IReadOnlyList<ParameterInfo> parameters, int index)
    {
        ParameterInfo parameter = parameters[index];
        bool isParams = index == parameters.Count - 1 && index > 0 && parameter.IsDefined(typeof(ParamArrayAttribute), inherit: false);

        return isParams && parameter.ParameterType.GetElementType() == parameters[index - 1].ParameterType;
    }

    private static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) return TypeName(underlying);

        if (Keywords.TryGetValue(type, out string? keyword)) return keyword;

        if (type.IsArray) return $"{TypeName(type.GetElementType()!)}[]";

        if (!type.IsGenericType) return type.Name;

        Type[] arguments = type.GetGenericArguments();
        if (type.GetGenericTypeDefinition() == typeof(Expression<>)) return LambdaShape(arguments[0]);

        string definitionName = type.Name[..type.Name.IndexOf('`')];
        IEnumerable<string> argumentNames = arguments.Select(TypeName);

        return $"{definitionName}<{string.Join(", ", argumentNames)}>";
    }

    private static string LambdaShape(Type delegateType)
    {
        int arity = delegateType.GetMethod("Invoke")!
            .GetParameters()
            .Length;

        return arity switch
        {
            0 => "() => …",
            1 => "x => …",
            _ => throw new InvalidOperationException(
                $"No lambda shape is defined for an expression over {delegateType}, which takes {arity} parameters.")
        };
    }

    // The projection property of Selection whose type is the one receiver every overload shares, when exactly
    // one such property exists: MethodSelection is reached only through .Methods, while MemberSelection is
    // reached through both .Members and .Events and so names no single projection.
    private static string? ProjectionReachedThrough(VocabularyEntry entry)
    {
        List<Type?> receivers = entry.Members
            .Select(ShippedVocabulary.ReceiverOf)
            .Distinct()
            .ToList();
        if (receivers is not [{ IsGenericParameter: false } receiver]) return null;

        if (!typeof(MemberSelection).IsAssignableFrom(receiver)) return null;

        List<PropertyInfo> projections = typeof(Selection).GetProperties()
            .Where(property => property.PropertyType == receiver)
            .ToList();

        return projections is [var projection] ? projection.Name : null;
    }
}
