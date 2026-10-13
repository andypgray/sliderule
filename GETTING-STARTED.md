# Getting started

SlideRule reads your architecture from a spec project, a C# class library in your solution that states the rules. Until the solution has one, `check` stops before it evaluates anything and prints `No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.`

This page takes you from that message to a green check. There are two ways to start, and they differ in who drafts the first rules, not in how old the code is. On path 1 you write them yourself. On path 2 a coding agent drafts them, and you decide which ones stay. Pick one before you run anything, because each makes its own spec project, and `check` stops on a solution with two (`Multiple spec projects found`). On either path, `sliderule graph MyApp.sln` is a safe first look: it surveys the solution's projects, references and namespaces without a spec.

## Requirements

The machine that runs SlideRule needs a .NET 10 SDK. The commands that load a solution (`check`, `explain`, `render`, `status`, `graph`, `baseline`, `hook` and `mcp`) do so through MSBuildWorkspace, and a runtime alone cannot host that load. The code under check can target .NET Framework 4.8 or anything newer, and SlideRule never retargets it. The spec project compiles against one package, `SlideRule`, which targets netstandard2.0.

SlideRule ships as a .NET global tool:

```bash
dotnet tool install -g SlideRule.Cli
```

Run the install from outside any repository whose `global.json` pins an SDK below 10. Inside one, `dotnet` selects the pinned SDK, which cannot install a tool built for .NET 10.

SlideRule never builds the code it checks. It reads the code from source, so an edit counts as soon as it is saved, built or not. It does need the solution restored, and it runs the spec from the spec project's last build. Restore and build the solution before the first check, and rebuild the spec project after each change to the spec.

## Path 1: rules you write

Run these from the directory with your solution file, with your own names in place of `MyApp`. They create the spec project, add the package to it, and add the project to the solution:

```bash
dotnet new classlib -o arch/MyApp.Architecture
dotnet add arch/MyApp.Architecture package SlideRule
dotnet sln MyApp.sln add arch/MyApp.Architecture/MyApp.Architecture.csproj
```

`dotnet add package` takes the newest `SlideRule`, which matches a tool installed the same day. Keep the package at the version `sliderule --version` prints, and if the tool is older than the package, update it with `dotnet tool update -g SlideRule.Cli`. A rule that names a product type with `typeof()` needs a spec project that can reference that type's project. [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md#the-spec-project) explains how to pick the spec project's target framework, and what changes on central package management.

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

Change the two namespace patterns, the `Because` and the `Fix` to your own. A pattern that ends in `.*` covers that namespace and every namespace below it, so `MyApp.Domain.*` includes `MyApp.Domain` itself. A rule whose subject pattern matches no types fails, so a typo there shows up on the first check. A typo in a target pattern, such as the Web one here, leaves the rule passing with a warning. Every rule needs a `Because`, and a spec with one missing fails to load. The `Fix` is optional, and it tells the developer or agent who breaks the rule what to do instead. A one-line test with `SpecValidation.Validate(new ArchitectureSpec())` proves that the spec's rules are valid without loading a solution.

While you type, the `SlideRule` package's analyzer reports a malformed pattern or ID as a warning on the literal. Once the spec project references your product projects, for example with `dotnet add arch/MyApp.Architecture reference src/MyApp.Domain/MyApp.Domain.csproj`, the package's completion offers their namespaces and type names.

Build the solution, check it, and render the rules for your agents:

```bash
dotnet build MyApp.sln
sliderule check MyApp.sln
sliderule render MyApp.sln
```

`check` finds the spec project by convention, as the one project in the solution that references `SlideRule`. It prints a line for each rule and [exits 0](https://github.com/andypgray/sliderule/blob/main/CLI.md#exit-codes) while every rule passes. `render` writes the rules into a managed block in the `AGENTS.md` in the solution's directory, the file [coding agents read](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files). It also writes a card into the directory of each layer a rule governs, here `src/MyApp.Domain/AGENTS.md`. Commit the spec project, the solution file and the `AGENTS.md` files `render` wrote.

Next, write down what the code already keeps as more `Enforce` rules. A rule the code does not keep yet takes `.Migrate(from:, to:)` in place of `.Enforce(...)`. The constraint moves into `to`, and `from` describes the old pattern in words. Then `sliderule baseline init MyApp.sln` records the rule's current violations in `arch/baselines/<rule-id>.json` for you to commit. New violations fail from then on, and `sliderule baseline accept-reductions` shrinks the baseline as sites are fixed. [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) shows a `Migrate` rule and both kinds of scope in code, and the [Meridian](https://github.com/andypgray/sliderule/tree/main/examples/Meridian) example shows `Migrate` rules and their baselines on a codebase partway through a migration.

On a new codebase this path starts with every rule green. The [Meridian.Quoting](https://github.com/andypgray/sliderule/tree/main/examples/Meridian.Quoting) example shows where it ends, a four-layer clean architecture governed by `Enforce` rules alone.

## Path 2: rules an agent drafts

Connect SlideRule's MCP server to your coding agent first, as [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server) shows. The server ships one prompt, [`derive_spec`](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#drafting-a-spec-with-derive_spec), which walks the agent from a solution with no spec to a draft spec. In Claude Code, run it as `/sliderule:derive_spec`, and in another MCP client, pick it from the server's prompts.

The agent surveys the solution with `arch_graph` and then scaffolds the spec project itself, so this path skips path 1's spec-project commands. It drafts every candidate rule as `Enforce` and runs `arch_check` to count each one's violations. Those counts decide the posture it proposes for each rule.

SlideRule does not infer the architecture, and the agent does not ratify it. You review every rule the agent proposes, and accept, edit or drop it. The agent leaves the baseline, the render and the commit to you. Build the spec project after your edits, run `sliderule baseline init MyApp.sln`, then `sliderule render MyApp.sln`, and commit the result as one change. [Adopting SlideRule on an existing codebase](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) replays this path on a committed example, one real command at a time.

## Next steps

Name the solution and the spec once, in a `sliderule.json` at the root of the repository, and commit it:

```json
{
  "solution": "MyApp.sln",
  "spec": "arch/MyApp.Architecture/MyApp.Architecture.csproj"
}
```

Every command run in the repository reads it, and so do the MCP server and the hook, so none of them needs the solution or the spec named again. A command searches up from the directory it runs in, and stops at the first directory with a `sliderule.json` or exactly one solution. It climbs past a directory with several solutions. When it stops at a solution, the nearest `sliderule.json` above still applies if that file names the solution. Paths in the file are relative to the file, and an argument on the command line still wins. A solution under `src/` needs the file, because without it a command run from the repository root looks for a solution only there and in the directories above. The [settings file reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#the-settings-file) lists its other keys, such as `render.model`, which adds the model file the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) reads.

In CI, after your build step, write a SARIF report and fail on any rendered file that is out of date:

```bash
sliderule check --sarif sliderule.sarif
sliderule render --check
```

`render --check` writes no files. It prints `current` or `stale` for each file a render would write and exits 1 when any is stale, including an `AGENTS.md` the spec now places in a directory where none is committed. A spec with a `Quarantine` or `Caution` scope also wants `--diff-base` on the check, set to the pull request's base ref, or each scope's tripwire reports skipped. [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#ci) covers the rest of the CI job, including the SARIF upload.

From there, each page below takes one next step:

- [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md): postures, scopes and selections beyond the first rule. [GRAMMAR.md §5](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists every noun, adjective and verb.

- [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks): `sliderule hook` checks the tree when a Claude Code agent's turn ends, and blocks the stop while a rule is red.

- [The build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md): the same findings as compiler warnings, in the editor and in the build.

- [The xUnit adapter](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Xunit/README.md): every rule as a named test in your test project.

- [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md): working a ratchet down, upgrading, and what to do when a run fails.
