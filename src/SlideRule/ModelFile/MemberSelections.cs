using SlideRule.Fluent;
using SlideRule.Model;

namespace SlideRule.ModelFile;

/// <summary>
///     Which member-selection class a kind filter belongs to — the one place that answers it.
/// </summary>
/// <remarks>
///     The concrete class is meaningful data, not decoration: it gates which kind-only verbs bind, so a
///     file that carried the kind and minted the wrong class would silently produce a selection the
///     fluent surface could never have written. The mapping is total today and the default arm throws
///     rather than guessing, because a new kind filter arriving without a class here is a bug that must
///     not read as a working selection.
/// </remarks>
internal static class MemberSelections
{
    internal static MemberSelection Of(Selection source, MemberKindFilter kind, IReadOnlyList<MemberAdjective> adjectives)
    {
        switch (kind)
        {
            case MemberKindFilter.Any:
            case MemberKindFilter.Event:
                return new KindMemberSelection(source, kind, adjectives);
            case MemberKindFilter.Method:
                return new MethodSelection(source, adjectives);
            case MemberKindFilter.Property:
                return new PropertySelection(source, adjectives);
            case MemberKindFilter.Field:
                return new FieldSelection(source, adjectives);
            default:
                throw new InvalidOperationException($"Unhandled member kind filter '{kind}'.");
        }
    }
}
