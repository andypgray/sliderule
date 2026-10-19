using System.Reflection;
using System.Runtime.Loader;
using Shouldly;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using SlideRule.Xunit;
using Xunit;
using Xunit.Sdk;

namespace SlideRule.Tests.Xunit;

/// <summary>
///     The adapter's version check, driven over a real skewed pair: the adapter beside a copy of
///     <c>SlideRule</c> at another version, entered through the three members xUnit calls.
/// </summary>
/// <remarks>
///     The pair is built in a collectible load context of its own. The stand-in contract comes from
///     <see cref="SkewedContract" />. The adapter and the two assemblies it carries are the real build, loaded
///     from their bytes so that their references to the contract bind to the stand-in. A consumer assembly
///     holding a spec and an <c>ArchRuleTests</c> subclass is compiled in memory. Everything else, xUnit
///     included, comes from the default context, so the skip and failure the adapter raises are the runner's
///     own types. The JIT binds a method's references when it compiles the method, so these tests are the only
///     thing that sees an entry point whose body names the engine, which a skewed contract may not have.
/// </remarks>
public sealed class AdapterContractSkewTests
{
    private const string ContractAssemblyName = "SlideRule";

    private const string ProbeAssemblyName = "SlideRule.AdapterSkewProbe";

    private const string ProbeTestsTypeName = "AdapterSkewProbe.ProbeTests";

    // Never part of this assembly: a real ArchRuleTests subclass here would be discovered by the runner and
    // run against a solution that does not exist.
    private const string ProbeSource = """
                                       using SlideRule;
                                       using SlideRule.Xunit;

                                       namespace AdapterSkewProbe
                                       {
                                           public sealed class ProbeSpec : IArchitectureSpec
                                           {
                                               public void Define(Arch arch)
                                               {
                                               }
                                           }

                                           public sealed class ProbeTests : ArchRuleTests<ProbeSpec>
                                           {
                                               protected override string SolutionPath => "never-loaded.slnx";
                                           }
                                       }
                                       """;

    // Above the adapter's own reference, so the compiler unifies that reference upward to the stand-in.
    private static readonly Version NewerContract = new(2, 0, 0, 0);

    private static readonly Lazy<byte[]> ProbeImage = new(EmitProbe);

    // The context's adapter is the same bytes as this process's, so it carries the same informational version.
    private static readonly string ExpectedRefusal = ContractVersion.Mismatch(
        typeof(ArchRuleTests<>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
        NewerContract.ToString())!;

    [Fact]
    public void RuleRows_OnASkewedPair_ReturnsOneSkippedRowNamingBothVersions()
    {
        // Arrange
        var context = new SkewedPairContext();
        try
        {
            Type probeTests = ShouldLoadProbeTestsOverTheStandIn(context);
            MethodInfo ruleRows = probeTests.GetMethod(
                "RuleRows", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)!;

            // Act
            object? rows = ruleRows.Invoke(null, BindingFlags.DoNotWrapExceptions, null, null, null);

            // Assert
            ITheoryDataRow row = ((IEnumerable<ITheoryDataRow>)rows!).ToList()
                .ShouldHaveSingleItem();
            row.TestDisplayName.ShouldBe("ProbeSpec: not checked");
            row.Skip.ShouldBe(ExpectedRefusal);
            row.Skip!.ShouldContain("loaded version 2.0.0.0 of SlideRule");
        }
        finally
        {
            // Best-effort unload; nothing gates on GC timing.
            context.Unload();
        }
    }

    [Fact]
    public async Task RuleHolds_OnASkewedPair_SkipsWithBothVersions()
    {
        // Arrange
        var context = new SkewedPairContext();
        try
        {
            Type probeTests = ShouldLoadProbeTestsOverTheStandIn(context);
            object instance = Activator.CreateInstance(probeTests)!;
            var ruleHolds = probeTests.GetMethod("Rule_Holds")!
                .CreateDelegate<Func<string, Task>>(instance);

            // Act
            Exception? exception = await AdapterOutcome.CaughtAsync(() => ruleHolds(string.Empty));

            // Assert: a wrong exception is printed whole, since a load error is the likeliest one.
            // SkipException.ForSkip prefixes the reason with a marker, so the reason is the suffix.
            var skip = exception.ShouldBeOfType<SkipException>(exception?.ToString());
            skip.Message.ShouldEndWith(ExpectedRefusal);
            skip.Message.ShouldContain("loaded version 2.0.0.0 of SlideRule");
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public async Task WorkspaceLoadedCompletely_OnASkewedPair_FailsWithBothVersions()
    {
        // Arrange
        var context = new SkewedPairContext();
        try
        {
            Type probeTests = ShouldLoadProbeTestsOverTheStandIn(context);
            object instance = Activator.CreateInstance(probeTests)!;
            var workspaceLoadedCompletely = probeTests.GetMethod("Workspace_LoadedCompletely")!
                .CreateDelegate<Func<Task>>(instance);

            // Act
            Exception? exception = await AdapterOutcome.CaughtAsync(workspaceLoadedCompletely);

            // Assert
            var failure = exception.ShouldBeOfType<FailException>(exception?.ToString());
            failure.Message.ShouldBe(ExpectedRefusal);
            failure.Message.ShouldContain("loaded version 2.0.0.0 of SlideRule");
        }
        finally
        {
            context.Unload();
        }
    }

    // Held by attribute rather than by the probe above, which runs under the host's default JIT: a JIT that inlines
    // would compile a helper's references into its caller, ahead of the check.
    [Theory]
    [InlineData("ModelRows")]
    [InlineData("VerdictAsync")]
    public void EngineHelper_IsDeclaredNeverInlined(string name)
    {
        MethodInfo helper = typeof(ArchRuleTests<>).GetMethod(
            name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;

        helper.MethodImplementationFlags
            .HasFlag(MethodImplAttributes.NoInlining)
            .ShouldBeTrue();
    }

    // The difference guard, ahead of every act: the adapter the probe derives from must be the context's
    // copy, and the only contract in the context the stand-in. A probe that fell through to this process's
    // own adapter or contract would see agreeing versions and test nothing.
    private static Type ShouldLoadProbeTestsOverTheStandIn(SkewedPairContext context)
    {
        using var stream = new MemoryStream(ProbeImage.Value);
        Assembly probe = context.LoadFromStream(stream);
        Type probeTests = probe.GetType(ProbeTestsTypeName, true)!;

        Assembly adapter = probeTests.BaseType!.Assembly;
        AssemblyLoadContext.GetLoadContext(adapter)
            .ShouldBeSameAs(context);

        Assembly contract = context.Assemblies
            .Where(loaded => loaded.GetName()
                .Name == ContractAssemblyName)
            .ShouldHaveSingleItem();
        contract.GetName()
            .Version
            .ShouldBe(NewerContract);

        return probeTests;
    }

    private static byte[] EmitProbe()
    {
        return SpecAssemblyCompiler.EmitImage(
            ProbeSource,
            ProbeAssemblyName,
            [.. SpecAssemblyCompiler.PlatformReferences, SkewedContract.Reference(NewerContract)]);
    }

    // The consumer's process in miniature: the contract is the stand-in, the adapter and its two carried
    // assemblies are the real build read as bytes, and every other name falls through to the default
    // context.
    private sealed class SkewedPairContext() : AssemblyLoadContext("adapter-contract-skew", true)
    {
        private static readonly Dictionary<string, string> CarriedPaths = new[]
            {
                typeof(ArchRuleTests<>).Assembly,
                typeof(WorkspaceLoader).Assembly,
                typeof(FragmentExtractor).Assembly
            }
            .ToDictionary(assembly => assembly.GetName()
                .Name!, assembly => assembly.Location, StringComparer.Ordinal);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (name is null) return null;

            byte[]? image = ImageOf(name);
            if (image is null) return null;

            using var stream = new MemoryStream(image);
            return LoadFromStream(stream);
        }

        private static byte[]? ImageOf(string name)
        {
            if (name == ContractAssemblyName) return SkewedContract.Image(NewerContract);

            return CarriedPaths.TryGetValue(name, out string? path) ? File.ReadAllBytes(path) : null;
        }
    }
}
