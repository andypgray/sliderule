# SlideRule.Xunit

`SlideRule.Xunit` is the [SlideRule](https://github.com/andypgray/sliderule) xUnit
adapter: every rule in an architecture spec runs as an individually named xUnit test. The rule
ID is the test's display name, and a failing rule's message is the exact human block the
`sliderule` CLI prints.

## Usage

Derive a class from `ArchRuleTests<TSpec>` in your test project and point it at the solution
to check:

```csharp
using SlideRule.Xunit;

public sealed class ArchitectureTests : ArchRuleTests<MyApp.Architecture.ArchitectureSpec>
{
    protected override string SolutionPath => FindSolutionUp("MyApp.sln");
}
```

`SolutionPath` is the only required override. `FindSolutionUp` climbs from the test output
directory to the first ancestor holding the named file and returns its absolute path. On a miss
it throws `FileNotFoundException` naming the start directory. Resolve the path however suits your
repo; relative values resolve against the test process's working directory, not the output
directory `FindSolutionUp` starts from. The test explorer lists one case per rule ID. The
workspace load, extraction, and check run once per spec type, and every rule case reads its
verdict from that shared run. A scope's tripwire rule, `Quarantine` or `Caution`, reports as
skipped (a test run has no diff context). Everything else passes or fails like any other test.

A spec project the checked solution declares is excluded from the checked universe
automatically (mirroring the CLI), along with any project only it pulls in. Those are libraries
the spec references that the solution file does not declare, such as a shared rule pack. Projects
the solution declares stay in the universe even when the spec references them, so a spec may
reference the very code it governs. If your spec lives outside the target solution, override
`ExcludeProjectName` to return `null`.

## When the workspace does not load completely

A project that fails to load would otherwise vanish from the checked universe. Every rule would
be measured over a codebase missing whole projects, and nothing in a green verdict would say what
was missing. The adapter refuses to let that read as green. One named test,
`Workspace_LoadedCompletely`, fails carrying the load diagnostics and the MSBuild selection that
produced them. Every rule case skips, because its verdict was never reached. Restore and build
the target solution, then rerun.

To check whatever did load anyway, opt in:

```csharp
public sealed class ArchitectureTests : ArchRuleTests<MyApp.Architecture.ArchitectureSpec>
{
    protected override string SolutionPath => FindSolutionUp("MyApp.sln");
    protected override bool AllowWorkspaceDiagnostics => true;
}
```

Rule verdicts then come from the partial model. `Workspace_LoadedCompletely` reports as skipped,
still carrying the diagnostics, rather than pass under a name the run cannot vouch for.

## When a solution filter narrows the run

A `.slnf` solution path checks the projects the filter selects plus everything they reference,
which can be fewer than the solution declares. Rule cases still report, since a narrowed universe
is a smaller true answer and every verdict reached is real. The completeness claim cannot pass.
`Workspace_LoadedCompletely` reports as skipped and names the declared projects the run never
checked, because a pass would let a subset speak for the whole solution. Point `SolutionPath` at
the solution the filter references to get the whole answer. A filter whose selection pulls in every
declared project narrows nothing, and the run is indistinguishable from one over the solution.

## Requirements

- **xunit.v3 4.0.1 or later.** The adapter is built against the xunit.v3 authoring libraries at
  that version, and the metapackage pins them exactly, so a test project on an older one restores
  a mix of the two versions and warns NU1608. Your test project keeps its own `xunit.v3`
  metapackage reference; the adapter brings only the authoring pair.
- **Microsoft.Testing.Platform, not VSTest.** xunit.v3 4.0.0 ships that platform, and the .NET
  SDK declines to drive it through the VSTest target. A test project on this adapter therefore
  names the runner in `global.json` and carries no `Microsoft.NET.Test.Sdk` or
  `xunit.runner.visualstudio`:

  ```json
  { "test": { "runner": "Microsoft.Testing.Platform" } }
  ```
- **A .NET SDK on the test host.** The checker loads the target solution through
  MSBuildWorkspace (via MSBuildLocator), so plain runtime-only environments cannot run these
  tests.
- The target solution must be restored and built before the tests run. The checker never
  builds; stale builds give stale verdicts.

## Writing the spec

The spec itself is authored against the
[`SlideRule`](https://www.nuget.org/packages/SlideRule) contract package; see
its README for the fluent surface. The
[`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) global tool
runs the same rules at the command line, in CI, and as an MCP server for coding agents. The
adapter and the CLI produce identical failure text by construction. All four SlideRule
packages ship one version in lockstep; reference the adapter and the contract at the same one. At two
versions the adapter checks nothing: `Workspace_LoadedCompletely` fails naming both, and every rule
case skips.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
