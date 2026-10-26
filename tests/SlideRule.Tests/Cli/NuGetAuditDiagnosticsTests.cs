using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     Pins the audit-family boundary that <see cref="NuGetAuditDiagnostics.IsAudit" /> draws over raw
///     diagnostic message strings. The matching arm leads with the shapes that actually reach a host —
///     captured from MSBuildWorkspace runs over solutions carrying real advisories, where Roslyn has
///     already dropped the NU19xx code, so a code-only matcher never fires on one. A neighbouring restore
///     code, a truncated token, an ordinary load failure, and text that merely mentions a vulnerability all
///     still miss. No workspace is involved, so this is a fast unit pin.
/// </summary>
/// <remarks>
///     This classifier decides no verdict: the fail-closed gate reads which projects failed to load, off the
///     loaded solution's structure, so what turns on these cases is which key a line is reported under —
///     <c>packageAudit</c> or the diagnostic stream — and the order a bounded quote puts its evidence in. The
///     boundary stays narrow and stays pinned because a genuine load failure must never be reported as an
///     advisory, and a message that offers the load as an explanation should not be triggered by an advisory
///     published this morning.
/// </remarks>
public sealed class NuGetAuditDiagnosticsTests
{
    /// <summary>The frame Roslyn wraps every project-load diagnostic in, code already discarded.</summary>
    private const string Wrapper =
        CapturedAuditLines.FramePrefix + @"C:\src\App\App.csproj" + CapturedAuditLines.FrameSeparator;

    [Theory]
    // Captured verbatim: a NU1902 advisory as it arrives after Roslyn drops the code.
    [InlineData(Wrapper + CapturedAuditLines.CryptographyXmlAdvisory)]
    // The same advisory escalated by TreatWarningsAsErrors, which only adds a prefix.
    [InlineData(Wrapper + CapturedAuditLines.EscalatedAdvisory)]
    // The shape from a user's report.
    [InlineData(
        Wrapper + "Package 'KubernetesClient' 15.0.1 has a known moderate severity vulnerability, "
                + "https://github.com/advisories/GHSA-w7r3-mgwf-4mqq")]
    // NU1900, the audit fetch itself failing — an offline or unreachable-source run.
    [InlineData(Wrapper + CapturedAuditLines.AuditFetchFailure)]
    [InlineData("NU1900: Error communicating with the package source while running a security audit.")]
    // And the code-bearing shapes, for any path that still preserves one.
    [InlineData(CapturedAuditLines.CodedLine)]
    [InlineData("NU1901: Package 'Contoso.Widgets' 1.2.0 has a known low severity vulnerability")]
    [InlineData("Restore surfaced advisory code NU1999")]
    public void IsAudit_AuditAdvisory_ReturnsTrue(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(
        "NU1701: Package 'Legacy.Lib' 2.0.0 was restored using '.NETFramework,Version=v4.8' instead of the "
        + "project target framework 'net10.0'.")]
    [InlineData("NU19")]
    [InlineData("XNU1903 leaves no word boundary before the code, so it is not an advisory")]
    [InlineData("code NU19034 carries five digits and is not a NU19xx advisory")]
    [InlineData(DiagnosticInjectingSolutionSource.SimulatedLoadFailure)]
    // A genuine load failure that happens to name a vulnerability: the phrasing NuGet owns is absent, so
    // this stays in the diagnostic stream. Widening the match to the bare word would move it under packageAudit.
    [InlineData(Wrapper + "The type 'VulnerabilityScanner' could not be found in the referenced assembly.")]
    // A genuine load failure in a project under a directory named for an audit code. The path inside the frame
    // is the project's to choose, so the code is looked for in NuGet's message alone, and this stays a load
    // failure rather than moving under packageAudit.
    [InlineData(
        CapturedAuditLines.FramePrefix + @"C:\src\NU1903-repro\App.csproj" + CapturedAuditLines.FrameSeparator
        + "The imported project was not found.")]
    [InlineData("")]
    public void IsAudit_NonAuditText_ReturnsFalse(string diagnostic)
    {
        NuGetAuditDiagnostics.IsAudit(diagnostic)
            .ShouldBeFalse();
    }
}
