namespace SlideRule.Model;

/// <summary><c>.MustBeRecords()</c> → "must be records" (GRAMMAR §5.3).</summary>
internal sealed class MustBeRecordsConstraint(Selection subject) : Constraint(subject)
{
    internal override string VerbPhrase => "must be records";
}
