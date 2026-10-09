using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     What a fault inside the analyzer reaches the developer as, and what a cancellation does not.
/// </summary>
/// <remarks>
///     An analyzer that throws surfaces as the compiler's own <c>AD0001</c>, which carries no ID anybody
///     can exclude and so fails an estate that builds with warnings as errors. A warning of its own is the
///     answer, and once per compilation rather than once per call, because the fault that stops one call
///     into the contract usually stops every one of them. Cancellation is not a fault and has to keep
///     travelling: swallowing it would turn a cancelled build into a reported one.
/// </remarks>
public sealed class SpecLiteralFaultTests
{
    private static readonly string ThreeCallSites = SpecSnippet.Around(
        """arch.Namespace("MyApp.Web.*");""",
        """arch.Namespace("MyApp.Domain.*");""",
        """arch.Namespace("MyApp.Data.*");""");

    [Fact]
    public async Task AFaultAtEveryCallIntoTheContract_IsOneWarningForTheWholeCompilation()
    {
        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            ThreeCallSites,
            () => throw new InvalidOperationException(
                "The contract went missing.\nA second line no build surface would show."),
            Ct);

        // Which of the three call sites reports it is whichever callback the compiler ran first, so the
        // position is deliberately not pinned; that there is exactly one of them is the fact.
        Diagnostic fault = reported.ShouldBeTheOnly("SR1093");
        fault.ShouldSay(
            "The SlideRule spec analyzer failed at this call and may have missed literals in this "
            + "project: InvalidOperationException: The contract went missing. This is a fault in the "
            + "analyzer and says nothing about the spec, which is validated in full when it is loaded. "
            + "Report it at https://github.com/andypgray/sliderule/issues.");
        fault.ShouldPointAtNothing();
        fault.ShouldCarry();
    }

    [Fact]
    public async Task AFaultWhenTheBodyEnds_IsTheSameWarningPlacedNowhere()
    {
        string source = SpecSnippet.Around("""arch.Rule("area/one");""");
        var calls = 0;

        // The first call into the contract decides the one literal. The second is the end of the body,
        // where the several-literal checks run, and that is the guarded region this fault has to land in.
        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            source,
            () =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                    throw new InvalidOperationException("The contract went missing.");
            },
            Ct);

        // Nothing at the end of a body belongs to one call site, so the warning claims none of them.
        Diagnostic fault = reported.ShouldBeTheOnly("SR1093");
        fault.Location.ShouldBe(Location.None);
    }

    [Fact]
    public async Task ACancellationFromInsideTheGuardedRegion_IsNotReportedAsAFault()
    {
        string source = SpecSnippet.Around("""arch.Namespace("MyApp.Web.*");""");

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            source, () => throw new OperationCanceledException("The analysis stopped."), Ct);

        // The guard lets cancellation past, so what comes back is the compiler's own analyzer-failure
        // diagnostic rather than a SlideRule warning claiming to have read the spec.
        reported.ShouldReport("AD0001");
    }
}
