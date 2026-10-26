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

`SolutionPath` is the only required override. `ExcludeProjectName` and `AllowWorkspaceDiagnostics`
are optional, and both are covered below. The adapter reads no `sliderule.json` and uses no
persisted cache, so these three overrides are its whole configuration in code.

`FindSolutionUp` climbs from the test output directory to the first ancestor that contains the
named file and returns its absolute path. If the climb reaches the root without a match,
`FindSolutionUp` throws `FileNotFoundException` naming the start directory. Any other way of
resolving the path works too. A relative path resolves against the test process's working
directory, which can differ from the output directory `FindSolutionUp` starts from.

The test explorer lists one case per rule ID. The workspace load, extraction, and check run once per
spec type, solution and excluded project, and every rule case reads its verdict from that shared run.
Two classes over one spec that point at different solutions, or exclude different projects, each
check their own. A scope's tripwire rule,
`Quarantine` or `Caution`, reports as skipped (a test run has no diff context). Everything else
passes or fails like any other test. A spec that fails to build is listed as one case instead,
`ArchitectureSpec: spec build failed` for the spec above. That case fails with the spec's own errors.

As in the CLI, a spec project the solution declares is excluded from the checked universe
automatically, along with any project only it pulls in. Those are libraries the spec references that
the solution file does not declare, such as a shared rule pack. Projects the solution declares stay
in the universe even when the spec references them, so a spec may reference the very code it
governs. If your spec lives outside the target solution, override `ExcludeProjectName` to return
`null`.

## When the workspace does not load completely

A project that fails to load would otherwise drop out of the checked universe. Every rule would be
measured over a codebase missing whole projects, and a green verdict would not name the missing
projects. So the adapter fails one named test, `Workspace_LoadedCompletely`, and its message lists
the load diagnostics and the MSBuild selection that produced them. Every rule case skips, because its
verdict was never reached. Restore and build the target solution, then rerun.

To check whatever did load anyway, opt in:

```csharp
public sealed class ArchitectureTests : ArchRuleTests<MyApp.Architecture.ArchitectureSpec>
{
    protected override string SolutionPath => FindSolutionUp("MyApp.sln");
    protected override bool AllowWorkspaceDiagnostics => true;
}
```

Rule verdicts then come from the partial model. `Workspace_LoadedCompletely` reports as skipped,
still listing the diagnostics, rather than pass under a name the run cannot vouch for.

## When the run checks fewer projects than the solution declares

A `.slnf` solution path checks the projects the filter selects plus everything they reference,
which can be fewer than the solution declares. Rule cases still report, since a narrowed universe
is a smaller true answer and every verdict reached is real. A rule whose subject matched no type in
the projects that loaded reports as skipped, with a reason that names the filter.
`Workspace_LoadedCompletely` reports as skipped and names the declared projects the run did not
check, because a pass would present a subset as the whole solution. Point `SolutionPath` at the
solution the filter references to get the whole answer. A filter whose selection pulls in every
declared project checks the whole solution, and the run is the same as one over the solution file.

`Workspace_LoadedCompletely` also skips on a solution declaring projects the checker cannot read,
such as an F#, VB, SQL or C++ project, and names those projects. Every rule case still reports, over
the projects the checker did read.

## Requirements

- xunit.v3 4.0.1 or later. The adapter is built against the xunit.v3 authoring libraries at that
  version, and the metapackage pins them exactly. A test project on an older metapackage restores a
  mix of the two versions and warns NU1608. Your test project keeps its own `xunit.v3` metapackage
  reference, and the adapter brings only the authoring pair.
- Microsoft.Testing.Platform, not VSTest. xunit.v3 4.0.0 ships that platform, and the .NET SDK will
  not run it through the VSTest target. A test project on this adapter therefore names the runner in
  `global.json` and references neither `Microsoft.NET.Test.Sdk` nor `xunit.runner.visualstudio`:

  ```json
  { "test": { "runner": "Microsoft.Testing.Platform" } }
  ```

  The setting applies to every `dotnet test` run under that `global.json`, so every other test
  project under it must run on Microsoft.Testing.Platform as well. Under this platform `dotnet test`
  exits 2 when a test fails, and 8 when a filter matches no test.
- A test project on `net10.0` or later, because the adapter targets `net10.0`.
- A .NET SDK on the test host. The checker loads the target solution through MSBuildWorkspace (via
  MSBuildLocator), so plain runtime-only environments cannot run these tests.
- The target solution must be restored before the tests run. The checker never builds it and reads
  its code from source, built or not; the spec is the one your test project's build compiled.

## Writing the spec

You write the spec against the [`SlideRule`](https://www.nuget.org/packages/SlideRule) contract
package, whose README covers the fluent surface. The
[`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) global tool runs the same rules at
the command line, in CI, and as an MCP server for coding agents. The adapter and the CLI produce
identical failure text by construction. All four SlideRule packages ship one version in lockstep;
reference the adapter and the contract at the same one. At two versions, `Workspace_LoadedCompletely`
fails and names both, and a single skipped case, `ArchitectureSpec: not checked` for the spec above,
replaces the rule cases.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
