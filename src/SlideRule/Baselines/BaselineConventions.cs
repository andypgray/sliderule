using SlideRule.Internal;
using SlideRule.Validation;

namespace SlideRule.Baselines;

/// <summary>
///     Where SlideRule looks for a baseline file when a rule does not name one.
/// </summary>
internal static class BaselineConventions
{
    /// <summary>
    ///     The directory convention baselines live under, relative to the solution and written with a forward slash.
    /// </summary>
    public const string ConventionalRoot = "arch/baselines";

    /// <summary>
    ///     The baseline file a rule uses when it declares no <c>Baseline(path)</c> of its own:
    ///     <c>arch/baselines/{ruleId}.json</c>, each <c>/</c> in the rule ID becoming a directory, so
    ///     <c>legacy/no-direct-sql</c> is baselined in <c>arch/baselines/legacy/no-direct-sql.json</c>. A
    ///     quarantined scope reaches the same convention through its containment rule, whose ID is
    ///     <c>{scope-id}/containment</c>. The path is written with forward slashes and resolves against
    ///     the solution directory. A blank rule ID, or an ID outside the rule-ID grammar, is refused.
    /// </summary>
    // Rule IDs are checked against ^[a-z0-9-]+(/[a-z0-9-]+)*$ (GRAMMAR §4.4) here, so turning the separators
    // into directories always yields a filesystem-safe path.
    public static string DefaultPath(string ruleId)
    {
        string id = Guard.NotNullOrWhiteSpace(ruleId, nameof(ruleId));
        if (!RuleIdSyntax.IsValid(id))
            throw new ArgumentException($"'{id}' is not a well-formed rule ID.", nameof(ruleId));

        return ConventionalRoot + "/" + id + ".json";
    }
}
