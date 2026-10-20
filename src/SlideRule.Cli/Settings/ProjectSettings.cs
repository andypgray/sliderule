namespace SlideRule.Cli.Settings;

/// <summary>
///     A committed <c>sliderule.json</c>, read from <paramref name="FilePath" />. Every path is absolute, resolved
///     against the directory the file sits in, so one file serves every surface whatever directory it is run from.
/// </summary>
/// <remarks><paramref name="Spec" /> is a solution-member csproj or a built DLL, or null for the convention.</remarks>
internal sealed record ProjectSettings(string FilePath, string Solution, string? Spec, RenderSettings Render)
{
    /// <summary>The file name every surface looks for, walking up from where it starts.</summary>
    public const string FileName = "sliderule.json";
}

/// <summary>The files a plain <c>render</c> writes beyond the managed blocks it always writes.</summary>
internal sealed record RenderSettings(bool Model, DiagramSettings? Diagram)
{
    /// <summary>Nothing beyond the managed blocks: what a settings file with no <c>render</c> key declares.</summary>
    public static RenderSettings None { get; } = new(false, null);
}

/// <summary>
///     Where the two drawings go, and which declared projects the survey fence draws; an empty
///     <paramref name="Only" /> draws them all.
/// </summary>
internal sealed record DiagramSettings(string Target, IReadOnlyList<string> Only, IReadOnlyList<string> Exclude);
