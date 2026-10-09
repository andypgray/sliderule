using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Locates the shell this repository's committed recipes run under, for the suites that execute those
///     recipes as real child processes.
/// </summary>
/// <remarks>
///     Every CI OS has a POSIX shell, Git for Windows supplying it on Windows, where it ships without necessarily
///     landing on the machine <c>PATH</c>, hence the fallback home. A machine with no shell at all skips the
///     suites that need one, with a named reason, rather than failing them.
/// </remarks>
internal static class ShellInterpreter
{
    /// <summary>The POSIX shell: the interpreter every committed <c>.sh</c> recipe is run under.</summary>
    private const string Sh = "sh";

    /// <summary>
    ///     The path to the POSIX shell, skipping the test with a named reason where it is not installed — the
    ///     shape every suite that shells out wants.
    /// </summary>
    internal static string Require()
    {
        string path = Locate() ?? string.Empty;
        Assert.SkipWhen(
            path.Length == 0,
            $"'{Sh}' is not available on this machine, so this arm cannot run here. It runs wherever "
            + "the interpreter is installed, which for sh is every CI OS.");

        return path;
    }

    /// <summary>
    ///     <paramref name="path" /> with forward slashes throughout: a POSIX shell takes <c>C:/...</c> as a
    ///     path, while a backslash inside it is an escape character rather than a separator.
    /// </summary>
    internal static string Posix(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>
    ///     Writes an executable stub named <paramref name="name" /> into <paramref name="directory" />: an
    ///     extensionless shebang script carrying <paramref name="shScript" />, which is what a POSIX shell resolves
    ///     on every OS.
    /// </summary>
    internal static void WriteExecutableStub(string directory, string name, string shScript)
    {
        string posix = Path.Combine(directory, name);
        File.WriteAllText(posix, shScript);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                posix,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>
    ///     A <c>PATH</c> with <paramref name="directory" /> in front of this process's — how a stub is put
    ///     where a child resolves the real command from. The shell's own directory comes next, so the shell
    ///     <see cref="Require" /> returns finds its commands even where this process's <c>PATH</c> never named
    ///     that directory.
    /// </summary>
    internal static string PrependedPath(string directory)
    {
        string? shell = Locate();
        string? shellDirectory = Path.GetDirectoryName(shell);
        string inherited = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] entries = shellDirectory is null ? [directory, inherited] : [directory, shellDirectory, inherited];

        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>
    ///     The POSIX shell's path, or null where there is none: the first <c>sh</c> on <c>PATH</c>, else Git for
    ///     Windows' own where it is installed off the <c>PATH</c>.
    /// </summary>
    private static string? Locate()
    {
        string? found = SearchPath(Sh);

        return found is null ? GitForWindowsShell() : PastGitLauncher(found);
    }

    /// <summary>
    ///     <paramref name="found" />, unless it is Git for Windows' <c>bin\sh.exe</c>, in which case the
    ///     <c>usr\bin\sh.exe</c> that launcher starts.
    /// </summary>
    /// <remarks>
    ///     The launcher starts the shell with <c>/mingw64/bin</c> and <c>/usr/bin</c> in front of the <c>PATH</c> it
    ///     was given, so a stub that <see cref="PrependedPath" /> puts in front loses to the real command. Without
    ///     this, every launcher test that stubs a command fails that way wherever <c>Git\bin</c> comes first on the
    ///     <c>PATH</c>, as it does on the hosted Windows CI images.
    /// </remarks>
    private static string PastGitLauncher(string found)
    {
        if (!OperatingSystem.IsWindows()) return found;

        string? directory = Path.GetDirectoryName(found);
        string? root = Path.GetDirectoryName(directory);
        bool inBin = string.Equals(Path.GetFileName(directory), "bin", StringComparison.OrdinalIgnoreCase);
        if (!inBin || root is null) return found;

        string shell = Path.Combine(root, "usr", "bin", "sh.exe");

        return File.Exists(shell) ? shell : found;
    }

    private static string? SearchPath(string command)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [command + ".exe", command + ".cmd", command + ".bat"]
            : [command];

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            try
            {
                foreach (string candidate in candidates)
                {
                    string full = Path.Combine(directory.Trim('"'), candidate);
                    if (File.Exists(full)) return full;
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with invalid path characters — skip it, as the OS loader does.
            }
        }

        return null;
    }

    private static string? GitForWindowsShell()
    {
        if (!OperatingSystem.IsWindows()) return null;

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string shell = Path.Combine(programFiles, "Git", "usr", "bin", "sh.exe");

        return File.Exists(shell) ? shell : null;
    }
}
