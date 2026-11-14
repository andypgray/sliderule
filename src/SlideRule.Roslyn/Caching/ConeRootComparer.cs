using SlideRule.Internal;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     Compares <see cref="ProjectCone" /> roots, each a project directory with the source suffix its cone walks: the
///     directory as a path, and the suffix ordinally, as the cone matches it.
/// </summary>
internal sealed class ConeRootComparer : IEqualityComparer<(string Directory, string Suffix)>
{
    public static readonly ConeRootComparer Instance = new();

    public bool Equals((string Directory, string Suffix) x, (string Directory, string Suffix) y)
    {
        return PathComparison.Comparer.Equals(x.Directory, y.Directory)
               && string.Equals(x.Suffix, y.Suffix, StringComparison.Ordinal);
    }

    public int GetHashCode((string Directory, string Suffix) obj)
    {
        return HashCode.Combine(PathComparison.Comparer.GetHashCode(obj.Directory), obj.Suffix);
    }
}
