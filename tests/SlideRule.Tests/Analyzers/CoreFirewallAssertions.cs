using Shouldly;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The Shouldly surface over what <see cref="CoreFirewall" /> found in an assembly: no breach at all, or
///     exactly the breaches a probe was written to commit.
/// </summary>
/// <remarks>
///     Every failure message carries every breach, one per line, with the rule it breaks and what it names,
///     because the first question a red raises is which methods to move behind a boundary. Attributed
///     <c>[ShouldlyMethods]</c> for the reason <c>DocGateAssertions</c> records: the frame inside this file
///     asserts on a list it built from its own parameter, so attributed, the red names the caller's line.
/// </remarks>
[ShouldlyMethods]
internal static class CoreFirewallAssertions
{
    /// <summary>
    ///     Asserts the walk over <paramref name="assembly" /> found no breach, failing with every breach it
    ///     did find.
    /// </summary>
    internal static void ShouldHoldTheFirewall(this IReadOnlyList<FirewallBreach> breaches, string assembly)
    {
        List<string> lines = breaches.Select(breach => breach.ToString())
            .ToList();

        lines.ShouldBeEmpty(
            $"{assembly} can reach the compiler as something other than its own fault warning when the "
            + $"SlideRule it is handed will not bind. Every breach:\n{string.Join("\n", lines)}");
    }

    /// <summary>Asserts the walk found exactly <paramref name="expected" />, each written as a breach prints.</summary>
    internal static void ShouldBreachExactly(this IReadOnlyList<FirewallBreach> breaches, params string[] expected)
    {
        List<string> lines = breaches.Select(breach => breach.ToString())
            .ToList();

        lines.ShouldBe(expected, Case.Sensitive, $"The walk found:\n{string.Join("\n", lines)}");
    }
}
