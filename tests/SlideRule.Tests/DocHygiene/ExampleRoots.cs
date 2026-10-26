namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The repository-relative roots a quoting doc's paths resolve against: each example solution's directory,
///     and this repository's own root.
/// </summary>
internal static class ExampleRoots
{
    /// <summary>The flagship example.</summary>
    internal const string Meridian = "examples/Meridian";

    /// <summary>The clean-architecture example.</summary>
    internal const string Quoting = "examples/Meridian.Quoting";

    /// <summary>The module-isolation example.</summary>
    internal const string Operations = "examples/Meridian.Operations";

    /// <summary>The cited-guidance example.</summary>
    internal const string Interchange = "examples/Meridian.Interchange";

    /// <summary>
    ///     This repository's own root, for docs that quote its own sources or runs. Empty, because a path quoted
    ///     from this repository is already repository-relative.
    /// </summary>
    internal const string Self = "";

    /// <summary>
    ///     The repository-relative path of <paramref name="path" /> under <paramref name="root" />. An empty root
    ///     returns the path unchanged: prefixing a separator would yield a rooted path, which
    ///     <see cref="Path.Combine(string,string)" /> resolves outside the repository, taking every such path out
    ///     of a gate's reach.
    /// </summary>
    internal static string RepoRelative(string root, string path)
    {
        return root.Length == 0 ? path : $"{root}/{path}";
    }
}
