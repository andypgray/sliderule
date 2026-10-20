using SlideRule.Internal;

namespace SlideRule.ModelFile;

/// <summary>
///     Why a model file was refused, and where: shaped as a <see cref="StrictJsonError" /> is, so a caller
///     printing either prints the same thing.
/// </summary>
internal sealed class ModelRefusal
{
    internal ModelRefusal(ModelRefusalKind kind, string message, JsonPosition position)
    {
        Kind = kind;
        Message = message;
        Position = position;
    }

    internal ModelRefusalKind Kind { get; }

    /// <summary>
    ///     The refusal on its own, as a lower-case sentence ending in a full stop. It never names a position.
    /// </summary>
    internal string Message { get; }

    /// <summary>
    ///     The offending value or key, never a guess: a refusal about the whole file carries the file's own
    ///     start (line 1, column 1, offset 0) rather than a sentinel.
    /// </summary>
    internal JsonPosition Position { get; }

    internal string Describe()
    {
        return Position.Describe(Message);
    }
}
