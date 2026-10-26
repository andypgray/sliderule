using System.Reflection;
using Shouldly;
using SlideRule.Cli.SpecLoading;
using SlideRule.Discovery;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Cli;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The end-to-end spec-loading proof: load the fixture DLL — built by a
///     separate csproj with no compile-time visibility here — in an isolated ALC, discover its spec
///     by reflection, which finds none unless type identity holds across the boundary, build the model,
///     and enumerate a known rule. The fixture's path is baked into this assembly's metadata by the build.
/// </summary>
public class SpecLoadingTests
{
    [Fact]
    public void LoadFixtureSpec_ViaAssemblyLoadContext_DiscoversBuildsAndEnumerates()
    {
        string fixturePath = CliRunner.SpecFixtureDll;
        using var context = new SpecLoadContext(fixturePath);
        Assembly fixture = context.LoadWithoutLocking(fixturePath);
        // Discovery finds no spec, and throws, unless type identity holds across the ALC boundary — the
        // shared-contract-to-Default delegation.
        IReadOnlyList<IArchitectureSpec> specs = SpecDiscovery.FindSpecs(fixture);

        ArchitectureModel model = ArchModelBuilder.Build(specs);
        ArchRule rule = model.Rule("fixture/interfaces");
        rule.Sentence.ShouldBe("Interfaces in `Fixture.*` must be named `I*`.");
    }
}
