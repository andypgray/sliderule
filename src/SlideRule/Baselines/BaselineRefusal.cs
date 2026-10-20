using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>Why a baseline file was refused, and where in it the refusal sits.</summary>
/// <remarks>
///     The position is the offending thing's own start and never a guess: the strict reader's own
///     position for a syntax refusal, the offending value where a value is of the wrong kind or carries
///     the wrong content, the property name for a key nothing admits, the object that lacks it for a
///     required key, and the entry object for a refusal about an entry's shape or its seal.
///     <see cref="Describe" /> puts the position in front of <see cref="Detail" />, in the same shape the
///     JSON reader's own refusals take, and only a caller that knows the path says which file it is.
/// </remarks>
internal sealed class BaselineRefusal
{
    internal BaselineRefusal(
        BaselineRefusalKind kind, string detail, string? ruleId, string? entryIdentity, JsonPosition position)
    {
        Kind = kind;
        Detail = detail;
        RuleId = ruleId;
        EntryIdentity = entryIdentity;
        Position = position;
    }

    internal BaselineRefusalKind Kind { get; }

    /// <summary>
    ///     The refusal on its own: a lower-case sentence ending in a full stop, naming neither a position nor a
    ///     file path.
    /// </summary>
    internal string Detail { get; }

    /// <summary>
    ///     The rule whose section the refusal sits under, or null for one about the file as a whole.
    /// </summary>
    internal string? RuleId { get; }

    /// <summary>
    ///     The entry the seal check refused — its subject, or <c>source -&gt; target</c> — or null on
    ///     every other refusal.
    /// </summary>
    internal string? EntryIdentity { get; }

    internal JsonPosition Position { get; }

    /// <summary>The refusal with its position in front: <c>line 2, column 8: ...</c>.</summary>
    internal string Describe()
    {
        return Position.Describe(Detail);
    }
}
