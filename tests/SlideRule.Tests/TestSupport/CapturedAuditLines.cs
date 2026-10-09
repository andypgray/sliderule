namespace SlideRule.Tests.TestSupport;

/// <summary>
///     NuGet audit output in the shapes a workspace load really delivers it: advisory bodies captured from real
///     loads, the two frameless line shapes, and the project-load frame Roslyn puts around every such message.
/// </summary>
/// <remarks>
///     <para>
///         Roslyn records the message and never the code, so a framed body here carries no <c>NU190x</c> token:
///         a body that carried one would let a code-only matcher pass while missing every real advisory. The
///         frame is where a project's name is read from, so a row that means "this project reported it" builds
///         its line through <see cref="Framed" /> rather than writing the frame out.
///     </para>
///     <para>
///         Paths are neutral roots. The subject is the shape of the frame, never the machine a capture came
///         from.
///     </para>
/// </remarks>
internal static class CapturedAuditLines
{
    /// <summary>The project-load frame's opening, up to the project file it names.</summary>
    internal const string FramePrefix = "Msbuild failed when processing the file '";

    /// <summary>What closes that path and introduces the message the load carried.</summary>
    internal const string FrameSeparator = "' with message: ";

    /// <summary>A moderate advisory against a package a build-time dependency brings in.</summary>
    internal const string BuildTasksGitAdvisory =
        "Package 'Microsoft.Build.Tasks.Git' 10.0.300 has a known moderate severity vulnerability, "
        + "https://github.com/advisories/GHSA-23fw-v26w-5fgq";

    /// <summary>A critical advisory, as NuGet raises it before any escalation.</summary>
    internal const string DrawingCommonAdvisory =
        "Package 'System.Drawing.Common' 4.7.0 has a known critical severity vulnerability, "
        + "https://github.com/advisories/GHSA-rxg9-xrhp-64gj";

    /// <summary>
    ///     <see cref="DrawingCommonAdvisory" /> escalated by <c>TreatWarningsAsErrors</c>, which only adds a
    ///     prefix — and so makes it a different message.
    /// </summary>
    internal const string EscalatedAdvisory = "Warning As Error: " + DrawingCommonAdvisory;

    /// <summary>
    ///     A moderate advisory against a package a project references directly — the one every end-to-end row
    ///     injects, and the body under <see cref="ConsoleLoggerLine" />.
    /// </summary>
    internal const string CryptographyXmlAdvisory =
        "Package 'System.Security.Cryptography.Xml' 4.7.0 has a known moderate severity vulnerability, "
        + "https://github.com/advisories/GHSA-vh55-786g-wjwj";

    /// <summary>NU1900 in English: the audit fetch itself failed, so there is no package and no advisory link.</summary>
    internal const string AuditFetchFailure =
        "Error occurred while getting package vulnerability data: Unable to load the service index for source "
        + "https://nuget.example.com/v3/index.json.";

    /// <summary>
    ///     NU1900 in German, captured verbatim: no advisory link, no code and no English phrase, so nothing
    ///     recognises it as the audit speaking.
    /// </summary>
    internal const string GermanAuditFetchFailure =
        "Fehler beim Abrufen von Paketsicherheitsrisikodaten: Der Dienstindex für die Quelle "
        + "\"https://nuget.fieldtest.invalid/v3/index.json\" konnte nicht geladen werden.";

    /// <summary>A line from a path that keeps NuGet's code and wraps it in no frame, such as a binlog replay's.</summary>
    internal const string CodedLine =
        "NU1903: Package 'System.Security.Cryptography.Xml' 4.7.0 has a known high severity vulnerability, "
        + "https://github.com/advisories/GHSA-7h4f-3q2m-9xrv";

    /// <summary>The console logger's shape: the project path, then the severity and the code, and no frame.</summary>
    internal const string ConsoleLoggerLine =
        "/src/App/App.csproj : warning NU1902: " + CryptographyXmlAdvisory;

    /// <summary>
    ///     Twelve advisories against one package version, each its own link — the shape in which one project
    ///     raises several entries.
    /// </summary>
    internal static readonly string[] MessagePackAdvisories =
    [
        MessagePackAdvisory("high", "GHSA-382j-8mxh-c7x2"),
        MessagePackAdvisory("high", "GHSA-hv8m-jj95-wg3x"),
        MessagePackAdvisory("high", "GHSA-vh6j-jc39-fggf"),
        MessagePackAdvisory("moderate", "GHSA-2f33-pr97-265q"),
        MessagePackAdvisory("moderate", "GHSA-2x83-8g95-xh59"),
        MessagePackAdvisory("moderate", "GHSA-cj9g-3mj2-g8vv"),
        MessagePackAdvisory("moderate", "GHSA-cxmj-83gh-fp49"),
        MessagePackAdvisory("moderate", "GHSA-q2h6-ghwm-5qm8"),
        MessagePackAdvisory("moderate", "GHSA-qhmf-xw27-6rqr"),
        MessagePackAdvisory("moderate", "GHSA-v72x-2h86-7f8m"),
        MessagePackAdvisory("moderate", "GHSA-w567-gjr2-hm5j"),
        MessagePackAdvisory("moderate", "GHSA-wfr3-xj75-pfwh")
    ];

    /// <summary><paramref name="body" /> as Roslyn reports it for the project file at <paramref name="path" />.</summary>
    internal static string Framed(string path, string body)
    {
        return FramePrefix + path + FrameSeparator + body;
    }

    /// <summary>
    ///     The same, for a <paramref name="project" /> whose file sits at the conventional
    ///     <c>/src/{project}/{project}.csproj</c> — the layout every injected bed uses.
    /// </summary>
    internal static string FramedFor(string project, string body)
    {
        return Framed($"/src/{project}/{project}.csproj", body);
    }

    /// <summary>
    ///     One advisory as a whole load delivers it: <paramref name="body" /> once per project per framework.
    ///     MSBuild reports it for every framework a project restores on, and the frame names the project but
    ///     not the framework, so <paramref name="projects" /> × <paramref name="frameworks" /> lines arrive in
    ///     alike pairs.
    /// </summary>
    internal static IReadOnlyList<string> PerProjectAndFramework(
        IReadOnlyList<string> projects, int frameworks, string body)
    {
        return projects
            .SelectMany(project => Enumerable.Repeat(FramedFor(project, body), frameworks))
            .ToList();
    }

    private static string MessagePackAdvisory(string severity, string advisory)
    {
        return $"Package 'MessagePack' 3.1.6 has a known {severity} severity vulnerability, "
               + $"https://github.com/advisories/{advisory}";
    }
}
