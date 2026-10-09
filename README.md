# SlideRule

<!-- mcp-name: io.github.andypgray/sliderule -->

[![CI](https://github.com/andypgray/sliderule/actions/workflows/ci.yml/badge.svg)](https://github.com/andypgray/sliderule/actions/workflows/ci.yml) [![OpenSSF Scorecard](https://img.shields.io/ossf-scorecard/github.com/andypgray/sliderule?label=openssf+scorecard)](https://scorecard.dev/viewer/?uri=github.com/andypgray/sliderule) [![NuGet](https://img.shields.io/nuget/v/SlideRule.Cli?logo=nuget&label=nuget)](https://www.nuget.org/packages/SlideRule.Cli) [![NuGet downloads](https://img.shields.io/nuget/dt/SlideRule.Cli?label=downloads)](https://www.nuget.org/packages/SlideRule.Cli)

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents.

A long-lived codebase has layers, boundaries and rules. They live in a few heads, no build step checks them, and the diagrams drift. Nothing fails when a change crosses a boundary. A coding agent makes that change quickly and plausibly, and it cannot see which walls are structural. SlideRule's answer is architecture-as-code. The rules become one C# spec, and the spec produces every surface this page names.

1. **Enforcement.** One checker passes or fails the rules at the command line, in CI and as named xUnit tests. An agent hook runs the same check when the agent's turn ends.
2. **Agent context.** The rules render to a managed `AGENTS.md` block, per-directory rule cards and MCP query tools for coding agents.

Write your architecture once. Use it everywhere.

SlideRule is pre-alpha. Minor versions can still change the spec API before 1.0. [Status](#status) holds the current inventory.

Install the tool, build your solution, and check it:

```bash
dotnet tool install -g SlideRule.Cli
dotnet build MyApp.sln
sliderule check MyApp.sln
```

`check` reads the rules from a spec project in your solution, and [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) makes one with the `SlideRule` package.

A rule is one statement:

```csharp
arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(application, infrastructure, api))
    .Because("The Domain holds the quote and rate model the rest of the subsystem is built on; it stays free of the layers that depend on it so it can be reasoned about and tested on its own.")
    .Fix("Move the dependency out of Domain: define an interface here and implement it in the outer layer that needs it.");
```

That is the whole rule. It has an ID, a posture, a constraint, a reason and a fix. Here the posture is `Enforce`. The rule is committed in the [clean-architecture example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting), and CI holds `check` green against the codebase it governs.

## When an agent breaks a rule

Rules like the one above govern this repository too. One of them keeps the CLI off stdout, because the MCP server speaks JSON-RPC over that channel. Suppose an agent adds a progress printer to the CLI and reaches for `Console.WriteLine`. When it tries to hand the work back, the `Stop` hook in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) runs `check` over the working tree. The rule goes red, and the hook refuses the stop with this report on the agent's stderr:

```text
FAIL cli/no-stdout — The Host layer must not use `Console.Out`, `Console.Write()` or `Console.WriteLine()`.
  because: Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI output flows through System.CommandLine's console — so a direct Console write corrupts the wire and is invisible to the in-process tests.
  citation: https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio
  fix: Write CLI output through the command's InvocationConfiguration console; route server diagnostics to the logger or Console.Error.
  subject: 191 types, 1 generated
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:10 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
  src/SlideRule.Cli/Rendering/ProgressPrinter.cs:15 — SlideRule.Cli.Rendering.ProgressPrinter uses System.Console.WriteLine()
```

Every line of that report is something the agent can act on without asking a human. The agent routes the output through the command's console instead, and the next stop is clean.

[SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md) shows this repository's own spec on the other surfaces too. It covers the committed rule and the prose it renders, the xUnit run, code scanning and the graph. It also says how every excerpt on both pages is held to its source.

## The four postures

Every rule carries one of the first two, and every scope one of the last two.

| Posture | What it is | What fails |
|---|---|---|
| `Enforce` | the law | every violation, even ones predating the rule |
| `Migrate` | a ratchet over a counted baseline | new violations; baselined sites stay quiet |
| `Quarantine` | containment for a scope | a new reference into the scope |
| `Caution` | dragons for code new callers are welcome to | nothing; a change set touching it draws a warning |

`Enforce` fails violations that predate the rule, and `Migrate` exists for that case. `sliderule baseline` records a rule's current violations, new ones fail from the next commit, and the baseline only shrinks. At zero, the tool suggests promoting the rule to `Enforce`. A rule renamed after capture keeps its entries through `sliderule baseline rename --from <old> --to <new>`. `status` lists any baseline section filed under an ID the spec no longer declares. Every scope carries a diff-aware tripwire. With `check --diff-base <ref>`, a change set that touches the scope draws a warning. For a `Caution` scope that tripwire is the whole posture. The dragons prose lands on the scope's directory as a card, and `explain` and `arch_context` serve it. No reference into a `Caution` scope is ever a violation.

The three postures after `Enforce` each answer a way a coding agent goes wrong on an established codebase. `Enforce` is the law the code already keeps, and a change that crosses it fails wherever the check runs. The [Meridian example](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/README.md#three-ways-an-agent-goes-wrong-here) walks three of those ways on real output.

- An agent copies the majority pattern, the statistical prior. Six of Meridian's eight controllers open a `SqlConnection`, so inline SQL reads as house style and an agent writes its next controller the same way. `Migrate` answers it. The rendered context calls that pattern debt, in words the agent reads before it writes.
- An agent tidies away a gateway, the helpful refactor. A public type inside a module invites a direct call that skips the gateway in front of it. A `Quarantine` scope makes the gateway the only sanctioned way in.
- An agent corrects structural weirdness. A check-digit table that skips values looks like a bug, and making it contiguous breaks every real container number. The dragons prose on a `Quarantine` or `Caution` scope answers it. The card on that scope's directory says what the code does, which part is structural, and how to call in.

## One spec produces

Each target below consumes the same reified model. Every violation report carries the rule ID, the generated rule sentence, the reason, the fix, and the exact `file:line`.

| Target | What it is |
|---|---|
| [`sliderule check`](https://github.com/andypgray/sliderule/blob/main/CLI.md#check) | one pass-or-fail verdict for the command line and CI |
| [`check --sarif`](https://github.com/andypgray/sliderule/blob/main/CLI.md#sarif) | that verdict as SARIF 2.1.0, for code scanning |
| xUnit adapter | every rule an individually named test |
| build analyzer | a broken rule as a squiggle in the editor and a compiler warning in the build |
| [`sliderule render`](https://github.com/andypgray/sliderule/blob/main/CLI.md#render) | the managed `AGENTS.md` block, per-directory rule cards, and the model as a JSON file |
| [`sliderule mcp`](https://github.com/andypgray/sliderule/blob/main/CLI.md#mcp) | `arch_check`, `arch_status`, `arch_explain`, `arch_context`, and `arch_graph`, plus a `derive_spec` prompt |
| [`sliderule hook`](https://github.com/andypgray/sliderule/blob/main/CLI.md#hook) | `check` when an agent's turn ends; a red rule refuses the stop, report on stderr |

The adapter's failure text is byte-identical to the CLI's. The two share one renderer, and a product test pins them equal. The managed block plus `sliderule explain` are also the generated architecture documentation, written for agents first and readable by people. See [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#the-prose-it-generates) for the gate that keeps it current.

The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files) page says which agents read the `AGENTS.md` files, and which line a repository with its own `CLAUDE.md` adds for Claude Code.

The compiler is the source of truth for your code. SlideRule is the source of truth for your architecture.

## Where this sits next to ArchUnitNET and NetArchTest

[NetArchTest](https://github.com/BenMorris/NetArchTest) and [ArchUnitNET](https://github.com/TNG/ArchUnitNET) run architecture rules inside your unit tests, and they are good at it. SlideRule moves the rules out of test code into one spec and renders every surface above from it.

| Tool | What you write | Where it runs |
|---|---|---|
| NetArchTest | fluent assertions in test methods | your test runner |
| ArchUnitNET | ArchUnit-style rules in test classes | your test runner |
| SlideRule | one spec in its own project | every target above |

The grammar comes from surveying that prior art, and [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) records each divergence. Constraints negate in the verb (`MustNotReference`), following ArchUnitNET. ArchUnit's `FreezingArchRule` accepts a rule's current violations as a baseline, and here that is `Migrate` with its counted baseline. `Quarantine` contains a scope; it does not accept the scope's violations.

`Because` is mandatory. A rule without one is an invalid spec. `check` refuses to run it and reports every spec error in one pass. Even the predicate escape hatch, `Must(condition, description:)`, does not compile without its description. Every reason ships to your agents in the rendered context. In the [Interchange example](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange), each of the twelve rules carries a `Citation` naming the learn.microsoft.com page its reason rests on. Nine of those twelve come from a shared rule pack, an ordinary class library of static methods. The pack owns the citation, and the spec picks the posture. That pack ships in this repository as a working example. A pack is a pattern you own, so there is no registry to depend on.

## Starting on a codebase that already exists

SlideRule is built for long-lived, business-critical .NET codebases, where the architecture is real but written down nowhere. You can write the first rules yourself or have an agent draft them through the `derive_spec` prompt, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) shows. Every rule the agent proposes crosses a curation gate where you accept, edit, or drop it.

## What it handles

Real solutions are rarely one shape. Each row is one such shape and what the checker does with it. The [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) records the release each landed in.

| Shape | What the checker does |
|---|---|
| Linked source files | reads a shared file's references once per compiling project |
| Multi-targeting projects | one project; its shared types take one framework's members and hierarchy and every framework's references |
| One type name in several projects, or in a package too | follows the compiler's binding for each reference |
| Generated code | counts it per project and per rule subject |
| Polyglot solutions | names each F#, VB or SQL project it cannot read |
| A repository on central package management | the spec project scaffold builds under `Directory.Packages.props` |
| Non-SDK-style .NET Framework projects | loads them through the Framework build host; see [.NET Framework](#net-framework) |
| A binlog from a real build | `check --binlog` replays it; see [.NET Framework](#net-framework) |
| Solution filters | stamps the projects a filtered run never checked; see the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#wrong-and-smaller-models) |

A project in a language the checker does not read makes the model smaller. Every document names it, and the run still answers. The survey, `sliderule graph`, names the framework a multi-targeting project's shared types follow. It also lists every type name that two projects, or a project and a package, both declare. `.Authored()` on a rule's subject keeps the rule off generated code.

## .NET Framework

The tool runs on .NET 10. The codebase it checks does not have to, and neither does the spec that governs it.

A spec project can target `net48` and compile at that framework's default language level, C# 7.3. It references the same netstandard2.0 `SlideRule` package every other spec does, and the CLI loads the built DLL in an isolated load context. A `typeof()` anchor works from there while the anchored type's own closure stays inside netstandard2.0. Past that line, including .NET Framework types with no counterpart on .NET, a namespace pattern is the anchor. It needs no assembly load at all.

Old project files load too. A non-SDK-style Framework project is the kind in the 2003 MSBuild XML namespace, with explicit `<Reference>` items and a hand-maintained `AssemblyInfo.cs`. It loads through the .NET Framework build host Roslyn ships and reports at `file:line` like anything else:

```text
FAIL data-access/no-inline-sql — Types in `Classic.*` must not reference types in `System.Data.*`.
  Classic.Billing/BillingCalculator.cs:10 — Classic.Billing.BillingCalculator references System.Data.SqlClient.SqlConnection
```

And the build server can stay where it is. `check --binlog` replays a binary log from a real build, including one produced by .NET Framework `MSBuild.exe`. The machine that builds therefore needs no .NET 10; only the machine that analyses does. Replaying that log and opening the workspace directly produce byte-identical output.

Loading a non-SDK-style project and replaying a log from .NET Framework `MSBuild.exe` both need Windows, because that is where the Framework build host and `MSBuild.exe` come from. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#framework-host-selection) says which Visual Studio or Build Tools install the tool takes, and how to choose one. A net48 spec project carries no such requirement and builds anywhere.

## Examples

Six worked examples in [`examples/`](https://github.com/andypgray/sliderule/tree/main/examples) share one fictional freight-forwarding company. Four are solutions. CI builds each one, holds `check` green against the committed tree, and runs `render --check` to prove every file a render writes under `examples/` current. The other two walk a flow with captured output. Three are whole codebases:

- [Enforce-only clean architecture](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting): the greenfield quoting subsystem. Nine rules hold a four-layer clean architecture, and every rule runs as a named xUnit test.

- [Three of the four postures on one codebase](https://github.com/andypgray/sliderule/tree/main/examples/Meridian): a mid-migration monolith. It has the law, three ratchets and their burndown, and one quarantined scope.

- [Module isolation as law](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Operations): a modular monolith. Every module directory carries its own rendered rule card, and one module is quarantined behind its facade.

Three go deeper on one surface each:

- [Microsoft guidance, enforced and cited](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Interchange): the cookbook page. It shows the canon sentence, spec excerpt, and real violation, rule by rule.

- [Day-one adoption on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md): the full derive flow, every step a real command with real output.

- [The agent loop, closed by a hook](https://github.com/andypgray/sliderule/tree/main/examples/Meridian/hooks): the storyboard for the loop above, walked beat by beat with captured output. It also holds the paste-in hook config.

## Installing

The CLI is a .NET global tool, and the machine that runs it needs a .NET 10 SDK. The [requirements](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) in Getting started cover the rest, including an install inside a repository whose `global.json` pins an SDK below 10.

The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md) covers each command's options and exit codes, and what a run does when a project fails to load or a solution filter narrows it.

The command is `sliderule`. Four lockstep-versioned packages make up a release, and a spec project references `SlideRule` at the version `sliderule --version` prints:

| Package | What it is |
|---|---|
| [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) | The `sliderule` global tool: `check`, `render`, `explain`, `status`, `graph`, `baseline`, and the MCP server (`sliderule mcp`). |
| [`SlideRule`](https://www.nuget.org/packages/SlideRule) | The spec contract, zero dependencies. It carries an analyzer that reports a spec's bad string literals as compiler warnings while you write it. |
| [`SlideRule.Xunit`](https://www.nuget.org/packages/SlideRule.Xunit) | The xUnit adapter: every rule as an individually named test. |
| [`SlideRule.Analyzers`](https://www.nuget.org/packages/SlideRule.Analyzers) | The build analyzer: a broken rule as a squiggle in the editor and a compiler warning in `dotnet build`, read from the model file `render --model` writes. |

## Connecting an MCP client

An MCP client launches the same tool with the `mcp` verb; the server speaks stdio. The `.mcp.json` shape:

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

A config generated from the MCP registry passes no solution, so bind one at install time wherever the server cannot find it by walking up. The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md) page covers binding, the tools and the `derive_spec` prompt.

## Building

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx
```

## Status

The reified model, the fluent builder, Roslyn extraction and the CLI verbs exist, and all four postures evaluate. The SARIF writer, the xUnit adapter, the build analyzer, the MCP server and the render pipeline exist too. The [SlideRule on itself](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md) page shows these parts running over this repository's own code, and CI uploads that `check --sarif` run to code scanning. The spec contract carries its own analyzer, which checks a spec's string literals. It also carries a completion provider, which offers your projects' namespace and type names as you type. The fluent surface can still move; [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) is its spec, and [its vocabulary table](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) is generated from the shipped API.

The four packages version in lockstep under 0.x semver. Every spec-API change is listed under Breaking in the [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) and ships without a shim, and a patch version makes none. The [JSON documents](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions) carry a `schemaVersion` that moves when their shape does, and the human-readable reports can gain lines in any release. The public surface of the two packages a consumer compiles against is pinned by tests, and it is the spec language alone: the engine behind it is internal. It is also compared with the previous release at pack time, so nothing there moves by accident.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
