using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     An <see cref="ISolutionSource" /> that wraps a real load, then re-wraps the handle with synthetic
///     workspace-load diagnostics, synthetic failed projects, synthetic restore-failed, missing and/or
///     unchecked ones — the one way to drive the incomplete-model gate, and the narrowed-universe slots, over
///     a fixture that loads perfectly well. A verb's gate test takes its input from here rather than
///     inventing its own, so every gated verb is exercised against the same input.
/// </summary>
/// <remarks>
///     <para>
///         <b>The inputs are separate on purpose, because the product separates them.</b> Diagnostics
///         render and never gate; failed, restore-failed and missing projects each gate, with their own
///         wording and their own remedy, and are what every incomplete-model refusal names; unchecked projects
///         scope the answer and never trip that gate, though <c>render</c> and the baseline modes that read
///         absence as evidence refuse a narrowed model on their own terms. A caller that wants an
///         incomplete-model refusal injects a failed, restore-failed or missing project, a caller that wants a
///         rendered-but-harmless warning injects a diagnostic, a caller that wants a narrowed universe injects an
///         unchecked project, and a caller testing that they do not leak into each other injects one without the
///         others.
///     </para>
///     <para>
///         <b>Why the restore cause is injected rather than restored.</b> A real broken restore costs a live
///         SDK, a folder feed and a private package root, which is what
///         <see cref="SlideRule.Tests.Cli.RestoreFailureSilentEdgeE2ETests" /> pays once for the
///         end-to-end fact. Every wording, exit code and document slot downstream of the gate is a function of
///         the list alone, so paying that cost again per surface would buy nothing.
///     </para>
///     <para>
///         The inner source is the shared warm pool unless a caller passes its own: nothing here asserts on
///         whether a workspace was opened, only on what the gate does with what rides the handle. The real
///         handle rides as the owned
///         disposable so the ownership chain still holds — it is the pool's, so disposing it is a no-op.
///         Injected values never reach a real workspace, so the pooled session stays clean for the next
///         test; a genuinely partial load belongs in
///         <see cref="SlideRule.Tests.Cli.PartialLoadWorkspaceE2ETests" />, which drives a cold load
///         over a fixture the loader really does fail on.
///     </para>
/// </remarks>
internal sealed class DiagnosticInjectingSolutionSource(
    IReadOnlyList<string> diagnostics,
    IReadOnlyList<string>? failedProjects = null,
    IReadOnlyList<string>? uncheckedProjects = null,
    IReadOnlyList<string>? restoreFailedProjects = null,
    ISolutionSource? inner = null,
    IReadOnlyList<string>? missingProjects = null) : ISolutionSource
{
    /// <summary>The load failure the gate rows inject as a diagnostic: it renders, and decides nothing.</summary>
    internal const string SimulatedLoadFailure =
        "Project 'MyApp.Broken' failed to load: simulated workspace-load failure.";

    /// <summary>The project the gate rows inject as failed to load: the input that decides.</summary>
    internal const string SimulatedFailedProject = "C:/repo/MyApp.Broken/MyApp.Broken.csproj";

    /// <summary>
    ///     The project the gate rows inject as restore-failed: it loaded, and the restore gate refuses it.
    /// </summary>
    internal const string SimulatedRestoreFailedProject = "C:/repo/MyApp.Unrestored/MyApp.Unrestored.csproj";

    /// <summary>The project file the gate rows inject as declared and not on disk.</summary>
    internal const string SimulatedMissingProject = "C:/repo/MyApp.Vendor/MyApp.Vendor.csproj";

    public async Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
    {
        SolutionHandle real = await (inner ?? WarmWorkspacePool.Source).AcquireAsync(solutionPath, ct);
        WorkspaceDiagnostics injected = WorkspaceDiagnostics.None with
        {
            LoadFailures = diagnostics,
            FailedProjects = failedProjects ?? [],
            UncheckedProjects = uncheckedProjects ?? [],
            RestoreFailedProjects = restoreFailedProjects ?? [],
            MissingProjects = missingProjects ?? []
        };
        // Every warm affordance the inner handle carried is forwarded, not just the ones a caller happens to
        // exercise today: this double re-wraps a real handle, so anything it drops silently sends the run
        // down the cold path and quietly changes what the gate is being tested against.
        return new SolutionHandle(
            real.Solution, injected, real, real.ProjectFiles, real.WarmCodebase, real.WarmSpecResolution,
            real.TargetFrameworks, warmDeclaredMembers: real.WarmDeclaredMembers);
    }
}
