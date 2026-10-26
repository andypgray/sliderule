# SlideRule

<!-- mcp-name: io.github.andypgray/sliderule -->

[![CI](https://github.com/andypgray/sliderule/actions/workflows/ci.yml/badge.svg)](https://github.com/andypgray/sliderule/actions/workflows/ci.yml) [![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/andypgray/sliderule?label=openssf+scorecard)](https://scorecard.dev/viewer/?uri=github.com/andypgray/sliderule) [![NuGet](https://img.shields.io/nuget/v/SlideRule.Cli?logo=nuget&label=nuget)](https://www.nuget.org/packages/SlideRule.Cli) [![NuGet downloads](https://img.shields.io/nuget/dt/SlideRule.Cli?label=downloads)](https://www.nuget.org/packages/SlideRule.Cli)

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents.

A long-lived codebase has layers, boundaries and rules. They live in a few heads, no build step checks them, and the diagrams drift. A change can cross a boundary and the build still passes. A coding agent makes that change quickly and plausibly, and it cannot see which walls are structural. SlideRule's answer is architecture-as-code. The rules become one C# spec, and the spec produces every surface this page names.

1. **Enforcement.** One checker passes or fails the rules at the command line, in CI, in the build and as named xUnit tests. A Claude Code hook runs the same check when an agent's turn ends.
2. **Agent context.** The rules render to a managed `AGENTS.md` block, per-directory rule cards and MCP query tools for coding agents.

SlideRule is pre-alpha. Minor versions can still change the spec API before 1.0. [Status](#status) describes what exists today and the versioning rules.

Install the tool, build your solution, and check it:

```bash
dotnet tool install -g SlideRule.Cli
dotnet build MyApp.sln
sliderule check MyApp.sln
```

`check` reads the rules from a spec project, a C# class library in your solution. On a solution with no spec project yet, `check` stops and reports that it found none. The [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) page makes one with the `SlideRule` package, and `sliderule graph` surveys a solution before it has any rules.

A rule is one statement:

```csharp
arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(application, infrastructure, api))
    .Because("The Domain holds the quote and rate model the rest of the subsystem is built on; it stays free of the layers that depend on it so it can be reasoned about and tested on its own.")
    .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");
```

That is the whole rule. It has an ID, a posture, a constraint, a reason and a fix. Here the posture is `Enforce`, and `domain` and the three layers it must not reference are declared earlier in the spec by namespace pattern. The rule is committed in the [clean-architecture example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting), and CI keeps `check` green against the codebase it governs. A rule can also cite the page its reason rests on and name a type to copy, as [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) shows.

## When an agent breaks a rule

Rules like the one above govern this repository too. One of them keeps the CLI off stdout, because the MCP server speaks JSON-RPC over that channel. Suppose an agent adds a progress printer to the CLI and reaches for `Console.WriteLine`. When it tries to end its turn, the Claude Code `Stop` hook in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) runs `check` over the working tree. The rule goes red, and the hook blocks the stop with this report on the agent's stderr:

```text
FAIL cli/no-stdout — The Host layer must not use `Console.Out`, `Console.Write()` or `Console.WriteLine()`.
  because: Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI output flows through System.CommandLine's console — so a direct Console write corrupts the wire and is invisible to the in-process tests.
  citation: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio
  fix: Write CLI output through the command's InvocationConfiguration console; route server diagnostics to the logger or Console.Error.
  subject: 195 types, 1 generated
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:10 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:15 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
```

The report gives the rule, its reason, its citation, the fix and each offending line. The `subject:` line appears only when a rule's subject includes generated code, here 1 of its 195 types. The agent routes the output through the command's console instead, and the next stop is clean.

The [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md) page shows this repository's own spec on the other surfaces too, from the prose it renders to code scanning and the graph. It also explains how every excerpt on both pages is held to its source.

## The four postures

Every rule has one of the first two, and every scope one of the last two.

| Posture | What it is | What fails |
|---|---|---|
| `Enforce` | the law | every violation, even ones predating the rule |
| `Migrate` | a ratchet over a counted baseline | new violations; baselined sites stay quiet |
| `Quarantine` | containment for a scope | a new reference into the scope |
| `Caution` | dragons for code new callers may use | never fails; a change touching it warns |

`Migrate` is for a rule the code does not keep yet. `sliderule baseline init` records the rule's current violations, and any new violation fails from then on. As sites are fixed, `sliderule baseline accept-reductions` shrinks the baseline, and once it is empty, `status` suggests promoting the rule to `Enforce`.

A scope marks code that needs care. Its dragons are notes on why the code is fragile and how to call into it. `render` puts them on a card in the scope's directory. `Quarantine` also records the references into the scope on a baseline, and fails any new one except through the surface `BoundaryOnlyVia` names. Both kinds of scope warn when a change touches them, given `check --diff-base <ref>`. The [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) page shows each posture in code, and [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md) follows a ratchet from capture to promotion.

The three postures after `Enforce` each answer a way a coding agent goes wrong on an established codebase. `Enforce` is the law the code already keeps, and a change that crosses it fails wherever the check runs. The [Meridian example](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/README.md#three-ways-an-agent-goes-wrong-here) walks three of those ways on real output.

- An agent copies the majority pattern, the statistical prior. Six of Meridian's eight controllers open a `SqlConnection`, so inline SQL reads as house style and an agent writes its next controller the same way. `Migrate` answers it. The rendered context calls that pattern debt, in words the agent reads before it writes.
- An agent tidies away a gateway, the helpful refactor. A public type inside a module invites a direct call that skips the gateway in front of it. A `Quarantine` scope makes the gateway the only sanctioned way in.
- An agent corrects structural weirdness. A check-digit table that skips values looks like a bug, and making it contiguous breaks every real container number. The dragons on a `Quarantine` or `Caution` scope answer it. The card on that scope's directory explains what the code does, which part is structural, and how to call in.

## What one spec produces

Each target below reads the same model. Every violation report gives the rule ID, the generated rule sentence, the reason and the exact `file:line`. A rule's fix, citation and exemplar appear there too when it has them.

| Target | What it is |
|---|---|
| [`sliderule check`](https://github.com/andypgray/sliderule/blob/main/CLI.md#check) | one pass-or-fail verdict for the command line and CI |
| [`check --sarif`](https://github.com/andypgray/sliderule/blob/main/CLI.md#sarif) | that verdict as SARIF 2.1.0, for code scanning |
| [xUnit adapter](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Xunit/README.md) | every rule an individually named test |
| [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) | a broken rule as a squiggle in the editor and a compiler warning in the build |
| [`sliderule render`](https://github.com/andypgray/sliderule/blob/main/CLI.md#render) | the managed `AGENTS.md` block, per-directory rule cards, and the model as a JSON file |
| [`render --diagram`](https://github.com/andypgray/sliderule/blob/main/CLI.md#render) | two Mermaid diagrams: the projects and their references, and the spec's law |
| [`sliderule mcp`](https://github.com/andypgray/sliderule/blob/main/CLI.md#mcp) | `arch_check`, `arch_status`, `arch_explain`, `arch_context`, and `arch_graph`, plus a `derive_spec` prompt |
| [`sliderule hook`](https://github.com/andypgray/sliderule/blob/main/CLI.md#hook) | `check` when a Claude Code agent's turn ends; a red rule blocks the stop |

The adapter's failure text is byte-identical to the CLI's. The two share one renderer, and a product test pins them equal. The managed block plus `sliderule explain` are also the generated architecture documentation, written for agents first and readable by people. The [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#the-prose-it-generates) page shows the gate that keeps it current.

The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#which-agents-use-what) page explains which agents read the `AGENTS.md` files and use each surface. It also gives the line a repository with its own `CLAUDE.md` adds for Claude Code.

## Compared with ArchUnitNET and NetArchTest

[NetArchTest](https://github.com/BenMorris/NetArchTest) and [ArchUnitNET](https://github.com/TNG/ArchUnitNET) run architecture rules inside your unit tests, and they are good at it. SlideRule moves the rules out of test code into one spec and renders every surface above from it.

| Tool | What you write | Where it runs |
|---|---|---|
| NetArchTest | fluent assertions in test methods | your test runner |
| ArchUnitNET | ArchUnit-style rules in test classes | your test runner |
| SlideRule | one spec in its own project | every target above |

The grammar comes from surveying that prior art, and [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) records each divergence. Constraints negate in the verb (`MustNotReference`), following ArchUnitNET. ArchUnit's `FreezingArchRule` accepts a rule's current violations as a baseline, and here that is `Migrate` with its counted baseline. `Quarantine` applies the same ratchet to a region of code, counting the references into it today and failing any new one that bypasses its sanctioned surface.

`Because` is mandatory. A rule without one is an invalid spec, and `check` stops before it evaluates anything, reporting every spec error in one pass. Even the predicate escape hatch, `Must(condition, description:)`, does not compile without its description. Every reason ships to your agents in the rendered context. In the [Interchange example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange), each of the 12 rules has a `Citation` naming the learn.microsoft.com page its reason rests on. Of those 12, 9 come from a shared rule pack, an ordinary class library of static methods. The pack owns the citation, and the spec picks the posture. That pack is a worked example in this repository rather than a published package. A pack of your own is a class library in your repository, with no registry to depend on.

## Starting on a codebase that already exists

SlideRule is built for long-lived, business-critical .NET codebases, where the architecture is real but lives in a few people's heads. You can write the first rules yourself or have an agent draft them through the `derive_spec` prompt, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) shows. You review every rule the agent proposes, and accept, edit or drop it before anything is committed.

## What it handles

Real solutions are rarely one shape. Each row is one case and what the checker does with it. The [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) records the release each landed in.

| Case | What the checker does |
|---|---|
| Linked source files | reads a shared file's references once per compiling project |
| Multi-targeting projects | one project, read with one framework's members and hierarchy and every framework's references |
| One type name in several projects, or in a package too | follows the compiler's binding for each reference |
| Generated code | counts it per project and per rule subject |
| Polyglot solutions | names each F#, VB or SQL project it cannot read |
| A repository on central package management | the [`derive_spec` scaffold](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md#the-spec-project) builds under `Directory.Packages.props` |
| Non-SDK-style .NET Framework projects | loads them through the Framework build host; see [.NET Framework](#net-framework) |
| A binlog from a real build | `check --binlog` replays it; see [.NET Framework](#net-framework) |
| Solution filters | lists the projects a filtered run never checked; see the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#wrong-and-smaller-models) |
| A second run over the same solution | reuses a [persisted cache](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching) and re-extracts only the changed projects, and their dependents when a declaration changed |
| An edit confined to method bodies | re-extracts the edited project alone |

A project in a language the checker does not read makes the model smaller. The check, status and graph documents list it with the reason, and the run still answers. The survey, `sliderule graph`, names the framework a multi-targeting project's shared types follow. It also lists every type name that two projects, or a project and a package, both declare. `.Authored()` on a rule's subject keeps the rule off generated code.

After an edit, a check loads only the projects it re-extracts and the projects they reference. That is the check `hook` runs when an agent's turn ends, and its output is byte for byte what a cold run prints. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching) lists the cases where such a check loads the whole solution instead.

## .NET Framework

The tool runs on .NET 10. The codebase it checks does not have to, and neither does the spec that governs it.

A spec project can target `net48` and compile at that framework's default language level, C# 7.3. It references the same netstandard2.0 `SlideRule` package every other spec does, and the CLI loads the built DLL in an isolated load context. A `typeof()` reference to a product type works while that type and everything it depends on stay inside netstandard2.0. Anything past that, such as a .NET Framework type with no .NET counterpart, is named by a namespace pattern. A filter such as `DerivedFrom` also takes the type's full name as a string. Neither needs an assembly load.

Old project files load too. A non-SDK-style Framework project is the kind in the 2003 MSBuild XML namespace, with explicit `<Reference>` items and a hand-maintained `AssemblyInfo.cs`. It loads through the .NET Framework build host Roslyn ships and reports at `file:line` like anything else:

```text
FAIL data-access/no-inline-sql — Types in `Classic.*` must not reference types in `System.Data.*`.
  Classic.Billing/BillingCalculator.cs:10 — Classic.Billing.BillingCalculator references System.Data.SqlClient.SqlConnection
```

`check --binlog` replays a binary log from a real build of the solution on the same machine, in place of its own design-time build. The log can come from .NET Framework `MSBuild.exe`. Replaying the log and opening the workspace directly produce byte-identical output.

Loading a non-SDK-style project and replaying a log from .NET Framework `MSBuild.exe` both need Windows, because that is where the Framework build host and `MSBuild.exe` come from. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#framework-host-selection) explains which Visual Studio or Build Tools install the tool takes, and how to choose one. A net48 spec project has no such requirement and builds anywhere.

## Examples

Six worked examples in [`examples/`](https://github.com/andypgray/sliderule/tree/main/examples) share one fictional freight-forwarding company. Four are solutions. CI builds each one, keeps `check` green against the committed tree, and runs `render --check` to prove every file a render writes under `examples/` current:

- [Enforce-only clean architecture](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting): the greenfield quoting subsystem. Nine rules govern a four-layer clean architecture, and every rule runs as a named xUnit test.

- [Three of the four postures on one codebase](https://github.com/andypgray/sliderule/tree/main/examples/Meridian): a mid-migration monolith. It has the law, three ratchets and their burndown, and one quarantined scope.

- [Module isolation as law](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Operations): a modular monolith. Every module directory has its own rendered rule card, and one module is quarantined behind its facade.

- [Microsoft guidance, enforced and cited](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange): the cookbook page. It shows the canon sentence, spec excerpt, and real violation, rule by rule.

The other two walk a flow on the Meridian monolith, with captured output:

- [Day-one adoption on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md): the full derive flow, every step a real command with real output.

- [The agent loop, closed by a hook](https://github.com/andypgray/sliderule/tree/main/examples/Meridian/hooks): the storyboard for the loop above, walked beat by beat. It also has the paste-in hook config.

## Installing

The CLI is a .NET global tool, and the machine that runs it needs a .NET 10 SDK. The [requirements](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) in Getting started cover the rest, including an install inside a repository whose `global.json` pins an SDK below 10.

The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md) covers each command's options and exit codes, and what a run does when a project fails to load or a solution filter narrows it.

The command is `sliderule`. Four lockstep-versioned packages make up a release, and a spec project references `SlideRule` at the version `sliderule --version` prints:

| Package | What it is |
|---|---|
| [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) | The `sliderule` global tool: `check`, `render`, `explain`, `status`, `graph`, `baseline`, `hook`, and the MCP server (`sliderule mcp`). |
| [`SlideRule`](https://www.nuget.org/packages/SlideRule) | The spec contract, zero dependencies. It includes an analyzer that reports a spec's bad string literals as compiler warnings while you write it, and completion for namespace and type names. |
| [`SlideRule.Xunit`](https://www.nuget.org/packages/SlideRule.Xunit) | The xUnit adapter: every rule as an individually named test. |
| [`SlideRule.Analyzers`](https://www.nuget.org/packages/SlideRule.Analyzers) | The build analyzer: a broken rule as a squiggle in the editor and a compiler warning in `dotnet build`, read from the model file `render --model` writes. |

[Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#upgrading) explains how to keep the tool and the packages on one version as releases come out.

## Connecting an MCP client

An MCP client launches the same tool with the `mcp` command; the server speaks stdio. The `.mcp.json` shape:

```json
{
  "mcpServers": {
    "sliderule": {
      "command": "sliderule",
      "args": ["mcp", "MyApp.sln"]
    }
  }
}
```

A config generated from the MCP registry passes no solution. The server then finds one as every command does, from a committed `sliderule.json` or by walking up from its working directory. Where it cannot, bind one at install time. The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md) page covers binding, the tools and the `derive_spec` prompt.

## Documentation

- [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md): the first run, from no spec to a green check.

- [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md): the parts of a rule, the postures and scopes, and what a rule covers.

- [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md): the context files, the MCP server and the hook.

- [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md): CI, ratchets, the build analyzer, upgrades and failed runs.

- [Command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md): every command, option, exit code and document.

- [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md): the language reference.

- [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md): this repository's own spec, checked and rendered over its own code.

## Status

SlideRule 0.1.0 is the first release. Every surface on this page exists today, and all four postures evaluate. [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md) shows these surfaces running over this repository's own code, and CI uploads that `check --sarif` run to code scanning. The build analyzer is the exception, [proven on the four example solutions](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#in-the-build) instead. The fluent surface can still move; [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) is its spec, and [its vocabulary table](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) is generated from the shipped API.

The packages version in lockstep under 0.x semver. Every spec-API change is listed under Breaking in the [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) and ships without a shim, and a patch version makes none. The [JSON documents](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions) have a `schemaVersion` that moves when their shape does, and the human-readable reports can gain lines in any release. Tests pin the public surface of `SlideRule` and `SlideRule.Xunit`, the two packages a consumer compiles against. That surface is the spec language and the adapter's test base class, and the engine behind them is internal. The surface is also compared with the previous release at pack time, so a change there cannot slip in by accident.

## Building

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx
```

[CONTRIBUTING.md](https://github.com/andypgray/sliderule/blob/main/CONTRIBUTING.md) covers the rest of working on SlideRule itself.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
