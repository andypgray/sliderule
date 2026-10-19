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
///     by reflection, assert type identity across the boundary, build the model, and enumerate a
///     known rule. The fixture's path is baked into this assembly's metadata by the build.
/// </summary>
public class SpecLoadingTests
{
    [Fact]
    public void LoadFixtureSpec_ViaAssemblyLoadContext_DiscoversBuildsAndEnumerates()
    {
        string fixturePath = CliRunner.SpecFixtureDll;
        var context = new SpecLoadContext(fixturePath);
        try
        {
            Assembly fixture = context.LoadFromAssemblyPath(fixturePath);
            IReadOnlyList<IArchitectureSpec> specs = SpecDiscovery.FindSpecs(fixture);

            specs.ShouldNotBeEmpty();
            // Type identity across the ALC boundary — the shared-contract-to-Default delegation.
            specs[0]
                .ShouldBeAssignableTo<IArchitectureSpec>();

            ArchitectureModel model = ArchModelBuilder.Build(specs);
            ArchRule rule = model.Rule("fixture/interfaces");
            rule.Sentence.ShouldBe("Interfaces in `Fixture.*` must be named `I*`.");
        }
        finally
        {
            // Best-effort unload; nothing gates on GC timing.
            context.Unload();
        }
    }
}
