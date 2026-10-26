using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The residence verb <c>MustResideInProject</c> over the fast path (GRAMMAR §5.3, §4.1, §4.3): a
///     subject is red unless the named project declares it, and declaration is N-way — one source file
///     compiled into several projects resides in every one of them, so <em>any</em> declarer satisfies the
///     verb while a project that merely references the declaration does not.
/// </summary>
/// <remarks>
///     The verb reads the declarer roster extraction already records, so it adds no extracted fact and
///     nothing for the extraction tests to cover. The bed is <see cref="MultiplyDeclaredCodebase" />, where
///     three projects compile the shared file and Client only references it, which gives the any-declarer
///     claim both its halves. Violations are <see cref="ViolationKind.Shape" />: identity is the subject
///     symbol ID riding <see cref="BaselineEntry.ForSubject" />, and the subject's declaration sites are
///     evidence, never identity.
/// </remarks>
public sealed class MustResideInProjectVerbTests
{
    [Fact]
    public void MustResideInProject_SubjectDeclaredByAnotherProject_FailsWithSubjectAndDeclarationSites()
    {
        // A shape violation has no edge to cite, so the file:line an agent jumps to is where the offending
        // type is declared — the subject's own declaration sites, carried as evidence.
        Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Namespace("Tool.*").MustResideInProject("Core"))
            .ShouldHaveFailedWithSubjectAtSites("Tool.Command", ["Command.cs:3"]);
    }

    [Fact]
    public void MustResideInProject_EverySubjectDeclaredByTheNamedProject_PassesClean()
    {
        Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Namespace("Core.*").MustResideInProject("Core"))
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustResideInProject_MultiplyDeclaredSubject_IsSatisfiedByAnyDeclarerAndRedByANonDeclarer()
    {
        // The shared types are one node each, whose facts follow Core, the first declarer. Naming Tool — a
        // project that compiles its own copy — satisfies the verb all the same, because residence is the
        // declarer roster and not the single ProjectName the node carries.
        Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Namespace("Shared.*").MustResideInProject("Tool"))
            .ShouldHavePassedClean();

        // The negative half over a project that exists: Client references the declaration rather than
        // compiling it, so it is on no roster and both shared types red.
        Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Namespace("Shared.*")
                .MustResideInProject("Client"))
            .ShouldHaveFailedWithSubjects(["Shared.Widget", "Shared.WidgetPart"]);
    }

    [Fact]
    public void MustResideInProject_UnknownProjectName_RedsEverySubject()
    {
        // A misspelt project name is on no roster, so every subject fails. That is the documented behaviour,
        // not a gap: the name is never held against the codebase at build time (validation runs before any
        // workspace exists), so a loud red at check time is how the typo surfaces, and the stricter-than-written
        // warning is what names the name as the cause.
        RuleResult result = Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Types
            .MustResideInProject("Cor"));

        result.ShouldHaveFailedWithSubjects(
        [
            "Shared.Widget", "Shared.WidgetPart", "Core.CoreOnly", "Core.User", "Tool.Command",
            "Client.Consumer"
        ]);
        result.ShouldHaveWarnedStricterOnMembership("types in project `Cor`");
    }

    [Fact]
    public void MustResideInProject_UnknownProjectName_WarnsExactlyAsItsMustBelongToForm()
    {
        // The pair is MustBelongTo with one membership, so its warning is that form's in every slot a host reads.
        CheckWarning reside = Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Types
                .MustResideInProject("Cor"))
            .Warnings.ShouldHaveSingleItem();
        CheckWarning belong = Checker.Verdict(MultiplyDeclaredCodebase.Model, arch => arch.Types
                .MustBelongTo(arch.Project("Cor")))
            .Warnings.ShouldHaveSingleItem();

        $"{reside.Kind} {reside.Key}: {reside.Message} | {reside.Hint}"
            .ShouldBe($"{belong.Kind} {belong.Key}: {belong.Message} | {belong.Hint}");
    }

    [Fact]
    public void MustResideInProject_GrandfatheredSubjectPasses_NewMisplacedTypeStaysRed()
    {
        // Identity is the subject symbol ID (GRAMMAR §4.3), one entry per misplaced type. Tool.Command is
        // blessed; Client.Consumer is a distinct identity the same rule keeps red.
        BaselineIndex index = Checker.Baselines("layering/consolidate", BaselineEntry.ForSubject("T:Tool.Command"));

        RuleResult result = Checker.Run(MultiplyDeclaredCodebase.Model, index, arch =>
                arch.Rule("layering/consolidate")
                    .Migrate(
                        "Satellite projects still declare types of their own.",
                        arch.AnyOf(arch.Namespace("Tool.*"), arch.Namespace("Client.*"))
                            .MustResideInProject("Core"))
                    .Because("One project owns the product's types."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Client.Consumer"]);
        result.ShouldHaveGrandfathered(1);
    }
}
