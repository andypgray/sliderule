namespace MultiTfm.Core
{
    // Compiled under both frameworks and carrying a framework-conditional region: under net10.0 the
    // field's lines are disabled text and under netstandard2.0 they are code, so the two frameworks' line
    // maps for this file differ, and a comment edit outside the region moves each framework's sites by its own map.
    public partial class Widget
    {
#if !NET
        // net10.0 wins Widget's facts, so this field is not in the model; the reference it makes to
        // LegacyOnly is, because references union across a project's frameworks.
        public MultiTfm.Core.Legacy.LegacyOnly Legacy;
#endif
    }
}
