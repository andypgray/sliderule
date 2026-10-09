using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     A baseline section filed under a rule ID the spec does not declare, such as a renamed rule's old section:
///     its entries grandfather nothing, because no rule reads them.
/// </summary>
// A class rather than a record, because this target framework declares no IsExternalInit.
internal sealed class BaselineOrphan
{
    /// <summary>The section filed under <paramref name="ruleId" /> in <paramref name="path" />.</summary>
    /// <param name="ruleId">The rule ID the section is filed under.</param>
    /// <param name="path">The baseline file holding it, solution-relative with forward slashes.</param>
    /// <param name="entries">How many entries the section holds.</param>
    internal BaselineOrphan(string ruleId, string path, int entries)
    {
        RuleId = ruleId;
        Path = path;
        Entries = entries;
    }

    /// <summary>Gets the rule ID the section is filed under, which no rule in the spec declares.</summary>
    internal string RuleId { get; }

    /// <summary>Gets the baseline file holding the section, solution-relative with forward slashes.</summary>
    internal string Path { get; }

    /// <summary>Gets how many entries the section holds.</summary>
    internal int Entries { get; }

    /// <summary>
    ///     The section as every orphan line names it, such as
    ///     <c>baseline section 'old/id' in arch/baselines/x.json (2 entries) names no rule in the spec</c>.
    /// </summary>
    internal string Describe()
    {
        string entries = Plurals.Counted(Entries, "entry");
        return $"baseline section '{RuleId}' in {Path} ({entries}) names no rule in the spec";
    }

    /// <summary>The command that moves this section to <paramref name="newId" />.</summary>
    internal string RenameCommand(string newId)
    {
        return $"sliderule baseline rename --from {RuleId} --to {newId}";
    }
}
