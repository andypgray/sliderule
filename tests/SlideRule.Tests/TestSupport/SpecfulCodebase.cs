using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Codebase;
using SlideRule.Hosting;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A solution extracted the way the verbs extract it: through their own <see cref="CodebaseSource" />,
///     over the warm pool, with the persisted extraction cache left alone. One walk yields both models a
///     spec-ful run reads, which is the shape <c>render --diagram</c> takes.
/// </summary>
/// <param name="Model">The spec's model, loaded from the assembly the spec resolved to.</param>
/// <param name="SpecName">
///     The spec assembly's file name without its extension, which is what <c>render</c> names the spec by.
/// </param>
/// <param name="Survey">
///     Every project, nothing excluded: the model <c>graph</c> summarizes and <c>render --diagram</c> draws.
/// </param>
/// <param name="Governed">
///     The projects the spec governs, the spec project and the plumbing only it references left out: the
///     model <c>check</c> judges, and the one <c>render</c> places cards and writes the model file from.
/// </param>
/// <param name="ExcludeProjectNames">
///     The projects <see cref="Governed" /> leaves out, as the spec resolution named them, for a caller that
///     extracts the same universe itself.
/// </param>
internal sealed record SpecfulCodebase(
    ArchitectureModel Model,
    string SpecName,
    CodebaseModel Survey,
    CodebaseModel Governed,
    IReadOnlyCollection<string> ExcludeProjectNames)
{
    /// <summary>
    ///     Discovers <paramref name="solution" />, resolves <paramref name="spec" /> against it, and extracts
    ///     both models off the one walk.
    /// </summary>
    /// <param name="solution">The solution argument, as a verb takes it.</param>
    /// <param name="spec">The <c>--spec</c> value, or null for the convention.</param>
    /// <param name="workingDirectory">The directory solution discovery walks up from.</param>
    /// <param name="ct">The test's cancellation token.</param>
    internal static async Task<SpecfulCodebase> ExtractAsync(
        string solution, string? spec, string workingDirectory, CancellationToken ct)
    {
        // Cache-free, so what a test reads is never a fragment another run wrote.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            WarmWorkspacePool.Source, environment: new SystemEnvironment(), solution, spec, workingDirectory,
            noCache: true, ct);

        // One after the other, never together: the source memoizes its merges unlocked, because it belongs
        // to one run. The second call merges off the fragments the first one walked.
        CodebaseModel survey = await source.ExtractAsync([], ct);
        CodebaseModel governed = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);
        string specName = Path.GetFileNameWithoutExtension(source.Resolution.DllPath);

        return new SpecfulCodebase(source.Model, specName, survey, governed, source.Resolution.ExcludeProjectNames);
    }
}
