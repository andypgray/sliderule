using Shouldly;
using SlideRule.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.ModelFile;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The parity pin over every bed this repository carries a spec for: what the build tier says inside
///     each project of a real solution, against what <c>check</c> says over the whole of it.
/// </summary>
/// <remarks>
///     <para>
///         Each row reads its bed's compilations once per class, through <see cref="ParityBeds" />, and its
///         extraction from <see cref="FixtureCodebase" />, both off the <see cref="WarmWorkspacePool" /> the
///         rest of the suite already pays for.
///     </para>
///     <para>
///         The unmeasured list is part of each row rather than derived, so a rule that stops being
///         measured — an operand that matched nothing after a fixture moved — cannot silently leave the
///         pin.
///     </para>
///     <para>
///         Every row holds both drivers: what each project reported asked whole, and then the union over
///         its documents asked one at a time, which has to be that same set with each key in the document
///         that holds its site.
///     </para>
/// </remarks>
public sealed class BeddedSpecParityTests(ParityBeds beds) : IClassFixture<ParityBeds>
{
    /// <summary>
    ///     The rules the whole-solution check leaves unmeasured on a bedded spec's bed, where it leaves any.
    /// </summary>
    private static readonly Dictionary<string, string[]> Unmeasured = new(StringComparer.Ordinal)
    {
        ["MyAppViolatedSpec"] = ["naming/nonexistent"]
    };

    /// <summary>
    ///     Every spec this repository carries against a bed, with the rules the whole-solution check leaves
    ///     unmeasured there.
    /// </summary>
    public static TheoryData<string, string, string[]> Specs =>
    [
        .. CliRunner.BeddedSpecs.Select(spec => (spec.Name, spec.Solution, Unmeasured.GetValueOrDefault(spec.Name, [])))
    ];

    [Theory]
    [MemberData(nameof(Specs))]
    public async Task TheBuildTier_OverEveryProjectOfABedItsSpecGoverns_AgreesWithTheWholeSolutionCheck(
        string name, string solutionPath, string[] unmeasured)
    {
        CliRunner.SkipUnlessLoadable(solutionPath);

        // Arrange
        ArchitectureModel model = ModelFileCorpus.ModelOf(name);
        ParitySubject subject = await beds.SubjectAsync(name, model, solutionPath);

        // Assert — the exclusion first, so a row that grew one reds naming the rule rather than the keys.
        subject.Expectation.Unmeasured.ShouldBe(
            unmeasured, $"{name}: rule(s) the whole-solution check never measured, which the pin holds out");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverTheBedItsViolatedSpecGoverns_ReportsSomething()
    {
        // The corpus's anti-vacuity half: every row above passes over a bed that reports nothing at all, so
        // one row states that the pin is comparing sets with something in them.
        const string name = "MyAppViolatedSpec";
        ArchitectureModel model = ModelFileCorpus.ModelOf(name);
        ParitySubject subject = await beds.SubjectAsync(name, model, CliRunner.MyAppSolution);

        subject.Expectation.Checked.ShouldNotBeEmpty("rules the pin holds the build to");
        subject.Expectation.Keys.ShouldNotBeEmpty("findings the whole-solution check gives");
        subject.Expectation.Uncaptured.ShouldNotBeEmpty("ratcheted rules with no captured baseline");
    }
}
