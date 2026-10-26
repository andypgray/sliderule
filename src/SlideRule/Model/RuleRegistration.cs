namespace SlideRule.Model;

/// <summary>Mutable backing state for a rule anchor.</summary>
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
