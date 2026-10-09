using SlideRule.Hosting;
using SlideRule.Validation;

namespace SlideRule;

/// <summary>
///     Checks a spec for the mistakes <c>sliderule check</c> refuses a spec for before it reads any code: a rule
///     with no reason, two rules with one ID, a blank or multi-line piece of prose, and the rest. A rule pack's own
///     tests use it to prove that the rules its methods declare are valid, with no solution to load.
/// </summary>
public static class SpecValidation
{
    /// <summary>
    ///     Builds <paramref name="spec" /> and returns every mistake in it, one message each, in the words
    ///     <c>sliderule check</c> prints them. A message opens with the <c>file:line</c> of the call it is about
    ///     where that position was captured. The list is empty when the spec is valid, so a test asserts on it
    ///     directly: <c>SpecValidation.Validate(new MySpec()).ShouldBeEmpty();</c>
    /// </summary>
    /// <param name="spec">
    ///     The spec to build: an instance of a class that implements <see cref="IArchitectureSpec" />.
    /// </param>
    /// <returns>The messages, in the order <c>sliderule check</c> reports them; empty for a valid spec.</returns>
    public static IReadOnlyList<string> Validate(IArchitectureSpec spec)
    {
        try
        {
            ArchModelBuilder.Build(spec);
            return [];
        }
        catch (SpecValidationException exception)
        {
            return exception.Errors
                .Select(error => error.Message)
                .ToList();
        }
    }
}
