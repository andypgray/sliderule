using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI writes workspace diagnostics to stderr.
/// </summary>
/// <remarks>
///     Diagnostics carry the <c>warning:</c> prefix in human mode and go bare under <c>--json</c>, where they
///     also ride the document on stdout — the NuGet audit advisories in <c>packageAudit</c>, the rest in
///     <c>workspaceDiagnostics</c>; stdout purity is why nothing here ever writes to it. The deliberate
///     exception to that rule is the incomplete-model caveat that <c>context</c> and <c>explain</c>'s type and
///     path forms write straight to stdout, ahead of their answer, through
///     <see cref="IncompleteModelNotices.Caveat" />: the body is the MCP tool's only channel. <c>explain</c>
///     echoes here as well; <c>context</c>, with no CLI verb and no <c>--json</c>, does not route through this
///     class at all.
///     What gets written is <see cref="WorkspaceDiagnostics.PackageAudit" />, one line per advisory, then
///     <see cref="WorkspaceDiagnostics.Rendered" /> (or, for <c>check</c>,
///     <see cref="WorkspaceDiagnostics.RenderedWithMergeNotes" />) — composed once by the caller and handed
///     to <em>both</em> surfaces, stderr and the JSON document, so the MSBuild-selection note reaches the
///     MCP tools, which pass <see cref="TextWriter.Null" /> here. The advisories go first, so the stream and
///     its note stay directly above a refusal that points at the warnings above it. This class therefore only
///     echoes: it never appends, and never sees the projects the gate keys on.
/// </remarks>
internal static class WorkspaceDiagnosticsRenderer
{
    /// <summary>
    ///     Writes <paramref name="packageAudit" /> and then <paramref name="diagnostics" /> to
    ///     <paramref name="error" />, one line each. A no-op when both are empty.
    /// </summary>
    /// <param name="error">The stderr writer; never stdout.</param>
    /// <param name="packageAudit">The grouped NuGet audit advisories, one line each.</param>
    /// <param name="diagnostics">The composed diagnostics to echo.</param>
    /// <param name="json">
    ///     <see langword="true" /> under <c>--json</c>, which drops the <c>warning:</c> prefix — the
    ///     diagnostics are structured data in the document, and stderr is their unadorned echo.
    /// </param>
    internal static void Render(
        TextWriter error,
        IReadOnlyList<PackageAuditEntry> packageAudit,
        IReadOnlyList<string> diagnostics,
        bool json = false)
    {
        foreach (PackageAuditEntry entry in packageAudit) error.WriteLine(Line(entry.Describe(), json));
        foreach (string diagnostic in diagnostics) error.WriteLine(Line(diagnostic, json));
    }

    /// <summary>
    ///     Both streams straight off the load, for a verb whose only diagnostics are the load's own:
    ///     <see cref="WorkspaceDiagnostics.PackageAudit" /> and then
    ///     <see cref="WorkspaceDiagnostics.Rendered" />.
    /// </summary>
    /// <param name="error">The stderr writer; never stdout.</param>
    /// <param name="diagnostics">The load's verdict, which both streams are read off.</param>
    internal static void Render(TextWriter error, WorkspaceDiagnostics diagnostics)
    {
        Render(error, diagnostics.PackageAudit, diagnostics.Rendered);
    }

    /// <summary>
    ///     The advisories the document already stamped, and then the stream the caller composed — for the
    ///     verbs that write a document beside this echo.
    /// </summary>
    /// <remarks>
    ///     Reading the advisories off the stamp rather than off the load is what makes a verb group them
    ///     once: the stamp is built for the document and carries the same entries, in the same order, that
    ///     asking the load a second time would regroup.
    /// </remarks>
    /// <param name="error">The stderr writer; never stdout.</param>
    /// <param name="trust">The stamp the run's document carries, holding the grouped advisories.</param>
    /// <param name="diagnostics">The composed diagnostics to echo.</param>
    /// <param name="json">
    ///     <see langword="true" /> under <c>--json</c>, which drops the <c>warning:</c> prefix.
    /// </param>
    internal static void Render(
        TextWriter error, WorkspaceTrustStamp trust, IReadOnlyList<string> diagnostics, bool json)
    {
        Render(error, trust.PackageAudit ?? [], diagnostics, json);
    }

    private static string Line(string text, bool json)
    {
        return json ? text : $"warning: {text}";
    }
}
