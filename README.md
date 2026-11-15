# SlideRule

<!-- mcp-name: io.github.andypgray/sliderule -->

[![CI](https://github.com/andypgray/sliderule/actions/workflows/ci.yml/badge.svg)](https://github.com/andypgray/sliderule/actions/workflows/ci.yml) [![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/andypgray/sliderule?label=openssf+scorecard)](https://scorecard.dev/viewer/?uri=github.com/andypgray/sliderule) [![NuGet](https://img.shields.io/nuget/v/SlideRule.Cli?logo=nuget&label=nuget)](https://www.nuget.org/packages/SlideRule.Cli) [![NuGet downloads](https://img.shields.io/nuget/dt/SlideRule.Cli?label=downloads)](https://www.nuget.org/packages/SlideRule.Cli)

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents.

A build can succeed after a change crosses a boundary your team intended to preserve. Diagrams and instructions then depend on someone noticing and updating them. SlideRule compiles your architecture decisions into one model for enforcement and generated agent context.

1. **Enforcement.** One checker evaluates rules at the command line, in CI, inside the C# compiler and as individually named xUnit tests. A Claude Code hook checks when an agent's turn ends.
2. **Agent context.** `sliderule render` writes a managed `AGENTS.md` block and per-directory rule cards. `sliderule mcp` serves the rules through query tools.

SlideRule is pre-alpha. Minor versions can still change the spec API before 1.0. [Status](#status) describes the available features and versioning rules.

Install the tool, build your solution, and check it:

```bash
dotnet tool install -g SlideRule.Cli
dotnet build MyApp.sln
sliderule check MyApp.sln
```

`check` needs a spec project in the solution that references the `SlideRule` package (`SlideRule.dll`). Without one, it reports `No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.` and exits 2. Use `sliderule init` to create the csproj and empty spec class; [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) walks the setup. The class implements `IArchitectureSpec`; its `Define(Arch arch)` declares layers, rules and scopes. `sliderule graph` surveys the solution without a spec.

A rule is one statement:

```csharp
arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(application, infrastructure, api))
    .Because("The Domain holds the quote and rate model the rest of the subsystem is built on; it stays free of the layers that depend on it so it can be reasoned about and tested on its own.")
    .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");
```

That is the whole rule. Its ID is `layering/domain-independent`; `Enforce` is the posture and `domain.MustNotReference(application, infrastructure, api)` is the constraint. `Because` explains the reason; `Fix` describes the remedy. The four layers were declared earlier by namespace pattern, including `arch.Layer("Domain", "Meridian.Quoting.Domain.*")`. The [clean-architecture example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting) includes this rule; CI builds and checks that solution with both the repository CLI and the packed tool. [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) covers optional `Fix`, `Citation(uri)` and `Exemplar(selection)`, which names a type to copy.

## When an agent breaks a rule

This repository's `cli/no-stdout` rule forbids direct `Console.Out`, `Console.Write()` and `Console.WriteLine()` use in the Host layer, the CLI project. The MCP server uses stdout for JSON-RPC. Suppose an agent adds `Rendering/ProgressPrinter.cs` with writes on lines 10 and 15. That imagined change breaks only this rule. The Claude Code `Stop` hook runs `check` over the working tree; `SubagentStop` covers workers too. The configuration is in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks). On the first failed stop, the hook exits 2 and blocks the stop with this report on the agent's stderr:

```text
FAIL cli/no-stdout — The Host layer must not use `Console.Out`, `Console.Write()` or `Console.WriteLine()`.
  because: Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI output flows through System.CommandLine's console — so a direct Console write corrupts the wire and is invisible to the in-process tests.
  citation: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio
  fix: Write CLI output through the command's InvocationConfiguration console; route server diagnostics to the logger or Console.Error.
  subject: 271 types, 2 generated
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:10 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:15 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
```

Claude Code returns stderr to the agent so it can continue. The report starts with the rule ID and sentence, then the reason, optional citation, fix and exemplar, followed by the subject and offending lines. The `subject:` line appears only when the subject includes generated types. The agent writes through the command's `InvocationConfiguration` console, and the next stop passes.

The printer exists only in the demonstration. [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md) shows the same report and explains how excerpts are checked against their sources. It also shows the generated prose, code scanning and graph. The hook permits three consecutive continuations per prompt by default, configurable with `--max-rounds`; after that it reports the failure without blocking.

## The four postures

Every rule has one of the first two, and every scope one of the last two.

| Posture | What it is | What fails |
|---|---|---|
| `Enforce` | the law | every violation, even ones predating the rule |
| `Migrate` | a ratchet over a counted baseline | new violations; baselined sites stay quiet |
| `Quarantine` | containment for a scope | a new reference into the scope |
| `Caution` | dragons for code new callers may use | never fails; a change touching it warns |

Use `arch.Rule(id).Enforce(...)` for a rule that must pass now, or `.Migrate(from, to)` to introduce a target alongside existing violations. `sliderule baseline init` records the current violations of each ratcheted rule. New violations fail; baselined sites pass. As sites are fixed, `sliderule baseline accept-reductions` shrinks the counted baseline. When it is empty, `sliderule status` reports `promotable to Enforce (baseline is empty)`.

Declare a scope with `arch.Scope(id).Quarantine(selection)` or `.Caution(selection)`. `Dragons(prose)` or `DragonsDoc(path)` explains the fragile code and how to call it; `render` writes that guidance into its directory's card. Quarantine baselines references into the scope and fails new ones that bypass the surface named by `BoundaryOnlyVia`. Both scopes also warn when a changed file declares a type in the scope. Use `check --diff-base <ref>` for those tripwires; without diff context they are skipped. Tripwires never fail, and `Caution` has no failing containment rule.

[Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) shows all four postures in code. [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md) follows a ratchet from capture to promotion. The [Meridian example](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/README.md#three-ways-an-agent-goes-wrong-here) demonstrates three changes with check output:

- Copying the majority pattern. Six of Meridian's eight controllers open a `SqlConnection` and run inline SQL; Bookings and Quotes do not. `Migrate` identifies the old pattern in generated context: `That is grandfathered debt, not house style. New code must follow: ...` An existing pattern can therefore remain in the code while new instances fail.

- Bypassing a gateway. Meridian quarantines `Meridian.Clearance.*` behind `IClearanceGateway` and `ClearanceGateway`. A new direct reference to its public `ContainerNumberValidator` fails `clearance/engine/containment`, with the fix to use `IClearanceGateway`.

- Changing code that looks wrong. The clearance engine's ISO 6346 letter-value table skips every multiple of 11. Making it contiguous breaks real container numbers. The dragons on the module's directory card explain those gaps before an agent edits them.

## What one spec produces

Each target below reads the same model. A violation report gives the rule ID, generated sentence, `Because` and offending `file:line`, plus any citation, fix and exemplar.

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

An xUnit failure uses the CLI's rule block from the same renderer; a test requires their text to match. The managed block and `sliderule explain <rule-id>` provide the architecture documentation, including a rule's reason and fix. `render --model` writes `sliderule.model.json`; later renders refresh it whenever it exists. `render --diagram <path>` puts both diagrams in that file's managed block. In this repository, tests recompose the root block and every committed card and fail on drift. [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#the-prose-it-generates) shows the generated files.

The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#which-agents-use-what) page maps agents to the surfaces they read. If your repository has its own `CLAUDE.md`, add `@AGENTS.md` to import the managed block into Claude Code. `render` warns when it finds a Claude instruction file without that import.

## Compared with ArchUnitNET and NetArchTest

[NetArchTest](https://github.com/BenMorris/NetArchTest) and [ArchUnitNET](https://github.com/TNG/ArchUnitNET) run architecture rules through your test runner. SlideRule defines the rules in a separate spec project that supplies every target above.

| Tool | What you write | Where it runs |
|---|---|---|
| NetArchTest | fluent assertions in test methods | your test runner |
| ArchUnitNET | ArchUnit-style rules in test classes | your test runner |
| SlideRule | one spec in its own project | every target above |

[GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) records the differences from ArchUnit, ArchUnitNET and NetArchTest. Negation belongs in the constraint verb, as in `MustNotReference`, following ArchUnitNET; there is no `ShouldNot()`. ArchUnit's `FreezingArchRule` accepts current violations as a baseline. SlideRule uses `Migrate` with a counted baseline for that purpose. `Quarantine` applies the same ratchet to references into a scope.

Every rule and scope requires `Because`; omitting it produces `MissingBecause`. An invalid spec stops `check` before evaluation and reports all spec errors in one pass. The predicate escape hatch, `Must(predicate, description:)`, requires a description even to compile. Reasons reach agents through the directory cards, `explain`, MCP's `arch_explain` and failed-rule reports. The [Interchange example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange) has 12 rules, each citing a learn.microsoft.com page. Nine come from the `DotNetGuidance` pack, an ordinary class library of static methods; three are written in the spec. Each pack method supplies its rule's citation, and the spec chooses a `PackPosture`, such as `PackPosture.Enforce`. The pack is a worked example in this repository and is not published as a package.

## Starting on a codebase that already exists

Start by writing down the boundaries your team already intends to preserve. You can write the rules yourself or ask an agent to draft them through the `derive_spec` prompt in [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md). The recipe stops for you to accept, edit or drop each proposed rule individually. It leaves `baseline init`, `render` and the commit to you. [A reference architecture held to its own book](https://github.com/andypgray/sliderule/tree/main/examples/eShopOnWeb) shows an agent-drafted, human-curated spec on a public fork of Microsoft's reference application.

## What it handles

The survey reports which parts of a solution it could read. These cases describe how projects and references enter the model; see the [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) for release history.

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

Projects in unsupported languages remain visible in the check, status and graph documents, with reasons under `unsupportedProjects`; the run still answers. A solution filter's omitted projects appear under `uncheckedProjects`. `sliderule graph` identifies the framework supplying shared types' members and hierarchy, and lists names declared in multiple projects or shadowing package types. For each shared type, members and hierarchy come from the first framework that declares it; references include every framework. Apply `.Authored()` to a rule's subject to exclude generated types. CI scaffolds and checks the documented spec project with per-project package versions and with central package management.

The persisted cache has one directory per solution. After an edit, a cached check loads the projects needing extraction and the projects they reference. Its stdout, stderr and exit code match a `--no-cache` run. `hook` uses that cached check when one is due. A missing spec-resolution record, rejected filter or structural edit can require loading the whole solution. Changes to a project file or `Directory.Build.props` are examples; the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching) lists the fallback cases.

## .NET Framework

The tool runs on the newest .NET runtime installed, 10 or later. Your application and its spec can target .NET Framework.

A spec project can target `net48` at its default C# 7.3 language version and build on any OS, including Linux. It references the `netstandard2.0` `SlideRule` contract package. The CLI loads its built DLL into a collectible, isolated `AssemblyLoadContext` and builds the model on the runtime the tool runs on. A `typeof()` anchor on a product type works when that runtime can load the type and its dependencies. A type implementing `System.Web.IHttpHandler` cannot load there; the resulting `TypeLoadException` becomes a user error naming the unavailable type. Use a namespace pattern for such types. Patterns require no assembly load, even from a spec that cannot reference `System.Data`. `DerivedFrom` also accepts a base type's fully qualified name as a string when the spec cannot compile against it.

Non-SDK-style projects load through Roslyn's `net472` Framework build host. These include projects in the 2003 MSBuild XML namespace, with explicit `<Reference>` items and a `Properties\AssemblyInfo.cs` compile item. A check reports at `file:line` even from an unbuilt tree:

```text
FAIL data-access/no-inline-sql — Types in `Classic.*` must not reference types in `System.Data.*`.
  Classic.Billing/BillingCalculator.cs:10 — Classic.Billing.BillingCalculator references System.Data.SqlClient.SqlConnection
```

`check --binlog` replays a `.binlog` from a real build of the solution on the same machine, replacing the design-time build. The capture persists; later runs replay it automatically while it remains structurally valid. The log can come from .NET Framework `MSBuild.exe`, with stdout, stderr and exit code identical to a direct workspace check. Basic.CompilerLog replays it into an `AdhocWorkspace`; it opens no `MSBuildWorkspace` and starts no build host. It reads current sources from the disk paths recorded by the build.

Direct loading of non-SDK-style projects needs Windows and a Visual Studio or Build Tools MSBuild installation, located with `vswhere`. A log from Framework `MSBuild.exe` is also made and replayed on Windows, on its originating machine. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#framework-host-selection) explains host selection and the `SLIDERULE_VS_INSTALL_PATH` override.

## Examples

Six examples in [`examples/`](https://github.com/andypgray/sliderule/tree/main/examples) use Meridian, a fictional freight-forwarding company. Four are solutions that CI builds and checks for a passing verdict, then verifies with `render --check`, including their diagrams:

- [Enforce-only clean architecture](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting): nine `Enforce` rules over Domain, Application, Infrastructure and Api. CI also runs each rule as a named xUnit test.

- [Three of the four postures on one codebase](https://github.com/andypgray/sliderule/tree/main/examples/Meridian): `Enforce` rules that already hold, three `Migrate` rules and a quarantined clearance engine. Two migrations are written locally; one comes from the rule pack. The quarantine's entry points are `IClearanceGateway` and `ClearanceGateway`.

- [Module isolation as law](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Operations): rendered cards for all five modules, Demurrage, Dispatch, Host, Invoicing and Tracking. Demurrage is quarantined behind `IDemurrageCalculator` and `DemurrageCalculator`.

- [Microsoft guidance, enforced and cited](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange): a cookbook showing each rule's guidance sentence, spec excerpt and real violation.

Two walkthroughs use the Meridian monolith:

- [Day-one adoption on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md): derive the first spec, review its rules and capture the baseline, with command output.

- [The agent loop, closed by a hook](https://github.com/andypgray/sliderule/tree/main/examples/Meridian/hooks): a storyboard of a violation caught at turn end, with hook configuration.

The seventh example uses Microsoft's reference application:

- [A reference architecture held to its own book](https://github.com/andypgray/sliderule/tree/main/examples/eShopOnWeb): a public eShopOnWeb fork with an agent-drafted, human-curated spec derived from its e-book. `showcase.json` pins the fork's commit. CI fetches it, checks it and runs the page's commands against a fresh clone. Tests compare the page's output and excerpts with that checkout.

## Installing

Install `SlideRule.Cli` as a .NET global tool on a machine with a .NET 10 or later SDK. The [requirements](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) cover setup, including repositories whose `global.json` pins an SDK below 10.

The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md) describes every command's options and exit codes, including failed loads and filtered solutions.

The command is `sliderule`. Five lockstep-versioned packages make up a release. Reference `SlideRule` at the version `sliderule --version` prints; `init` writes that version into the scaffold:

| Package | What it is |
|---|---|
| [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) | The `sliderule` global tool: `init`, `check`, `render`, `explain`, `status`, `graph`, `baseline`, `hook`, and the MCP server (`sliderule mcp`). |
| [`SlideRule`](https://www.nuget.org/packages/SlideRule) | The spec contract, zero dependencies. It includes an analyzer that reports a spec's bad string literals as compiler warnings while you write it, and completion for namespace and type names. |
| [`SlideRule.Xunit`](https://www.nuget.org/packages/SlideRule.Xunit) | The xUnit adapter: every rule as an individually named test. |
| [`SlideRule.Analyzers`](https://www.nuget.org/packages/SlideRule.Analyzers) | The build analyzer: a broken rule as a squiggle in the editor and a compiler warning in `dotnet build`, read from the model file `render --model` writes. |
| [`SlideRule.Guidance.DotNet`](https://www.nuget.org/packages/SlideRule.Guidance.DotNet) | Microsoft's .NET guidance as compiler warnings, one ID per rule, with no spec to write. |

Guidance diagnostics start at `SR3001` and need no spec, model file or tool. [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#upgrading) explains how to upgrade the packages and tool together.

## Connecting an MCP client

For a stdio MCP server, launch the tool's `mcp` command with this `.mcp.json` configuration:

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

Registry-generated configurations pass `mcp` alone. Without a solution argument, the server reads `SLIDERULE_SOLUTION_PATH`, then walks upward from its working directory. It stops at the first directory with a `sliderule.json` or exactly one solution. If that finds none, pass a solution argument, set the environment variable at installation, or commit `sliderule.json` at the repository root. [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md) covers binding the server, its tools and the `derive_spec` prompt.

## Documentation

- [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md): create your first spec project and reach a green check.

- [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md): rule parts, postures, subjects and scopes.

- [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md): context files, MCP connections and the turn-end hook.

- [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md): CI, reducing baselines, analyzer rollout, upgrades and failed runs.

- [Command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md): command help, options, exit codes and JSON documents.

- [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md): the language reference.

- [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md): this repository's spec checked and rendered over its own code.

## Status

SlideRule's first release was 0.1.0 on 2026-10-09. All four postures evaluate. CI checks this repository through its own GitHub action and uploads the SARIF result to code scanning. The build analyzer runs at its last released version over this repository's projects, and as built from this repository's source over [the four example solutions](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#in-the-build). CI treats its diagnostics as errors. [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) specifies the language; [its vocabulary table](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) is generated from the shipped API.

The packages share one root `<Version>` under 0.x semantic versioning. Spec-API changes appear in the [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) and ship without a shim. The 0.2.0 selection-namespace move required rebuilding 0.1.x specs; an unrebuilt spec is rejected with a message naming the move. The [JSON documents](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions) have a top-level `schemaVersion` that moves when their shape does; the hook document's shape is Claude Code's and has none. Human-readable reports can gain lines in any release. Tests pin the public spec language in `SlideRule` and the `SlideRule.Xunit` test base, `ArchRuleTests<TSpec>`. These are the two packages a consumer compiles against. Both packages also validate against the last published release at pack time. The checking engine is internal.

## Building

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx
```

[CONTRIBUTING.md](https://github.com/andypgray/sliderule/blob/main/CONTRIBUTING.md) covers working on SlideRule itself.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
