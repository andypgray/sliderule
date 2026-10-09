namespace SlideRule.Cli.Settings;

/// <summary>
///     A committed <c>sliderule.json</c>: the solution a tree's runs are about, the spec that governs it, and
///     the files <c>render</c> writes beside the managed blocks. Every path is absolute, resolved against the
///     directory the file sits in, so one file serves every surface whatever directory it is run from.
/// </summary>
/// <param name="FilePath">The settings file itself, which every refusal about it names.</param>
/// <param name="Solution">The solution file the settings are for.</param>
/// <param name="Spec">The spec (a solution-member csproj or a built DLL), or null for the convention.</param>
/// <param name="Render">The render targets the file declares.</param>
internal sealed record ProjectSettings(string FilePath, string Solution, string? Spec, RenderSettings Render)
{
    /// <summary>The file name every surface looks for, walking up from where it starts.</summary>
    public const string FileName = "sliderule.json";
}

/// <summary>The files a plain <c>render</c> writes beyond the managed blocks it always writes.</summary>
/// <param name="Model">Whether <c>render</c> writes the model file, as <c>--model</c> asks.</param>
/// <param name="Diagram">The diagram target, or null for none.</param>
internal sealed record RenderSettings(bool Model, DiagramSettings? Diagram)
{
    /// <summary>Nothing beyond the managed blocks: what a settings file with no <c>render</c> key declares.</summary>
    public static RenderSettings None { get; } = new(false, null);
}

/// <summary>Where the two drawings go, and which declared projects the survey fence draws.</summary>
/// <param name="Target">The file whose managed block holds both drawings.</param>
/// <param name="Only">Declared-project globs the survey fence is limited to; empty draws them all.</param>
/// <param name="Exclude">Declared-project globs the survey fence leaves out.</param>
internal sealed record DiagramSettings(string Target, IReadOnlyList<string> Only, IReadOnlyList<string> Exclude);
