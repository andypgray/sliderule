using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI writes workspace diagnostics to stderr.
/// </summary>
/// <remarks>
///     Stdout purity is why nothing here ever writes to stdout: under <c>--json</c> the same lines ride the
///     document there. The diagnostics are composed once by the caller and handed to both surfaces, as
///     <see cref="WorkspaceDiagnostics" /> says. The advisories go first, so the stream and its note stay
///     directly above a refusal that points at the warnings above it. This class only echoes: it never
///     appends, and never sees the projects the gate keys on.
/// </remarks>
internal static class WorkspaceDiagnosticsRenderer
{
    /// <summary>
    ///     Writes <paramref name="packageAudit" /> and then <paramref name="diagnostics" /> to
    ///     <paramref name="error" />, stderr, one line each, with the <c>warning:</c> prefix unless
    ///     <paramref name="json" />, where the lines are structured data in the document and stderr is their
    ///     unadorned echo. A no-op when both are empty.
    /// </summary>
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
    internal static void Render(TextWriter error, WorkspaceDiagnostics diagnostics)
    {
        Render(error, diagnostics.PackageAudit, diagnostics.Rendered);
    }

    /// <summary>
    ///     The advisories the document already stamped, and then the stream the caller composed — for the
    ///     verbs that write a document beside this echo, so the advisories are grouped once per load.
    /// </summary>
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
