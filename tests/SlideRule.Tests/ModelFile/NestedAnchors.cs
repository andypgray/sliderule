// Two types whose simple names collide, one of them nested inside another type and one a namespace
// deeper. A nested type's display path is its namespace plus its own name, with the containing type
// dropped, which is not what splitting its full name on dots yields — so this pair is what makes the
// anchor path in a model file necessary rather than redundant.

namespace SlideRule.Tests.ModelFile.Nesting
{
    /// <summary>The containing type, which the nested anchor's display path leaves out.</summary>
    internal static class Books
    {
        /// <summary>The nested half of the colliding pair.</summary>
        internal abstract class Ledger;
    }
}

namespace SlideRule.Tests.ModelFile.Nesting.Archive
{
    /// <summary>The sibling half of the colliding pair, one namespace below the nested one's path.</summary>
    internal abstract class Ledger;
}
