using System.Diagnostics;
using System.Text.Json;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Roslyn.MsBuild;

/// <summary>
///     Discovers installed Visual Studio instances via the standard <c>vswhere.exe</c> tool.
/// </summary>
/// <remarks>
///     Used in place of <c>MSBuildLocator.QueryVisualStudioInstances()</c> because on .NET 5+ that
///     API only returns DotNetSdk and DevConsole entries — never VS Setup instances. SlideRule
///     runs on .NET 10, so it would otherwise miss every installed VS. <c>vswhere.exe</c> ships with
///     the VS Installer at a fixed path and reliably enumerates all VS installs (stable + preview)
///     including ones not yet picked up by MSBuildLocator.
/// </remarks>
internal static class VsWhereLocator
{
    // A local enumeration that normally returns in well under a second, so 10s only trips on a wedge —
    // and a trip costs nothing but the RegisterDefaults() fallback this probe already degrades to.
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);

    // Long enough for one vswhere error line, short enough that the selection note stays one readable line.
    private const int MaxDetailLength = 200;

    private static readonly string VsWherePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio",
        "Installer",
        "vswhere.exe");

    /// <summary>
    ///     Runs vswhere and returns the instances it lists, or why it listed none.
    /// </summary>
    /// <returns>
    ///     The discovered instances with a null <c>WhyNone</c>, or no instances and a one-line reason:
    ///     vswhere absent, timed out, failed to start or exited nonzero, or its output held no usable
    ///     instance. Callers treat no instances as a signal to fall back to
    ///     <see cref="Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults" />, and carry the reason
    ///     into the selection they report.
    /// </returns>
    internal static (IReadOnlyList<VsInstance> Instances, string? WhyNone) Query()
    {
        if (!OperatingSystem.IsWindows()) return ([], "this host is not Windows");
        if (!File.Exists(VsWherePath)) return ([], $"vswhere.exe is not at '{VsWherePath}'");

        ChildProcess.ProcessResult result;
        try
        {
            result = RunVsWhere();
        }
        catch (TimeoutException)
        {
            return ([], $"vswhere did not answer within {QueryTimeout.TotalSeconds:0} s");
        }
        catch (Exception ex)
        {
            // vswhere failure is non-fatal: callers fall back to MSBuildLocator.RegisterDefaults().
            return ([], $"vswhere could not run: {FirstLine(ex.Message)}");
        }

        return Interpret(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    /// <summary>
    ///     Reads one vswhere run's exit code and output into the instances it lists, or why it listed none.
    /// </summary>
    /// <param name="exitCode">vswhere's exit code; anything but zero is a failure whatever it printed.</param>
    /// <param name="standardOutput">The JSON array emitted by <c>vswhere -format json</c>.</param>
    /// <param name="standardError">vswhere's error output, whose first line explains a nonzero exit.</param>
    /// <returns>
    ///     One record per array element with a valid <c>installationPath</c> and parseable
    ///     <c>installationVersion</c> and a null <c>WhyNone</c>; or no records and a one-line reason when
    ///     the run failed, printed nothing, printed something other than a JSON array, or listed no
    ///     usable entry.
    /// </returns>
    internal static (IReadOnlyList<VsInstance> Instances, string? WhyNone) Interpret(
        int exitCode, string standardOutput, string standardError)
    {
        if (exitCode != 0)
        {
            string detail = FirstLine(standardError);
            return ([], detail.Length == 0
                ? $"vswhere exited with code {exitCode}"
                : $"vswhere exited with code {exitCode}: {detail}");
        }

        if (string.IsNullOrWhiteSpace(standardOutput)) return ([], "vswhere printed nothing");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(standardOutput);
        }
        catch (JsonException)
        {
            return ([], $"vswhere's output is not JSON: {FirstLine(standardOutput)}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return ([], "vswhere's output is not a JSON array");

            int listed = doc.RootElement.GetArrayLength();
            if (listed == 0) return ([], "vswhere listed no instances");

            List<VsInstance> instances = ParseEntries(doc.RootElement);
            if (instances.Count == 0)
                return ([], $"vswhere listed {listed} {(listed == 1 ? "instance" : "instances")}, none with a "
                            + "readable installationPath and installationVersion");

            return (instances, null);
        }
    }

    private static List<VsInstance> ParseEntries(JsonElement array)
    {
        List<VsInstance> result = [];
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (!element.TryGetProperty("installationPath", out JsonElement pathProp) ||
                !element.TryGetProperty("installationVersion", out JsonElement versionProp))
                continue;

            string? path = pathProp.GetString();
            string? versionString = versionProp.GetString();
            if (path is null || versionString is null || !Version.TryParse(versionString, out Version? version)) continue;

            string name = element.TryGetProperty("displayName", out JsonElement nameProp) && nameProp.GetString() is { } n
                ? n
                : "Visual Studio";

            result.Add(new VsInstance(path, version, name));
        }

        return result;
    }

    // The selection note is one line, so a reason quotes only the first non-blank line of what it carries.
    private static string FirstLine(string text)
    {
        string line = text.Split('\n')
            .Select(static candidate => candidate.Trim())
            .FirstOrDefault(static candidate => candidate.Length > 0) ?? string.Empty;

        return line.Length <= MaxDetailLength ? line : line[..MaxDetailLength] + "…";
    }

    // Synchronous on purpose, and the reason ChildProcess carries a synchronous arm at all. This probe
    // runs inside MsBuildBootstrap, which the CLI and the xUnit adapter both call from a NoInlining JIT
    // quarantine: no MSBuild type may be resolvable before MSBuildLocator has registered, so that chain
    // cannot become asynchronous without either breaking the quarantine or blocking on a Task somewhere
    // in it. ChildProcess.Run blocks on the process handle and never on a Task, so this path stays
    // within the no-blocking-waits rule while keeping the quarantine intact — and it still gets the
    // closed stdin that keeps a child from inheriting (and wedging on) the MCP server's live JSON-RPC
    // pipe, plus a bounded wait and a kill-tree on expiry.
    private static ChildProcess.ProcessResult RunVsWhere()
    {
        ProcessStartInfo psi = new(VsWherePath, "-all -prerelease -format json -products *")
        {
            CreateNoWindow = true
        };

        return ChildProcess.Run(psi, QueryTimeout);
    }
}
