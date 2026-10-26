using Shouldly;
using SlideRule.Validation;

namespace SlideRule.Tests;

/// <summary>
///     The Shouldly surface over <see cref="SpecValidationException" />: the questions the catalog asks of
///     every failing spec — was this code reported, against this rule, and how many times (GRAMMAR §8).
/// </summary>
/// <remarks>
///     <para>
///         A predicate assertion over the errors reds by printing its lambda, which names the check that
///         was expected and never the checks that ran. This lists every reported
///         (code, rule) pair in the order the validator found them, which is the first question a red
///         about a one-pass validator raises.
///     </para>
///     <para>
///         Each returns what it matched so the site can go on to pin a message, which stays at the call
///         site: those strings are the spec, and burying them in an overload would hide them.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class SpecValidationAssertions
{
    /// <summary>
    ///     Asserts <paramref name="exception" /> reported <paramref name="code" /> — against
    ///     <paramref name="ruleId" /> when one is given — and returns the first error that matched.
    /// </summary>
    internal static SpecValidationError ShouldHaveError(
        this SpecValidationException exception, SpecValidationErrorCode code, string? ruleId = null)
    {
        SpecValidationError? match = exception.Errors
            .FirstOrDefault(error => error.Code == code && (ruleId is null || error.RuleId == ruleId));

        if (match is null)
        {
            string wanted = ruleId is null ? $"{code}" : $"{code} on '{ruleId}'";
            throw new ShouldAssertException(
                $"No {wanted} was reported.{Environment.NewLine}{Describe(exception)}");
        }

        return match;
    }

    /// <summary>
    ///     Asserts <paramref name="exception" /> reported <paramref name="code" /> exactly <paramref name="count" />
    ///     times, zero included, and returns those errors in validator order.
    /// </summary>
    internal static IReadOnlyList<SpecValidationError> ShouldHaveErrors(
        this SpecValidationException exception, SpecValidationErrorCode code, int count)
    {
        List<SpecValidationError> matching = exception.Errors
            .Where(error => error.Code == code)
            .ToList();

        if (matching.Count != count)
            throw new ShouldAssertException(
                $"Expected {count} {code}, found {matching.Count}.{Environment.NewLine}{Describe(exception)}");

        return matching;
    }

    /// <summary>Every reported error as <c>Code on 'ruleId'</c>, in validator order.</summary>
    private static string Describe(SpecValidationException exception)
    {
        IEnumerable<string> lines = exception.Errors
            .Select(error => $"    {error.Code} on '{error.RuleId ?? "(no rule)"}'");

        return $"  reported {exception.Errors.Count} error(s):{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines);
    }
}
