using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SlideRule.Guidance.DotNet;

/// <summary>
///     The diagnostics the guidance analyzer reports: one per rule of the model it carries, each under its own ID
///     with the Microsoft page the rule rests on as its help link, and one for a fault in the analyzer itself.
/// </summary>
/// <remarks>
///     A rule's descriptor must agree with the rule in the embedded model, ID for ID and page for page; the
///     test that holds the two equal is what keeps a rule from reporting under a descriptor that cites another
///     page, or from having none and faulting the analyzer the first time it reports.
/// </remarks>
internal static class GuidanceDiagnostics
{
    private const string Category = "SlideRule.Guidance";

    private const string FaultHelpLink =
        "https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Guidance.DotNet/README.md#diagnostics";

    internal static readonly DiagnosticDescriptor ReuseHttpClient = Rule(
        "SR3001",
        "Do not construct HttpClient outside the composition root",
        "A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers.",
        "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");

    internal static readonly DiagnosticDescriptor NoServiceLocator = Rule(
        "SR3002",
        "Do not resolve services from IServiceProvider outside the composition root",
        "Resolving services from IServiceProvider at call sites hides a type's real dependencies; declare them as "
        + "constructor parameters.",
        "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines");

    internal static readonly DiagnosticDescriptor NoBuildServiceProvider = Rule(
        "SR3003",
        "Do not call BuildServiceProvider",
        "Calling BuildServiceProvider while configuring services builds a second container with its own "
        + "singletons, a duplicate-instance trap.",
        "https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines");

    internal static readonly DiagnosticDescriptor NoSyncOverAsync = Rule(
        "SR3004",
        "Do not block on a Task or ValueTask",
        "Blocking on a Task or ValueTask (.Result/.Wait/.GetResult) ties up a thread and can deadlock in a "
        + "captured context; await instead.",
        "https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios");

    internal static readonly DiagnosticDescriptor AsyncSuffix = Rule(
        "SR3005",
        "Name a Task-returning method with the Async suffix",
        "Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a "
        + "method must be awaited.",
        "https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap");

    internal static readonly DiagnosticDescriptor NoGeneralCatch = Rule(
        "SR3006",
        "Do not catch and swallow Exception outside a top-level handler",
        "Catching base Exception outside a top-level handler swallows the faults you meant to see; scope the "
        + "catch-all to that handler and let other code catch only the specific types it can handle.",
        "https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types");

    internal static readonly DiagnosticDescriptor AcceptCancellation = Rule(
        "SR3007",
        "Accept a CancellationToken in a Task-returning method",
        "Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the "
        + "calls it makes, so a Task- or ValueTask-returning method without one cannot take part in cooperative "
        + "cancellation.",
        "https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap",
        isEnabledByDefault: false);

    internal static readonly DiagnosticDescriptor NoMappingAttributes = Rule(
        "SR3008",
        "Do not put persistence mapping attributes on a type",
        "A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one "
        + "data-access technology, so the same model can no longer be stored another way or moved to a new "
        + "store; keep it ignorant of how it is persisted.",
        "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles");

    /// <summary>The analyzer itself failed, which is a fault in the analyzer and not a finding about code.</summary>
    internal static readonly DiagnosticDescriptor AnalyzerFault = new(
        "SR3093",
        "The SlideRule guidance analyzer failed",
        "The SlideRule guidance analyzer failed in this project, so some or all of its findings are missing: "
        + "{0}: {1}. This is a fault in the analyzer and says nothing about the code. Report it at "
        + "https://github.com/andypgray/sliderule/issues.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        "An internal fault stopped the analyzer in this project. It is reported as a warning of its own so that "
        + "a build treating warnings as errors can exclude it by ID.",
        FaultHelpLink,
        WellKnownDiagnosticTags.CompilationEnd);

    // In model order; declared after every descriptor field, because static initializers run in text order.
    private static readonly (string RuleId, DiagnosticDescriptor Descriptor)[] RuleTable =
    [
        ("http/reuse-httpclient", ReuseHttpClient),
        ("di/no-service-locator", NoServiceLocator),
        ("di/no-buildserviceprovider", NoBuildServiceProvider),
        ("async/no-sync-over-async", NoSyncOverAsync),
        ("naming/async-suffix", AsyncSuffix),
        ("exceptions/no-general-catch", NoGeneralCatch),
        ("async/accept-cancellation", AcceptCancellation),
        ("persistence/no-mapping-attributes", NoMappingAttributes)
    ];

    /// <summary>Each rule's descriptor, by the rule's ID in the embedded model.</summary>
    internal static IReadOnlyDictionary<string, DiagnosticDescriptor> ByRule { get; } =
        RuleTable.ToDictionary(entry => entry.RuleId, entry => entry.Descriptor, StringComparer.Ordinal);

    /// <summary>Every descriptor this analyzer can report, which is what it declares it supports.</summary>
    internal static ImmutableArray<DiagnosticDescriptor> All { get; } =
        RuleTable.Select(entry => entry.Descriptor).ToImmutableArray().Add(AnalyzerFault);

    // The message is the finding's own, which carries the rule's ID, its sentence, its reason and its fix, as the
    // build analyzer's does. The reason is the description, and the page it rests on is the help link.
    private static DiagnosticDescriptor Rule(
        string id, string title, string because, string citation, bool isEnabledByDefault = true)
    {
        return new DiagnosticDescriptor(
            id, title, "{0}", Category, DiagnosticSeverity.Warning, isEnabledByDefault, because, citation);
    }
}
