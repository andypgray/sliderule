using SlideRule.Internal;

namespace SlideRule.ModelFile;

/// <summary>Why a model file was refused, and where in it the refusal sits.</summary>
/// <remarks>
///     The position is the offending value or key, never a guess: a refusal about the whole file carries
///     line 1, column 1, offset 0, which is the file's own start rather than a sentinel. The message is a
///     lower-case sentence ending in a full stop and never names a position — <see cref="Describe" />
///     puts that in front, in the same shape the JSON reader's own refusals take, so a caller printing
///     either one prints the same thing.
/// </remarks>
internal sealed class ModelRefusal
{
    internal ModelRefusal(ModelRefusalKind kind, string message, JsonPosition position)
    {
        Kind = kind;
        Message = message;
        Position = position;
    }

    /// <summary>Which family of refusal this is.</summary>
    internal ModelRefusalKind Kind { get; }

    /// <summary>The refusal on its own, as a lower-case sentence ending in a full stop.</summary>
    internal string Message { get; }

    /// <summary>Where the refusal sits in the text that was read.</summary>
    internal JsonPosition Position { get; }

    /// <summary>The refusal with its position in front: <c>line 2, column 8: ...</c>.</summary>
    internal string Describe()
    {
        return Position.Describe(Message);
    }
}
