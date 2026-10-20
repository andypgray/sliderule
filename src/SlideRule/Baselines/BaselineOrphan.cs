using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     A baseline section filed under a rule ID the spec does not declare, such as a renamed rule's old section:
///     its entries grandfather nothing, because no rule reads them.
/// </summary>
internal sealed class BaselineOrphan
{
    internal BaselineOrphan(string ruleId, string path, int entries)
    {
        RuleId = ruleId;
        Path = path;
        Entries = entries;
    }

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
