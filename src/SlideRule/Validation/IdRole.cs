namespace SlideRule.Validation;

/// <summary>Which anchor an ID was declared on, which decides what it desugars into (GRAMMAR §7).</summary>
internal enum IdRole
{
    /// <summary>An <c>arch.Rule(id)</c> anchor, which carries its ID into the model unchanged.</summary>
    Rule,

    /// <summary>An <c>arch.Scope(id)</c> anchor, whose ID reserves a namespace and mints two children.</summary>
    Scope
}
