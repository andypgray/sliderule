using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using SlideRule.Analyzers;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     What a fault at each guarded point of the build analyzer reaches the developer as — one
///     <c>SR2093</c> for the compilation, never the compiler's <c>AD0001</c> and never silence — and what a
///     cancellation there does instead.
/// </summary>
/// <remarks>
///     <para>
///         The points are the ones a copy of SlideRule that will not bind breaks first: the start of
///         the compilation, the model and baseline reads, and the two actions that report a refused file. Each
///         seam here runs inside the production guard, the two read seams over reads of the armed analyzer's
///         own, so the fault takes the real path through the real driver.
///     </para>
///     <para>
///         Every fault fact claims one warning and nothing else, which is also what keeps an escaped
///         exception visible: the harness arms the driver with no exception callback, so an exception past a
///         guard is filed as <c>AD0001</c> in the same set, and the last fact here shows it arriving there.
///     </para>
/// </remarks>
public sealed class BuildTierGuardTests
{
    private const string FaultMessage =
        "The SlideRule analyzer failed in this project, so some or all of its findings are missing: "
        + "InvalidOperationException: boom. This is a fault in the analyzer and says nothing about the code, "
        + "and 'sliderule check' is unaffected. Report it at https://github.com/andypgray/sliderule/issues.";

    /// <summary>A point where the analyzer holds a fault of its own rather than let it out.</summary>
    public enum GuardedPoint
    {
        /// <summary>The start of the compilation, past the version check.</summary>
        Start,

        /// <summary>The model file's read, inside its guard.</summary>
        ModelRead,

        /// <summary>A baseline file's read, inside its guard.</summary>
        BaselineRead,

        /// <summary>The action that reports a model file that would not read.</summary>
        ModelRefusal,

        /// <summary>The action that reports a baseline file that would not read.</summary>
        BaselineRefusal
    }

    /// <summary>Every guarded point on both tiers, which end the compilation in different actions.</summary>
    public static TheoryData<GuardedPoint, string> PointsOnBothTiers =>
        new()
        {
            { GuardedPoint.Start, "live" },
            { GuardedPoint.Start, "build" },
            { GuardedPoint.ModelRead, "live" },
            { GuardedPoint.ModelRead, "build" },
            { GuardedPoint.BaselineRead, "live" },
            { GuardedPoint.BaselineRead, "build" },
            { GuardedPoint.ModelRefusal, "live" },
            { GuardedPoint.ModelRefusal, "build" },
            { GuardedPoint.BaselineRefusal, "live" },
            { GuardedPoint.BaselineRefusal, "build" }
        };

    /// <summary>Every guarded point once.</summary>
    public static TheoryData<GuardedPoint> Points =>
        new()
        {
            GuardedPoint.Start,
            GuardedPoint.ModelRead,
            GuardedPoint.BaselineRead,
            GuardedPoint.ModelRefusal,
            GuardedPoint.BaselineRefusal
        };

    [Theory]
    [MemberData(nameof(Points))]
    public async Task EachArrangement_ReachesItsPointAndReportsSomethingElseUnarmed(GuardedPoint point)
    {
        // The difference guard for the facts below: unarmed, each arrangement reports what its point exists
        // to report, so a fault there that became one SR2093 replaced a real answer rather than silence.
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            new BuildTierAnalyzer(), BuildTier.OwnCompilation(), FilesReaching(point), cancellationToken: Ct);

        reported.ShouldNotReport("SR2093");
        reported.ShouldNotBeEmpty(DiagnosticAssertions.Describe(reported, $"{point} unarmed should report"));
    }

    [Theory]
    [MemberData(nameof(PointsOnBothTiers))]
    public async Task AFaultAtAGuardedPoint_IsOneLB2093CarryingIt(GuardedPoint point, string tier)
    {
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            ArmedAt(point, Boom), BuildTier.OwnCompilation(), FilesReaching(point), tier, Ct);

        reported.ShouldBeTheOnly("SR2093")
            .ShouldSay(FaultMessage);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("build")]
    public async Task FaultsAtTwoPoints_AreStillOneLB2093(string tier)
    {
        var analyzer = new BuildTierAnalyzer(
            beforeCheck: Boom,
            beforeBaselineRefusal: () => throw new InvalidOperationException("the other fault"));

        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            analyzer, BuildTier.OwnCompilation(), BuildTier.MigratedWith(BuildTier.BaselineHoldingTheStore()), tier, Ct);

        reported.ShouldBeTheOnly("SR2093");
    }

    [Fact]
    public async Task TheTierOff_WithEverySeamArmed_ReportsNothingAndReachesNoSeam()
    {
        var reached = new List<string>();
        BuildTierAnalyzer analyzer = Trapped(reached);

        foreach (IReadOnlyList<AdditionalText> files in EveryArrangement())
        {
            IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
                analyzer, BuildTier.OwnCompilation(), files, "off", Ct);

            reported.ShouldBeSilent();
        }

        reached.ShouldBeEmpty("tier off should have touched nothing, the version check included");

        // The traps are live: the same analyzer on the live tier springs the first of them, the version check.
        IReadOnlyList<Diagnostic> live = await BuildTier.DiagnoseAsync(
            analyzer, BuildTier.OwnCompilation(), FilesReaching(GuardedPoint.Start), "live", Ct);
        live.ShouldBeTheOnly("SR2093");
        reached.ShouldBe(["versions"]);
    }

    [Theory]
    [MemberData(nameof(Points))]
    public async Task ACancellationAtAGuardedPoint_EndsTheRunByThrowing(GuardedPoint point)
    {
        using var cancellation = new CancellationTokenSource();

        void Cancel()
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }

        await Should.ThrowAsync<OperationCanceledException>(() => BuildTier.DiagnoseAsync(
            ArmedAt(point, Cancel), BuildTier.OwnCompilation(), FilesReaching(point),
            cancellationToken: cancellation.Token));
    }

    [Theory]
    [MemberData(nameof(Points))]
    public async Task ACancellationNobodyAskedFor_IsNeverHeldAsAFault(GuardedPoint point)
    {
        // The guard lets any cancellation through, asked for or not; what the driver then makes of one it
        // did not ask for is its own business, and is never this analyzer's fault warning.
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            ArmedAt(point, () => throw new OperationCanceledException("The analysis stopped.")),
            BuildTier.OwnCompilation(), FilesReaching(point), cancellationToken: Ct);

        reported.ShouldNotReport("SR2093", "a cancellation is not a fault and must pass through every guard");
    }

    [Fact]
    public async Task AnExceptionPastTheGuards_ArrivesInTheReportedSetAsAD0001()
    {
        // A cancellation nobody asked for is the one exception the guards let past, which makes it the way
        // to show where anything else that got past them would surface.
        IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
            ArmedAt(GuardedPoint.Start, () => throw new OperationCanceledException("The analysis stopped.")),
            BuildTier.OwnCompilation(), FilesReaching(GuardedPoint.Start), cancellationToken: Ct);

        reported.ShouldBeTheOnly("AD0001");
    }

    // ── the shared arrangements ───────────────────────────────────────────────────────────────────────────

    private static void Boom()
    {
        throw new InvalidOperationException("boom\nsecond line");
    }

    private static BuildTierAnalyzer ArmedAt(GuardedPoint point, Action seam)
    {
        return point switch
        {
            GuardedPoint.Start => new BuildTierAnalyzer(beforeStart: seam),
            GuardedPoint.ModelRead => new BuildTierAnalyzer(beforeModelRead: seam),
            GuardedPoint.BaselineRead => new BuildTierAnalyzer(beforeBaselineRead: seam),
            GuardedPoint.ModelRefusal => new BuildTierAnalyzer(beforeModelRefusal: seam),
            GuardedPoint.BaselineRefusal => new BuildTierAnalyzer(beforeBaselineRefusal: seam),
            _ => throw new ArgumentOutOfRangeException(nameof(point), point, null)
        };
    }

    // An analyzer whose every seam records that it was reached and then throws.
    private static BuildTierAnalyzer Trapped(List<string> reached)
    {
        Action Trap(string point)
        {
            return () =>
            {
                lock (reached)
                {
                    reached.Add(point);
                }

                throw new InvalidOperationException(point);
            };
        }

        return new BuildTierAnalyzer(
            beforeCheck: Trap("check"),
            beforeStart: Trap("start"),
            beforeModelRead: Trap("model read"),
            beforeBaselineRead: Trap("baseline read"),
            beforeModelRefusal: Trap("model refusal"),
            beforeBaselineRefusal: Trap("baseline refusal"),
            versions: () =>
            {
                Trap("versions")();
                return (null, null);
            });
    }

    // The files under which the analyzer reaches the point: a model it can read for the start and the model
    // read, one it cannot for the model refusal, and a ratcheted rule with a baseline for the other two.
    private static IReadOnlyList<AdditionalText> FilesReaching(GuardedPoint point)
    {
        return point switch
        {
            GuardedPoint.ModelRefusal => [BuildTier.ModelFile("[]")],
            GuardedPoint.BaselineRead => BuildTier.MigratedWith(BuildTier.BaselineHoldingTheStore()),
            GuardedPoint.BaselineRefusal => BuildTier.MigratedWith("{ this is not json"),
            _ => [BuildTier.ModelFile(BuildTier.ModelText(BuildTier.EnforceRule))]
        };
    }

    private static IEnumerable<IReadOnlyList<AdditionalText>> EveryArrangement()
    {
        return
        [
            FilesReaching(GuardedPoint.Start),
            FilesReaching(GuardedPoint.ModelRefusal),
            FilesReaching(GuardedPoint.BaselineRefusal),
            []
        ];
    }
}
