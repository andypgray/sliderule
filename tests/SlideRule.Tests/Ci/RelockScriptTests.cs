using System.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Ci;

/// <summary>
///     Runs the committed <c>.github/scripts/relock.sh</c> as a real child process on a Dependabot NuGet branch, in a
///     throwaway repository whose stub <c>dotnet</c> rewrites a lock file, and holds it to what the CI jobs rely on:
///     a job that packs gets the regenerated lock files as a local commit, and a job that uploads SARIF gets them
///     uncommitted, with HEAD still the commit GitHub checked out.
/// </summary>
public sealed class RelockScriptTests
{
    private const string LockFile = "src/A/packages.lock.json";

    private const string DependabotNuGetRef = "dependabot/nuget/src/A/Some.Package-1.2.3";

    [Fact]
    public void NoCommit_LeavesTheRegeneratedLockFileUncommittedAndHeadWhereItWas()
    {
        using TempDirectory temp = TestTempRoot.Fresh("relock");
        Bed bed = Bed.In(temp);
        string before = bed.Head();

        ChildProcess.ProcessResult result = bed.Relock("--no-commit");

        result.ExitCode.ShouldBe(0, result.Transcript());
        bed.Head()
            .ShouldBe(
                before,
                "a job that uploads SARIF must keep the checked-out commit at HEAD: code scanning refuses one that "
                + "was never pushed.");
        bed.Status()
            .ShouldBe($" M {LockFile}\n", "the regenerated lock file belongs in the tree the job builds, uncommitted.");
        bed.LocalUserName()
            .ShouldBeEmpty("a run that makes no commit has no reason to write an identity into the repository's config.");
    }

    [Fact]
    public void WithoutTheFlag_CommitsTheRegeneratedLockFileAndNothingElse()
    {
        using TempDirectory temp = TestTempRoot.Fresh("relock");
        Bed bed = Bed.In(temp);
        string before = bed.Head();
        File.WriteAllText(Path.Combine(bed.Root, "notes.txt"), "an untracked file that is not a lock file\n");

        ChildProcess.ProcessResult result = bed.Relock();

        result.ExitCode.ShouldBe(0, result.Transcript());
        GitCommand.Output(bed.Root, "log", "--format=%s", $"{before}..HEAD")
            .ShouldBe("Regenerate NuGet lock files\n", "pack-check names the commit it packs, so the relock must be one.");
        GitCommand.Output(bed.Root, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .ShouldBe($"{LockFile}\n", "the relock commit holds the lock files and nothing else in the tree.");
    }

    [Fact]
    public void AnUnknownArgument_ExitsTwoNamingIt_BeforeAnyRestore()
    {
        using TempDirectory temp = TestTempRoot.Fresh("relock");
        Bed bed = Bed.In(temp);

        ChildProcess.ProcessResult result = bed.Relock("--bogus");

        result.ExitCode.ShouldBe(2, result.Transcript());
        result.StandardError.ShouldBe("::error::relock.sh takes no argument but --no-commit, not '--bogus'.\n");
        bed.Status()
            .ShouldBeEmpty("an argument the script does not know stops it before the restore.");
    }

    /// <summary>
    ///     A git repository with one committed lock file, and a stub <c>dotnet</c> that rewrites it the way a restore
    ///     after a version bump does, both inside a temporary directory the test owns.
    /// </summary>
    private sealed class Bed
    {
        private readonly string _stubDirectory;

        private Bed(string root, string stubDirectory)
        {
            Root = root;
            _stubDirectory = stubDirectory;
        }

        internal string Root { get; }

        internal static Bed In(TempDirectory temp)
        {
            string root = temp.PathOf("repo");
            string stubDirectory = temp.PathOf("stub");
            string lockFile = Path.Combine(root, LockFile);
            Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);
            Directory.CreateDirectory(stubDirectory);

            File.WriteAllText(lockFile, "{\"version\": 2}\n");
            TempGitRepo.CommitAsRepository(root);

            ShellInterpreter.WriteExecutableStub(
                stubDirectory,
                "dotnet",
                $"#!/bin/sh\nprintf '{{\"version\": 2, \"regenerated\": true}}\\n' > '{LockFile}'\n");

            return new Bed(root, stubDirectory);
        }

        internal string Head()
        {
            return GitCommand.Output(Root, "rev-parse", "HEAD")
                .TrimEnd('\n');
        }

        internal string Status()
        {
            return GitCommand.Output(Root, "status", "--porcelain");
        }

        internal string LocalUserName()
        {
            return GitCommand.Attempt(Root, "config", "--local", "--get", "user.name")
                .StandardOutput;
        }

        /// <summary>
        ///     Runs the committed script with <paramref name="arguments" /> on a Dependabot NuGet head ref, under bash
        ///     as every CI step runs it, since its <c>set -o pipefail</c> is not POSIX.
        /// </summary>
        internal ChildProcess.ProcessResult Relock(params string[] arguments)
        {
            ProcessStartInfo startInfo = ShellInterpreter.CommittedScript(
                ".github/scripts/relock.sh", Root, _stubDirectory, arguments);
            startInfo.Environment["GITHUB_HEAD_REF"] = DependabotNuGetRef;
            // The script's commit takes the identity it writes, but a host's global signing setting would still
            // reach it.
            startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
            startInfo.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgsign";
            startInfo.Environment["GIT_CONFIG_VALUE_0"] = "false";

            return ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1));
        }
    }
}
