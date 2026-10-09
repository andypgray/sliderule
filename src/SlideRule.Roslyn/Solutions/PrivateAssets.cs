using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Whether a package reference's evaluated <c>PrivateAssets</c> metadata covers every asset — the one value
///     under which NuGet neither writes the package into the project's own package as a dependency nor hands it
///     on to a project that references this one.
/// </summary>
/// <remarks>
///     <para>
///         <b>The contract is NuGet's two rules.</b> Pack leaves a reference out of the package's dependencies
///         when its included assets are none or its private assets are all; restore, walking a project
///         reference, drops a package only when its private assets are all. So all is the one value under which
///         a reference reaches neither consumer, and <c>IncludeAssets</c> and <c>ExcludeAssets</c> cannot stand
///         in for it: a reference that includes nothing is left out of the package and still restored for every
///         project referencing this one.
///     </para>
///     <para>
///         <b>Parsed the way NuGet parses it.</b> The value splits on <c>;</c>, each part trimmed and empty
///         parts dropped. No part left means NuGet's default, which keeps build, content files and analyzers
///         and is not all. Otherwise the parts OR together case-insensitively, <c>buildtransitive</c> bringing
///         build with it, and anything else — <c>none</c>, an unknown word, a comma list, a number — adds
///         nothing, so a value made only of those is the empty set rather than the default. The switch mirrors
///         NuGet's own token for token, so the two can be read side by side.
///     </para>
/// </remarks>
internal static class PrivateAssets
{
    [Flags]
    private enum Assets
    {
        None = 0,
        Runtime = 1 << 0,
        Compile = 1 << 1,
        Build = 1 << 2,
        Native = 1 << 3,
        ContentFiles = 1 << 4,
        Analyzers = 1 << 5,
        BuildTransitive = 1 << 6,
        All = Runtime | Compile | Build | Native | ContentFiles | Analyzers | BuildTransitive
    }

    /// <summary>
    ///     Whether <paramref name="value" />, an evaluated <c>PrivateAssets</c>, resolves to every asset.
    /// </summary>
    /// <param name="value">The metadata value as evaluation left it; empty where nothing set it.</param>
    internal static bool CoversEveryAsset(string value)
    {
        // No part left needs no arm of its own: NuGet's default keeps build, content files and analyzers,
        // which is no more every asset than the empty union is, so both answer false here.
        Assets assets = MsBuildList.Items(value)
            .Aggregate(Assets.None, (union, token) => union | Parse(token));

        return assets == Assets.All;
    }

    private static Assets Parse(string token)
    {
        return token.ToLowerInvariant() switch
        {
            "all" => Assets.All,
            "runtime" => Assets.Runtime,
            "compile" => Assets.Compile,
            "build" => Assets.Build,
            "native" => Assets.Native,
            "contentfiles" => Assets.ContentFiles,
            "analyzers" => Assets.Analyzers,
            "buildtransitive" => Assets.BuildTransitive | Assets.Build,
            _ => Assets.None
        };
    }
}
