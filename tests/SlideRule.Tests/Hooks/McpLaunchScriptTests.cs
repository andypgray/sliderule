using System.Diagnostics;
using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Runs the committed <c>hooks/mcp-launch.sh</c> as a real child process and holds it to the six things
///     a client depends on: the server is executed from a copy outside the build tree, that copy is keyed to
///     the build it was taken from, the copy it settles on is marked in use so the sweep ages it from the last
///     launch rather than the first, the sweep removes what nothing has launched from in days whichever
///     <c>find</c> the <c>PATH</c> reaches first, a failure to stage still leaves a running server, and
///     nothing the launcher has to say ever reaches stdout.
/// </summary>
/// <remarks>
///     <para>
///         A stub <c>dotnet</c> first on <c>PATH</c> records the argument vector it was execed with and
///         returns, so the whole recipe is measured without starting a server: what is under test is which
///         copy of the CLI a client would have been connected to, not what that CLI then does.
///     </para>
///     <para>
///         The launcher runs from a working directory that is <em>not</em> the project directory and is told
///         where the build output is with a relative path, so the <c>CLAUDE_PROJECT_DIR</c> anchoring is under
///         test too: nothing can be staged at all unless the launcher changed directory first.
///     </para>
///     <para>
///         The last of the six is the one with no second chance. stdout is the JSON-RPC channel, and a single
///         stray line on it is a protocol error before the handshake — so the prelude is redirected wholesale
///         and every case here asserts an empty stdout, the ones that fail to stage included.
///     </para>
/// </remarks>
public sealed class McpLaunchScriptTests
{
    /// <summary>
    ///     The entry assembly the launcher execs, and one of the files whose modification time and size key the
    ///     copy.
    /// </summary>
    private const string ServerDll = "sliderule-cli.dll";

    /// <summary>
    ///     A dependency beside the entry assembly: proof the whole directory is staged, and a file whose rebuild
    ///     the key has to notice on its own.
    /// </summary>
    private const string SiblingDll = "SlideRule.dll";

    private const string StagedMarker = "staged-marker.txt";

    /// <summary>A build still writing however long the launcher waits for it.</summary>
    private const int Forever = int.MaxValue;

    [Fact]
    public void Launch_ExecsTheServerFromACopyOutsideTheBuildTree()
    {
        using var lab = new Lab();

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln", "--spec", "./Spec.dll");

        result.ShouldHaveExitedWith(0);

        string[] argv = lab.ExecutedArguments();
        argv.ShouldBe(["exec", lab.StagedServerDll(), "mcp", "MyApp.sln", "--spec", "./Spec.dll"]);

        // The whole build output travels, not just the file the identity is keyed on.
        File.Exists(Path.Combine(lab.StagedDirectories()
                .Single(), SiblingDll))
            .ShouldBeTrue($"{SiblingDll} was left behind, so the staged copy is not a runnable server.");
    }

    [Fact]
    public void SecondLaunchOnTheSameBuild_ReusesTheCopyAndRestagesNothing()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string first = lab.ExecedServerDll();

        // Written into the staged copy between the two launches: a re-stage would replace the directory this
        // sits in, so surviving is what "copied nothing" looks like from outside.
        File.WriteAllText(Path.Combine(lab.StagedDirectories()
            .Single(), StagedMarker), "still here");

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        lab.ExecedServerDll()
            .ShouldBe(first);
        string staged = lab.StagedDirectories()
            .ShouldHaveSingleItem(
                "a second session on one build must share the first session's copy, not mint its own.");
        File.Exists(Path.Combine(staged, StagedMarker))
            .ShouldBeTrue("the copy was re-staged over a build that had not changed.");
    }

    [Fact]
    public void ARebuild_MintsAFreshCopyAndLeavesTheOldOneStanding()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string beforeRebuild = lab.ExecedServerDll();

        lab.WriteBuildOutput("a rebuilt CLI, of a different size");
        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        string afterRebuild = lab.ExecedServerDll();
        afterRebuild.ShouldNotBe(
            beforeRebuild, "a rebuild has to be picked up, or the client is served the previous build forever.");

        // The old directory stays: a server may still be running from it, which is the whole reason the key
        // exists rather than one directory that gets overwritten.
        lab.StagedDirectories()
            .Length.ShouldBe(2);
        File.ReadAllText(afterRebuild)
            .ShouldBe("a rebuilt CLI, of a different size");
    }

    [Fact]
    public void MissingBuildOutput_StopsWithAnExplanationRatherThanLaunching()
    {
        using var lab = new Lab();
        lab.BuildOutput = "not-built";

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(1);
        result.StandardError.ShouldContain("not-built/sliderule-cli.dll not found");
        lab.Executed.ShouldBeFalse("there was nothing to run, so nothing should have been execed.");
    }

    [Fact]
    public void WhenTheCopyCannotBeStaged_ItRunsFromTheBuildOutputAndSaysWhy()
    {
        using var lab = new Lab();

        // A run root that cannot be created, because its parent is a regular file. Whatever the reason on a
        // given machine — a full disk, a read-only home — the recipe is the same: a contended server beats no
        // server, with the reason where the client will show it.
        string file = Path.Combine(lab.Root, "not-a-directory");
        File.WriteAllText(file, "occupied");
        lab.RunRoot = Path.Combine(file, "copies");

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        result.StandardError.ShouldContain("could not stage a copy");
        lab.ExecedServerDll()
            .ShouldBe($"{lab.BuildOutput}/{ServerDll}");
    }

    [Fact]
    public void ARebuiltDependency_MintsAFreshCopy()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string beforeRebuild = lab.ExecedServerDll();

        // Only the dependency moves, and to a new size, so no filesystem clock is coarse enough to hide it. The entry
        // assembly keeps its bytes and its timestamp: a change inside a project that builds a reference assembly
        // recompiles nothing downstream of it, so the CLI's own assembly is left exactly as it was.
        lab.WriteSiblingDll("a rebuilt dependency, of a different size");
        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        string afterRebuild = lab.ExecedServerDll();
        afterRebuild.ShouldNotBe(
            beforeRebuild, "a copy keyed on the entry assembly alone goes on serving the dependency from before the rebuild.");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(afterRebuild)!, SiblingDll))
            .ShouldBe("a rebuilt dependency, of a different size");
    }

    [Fact]
    public void AnEmptiedCopy_IsRestagedWithNoNestedStaging()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string copy = lab.StagedDirectories()
            .Single();

        // A key directory with nothing left in it, as a sweep interrupted mid-delete leaves one. Restaging it renames
        // a new copy onto a directory that already exists, and that rename moves the staging directory inside the
        // target instead of replacing it.
        foreach (string file in Directory.GetFiles(copy)) File.Delete(file);

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        lab.ExecedServerDll()
            .ShouldBe(
                lab.StagedServerDll(),
                "the launcher has to restage the key it found empty, not fall back to the build output.");
        Directory.GetDirectories(lab.RunRoot, ".staging.*", SearchOption.AllDirectories)
            .ShouldBeEmpty("a staging directory left inside a copy is a second copy of the build that nothing removes.");
    }

    [Fact]
    public void ABuildWritingDuringTheCopy_PublishesOnlyAStableCopy()
    {
        using var lab = new Lab();
        lab.InstallCopyThatRebuildsOnce("the dependency a build wrote during the copy");

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        string published = lab.StagedDirectories()
            .ShouldHaveSingleItem("the copy taken while the build wrote must be discarded, not published beside the stable one.");
        lab.ExecedServerDll()
            .ShouldBe(lab.StagedServerDll());
        File.ReadAllText(Path.Combine(published, SiblingDll))
            .ShouldBe(
                "the dependency a build wrote during the copy",
                "the server has to run the build as it stood once the write landed, not the mix the first copy caught.");
    }

    [Fact]
    public void ABuildStillWriting_IsWaitedOutRatherThanCopiedAgainWhileItWrites()
    {
        using var lab = new Lab();

        // Writing when the launch begins, and for the launcher's first three seconds of waiting after that.
        lab.InstallBuildStillWritingFor(seconds: 3);

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        lab.CopiesTaken.ShouldBe(
            2,
            "one copy the build wrote into and one once it held still: every copy taken while the build is still "
            + "moving is the whole build output copied only to be thrown away.");
        string published = lab.StagedDirectories()
            .ShouldHaveSingleItem("only the copy of the settled build is published.");
        lab.ExecedServerDll()
            .ShouldBe(lab.StagedServerDll());
        File.ReadAllText(Path.Combine(published, SiblingDll))
            .ShouldBe(lab.BuiltSibling, "the server has to run the build as it stood once it settled.");
    }

    [Fact]
    public void ABuildThatNeverSettles_RunsFromItsOneCopyUnpublished()
    {
        using var lab = new Lab();
        lab.InstallBuildStillWritingFor(Forever);

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        result.StandardError.ShouldContain("kept changing");
        lab.CopiesTaken.ShouldBe(1, "a build that never holds still is waited on, not copied again at every look.");
        string unpublished = lab.StagedDirectories()
            .ShouldHaveSingleItem("the copy the launcher runs from is the one directory it keeps.");
        Path.GetFileName(unpublished)
            .ShouldStartWith(".staging.", customMessage: "a copy the build wrote into is never published under a key.");
        lab.ExecedServerDll()
            .ShouldBe(
                ShellInterpreter.Posix(Path.Combine(unpublished, ServerDll)),
                "a copy the build wrote into still beats a build output that a build is writing to.");
    }

    [Fact]
    public void ACopyThatFailsAtTheRetryCap_RunsFromTheBuildOutputRatherThanTheCopyItCouldNotMake()
    {
        using var lab = new Lab();

        // A cp that copies nothing and a build that never settles, so the loop runs to its cap with a
        // staging directory that holds none of the assemblies the server loads.
        lab.InstallCopyThatFailsWhileTheBuildKeepsMoving();

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        result.StandardError.ShouldContain("could not stage a copy");
        lab.ExecedServerDll()
            .ShouldBe(
                $"{lab.BuildOutput}/{ServerDll}",
                "a copy the cp left unmade cannot start a server, and the build output at least still can.");
        lab.StagedDirectories()
            .ShouldBeEmpty("the copy that failed is not a copy to keep, published or staged.");
    }

    [Fact]
    public void ACopyThatFailsWhileTheBuildHoldsStill_RunsFromTheBuildOutputWithoutCopyingAgain()
    {
        using var lab = new Lab();
        lab.InstallCopyThatFailsWhileTheBuildHoldsStill();

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        result.StandardError.ShouldContain("could not stage a copy");
        lab.CopiesTaken.ShouldBe(1, "with nothing writing the build, a second copy fails the way the first did.");
        lab.ExecedServerDll()
            .ShouldBe($"{lab.BuildOutput}/{ServerDll}");
        lab.StagedDirectories()
            .ShouldBeEmpty("the copy that failed is not a copy to keep, published or staged.");
    }

    [Fact]
    public void ACopyTheLoopRekeyedOnto_IsMarkedInUseByTheLaunchThatExecsIt()
    {
        using var lab = new Lab();
        lab.Launch("MyApp.sln");
        string copy = lab.StagedDirectories()
            .Single();

        // The build moves, then moves back mid-copy onto the build this copy was taken from — another
        // session's copy, which this launch re-keys onto and exits the loop without staging anything.
        lab.InstallCopyThatRestoresTheBuildAsItStandsNow();
        lab.WriteSiblingDll("a rebuilt dependency, of a different size");

        // Staged a day ago by that other session, and not by this launch: the sweep spares it at two days,
        // so what the mark is worth is legible without waiting for one.
        DateTime staged = DateTime.UtcNow.AddDays(-1);
        Directory.SetLastWriteTimeUtc(copy, staged);

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        lab.ExecedServerDll()
            .ShouldBe(
                ShellInterpreter.Posix(Path.Combine(copy, ServerDll)),
                "the build went back to the one this copy holds, so the launcher has to re-key onto it.");
        Directory.GetLastWriteTimeUtc(copy)
            .ShouldBeGreaterThan(
                staged,
                "the copy the launcher settled on still carries the other session's mark, so it ages out of "
                + "the sweep while this client is connected to it.");
    }

    [Fact]
    public void AnAgedCopy_IsSweptWhicheverFindComesFirstOnPath()
    {
        using var lab = new Lab();

        // Stands in for Windows' own find.exe, which is first on PATH wherever System32 comes ahead of Git's tools,
        // and which refuses the POSIX options the sweep passes.
        lab.InstallFindThatRefusesItsArguments();
        lab.ResolvedCommand("find")
            .ShouldEndWith(
                "/stub/find",
                customMessage: "the shell does not reach the stub find, so this case would pass whether or not the "
                               + "sweep depends on PATH order.");

        // The sweep removes what is more than two whole days old, so five is well clear of the boundary.
        string aged = lab.WriteAgedCopy(TimeSpan.FromDays(5));

        ChildProcess.ProcessResult result = lab.Launch("MyApp.sln");

        result.ShouldHaveExitedWith(0);
        Directory.Exists(aged)
            .ShouldBeFalse(
                "a copy nothing has launched from in five days outlived the sweep, so copies of old builds pile up "
                + "under the run root for good.");
        lab.ExecedServerDll()
            .ShouldBe(lab.StagedServerDll());
    }

    /// <summary>
    ///     One launcher run's world: a project directory holding a fake build output, a run root for the
    ///     copies, and a stub <c>dotnet</c> on <c>PATH</c> that records what it was execed with.
    /// </summary>
    private sealed class Lab : IDisposable
    {
        /// <summary>
        ///     The real <c>cp</c>, run with the stub's own arguments, its status kept for the stub to exit with.
        ///     /bin/cp is where every CI OS keeps it, macOS included, and Git Bash resolves it there too.
        /// </summary>
        private const string RealCopy =
            "for real in /bin/cp /usr/bin/cp; do\n"
            + "  if [ -x \"$real\" ]; then \"$real\" \"$@\"; status=$?; break; fi\n"
            + "done\n";

        private readonly string _argumentsFile;
        private readonly string _copiesFile;
        private readonly string _secondsWaitedFile;
        private readonly string _stubDirectory;
        private readonly TempDirectory _temp = TestTempRoot.Fresh("mcp-launcher");

        internal Lab()
        {
            ProjectDirectory = Path.Combine(Root, "project");
            RunRoot = Path.Combine(Root, "copies");
            _stubDirectory = Path.Combine(Root, "stub");
            _argumentsFile = Path.Combine(Root, "execed-arguments.txt");
            _copiesFile = Path.Combine(Root, "copies-taken.txt");
            _secondsWaitedFile = Path.Combine(Root, "seconds-waited.txt");

            Directory.CreateDirectory(Path.Combine(ProjectDirectory, BuildOutput));
            Directory.CreateDirectory(_stubDirectory);
            WriteStubDotnet();
            // A sleep that returns at once. The launcher's waits are counted in looks at the build, never in
            // elapsed time, and every change these cases drive is a size a stub writes, so a real pause would
            // buy them only wall clock. A build still writing while the launcher waits brings a sleep of its own.
            Stub("sleep", "exit 0\n");
            WriteBuildOutput("a built CLI");
        }

        internal string Root => _temp.Path;

        private string ProjectDirectory { get; }

        /// <summary>
        ///     Where the copies live. Absolute, as a client would set it: the launcher never resolves it.
        /// </summary>
        internal string RunRoot { get; set; }

        /// <summary>Kept relative, so a launcher that did not anchor itself to the project finds nothing.</summary>
        internal string BuildOutput { get; set; } = "bin";

        /// <summary>The dependency beside the entry assembly, in the build output as it is set now.</summary>
        private string SiblingPath => Path.Combine(ProjectDirectory, BuildOutput, SiblingDll);

        /// <summary>The dependency's content in the build output as it stands now.</summary>
        internal string BuiltSibling => File.ReadAllText(SiblingPath);

        /// <summary>How many copies of the build output the launcher took, as the stub <c>cp</c> counts them.</summary>
        internal int CopiesTaken => File.Exists(_copiesFile) ? File.ReadAllLines(_copiesFile).Length : 0;

        /// <summary>Whether the launcher reached the exec at all.</summary>
        internal bool Executed => File.Exists(_argumentsFile);

        public void Dispose()
        {
            _temp.Dispose();
        }

        /// <summary>Writes the fake build output: the file the copy is keyed on, and one sibling beside it.</summary>
        internal void WriteBuildOutput(string serverDllContent)
        {
            File.WriteAllText(Path.Combine(ProjectDirectory, BuildOutput, ServerDll), serverDllContent);
            WriteSiblingDll("a dependency of the server");
        }

        /// <summary>
        ///     Rewrites the dependency alone, leaving the entry assembly's bytes and timestamp as they were.
        /// </summary>
        internal void WriteSiblingDll(string content)
        {
            File.WriteAllText(SiblingPath, content);
        }

        /// <summary>
        ///     Puts a <c>cp</c> first on <c>PATH</c> that runs the real one and then, the first time only, rewrites the
        ///     dependency in the build output with <paramref name="siblingContent" />: a build landing just after the
        ///     launcher's copy read the file, so the copy holds the build from before it.
        /// </summary>
        internal void InstallCopyThatRebuildsOnce(string siblingContent)
        {
            string sibling = ShellInterpreter.Posix(SiblingPath);
            string marker = ShellInterpreter.Posix(Path.Combine(Root, "rebuilt-once"));

            StubCp(
                RealCopy,
                $"if [ ! -f '{marker}' ]; then\n"
                + $"  : > '{marker}'\n"
                + $"  printf '%s' '{siblingContent}' > '{sibling}'\n"
                + "fi\n");
        }

        /// <summary>
        ///     Puts a <c>cp</c> and a <c>sleep</c> first on <c>PATH</c> for a build that is writing when the launch
        ///     begins and goes on until the launcher has waited <paramref name="seconds" /> seconds: until then the
        ///     dependency in the build output grows during every copy the launcher takes and at every second it
        ///     waits, and after that it holds still.
        /// </summary>
        internal void InstallBuildStillWritingFor(int seconds)
        {
            StubABuildStillWritingFor(seconds, RealCopy);
        }

        /// <summary>
        ///     Puts a <c>cp</c> first on <c>PATH</c> that copies nothing and fails, beside a build that never stops
        ///     writing: a copy that cannot be made while a build keeps landing, which is what carries the launcher
        ///     to its cap with nothing staged under it.
        /// </summary>
        internal void InstallCopyThatFailsWhileTheBuildKeepsMoving()
        {
            StubABuildStillWritingFor(Forever, "status=1\n");
        }

        /// <summary>
        ///     Puts a <c>cp</c> first on <c>PATH</c> that copies nothing and fails while nothing writes the build: a
        ///     failure no wait can cure, as a full disk is.
        /// </summary>
        internal void InstallCopyThatFailsWhileTheBuildHoldsStill()
        {
            StubABuildStillWritingFor(0, "status=1\n");
        }

        /// <summary>
        ///     Takes the dependency as it stands, then puts a <c>cp</c> first on <c>PATH</c> that runs the real
        ///     one and puts that file back — its bytes and its modification time alike, which is what the key is
        ///     read from. A build landing during the launcher's copy that returns the output to a build some
        ///     copy was staged from already.
        /// </summary>
        internal void InstallCopyThatRestoresTheBuildAsItStandsNow()
        {
            string sibling = SiblingPath;
            string reference = Path.Combine(Root, "the-dependency-before-the-rebuild");

            File.Copy(sibling, reference, true);
            File.SetLastWriteTimeUtc(reference, File.GetLastWriteTimeUtc(sibling));

            StubCp(
                RealCopy,
                $"cat '{ShellInterpreter.Posix(reference)}' > '{ShellInterpreter.Posix(sibling)}'\n"
                + $"touch -r '{ShellInterpreter.Posix(reference)}' '{ShellInterpreter.Posix(sibling)}'\n");
        }

        /// <summary>
        ///     Puts a <c>find</c> first on <c>PATH</c> that refuses whatever it is given, as Windows' own
        ///     <c>find.exe</c> refuses the POSIX options.
        /// </summary>
        internal void InstallFindThatRefusesItsArguments()
        {
            Stub("find", "printf 'FIND: Parameter format not correct\\n' >&2\nexit 2\n");
        }

        /// <summary>
        ///     What a bare <paramref name="command" /> resolves to under the <c>PATH</c> the launcher is run with, so
        ///     a case can show which of two same-named commands the launcher would reach.
        /// </summary>
        internal string ResolvedCommand(string command)
        {
            string shell = ShellInterpreter.Require();

            var startInfo = new ProcessStartInfo(shell);
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"command -v {command}");
            startInfo.Environment["PATH"] = ShellInterpreter.PrependedPath(_stubDirectory);

            return ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1))
                .StandardOutput.Trim();
        }

        /// <summary>
        ///     Writes a copy under the run root that nothing has launched from for <paramref name="age" />, and
        ///     returns its directory. The time is set after the file is written, since the write would reset it.
        /// </summary>
        internal string WriteAgedCopy(TimeSpan age)
        {
            string directory = Path.Combine(RunRoot, "aged-copy");

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ServerDll), "a CLI built days ago");
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow - age);

            return directory;
        }

        /// <summary>
        ///     Runs the committed launcher with <paramref name="serverArguments" /> to pass through, and asserts it
        ///     left stdout empty, which every case here holds it to.
        /// </summary>
        internal ChildProcess.ProcessResult Launch(params string[] serverArguments)
        {
            string shell = ShellInterpreter.Require();

            File.Delete(_argumentsFile);

            var startInfo = new ProcessStartInfo(shell)
            {
                // Not the project directory: the launcher has to reach CLAUDE_PROJECT_DIR itself.
                WorkingDirectory = Root
            };
            startInfo.ArgumentList.Add(ShellInterpreter.Posix(RepoRoot.Absolute("hooks/mcp-launch.sh")));
            foreach (string argument in serverArguments) startInfo.ArgumentList.Add(argument);

            startInfo.Environment["PATH"] = ShellInterpreter.PrependedPath(_stubDirectory);
            startInfo.Environment["CLAUDE_PROJECT_DIR"] = ShellInterpreter.Posix(ProjectDirectory);
            startInfo.Environment["SLIDERULE_MCP_BUILD_OUTPUT"] = BuildOutput;
            startInfo.Environment["SLIDERULE_MCP_RUN_ROOT"] = ShellInterpreter.Posix(RunRoot);
            startInfo.Environment["SLIDERULE_TEST_EXECED_ARGUMENTS"] = ShellInterpreter.Posix(_argumentsFile);

            return ChildProcess.Run(startInfo, TimeSpan.FromMinutes(1))
                .ShouldHaveLeftStdoutEmpty();
        }

        /// <summary>What the stub <c>dotnet</c> was execed with, one argument per line.</summary>
        internal string[] ExecutedArguments()
        {
            File.Exists(_argumentsFile)
                .ShouldBeTrue("the launcher never reached the exec.");

            return File.ReadAllLines(_argumentsFile);
        }

        /// <summary>
        ///     The server the launcher did exec: the dll is argv[1], because the exec vector is
        ///     <c>dotnet exec &lt;dll&gt; mcp …</c>.
        /// </summary>
        internal string ExecedServerDll()
        {
            return ExecutedArguments()[1];
        }

        /// <summary>The copies staged so far, newest key last.</summary>
        internal string[] StagedDirectories()
        {
            return Directory.Exists(RunRoot)
                ? Directory.GetDirectories(RunRoot)
                    .Order()
                    .ToArray()
                : [];
        }

        /// <summary>The server the launcher should have execed: the one file inside the single staged copy.</summary>
        internal string StagedServerDll()
        {
            return ShellInterpreter.Posix(Path.Combine(StagedDirectories()
                .Single(), ServerDll));
        }

        /// <summary>
        ///     Writes the stub <c>dotnet</c>: it records its argument vector and exits, so no server is ever
        ///     started.
        /// </summary>
        private void WriteStubDotnet()
        {
            Stub("dotnet", "printf '%s\\n' \"$@\" > \"$SLIDERULE_TEST_EXECED_ARGUMENTS\"\n");
        }

        /// <summary>
        ///     Puts a <c>sleep</c> and a <c>cp</c> first on <c>PATH</c> that write the dependency in the build output
        ///     for as long as the launcher has waited fewer than <paramref name="seconds" /> seconds, the <c>cp</c>
        ///     doing <paramref name="copy" /> first.
        /// </summary>
        private void StubABuildStillWritingFor(int seconds, string copy)
        {
            string counter = ShellInterpreter.Posix(_secondsWaitedFile);
            var secondsWaited = $"$(cat '{counter}' 2>/dev/null || echo 0)";

            // A byte each write, because the key reads size as well as time: content of one size rewritten
            // inside a filesystem clock's tick is a build the launcher cannot tell from the one before it.
            var write = $"printf 'x' >> '{ShellInterpreter.Posix(SiblingPath)}'";

            Stub(
                "sleep",
                $"waited=$(( {secondsWaited} + 1 ))\n"
                + $"printf '%s' \"$waited\" > '{counter}'\n"
                + $"if [ \"$waited\" -le {seconds} ]; then {write}; fi\n"
                + "exit 0\n");
            StubCp(copy, $"if [ \"{secondsWaited}\" -lt {seconds} ]; then {write}; fi\n");
        }

        /// <summary>
        ///     Puts a <c>cp</c> first on <c>PATH</c> that counts itself, does <paramref name="copy" />, then
        ///     <paramref name="afterCopy" />, and exits with the status <paramref name="copy" /> left in
        ///     <c>status</c>.
        /// </summary>
        private void StubCp(string copy, string afterCopy)
        {
            Stub(
                "cp",
                $"printf 'copy\\n' >> '{ShellInterpreter.Posix(_copiesFile)}'\n"
                + copy
                + afterCopy
                + "exit \"${status:-127}\"\n");
        }

        /// <summary>
        ///     Writes an executable <c>sh</c> script named <paramref name="command" /> into the stub directory.
        /// </summary>
        private void Stub(string command, string body)
        {
            ShellInterpreter.WriteExecutableStub(_stubDirectory, command, "#!/bin/sh\n" + body);
        }
    }
}
