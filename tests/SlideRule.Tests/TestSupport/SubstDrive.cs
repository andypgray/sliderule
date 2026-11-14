using System.Diagnostics;
using SlideRule.Roslyn.Hosting;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A drive letter substituted for a directory with <c>subst</c>, for the tests that reach a path through one,
///     held until disposed. The mapping belongs to the user's logon session, so it is visible to every process
///     there while it lasts.
/// </summary>
/// <remarks>
///     <c>subst</c> exits 1 for every failure, so a taken letter is told from a broken run by its message: only
///     "Drive already SUBSTed" and "Invalid parameter - L:" move on to the next letter, and any other failure throws
///     with both output channels.
/// </remarks>
internal sealed class SubstDrive : IDisposable
{
    private readonly string _target;

    private SubstDrive(string drive, string target)
    {
        Drive = drive;
        _target = target;
    }

    /// <summary>The substituted drive, such as <c>Z:</c>, with no trailing separator.</summary>
    public string Drive { get; }

    public void Dispose()
    {
        ChildProcess.ProcessResult result = Subst(Drive, "/D");
        if (result.ExitCode != 0) throw result.FailureOf($"subst {Drive} /D");
    }

    /// <summary>
    ///     <paramref name="target" />, an existing directory, behind the highest free drive letter, or a skip of the
    ///     calling test off Windows and when every letter is taken.
    /// </summary>
    public static SubstDrive OverOrSkip(string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "subst exists only on Windows.");

        HashSet<char> used = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        // A volume attached while the test runs takes the lowest free letter, so the highest stays clear of it.
        IEnumerable<char> candidates = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(letter => (char)letter)
            .Reverse()
            .Where(letter => !used.Contains(letter));
        foreach (char letter in candidates)
        {
            var drive = $"{letter}:";
            ChildProcess.ProcessResult result = Subst(drive, target);
            if (result.ExitCode == 0) return new SubstDrive(drive, target);

            string said = (result.StandardOutput + result.StandardError).Trim();
            bool taken = said == "Drive already SUBSTed" || said == $"Invalid parameter - {drive}";
            if (!taken) throw result.FailureOf($"subst {drive} {target}");
        }

        Assert.Skip("Every drive letter from D: to Z: is taken, so none is free to substitute.");
        return null!;
    }

    /// <summary><paramref name="path" />, which lies under the substituted directory, spelled through the drive.</summary>
    public string Spelling(string path)
    {
        string below = Path.GetRelativePath(_target, path);
        return below == "." ? Drive + Path.DirectorySeparatorChar : Path.Join(Drive + Path.DirectorySeparatorChar, below);
    }

    private static ChildProcess.ProcessResult Subst(string drive, string argument)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var startInfo = new ProcessStartInfo(Path.Join(system, "subst.exe")) { CreateNoWindow = true };
        startInfo.ArgumentList.Add(drive);
        startInfo.ArgumentList.Add(argument);
        return ChildProcess.Run(startInfo);
    }
}
