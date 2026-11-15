using System.Reflection;
using Shouldly;
using SlideRule.Cli.SpecLoading;
using SlideRule.Roslyn;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <see cref="SpecTargetRuntime" />: the refusal of a spec built for a newer .NET than the running tool, decided
///     from the spec's assembly references alone.
/// </summary>
public sealed class SpecTargetRuntimeTests
{
    private static readonly Version Runtime10 = new(10, 0, 15);

    [Fact]
    public void Refusal_ANewerSystemRuntime_NamesBothVersionsAndTheRemedy()
    {
        AssemblyName[] references = [Reference("System.Runtime", 11), Reference("SlideRule", 0)];
        string specDllPath = Path.Combine("arch", "bin", "MyApp.Architecture.dll");

        UserErrorException? refusal = SpecTargetRuntime.Refusal(references, Runtime10, specDllPath);

        refusal.ShouldNotBeNull().Message.ShouldBe(
            "The spec assembly 'MyApp.Architecture' was built for .NET 11, and sliderule is running on .NET 10.0.15. "
            + "Install the .NET 11 runtime, since sliderule runs on the newest .NET runtime installed, or retarget "
            + "the spec project to a framework .NET 10 runs, such as net10.0, and rebuild it.");
    }

    [Theory]
    [InlineData("System.Runtime", 10)]
    [InlineData("System.Runtime", 8)]
    [InlineData("mscorlib", 4)]
    [InlineData("netstandard", 2)]
    [InlineData("System.Runtime.Extensions", 11)]
    public void Refusal_AReferenceTheRuntimeCanLoad_Passes(string name, int major)
    {
        AssemblyName[] references = [Reference(name, major)];

        UserErrorException? refusal = SpecTargetRuntime.Refusal(references, Runtime10, "Spec.dll");

        refusal.ShouldBeNull();
    }

    private static AssemblyName Reference(string name, int major)
    {
        return new AssemblyName(name) { Version = new Version(major, 0, 0, 0) };
    }
}
