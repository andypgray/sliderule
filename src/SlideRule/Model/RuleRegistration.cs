namespace SlideRule.Model;

/// <summary>
///     Mutable backing state for a rule anchor. Trailers append to lists so a repeated trailer
///     (<c>Because</c> twice, two <c>Baseline</c>s) is detectable by validation (§8 item 6). A null
///     <see cref="Posture" /> means the anchor was left dangling (§8 item 2).
/// </summary>
internal sealed class RuleRegistration(string id) : Registration(id)
{
    /// <summary>The checkable constraint: the <c>Enforce</c> constraint or the Migrate <c>to</c> target.</summary>
    internal Constraint? Constraint { get; set; }

    /// <summary>The descriptive <c>from</c> prose of a Migrate rule (the OLD pattern).</summary>
    internal string? MigrateFrom { get; set; }

    internal List<string> Fixes { get; } = [];

    internal List<string> Citations { get; } = [];

    internal List<Selection> Exemplars { get; } = [];

    internal List<MigrationPolicy> Policies { get; } = [];
}
