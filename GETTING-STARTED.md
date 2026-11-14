# Getting started

A SlideRule spec project is a C# class library in your solution that references `SlideRule` and states your architecture rules. Without one, `check` stops before evaluating rules and exits 2 with `No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.`

Choose who drafts your first rules. On path 1, you write them; on path 2, a coding agent drafts them for your review. Each path creates a spec project, so follow one. Without a spec selection, two projects produce `Multiple spec projects found; pass --spec to disambiguate:`. You can survey the solution first with `sliderule graph MyApp.sln`. It lists projects, declared and observed references, and namespaces without a spec. Add `--format mermaid` for one Mermaid fence showing projects and references, as `render --diagram` draws them.

## Requirements

Install a .NET 10 SDK on the machine running SlideRule. The tool targets `net10.0`; solution loads use `MSBuildWorkspace` with the SDK's MSBuild, which a runtime alone does not provide. This applies to solution-loading runs of `init`, `check`, `explain`, `render`, `status`, `graph`, `baseline`, `hook` and `mcp`. Your product code can target .NET Framework 4.8, including non-SDK-style projects. SlideRule reads its source without retargeting it. The spec compiles against the `SlideRule` package, which targets `netstandard2.0`.

SlideRule ships as a .NET global tool:

```bash
dotnet tool install -g SlideRule.Cli
```

The installed command is `sliderule`. Run the install outside a repository whose `global.json` selects an SDK below 10; that SDK cannot install the .NET 10 tool.

Restore and build your solution before the first check. Checks read saved product source, so subsequent product edits count without a build. The spec runs from its project's last build, so rebuild it after editing rules. A human-readable run over a stale spec prints a `warning:` on stderr naming the changed file and the `dotnet build` command to rebuild the project.

## Path 1: rules you write

From the directory containing your restored solution, run this with your solution's name in place of `MyApp`:

```bash
sliderule init MyApp.sln
```

`init` creates `arch/MyApp.Architecture/MyApp.Architecture.csproj` and `ArchitectureSpec.cs` next to the solution. The class has an empty `Define` method. It adds the project to the solution, writes `sliderule.json` naming both, builds the spec and runs a first check. With no rules, that check passes; `init` returns its verdict. The package reference uses the running tool's version, printed by `sliderule --version`.

The scaffold disables central package management, packing and documentation generation, and clears an inherited `TargetFramework`. Without `--framework`, an all-.NET Framework solution gets its highest framework. Other solutions get `net10.0`, or `netN.0` when `global.json` pins an SDK below 10; that choice rejects pins below SDK 6. A `typeof()` anchor needs a spec framework compatible with the product project it references. See [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md#the-spec-project).

Before writing, `init` rejects an existing spec, settings file or destination folder, a solution filter, or a project above the destination that would compile the spec class. The [`init` reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#init) explains these cases and framework selection.

For a starting set of boundaries, use `sliderule init --from-graph MyApp.sln` in place of plain `init`. It records `Enforce` rules between product projects with no reference path in either direction. Product projects are solution members that declare types and reference no test framework. Connections include declared and observed references, followed transitively. A project connected to every other product project gets no rule. With no unconnected pair, the command prints `no rule recorded: every two product projects are connected one way or the other`. Each reason reads `Recorded from the project graph on <date>: no reference either way then. Replace this with the reason the boundary exists.` Follow the generated comment to rewrite each reason or delete the rule.

For the first rule here, replace the class in `arch/MyApp.Architecture/ArchitectureSpec.cs` with this code, keeping its namespace declaration:

```csharp
using SlideRule;

public sealed class ArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*");
        Layer web    = arch.Layer("Web",    "MyApp.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");
    }
}
```

Adapt the namespace patterns, reason and fix to your architecture. A trailing `.*` includes the named namespace and its descendants, so `MyApp.Domain.*` includes `MyApp.Domain`. An empty subject fails with an `emptySubject` violation. An empty target leaves the rule passing with an inert-rule warning. Every rule requires a `Because`; a missing reason is a validation error that stops `check` before code evaluation. Optional `.Fix` text is one line explaining what to do instead. It appears beneath a failed rule in `check`, in `explain`, in build warnings and as SARIF help text.

You can test the spec's validity without loading a solution. `SpecValidation.Validate(new ArchitectureSpec())` builds its model and returns every validation message; assert that the list is empty. The package's analyzer also reports malformed patterns, rule IDs and other invalid string literals as compiler warnings on the literal. The package's completion provider lists namespaces and type names from referenced projects. Add product project references to the spec to make those completions available.

Build the solution, check it, and render the rules for your agents:

```bash
dotnet build MyApp.sln
sliderule check MyApp.sln
sliderule render MyApp.sln
```

`check` uses the spec named in the applicable `sliderule.json`, or discovers the unique declared solution project referencing `SlideRule`. It prints a line per rule and [exits 0](https://github.com/andypgray/sliderule/blob/main/CLI.md#exit-codes) when all pass, 1 when a rule fails, or 2 when the run cannot answer. If this boundary is already respected, your first rule passes.

`render` writes a managed block into `AGENTS.md` in the solution's directory, where [coding agents read](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files) the rules. Each layer used as an `Enforce` or `Migrate` subject also gets a directory card. With Domain code under `src/MyApp.Domain/`, this example produces `src/MyApp.Domain/AGENTS.md`. Commit the spec project, solution changes and rendered files.

To have Claude Code run the check each time an agent's turn ends, install the hooks:

```bash
sliderule install-hook
```

This merges `UserPromptSubmit`, `Stop` and `SubagentStop` entries, each running `sliderule hook`, plus deny rules for agent-run baseline commands. They go into `.claude/settings.json` at the git work-tree root, or the working directory outside a git repository. Commit the file. [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-turn-end-hook) explains the turn-end behavior.

For a boundary the code does not yet respect, use `.Migrate(from:, to:)`. Describe the old pattern in `from` and put the constraint in `to`. Run `sliderule baseline init MyApp.sln` to record each uncaptured ratcheted rule's current violations. The default path is `arch/baselines/<rule-id>.json` under the solution directory; slashes in the ID become directories. Commit those files. New violations then fail, and `sliderule baseline accept-reductions` shrinks baselines as sites are fixed. [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) covers migration rules and scopes. [Meridian](https://github.com/andypgray/sliderule/tree/main/examples/Meridian) has `Migrate` rules with committed baselines; [Meridian.Quoting](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting) demonstrates a four-layer clean architecture using only `Enforce` rules.

## Path 2: rules an agent drafts

Connect SlideRule's MCP server to your coding agent using [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server). The server registers one prompt, [`derive_spec`](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#drafting-a-spec-with-derive_spec), titled "Derive an architecture spec". It guides the agent from a solution without a spec project to a compiling draft.

The recipe starts with architecture tests, README, CONTRIBUTING, analyzer configuration and every developer-documentation link in those pages. The agent then surveys with `arch_graph` and creates the same scaffold that `init` writes, without running `sliderule init`. It drafts candidate rules as `Enforce`, builds the spec and runs `arch_check`. Each rule's `violationCount` supplies evidence for the posture written into the spec before review.

The agent proposes rules, the checker validates them, and you accept, edit or drop each one. The server does not infer your architecture. After your edits, build the spec project, run `sliderule baseline init MyApp.sln`, then `sliderule render --model MyApp.sln`, and commit one reviewable diff. The recipe reserves baselining, rendering and committing for you. [Adopting SlideRule on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) replays this path on the committed Meridian example, command by command. [A reference architecture held to its own book](https://github.com/andypgray/sliderule/tree/main/examples/eShopOnWeb) compares one session's draft with its curated spec on a public fork pinned in `showcase.json`.

## Next steps

Give commands a shared solution and spec selection in `sliderule.json`. On path 1, `init` writes it at the git work-tree root, or next to the solution outside a git repository. On path 2, add it yourself. Paths are relative to the file. Commit it with the rest:

```json
{
  "solution": "MyApp.sln",
  "spec": "arch/MyApp.Architecture/MyApp.Architecture.csproj"
}
```

Subsequent commands, the MCP server and the hook share this settings lookup. With no solution argument or `SLIDERULE_SOLUTION_PATH`, lookup walks upward from the working directory. It stops at the first `sliderule.json` or directory containing exactly one solution, passing directories with several. For a found or explicitly selected solution, the nearest settings file above it applies only if it names that solution. Explicit arguments override file settings; `SLIDERULE_SOLUTION_PATH` also overrides the file's solution. The walk never searches downward, so use the file to find a solution under `src/` from the repository root. The [settings file reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#the-settings-file) covers required `solution` and optional `spec`, `render.model`, `render.diagram` and `$schema`. Unknown keys fail the command. Setting `render.model` to `true` adds `sliderule.model.json` to every render for the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md).

After the CI build, write a SARIF verdict report and fail on out-of-date rendered files:

```bash
sliderule check --sarif sliderule.sarif
sliderule render --check
```

`render --check` writes no files. It prints `current` or `stale` for every render target and exits 1 if any is stale, including a card missing from the checkout at a newly required placement. For `Quarantine` or `Caution` scopes, also pass `--diff-base` with the PR's base git ref to `check`. Changed files then trigger scope warnings only; without it, each tripwire is reported skipped. [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#ci) covers the CI job and SARIF upload.

- [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) covers postures, scopes and selections. [GRAMMAR.md §5](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists the vocabulary's nouns, adjectives and constraint verbs.

- [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) explains turn-end checks of a Claude Code agent's working tree. Exit 2 blocks a stop on a red rule.

- [The build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) reports the same findings as compiler warnings in the editor and build.

- [The xUnit adapter](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Xunit/README.md) runs every rule as a named test in your test project.

- [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md) covers reducing a ratchet, upgrading and recovering from failed runs.
