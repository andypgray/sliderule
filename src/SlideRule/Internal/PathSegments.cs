namespace SlideRule.Internal;

/// <summary>The <c>.</c> and <c>..</c> folding rule shared by every path compared as text.</summary>
internal static class PathSegments
{
    /// <summary>
    ///     <paramref name="segments" /> with every <c>.</c> dropped and each <c>..</c> folded into the segment
    ///     before it. <paramref name="anchored" /> says whether the path has a root for a leading <c>..</c> to stop
    ///     at.
    /// </summary>
    internal static List<string> Fold(IEnumerable<string> segments, bool anchored)
    {
        var folded = new List<string>();
        foreach (string segment in segments)
        {
            if (string.Equals(segment, ".", StringComparison.Ordinal)) continue;

            if (!string.Equals(segment, "..", StringComparison.Ordinal))
            {
                folded.Add(segment);
                continue;
            }

            int last = folded.Count - 1;
            bool climbable = last >= 0 && !string.Equals(folded[last], "..", StringComparison.Ordinal);
            if (climbable)
            {
                folded.RemoveAt(last);
                continue;
            }

            // A `..` at the root stays at the root. Above a path with no root it is kept instead, there
            // being nothing there to stop at and a dropped one naming a different directory.
            if (!anchored) folded.Add("..");
        }

        return folded;
    }
}
