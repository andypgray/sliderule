namespace SlideRule.Checking;

/// <summary>Where a selection sits in a constraint, which fixes its candidate universe (GRAMMAR §4.1).</summary>
internal enum SelectionPosition
{
    /// <summary>Subject position — solution-declared types only.</summary>
    Subject,

    /// <summary>Target/source position — all types, externals included.</summary>
    Target
}
