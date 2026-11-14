using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     The CLI's build, as the module version ids of the assemblies a check runs on. Two copies of one build in
///     different directories agree, and any rebuild that changes a compiled byte does not.
/// </summary>
internal static class BuildFingerprint
{
    /// <summary>The fingerprint of the build this process runs.</summary>
    public static string Current { get; } = Compute();

    // Each assembly read through a type of its own, never through whatever happens to be loaded, because the hook
    // and the server reach this at different stages of their loading. None of these types touches MSBuild, so
    // reading them cannot load it ahead of its registration.
    private static string Compute()
    {
        Type[] anchors = [typeof(Arch), typeof(ProjectArtifactFacts), typeof(PathCanonicalizer), typeof(BuildFingerprint)];
        IEnumerable<string> ids = anchors.Select(static anchor => anchor.Module.ModuleVersionId.ToString("N"));
        return FileStamping.HashText(string.Join("\n", ids));
    }
}
