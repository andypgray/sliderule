using SlideRule.Hosting;

namespace SlideRule.Validation;

/// <summary>
///     Thrown by <see cref="ArchModelBuilder" /> in place of a model when a spec is loaded and something in it
///     is wrong. <see cref="Errors" /> carries every mistake found in that one pass rather than only the first,
///     and the message is those errors one per line.
/// </summary>
internal sealed class SpecValidationException : Exception
{
    internal SpecValidationException(IReadOnlyList<SpecValidationError> errors)
        : base(string.Join("\n", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }

    /// <summary>Gets every mistake found, in the order the checks ran.</summary>
    public IReadOnlyList<SpecValidationError> Errors { get; }
}
