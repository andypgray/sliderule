using SlideRule.Codebase;

namespace SlideRule.Checking;

/// <summary>
///     The checker's one read of a type's record flag, which the record verb and the record adjective share.
/// </summary>
/// <remarks>
///     A record struct another project declares does not show as a record to a project compiled against its
///     assembly, so a check over one project's compilation can answer this differently from a check over the
///     whole solution. Both readers stay sound there: that build reports only sites in its own files, where a
///     sibling's type has none, so the verb's wrong answer about one never surfaces; and
///     <see cref="OneProjectDecidability" /> holds the adjective off every selection of other types. A second
///     read elsewhere under this directory would carry neither guarantee.
/// </remarks>
internal static class RecordShape
{
    internal static bool Matches(TypeNode type)
    {
        return type.IsRecord;
    }
}
