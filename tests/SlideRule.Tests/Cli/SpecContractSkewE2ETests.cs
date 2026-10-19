using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Shouldly;
using SlideRule.Cli.SpecLoading;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The version-skew path end to end: a spec built against a contract identity this host does not
///     carry, driven through the real CLI. Two skewed identities — one older than the host's, one newer —
///     bind and explain fine, and a spec reaching for a contract member or type this host does not define
///     is refused with what is missing, both versions, and both remedies named.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SpecContractBindTests" /> pins both halves separately and neither meets the other:
///         its bind arms resolve a hand-built <see cref="AssemblyName" /> through
///         <see cref="SpecLoadContext" /> without ever loading a spec, and its message arms map a
///         <em>fabricated</em> <see cref="MissingMethodException" /> off an <see cref="Assembly" /> subclass
///         that overrides nothing but <see cref="Assembly.GetReferencedAssemblies" />. These drive a real
///         DLL through a real <see cref="System.Runtime.Loader.AssemblyLoadContext" /> to the failure the
///         runtime itself raises, and assert the exit code a user sees.
///     </para>
///     <para>
///         The four refusals are the four ways a spec reaches past its contract. The runtime raises a
///         different exception for each, at a different point of the load, and all four must reach the same
///         refusal.
///     </para>
///     <para>
///         Every arm takes the <c>explain</c> DLL fast path, which short-circuits ahead of the workspace, so
///         no solution is loaded and each call is sub-second.
///     </para>
/// </remarks>
public sealed class SpecContractSkewE2ETests
{
    private const string RuleId = "naming/services-suffixed";

    private const string Because = "A service's name is the first thing a reader has to go on.";

    internal const string SpecAssemblyName = "SlideRule.ContractSkewSpec";

    private const string AbsentMemberCall = "arch.FutureVerb(\"a verb only the newer contract declares\");";

    private const string AbsentTypeCall = "arch.Rule(\"future/absent-type\").Enforce(arch.Types.MustBeFuture());";

    private const string AbsentInterfaceImplementation = "public sealed class FutureShaped : IFutureShape { }";

    private const string EagerSpecDeclaration = """
                                                public sealed class EagerSpec : IArchitectureSpec
                                                {
                                                    public EagerSpec()
                                                    {
                                                        FutureConstraints.MustBeFuture(null);
                                                    }

                                                    public void Define(Arch arch)
                                                    {
                                                    }
                                                }
                                                """;

    internal static readonly Version NewerContract = new(2, 0, 0, 0);

    internal static Version HostContractVersion => typeof(IArchitectureSpec).Assembly.GetName()
        .Version!;

    [Theory]
    [InlineData("0.2.0.0")] // older than the host's pinned identity…
    [InlineData("2.0.0.0")] // …and newer, the direction a version-keyed bind fails at load with a missing-file error
    public async Task Explain_SpecBuiltAgainstASkewedContract_BindsAndExplainsTheRule(string version)
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("spec-contract-skew");
        string specDll = EmitSkewedSpec(temp, Version.Parse(version), "");
        ShouldReferenceContract(specDll, Version.Parse(version));

        // Act
        CliResult result = await CliRunner.InvokeAsync("explain", RuleId, "--spec", specDll);

        // Assert: the rule's own fields, so a green that rendered nothing cannot pass for a bind.
        result.ShouldSucceed($"{RuleId} (enforce)", $"  because: {Because}");
    }

    [Fact]
    public async Task Explain_SkewedSpecCallingAnAbsentMember_RefusesNamingTheMemberBothVersionsAndBothRemedies()
    {
        // Arrange and act: the same spec, with the one call the host's contract cannot satisfy switched in. A
        // spec that does not CALL the member emits no memberref for it, which is why one contract serves both
        // directions.
        CliResult result = await ExplainSpecBuiltAgainstNewerContractAsync(AbsentMemberCall);

        // Assert
        result.ShouldRefuseAsContractSkew("SlideRule.Arch.FutureVerb(System.String)");
    }

    [Fact]
    public async Task Explain_SkewedSpecCallingIntoAnAbsentType_RefusesNamingTheTypeBothVersionsAndBothRemedies()
    {
        // Arrange and act: a verb on a class the newer contract adds. The class itself is what the host lacks, so
        // the runtime stops at the typeref, a step ahead of any member lookup.
        CliResult result = await ExplainSpecBuiltAgainstNewerContractAsync(AbsentTypeCall);

        // Assert: and not the remedy for a type that exists only on .NET Framework, which the runtime raises
        // the same exception type for.
        result.ShouldRefuseAsContractSkew("SlideRule.FutureConstraints");
        result.Err.ShouldNotContain("does not exist on .NET");
    }

    [Fact]
    public async Task Explain_SkewedSpecImplementingAnAbsentInterface_RefusesNamingTheInterfaceBothVersionsAndBothRemedies()
    {
        // Arrange and act: a type of the spec's own implementing an interface the newer contract adds. It fails
        // while the spec's types are discovered, before Define() is ever called.
        CliResult result = await ExplainSpecBuiltAgainstNewerContractAsync("", AbsentInterfaceImplementation);

        // Assert: and not the remedy for a dependency the spec's build did not stage.
        result.ShouldRefuseAsContractSkew("SlideRule.IFutureShape");
        result.Err.ShouldNotContain("CopyLocalLockFileAssemblies");
    }

    [Fact]
    public async Task Explain_SkewedSpecWhoseConstructorCallsIntoAnAbsentType_RefusesNamingTheTypeBothVersionsAndBothRemedies()
    {
        // Arrange and act: a second spec class whose constructor calls into a class the newer contract adds. It
        // fails while the spec classes are created, before Define() is ever called, and arrives wrapped by the
        // reflection that created it.
        CliResult result = await ExplainSpecBuiltAgainstNewerContractAsync("", EagerSpecDeclaration);

        // Assert: and not the stack trace an unexpected failure prints.
        result.ShouldRefuseAsContractSkew("SlideRule.FutureConstraints");
        result.Err.ShouldNotContain("TargetInvocationException");
    }

    private static async Task<CliResult> ExplainSpecBuiltAgainstNewerContractAsync(
        string extraStatement, string extraDeclaration = "")
    {
        using TempDirectory temp = TestTempRoot.Fresh("spec-contract-skew");
        string specDll = EmitSkewedSpec(temp, NewerContract, extraStatement, extraDeclaration);
        return await CliRunner.InvokeAsync("explain", RuleId, "--spec", specDll);
    }

    private static string EmitSkewedSpec(
        TempDirectory temp, Version contractVersion, string extraStatement, string extraDeclaration = "")
    {
        string specDll = temp.PathOf(SpecAssemblyName + ".dll");
        string source = SpecSource(extraStatement, extraDeclaration);
        SpecAssemblyCompiler.EmitSpecDll(source, specDll, SpecAssemblyName, SkewedContract.Reference(contractVersion));
        return specDll;
    }

    private static string SpecSource(string extraStatement, string extraDeclaration)
    {
        return $$"""
                 using SlideRule;

                 namespace ContractSkew
                 {
                     {{extraDeclaration}}

                     public sealed class ContractSkewSpec : IArchitectureSpec
                     {
                         public void Define(Arch arch)
                         {
                             {{extraStatement}}
                             arch.Rule("{{RuleId}}")
                                 .Enforce(arch.Types.MustHaveSuffix("Service"))
                                 .Because("{{Because}}");
                         }
                     }
                 }
                 """;
    }

    /// <summary>
    ///     Asserts the emitted spec really carries the skewed contract identity. Without it the green arms
    ///     would pass just as happily on a spec that had been built against the host's own contract — which
    ///     is the one thing they exist to rule out.
    /// </summary>
    private static void ShouldReferenceContract(string specDllPath, Version expected)
    {
        using FileStream stream = File.OpenRead(specDllPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();

        List<Version> referenced = metadata.AssemblyReferences
            .Select(metadata.GetAssemblyReference)
            .Where(reference => metadata.GetString(reference.Name) == SpecLoadContext.ContractAssemblyName)
            .Select(reference => reference.Version)
            .ToList();

        referenced.ShouldBe(
            [expected],
            $"'{Path.GetFileName(specDllPath)}' must reference exactly one {SpecLoadContext.ContractAssemblyName}, "
            + "at the skewed identity it was built against.");
    }
}

file static class SpecContractSkewAssertions
{
    /// <summary>
    ///     Asserts the run was refused as a spec built against a newer contract: the frame, both versions,
    ///     <paramref name="missing" /> named, and both remedies.
    /// </summary>
    /// <remarks>
    ///     The frame is pinned, and the runtime's own wording around the name ("Method not found:", "Could not
    ///     load type") is quoted only as far as the name itself. The part the reader acts on is ours; the
    ///     sentence around it is not.
    /// </remarks>
    internal static void ShouldRefuseAsContractSkew(this CliResult result, string missing)
    {
        string specName = SpecContractSkewE2ETests.SpecAssemblyName;
        Version newer = SpecContractSkewE2ETests.NewerContract;
        Version host = SpecContractSkewE2ETests.HostContractVersion;

        result.ShouldRefuseWith(
            $"The spec assembly '{specName}' uses SlideRule API this tool's contract does not have",
            $"(the spec was built against contract {newer}, this tool carries {host})",
            missing,
            "dotnet tool update -g SlideRule.Cli",
            "reference the SlideRule package that matches this tool");
    }
}
