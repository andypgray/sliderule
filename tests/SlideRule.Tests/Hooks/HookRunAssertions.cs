using Shouldly;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     One run of the hook verb against a sandbox, carrying what an assertion about the run has to be able to name.
/// </summary>
/// <param name="Result">The exit code and both output channels.</param>
/// <param name="Checks">Each check the hook spent, in order, as it asked for it; empty when it spent none.</param>
/// <param name="ProjectDirectory">
///     The sandbox's project directory, which <c>CLAUDE_PROJECT_DIR</c> named unless the row
///     removed it.
/// </param>
/// <param name="WorkingDirectory">The directory the run was started from, standing in for the process's own.</param>
internal sealed record HookRun(
    CliResult Result,
    IReadOnlyList<CheckRequest> Checks,
    string ProjectDirectory,
    string WorkingDirectory);

/// <summary>
///     The Shouldly surface over <see cref="HookRun" />: each assertion names what the hook was supposed to do
///     (exit under the contract a Claude Code hook reads, spend a check on a directory, or leave one alone) and
///     carries the whole run into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>Result.Exit.ShouldBe(2)</c> says <c>1 should be 2</c> and hides the stderr line that explains
///         it, and a bare check on the recorded requests says <c>False should be True</c> and not one word about
///         where the hook did look. Every assertion here passes <see cref="Describe" /> as its Shouldly reason
///         instead, so the exit code, both channels, the directory of every check, and the two
///         directories the run was pointed at are in front of the reader.
///     </para>
///     <para>
///         A directory is compared as a directory rather than as a spelling. The tree a stop checks is the one
///         git names, and git need not spell a path the way the sandbox did: the case, the separators and a short
///         name in the temp root can all differ while naming one directory.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class HookRunAssertions
{
    /// <summary>
    ///     Asserts the hook exited <paramref name="expected" />: 0 proceed, 1 SlideRule's own error or a
    ///     stand-down at the round cap, 2 block. <paramref name="because" /> names what that code meant here, where
    ///     a row has something to add beyond the code itself.
    /// </summary>
    internal static HookRun ShouldExitWith(this HookRun run, int expected, string? because = null)
    {
        run.Result.Exit.ShouldBe(expected, Explain(run, because));

        return run;
    }

    /// <summary>
    ///     Asserts the hook spent a check on <paramref name="directory" />: some check it asked for reads that tree.
    /// </summary>
    internal static HookRun ShouldHaveRunTheCheckIn(this HookRun run, string directory, string because)
    {
        run.Checks
            .Any(check => SameDirectory(check.WorkingDirectory, directory))
            .ShouldBeTrue(Explain(run, because));

        return run;
    }

    /// <summary>The other half: asserts no check the hook asked for reads <paramref name="directory" />.</summary>
    internal static HookRun ShouldNotHaveRunTheCheckIn(this HookRun run, string directory, string because)
    {
        run.Checks
            .Any(check => SameDirectory(check.WorkingDirectory, directory))
            .ShouldBeFalse(Explain(run, because));

        return run;
    }

    /// <summary>
    ///     Asserts the hook spent exactly one check, and spent it on <paramref name="directory" />: no other tree was
    ///     checked beside it.
    /// </summary>
    internal static HookRun ShouldHaveOnlyRunTheCheckIn(this HookRun run, string directory, string because)
    {
        string explained = Explain(run, because);
        CheckRequest check = run.Checks.ShouldHaveSingleItem(explained);
        SameDirectory(check.WorkingDirectory, directory)
            .ShouldBeTrue(explained);

        return run;
    }

    /// <summary>Asserts the hook spent no check at all.</summary>
    internal static HookRun ShouldNotHaveRunTheCheck(this HookRun run, string because)
    {
        run.Checks.ShouldBeEmpty(Explain(run, because));

        return run;
    }

    /// <summary>Asserts the hook spent exactly one check, and hands it back for the row to read.</summary>
    internal static CheckRequest ShouldHaveRunOneCheck(this HookRun run)
    {
        return run.Checks.ShouldHaveSingleItem(Explain(run, "The hook should have spent exactly one check."));
    }

    /// <summary>
    ///     Asserts the hook spent one check and handed it exactly <paramref name="expected" /> as the changed files,
    ///     named relative to the project directory, so the check's tripwires read those and no others.
    /// </summary>
    internal static HookRun ShouldHaveHandedTheCheckChangedFiles(
        this HookRun run, IReadOnlyList<string> expected, string because)
    {
        CheckRequest check = run.ShouldHaveRunOneCheck();
        IReadOnlyList<string> changedFiles = check.ChangedFiles
            .ShouldNotBeNull(Explain(run, "A stop that snapshotted the tree hands the check its changed files."));
        List<string> relative = changedFiles
            .Select(file => Path.GetRelativePath(run.ProjectDirectory, file)
                .Replace('\\', '/'))
            .ToList();
        relative.ShouldBe(expected, Explain(run, because));
        check.DiffBase.ShouldBeNull(
            Explain(run, "Handed its changed files, the check must not list its own since the diff base as well."));

        return run;
    }

    /// <summary>
    ///     Whether <paramref name="candidate" /> and <paramref name="directory" /> name one directory, whatever
    ///     spelling each arrived in: a file planted in one is looked for through the other.
    /// </summary>
    internal static bool SameDirectory(string candidate, string directory)
    {
        if (!Directory.Exists(candidate) || !Directory.Exists(directory)) return false;

        var probe = $".same-directory-{Guid.NewGuid():N}";
        string planted = Path.Combine(directory, probe);
        File.WriteAllText(planted, "");
        using ScopedFile held = ScopedFile.Planted(planted);
        return File.Exists(Path.Combine(candidate, probe));
    }

    /// <summary>The row's own reason with the run appended, so a red carries both rather than either.</summary>
    private static string Explain(HookRun run, string? because)
    {
        string report = Describe(run);

        return because is null ? report : $"{because}{Environment.NewLine}{report}";
    }

    /// <summary>
    ///     The exit code, both output channels, the tree of every check it ran, and the two
    ///     directories a run is pointed at: what the hook did, and where it was standing when it did it.
    /// </summary>
    private static string Describe(HookRun run)
    {
        string checks = run.Checks.Count == 0
            ? "no check"
            : "checks in " + string.Join(", ", run.Checks.Select(static check => check.WorkingDirectory));

        return $"Started in {run.WorkingDirectory}, against the sandbox project directory {run.ProjectDirectory}, "
               + $"having run {checks}.{Environment.NewLine}{run.Result.Transcript()}";
    }
}
