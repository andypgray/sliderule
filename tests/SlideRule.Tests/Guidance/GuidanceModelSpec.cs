using SlideRule.Packs.DotNet;

namespace SlideRule.Tests.Guidance;

/// <summary>
///     The rules <c>SlideRule.Guidance.DotNet</c> carries, at the selections it ships with: the pack's rules a
///     single project's build can decide, at Enforce, over every type of whatever project takes the package.
///     The package's embedded model is this spec's model, and <see cref="GuidanceModelTests" /> holds the two
///     equal.
/// </summary>
/// <remarks>
///     <c>di/no-captive-dependencies</c> is not called: it reads registrations collected across a whole
///     solution, so no build of one project can decide it, and the package's README says so instead.
/// </remarks>
internal sealed class GuidanceModelSpec : IArchitectureSpec
{
    /// <summary>The name the embedded model carries as its spec.</summary>
    internal const string SpecName = "SlideRule.Guidance.DotNet";

    // The attributes that mark a method a test framework calls by convention, whose name the framework rather
    // than the async-suffix rule decides, and which no caller hands a cancellation token.
    private static readonly string[] TestMethodAttributes =
    [
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute",
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.SetUpAttribute",
        "NUnit.Framework.TearDownAttribute",
        "NUnit.Framework.OneTimeSetUpAttribute",
        "NUnit.Framework.OneTimeTearDownAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute"
    ];

    // The base types whose public methods are actions a client reaches by name, where the routing convention
    // rather than the async-suffix rule decides the name: ASP.NET Core trims the suffix from an action name.
    private static readonly string[] ActionBaseTypes =
    [
        "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Microsoft.AspNetCore.SignalR.Hub",
        "System.Web.Mvc.Controller",
        "System.Web.Http.ApiController"
    ];

    /// <inheritdoc />
    public void Define(Arch arch)
    {
        Selection code = arch.Types;
        Selection compositionRoot = arch.AnyOf(arch.Types.Named("Program"), arch.Types.WithSuffix("Startup"));
        Selection topLevelHandler = arch.AnyOf(
            compositionRoot,
            arch.Types.DerivedFrom("Microsoft.Extensions.Hosting.BackgroundService"));

        List<MemberSelection> testMethods = TestMethodAttributes
            .Select(attribute => (MemberSelection)arch.Types.Methods.AttributedWith(attribute))
            .ToList();
        List<MemberSelection> actions = ActionBaseTypes
            .Select(baseType => (MemberSelection)arch.Types.DerivedFrom(baseType).Methods.ThatArePublic())
            .ToList();
        List<MemberSelection> conventionallyNamed = [.. testMethods, .. actions];

        DotNetGuidance.ReuseHttpClient(arch, code, compositionRoot, PackPosture.Enforce);
        DotNetGuidance.NoServiceLocator(arch, code, compositionRoot, PackPosture.Enforce);
        DotNetGuidance.NoBuildServiceProvider(arch, code, PackPosture.Enforce);
        DotNetGuidance.NoSyncOverAsync(arch, code, PackPosture.Enforce);
        DotNetGuidance.AsyncSuffix(arch, code, PackPosture.Enforce, exempt: conventionallyNamed);
        DotNetGuidance.NoGeneralCatch(arch, code, topLevelHandler, PackPosture.Enforce);
        DotNetGuidance.AcceptCancellation(arch, code, PackPosture.Enforce, exempt: testMethods);
        DotNetGuidance.NoMappingAttributes(arch, code, PackPosture.Enforce);
    }
}
