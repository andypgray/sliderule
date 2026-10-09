namespace SlideRule.Tests.DocHygiene;

/// <summary>The checker's tracked C# sources, for the gates that sweep what the checker reads.</summary>
internal static class CheckerSources
{
    /// <summary>The directory they sit under, in the forward-slash spelling the tracked set names them by.</summary>
    internal const string Root = "src/SlideRule/Checking/";

    /// <summary>Every tracked C# source under <see cref="Root" />.</summary>
    internal static IReadOnlyList<string> All =>
        TrackedFiles.CSharp
            .Where(static path => path.StartsWith(Root, StringComparison.Ordinal))
            .ToList();
}
