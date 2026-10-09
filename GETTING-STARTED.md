# Getting started

SlideRule reads your architecture from a spec project, a C# class library in your solution that states the rules. Until the solution has one, `check` stops before it evaluates anything and prints `No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.`

This page takes you from that message to a green check. There are two ways to start, and they differ in who drafts the first rules, not in how old the code is. On path 1 you write them yourself. On path 2 a coding agent drafts them, and you decide which ones stay. Pick one before you run anything, because each makes its own spec project and `check` refuses a solution holding two (`Multiple spec projects found`).

## Requirements

The machine that runs SlideRule needs a .NET 10 SDK. The commands that load a solution (`check`, `render`, `status`, `graph`, `baseline` and `mcp`) do so through MSBuildWorkspace, and a runtime alone cannot host that load. The code under check can target .NET Framework 4.8 or anything newer, and SlideRule never retargets it. The spec project compiles against one package, `SlideRule`, which targets netstandard2.0.

SlideRule ships as a .NET global tool:

```bash
dotnet tool install -g SlideRule.Cli
```

Run the install from outside any repository whose `global.json` pins an SDK below 10. Inside one, `dotnet` selects the pinned SDK, which cannot install a tool built for .NET 10.

SlideRule never builds the code it checks. Restore and build the solution before every check, because a stale build gives stale verdicts.

## Path 1: rules you write

Run these from the directory that holds your solution, with your own names in place of `MyApp`. They create the spec project, add the package to it, and add the project to the solution:

```bash
dotnet new classlib -o arch/MyApp.Architecture
dotnet add arch/MyApp.Architecture package SlideRule
dotnet sln MyApp.sln add arch/MyApp.Architecture/MyApp.Architecture.csproj
```

`dotnet add package` takes the newest `SlideRule`, which matches a tool installed the same day. Keep the package at the version `sliderule --version` prints. If the tool is older than the package, update it with `dotnet tool update -g SlideRule.Cli`. A rule that names a product type with `typeof()` needs a spec project that can reference that type's project. The package README's [Writing a spec](https://github.com/andypgray/sliderule/blob/main/src/SlideRule/README.md#writing-a-spec) says how to pick the spec project's target framework.

In `arch/MyApp.Architecture`, delete `Class1.cs` and save this as `ArchitectureSpec.cs`:

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

Change the two namespace patterns, the `Because` and the `Fix` to your own. A pattern that ends in `.*` covers that namespace and every namespace below it, so `MyApp.Domain.*` includes `MyApp.Domain` itself. Every rule needs a `Because`, and `check` refuses a spec in which one is missing. The `Fix` is optional, and it tells whoever breaks the rule what to do instead.

Build the solution, check it, and render the rules for your agents:

```bash
dotnet build MyApp.sln
sliderule check MyApp.sln
sliderule render MyApp.sln
```

`check` finds the spec project by convention, as the one project in the solution that references `SlideRule`. It prints a line for each rule and [exits 0](https://github.com/andypgray/sliderule/blob/main/CLI.md#exit-codes) while every rule holds. `render` writes the rules into a managed block in `AGENTS.md` beside the solution, the file [coding agents read](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files). It also writes a card into the directory of each layer a rule governs, here `src/MyApp.Domain/AGENTS.md`. Commit the spec project, the solution file and the `AGENTS.md` files `render` wrote.

Next, write down what the code already holds as more `Enforce` rules. A rule the code does not hold yet takes `.Migrate(from:, to:)` in place of `.Enforce(...)`. The constraint moves into `to`, and `from` describes the old pattern in words. Then `sliderule baseline init MyApp.sln` records the rule's current violations in a baseline file for you to commit. New violations fail from the next commit, and the baseline only shrinks. The [Meridian](https://github.com/andypgray/sliderule/tree/main/examples/Meridian) example shows `Migrate` rules and their baselines on a codebase partway through a migration.

On a new codebase this path starts with every rule green. The [Meridian.Quoting](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting) example shows where it ends, a four-layer clean architecture held by `Enforce` rules alone.

## Path 2: rules an agent drafts

Connect SlideRule's MCP server to your coding agent first, as [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server) shows. The server ships one prompt, [`derive_spec`](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#drafting-a-spec-with-derive_spec), which walks the agent from a solution with no spec to a draft spec. In Claude Code, run it as `/sliderule:derive_spec`.

The agent surveys the solution with `arch_graph` and then scaffolds the spec project itself, so this path skips path 1's spec-project commands. It drafts every candidate rule as `Enforce` and runs `arch_check` to count each one's violations. Those counts decide the posture it proposes for each rule.

SlideRule does not infer the architecture, and the agent does not ratify it. Every proposed rule crosses a curation gate, where you accept, edit or drop it. The agent leaves the baseline, the render and the commit to you. Run `sliderule baseline init MyApp.sln`, then `sliderule render MyApp.sln`, and commit the result as one change. [Adopting SlideRule on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) replays this path on a committed example, one real command at a time.

## Next steps

Name the solution and the spec once, in a `sliderule.json` at the root of the repository, and commit it:

```json
{
  "solution": "MyApp.sln",
  "spec": "arch/MyApp.Architecture/MyApp.Architecture.csproj"
}
```

Every command reads it from any directory in the repository, and so does the MCP server, so none of them needs either path again. Paths in the file are relative to the file, and an argument on the command line still wins. A solution under `src/` needs the file, because without it the commands look for a solution only in the directory they run from and the directories above it. The file also says what `render` writes besides the `AGENTS.md` files. `"render": { "model": true }` adds `sliderule.model.json`, the model the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) reads. `"render": { "diagram": { "path": "ARCHITECTURE.md" } }` adds a drawing of the solution's projects and the spec's layers, and `only` and `exclude` lists of project-name globs beside `path` choose which projects it draws. A key the file does not know is refused, naming the file.

An agent hook runs `check` when a coding agent tries to end its turn, and a red rule keeps the agent working. The hook is the `sliderule hook` command, and the snippet that wires it into Claude Code is in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks).

In CI, after your build step, write a SARIF report and fail on any rendered file that is out of date:

```bash
sliderule check --sarif sliderule.sarif
sliderule render --check
```

Upload `sliderule.sarif` to your code-scanning service to see each violation on the line that caused it. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#sarif) says what each result carries. `render --check` writes nothing. It prints `current` or `stale` for each file a render would write and exits 1 when any is stale, including an `AGENTS.md` the spec now places in a directory where none is committed. A spec with a `Quarantine` or `Caution` scope also wants `--diff-base` on the check, set to the pull request's base ref, or each scope's tripwire reports skipped.

[GRAMMAR.md §5](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists every noun, adjective and verb a rule can use.
