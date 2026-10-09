using System.Reflection;
using System.Runtime.CompilerServices;
using SlideRule.Fluent;

namespace SlideRule.Tests.TestSupport;

/// <summary>What a name on the fluent surface selects from: types, members, or projects.</summary>
internal enum VocabularyStratum
{
    Types,
    Members,
    Projects
}

/// <summary>The part a name plays in a spec sentence.</summary>
internal enum PartOfSpeech
{
    /// <summary>A starting point on <see cref="Arch" />: a selection, a project selection, or a member.</summary>
    Noun,

    /// <summary>A property of <see cref="Selection" /> that turns a type selection into a member selection.</summary>
    Projection,

    /// <summary>A method that takes a selection and hands one back, narrowed or annotated.</summary>
    Adjective,

    /// <summary>A method on a selection that returns a <see cref="Constraint" />.</summary>
    Verb
}

/// <summary>
///     One name on the fluent surface at one stratum and part of speech, with every public member that
///     spells it: the overloads of a method, or the one property.
/// </summary>
/// <param name="Stratum">
///     What the name's receiver selects; for a noun, what the noun itself selects, except that
///     <c>arch.Member</c>, which names one member, is filed under types.
/// </param>
/// <param name="PartOfSpeech">The part the name plays in a spec sentence.</param>
/// <param name="Name">The member name as a spec spells it.</param>
/// <param name="Members">Every public member spelling the name, ordinal-ordered by signature.</param>
internal sealed record VocabularyEntry(
    VocabularyStratum Stratum,
    PartOfSpeech PartOfSpeech,
    string Name,
    IReadOnlyList<MemberInfo> Members)
{
    /// <summary>This entry's stratum, part of speech and name, which a catalog row is matched on.</summary>
    internal VocabularyKey Key => new(Stratum, PartOfSpeech, Name);
}

/// <summary>What a catalog row and a sweep entry are matched on.</summary>
internal readonly record struct VocabularyKey(VocabularyStratum Stratum, PartOfSpeech PartOfSpeech, string Name)
{
    public override string ToString()
    {
        return $"{Stratum} {PartOfSpeech} {Name}";
    }
}

/// <summary>
///     Core's shipped fluent vocabulary, reflected once from the exported surface, for every gate that
///     inventories it.
/// </summary>
/// <remarks>
///     <para>
///         One walk over every public method and property each exported type declares itself feeds every
///         inventory here, so the gates cannot disagree about what ships. The inventories are found by shape
///         rather than by declaring class, so a class added later is swept without being named here.
///     </para>
///     <para>
///         The name collections are projections of <see cref="Entries" />, so a gate that reads names and
///         one that reads entries agree on what a verb or an adjective is: a verb is any
///         <see cref="Constraint" />-returning method on a selection receiver, extension and instance method
///         alike, and a static method that extends nothing is never one.
///     </para>
///     <para>
///         Every comparison is <see cref="StringComparer.Ordinal" />, for the reason the public-surface pin
///         gives: culture-aware collation differs between runner images.
///     </para>
/// </remarks>
internal static class ShippedVocabulary
{
    private const BindingFlags DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] SelectionRoots = [typeof(Selection), typeof(MemberSelection), typeof(ProjectSelection)];

    private static readonly Type[] NounTypes = [typeof(Selection), typeof(ProjectSelection), typeof(Member)];

    static ShippedVocabulary()
    {
        Type[] exported = typeof(Arch).Assembly.GetExportedTypes();

        List<MemberInfo> surface = exported
            .SelectMany(SurfaceOf)
            .ToList();

        Entries = surface
            .Select(Classify)
            .OfType<ClassifiedMember>()
            .GroupBy(classified => new VocabularyKey(classified.Stratum, classified.PartOfSpeech, classified.Member.Name))
            .Select(ToEntry)
            .OrderBy(entry => entry.Stratum)
            .ThenBy(entry => entry.PartOfSpeech)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        IEnumerable<string> verbs = Entries
            .Where(entry => entry.PartOfSpeech == PartOfSpeech.Verb)
            .Select(entry => entry.Name);
        VerbNames = DistinctOrdered(verbs);

        IEnumerable<string> adjectives = Entries
            .Where(entry => entry.PartOfSpeech == PartOfSpeech.Adjective)
            .Select(entry => entry.Name);
        AdjectiveNames = DistinctOrdered(adjectives);
    }

    /// <summary>
    ///     The whole inventory: one entry per stratum, part of speech and name, ordered by stratum, then part
    ///     of speech, then name, so it reads the same on every run.
    /// </summary>
    internal static IReadOnlyList<VocabularyEntry> Entries { get; }

    /// <summary>The <see cref="PartOfSpeech.Verb" /> entries' names, distinct and ordinal-ordered.</summary>
    internal static IReadOnlyList<string> VerbNames { get; }

    /// <summary>The <see cref="PartOfSpeech.Adjective" /> entries' names, distinct and ordinal-ordered.</summary>
    internal static IReadOnlyCollection<string> AdjectiveNames { get; }

    // Only what a type declares itself is classified, so an inherited member is counted once, on the type
    // that declares it.
    private static IEnumerable<MemberInfo> SurfaceOf(Type type)
    {
        IEnumerable<MemberInfo> methods = type.GetMethods(DeclaredPublic);
        IEnumerable<MemberInfo> properties = type.GetProperties(DeclaredPublic);

        return methods.Concat(properties);
    }

    private static ClassifiedMember? Classify(MemberInfo member)
    {
        Type type = member.DeclaringType!;

        (VocabularyStratum Stratum, PartOfSpeech PartOfSpeech)? classification = member switch
        {
            PropertyInfo property => ClassifyProperty(type, property),
            MethodInfo { IsSpecialName: false } method => ClassifyMethod(type, method),
            _ => null
        };

        return classification is { } found
            ? new ClassifiedMember(found.Stratum, found.PartOfSpeech, member)
            : null;
    }

    private static (VocabularyStratum, PartOfSpeech)? ClassifyProperty(Type type, PropertyInfo property)
    {
        Type propertyType = property.PropertyType;

        if (type == typeof(Arch))
            return IsNoun(propertyType) ? (NounStratum(propertyType), PartOfSpeech.Noun) : null;

        if (type == typeof(Selection) && typeof(MemberSelection).IsAssignableFrom(propertyType))
            return (VocabularyStratum.Types, PartOfSpeech.Projection);

        return null;
    }

    // An adjective takes a selection and returns one: an extension method over a selection receiver, or an
    // instance method of a selection type. Special names stay out, since the Members, Methods, Properties,
    // Fields and Events getters are projections, not adjectives. Layer.Purpose is swept too: it is the one
    // trailer that hands its selection back, and the recipe teaches it, so it is gated beside the
    // adjectives rather than excluded.
    private static (VocabularyStratum, PartOfSpeech)? ClassifyMethod(Type type, MethodInfo method)
    {
        Type returnType = method.ReturnType;

        if (type == typeof(Arch))
            return IsNoun(returnType) ? (NounStratum(returnType), PartOfSpeech.Noun) : null;

        Type? receiver = ReceiverOf(method);
        if (receiver is null || StratumOf(receiver) is not { } stratum) return null;

        if (StratumOf(returnType) is not null) return (stratum, PartOfSpeech.Adjective);

        return returnType == typeof(Constraint) ? (stratum, PartOfSpeech.Verb) : null;
    }

    /// <summary>
    ///     The type <paramref name="member" /> is called on: an extension method's first parameter, the declaring type
    ///     of any other method or of a property, and null for a static method that extends nothing.
    /// </summary>
    internal static Type? ReceiverOf(MemberInfo member)
    {
        if (member is not MethodInfo method) return member.DeclaringType;

        if (!method.IsStatic) return method.DeclaringType;

        return IsExtension(method) ? method.GetParameters()[0].ParameterType : null;
    }

    /// <summary>
    ///     Whether <paramref name="method" /> is an extension method, whose first parameter is its receiver.
    /// </summary>
    internal static bool IsExtension(MethodInfo method)
    {
        return method.IsDefined(typeof(ExtensionAttribute), inherit: false) && method.GetParameters().Length > 0;
    }

    // A selection root, a type derived from one, or a generic parameter constrained to one (the TSelf the
    // member adjectives take and return). Null for anything that selects nothing.
    private static VocabularyStratum? StratumOf(Type type)
    {
        if (type.IsGenericParameter)
            return type.GetGenericParameterConstraints()
                .Select(StratumOf)
                .FirstOrDefault(stratum => stratum is not null);

        Type? root = SelectionRoots.FirstOrDefault(candidate => candidate.IsAssignableFrom(type));

        return root switch
        {
            null => null,
            _ when root == typeof(MemberSelection) => VocabularyStratum.Members,
            _ when root == typeof(ProjectSelection) => VocabularyStratum.Projects,
            _ => VocabularyStratum.Types
        };
    }

    private static bool IsNoun(Type type)
    {
        return NounTypes.Any(noun => noun.IsAssignableFrom(type));
    }

    private static VocabularyStratum NounStratum(Type type)
    {
        return typeof(ProjectSelection).IsAssignableFrom(type) ? VocabularyStratum.Projects : VocabularyStratum.Types;
    }

    private static VocabularyEntry ToEntry(IGrouping<VocabularyKey, ClassifiedMember> group)
    {
        List<MemberInfo> members = group
            .Select(classified => classified.Member)
            .OrderBy(member => member.ToString(), StringComparer.Ordinal)
            .ThenBy(member => member.DeclaringType!.FullName, StringComparer.Ordinal)
            .ToList();

        return new VocabularyEntry(group.Key.Stratum, group.Key.PartOfSpeech, group.Key.Name, members);
    }

    private static List<string> DistinctOrdered(IEnumerable<string> names)
    {
        return names
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private sealed record ClassifiedMember(VocabularyStratum Stratum, PartOfSpeech PartOfSpeech, MemberInfo Member);
}
