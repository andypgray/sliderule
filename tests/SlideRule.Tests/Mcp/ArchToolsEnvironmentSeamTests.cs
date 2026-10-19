using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Tools;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The tools read process variables through the server's one <c>IEnvironment</c>, never a fresh reading of the
///     real process: an unbound server rediscovers its solution on every call, and that discovery must see the
///     variable the server was given.
/// </summary>
public sealed class ArchToolsEnvironmentSeamTests : IDisposable
{
    // A working directory with no solution anywhere above it, so only the variable can name one.
    private readonly TempDirectory _empty = TestTempRoot.Fresh("arch-tools-env-seam");

    public void Dispose()
    {
        _empty.Dispose();
    }

    [Fact]
    public async Task ArchCheck_UnboundServer_DiscoversTheSolutionThroughTheInjectedEnvironment()
    {
        FakeEnvironment environment = new FakeEnvironment()
            .SetVariable(SlideRuleEnvVars.SolutionPath, CliRunner.MyAppSolution);

        string document = await Tools(environment).CheckAsync(cancellationToken: Ct);

        using JsonDocument report = JsonDocument.Parse(document);
        report.RootElement.GetProperty("rules").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ArchCheck_UnboundServer_WithoutTheVariable_RefusesDiscovery()
    {
        _empty.Path.ShouldHaveNoSolutionInAnyAncestor("would bind this server — clean it.");

        await Should.ThrowAsync<UserErrorException>(() => Tools(new FakeEnvironment()).CheckAsync(cancellationToken: Ct));
    }

    private ArchTools Tools(FakeEnvironment environment)
    {
        McpServerBinding unbound = new(null, CliRunner.CleanSpecDll, _empty.Path);
        return new ArchTools(unbound, WarmWorkspacePool.Source, environment, ResponseFitter.FirstRung);
    }
}
