# SlideRule

`SlideRule` is the spec contract: the fluent API an architecture spec is written in, and the rule model it
builds. It is the only SlideRule package a spec project needs.

[SlideRule](https://github.com/andypgray/sliderule) is a .NET architecture checker that enforces
one C# spec and renders the same rules for coding agents. The check runs at the command line, in CI,
in the build, as xUnit tests and in agent hooks. Agents read the rules in a managed `AGENTS.md` block,
per-directory rule cards and MCP query tools.

A rule is either the law (`Enforce`) or a ratchet over a counted baseline (`Migrate`). A scope marks
code that needs care. `Quarantine` contains it: the references into it today are counted on a
baseline, and a new one fails. `Caution` leaves the scope open to new callers. Both kinds warn when a
change touches the scope. For both, `render` puts the scope's dragons, the notes on why its code is fragile,
on a card in the scope's directory.

## Writing a spec

A spec is a small class library with one class implementing `IArchitectureSpec`:

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

[Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) makes this project from the package with `dotnet new classlib` and `dotnet add package`, then runs the first check.
[The vocabulary](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists every noun, adjective and verb, with its overloads and the sentence fragment it renders.

Add the spec project to the target solution (`dotnet sln add arch/MyApp.Architecture/MyApp.Architecture.csproj`).
The `sliderule` tool finds it as the one project in the solution that references this package, unless
`--spec` or a settings file names it, as the
[command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#solution-and-spec-resolution)
describes. The check leaves out the spec project itself, and any library the spec references that the
solution does not declare.

The four SlideRule packages ship one version in lockstep: this one, `SlideRule.Analyzers`,
`SlideRule.Xunit` and the `SlideRule.Cli` tool. Reference the version `sliderule --version` prints,
without its `+<commit>` suffix. A spec project that also gets `SlideRule.Analyzers`, for example from a
`Directory.Build.props` covering every project, needs both on one version. Each
includes a copy of this contract. At two versions one of the two analyzers is given the other's
copy, reports `SR1093` or `SR2093`, and skips its checks.

Pick the spec project's target framework by one rule. It must be able to reference the product
projects it will `typeof()`. This package is `netstandard2.0` with zero dependencies, so a spec
project can target anything from `net48` up.

## Mistakes reported as you write

The package includes a Roslyn analyzer for the spec's own source. A spec project that references the
package gets compiler warnings on its string literals for the mistakes a literal alone decides. They
appear in the editor and in `dotnet build`. Each warning is reported on the literal, in the sentence
that loading the spec would print:

```text
arch/MyApp.Architecture/ArchitectureSpec.cs(12,37): warning SR1017: The namespace pattern 'MyApp.*.Controllers.*' has a trailing `.*` subtree operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.
```

Loading the spec still validates it in full, and that validation is the authority. The analyzer
reads compile-time string constants, and it compares IDs and layer names within one method body. A
value built at run time, or an ID declared twice across two methods or a rule pack, is reported when
the spec is loaded.

`SpecValidation.Validate(IArchitectureSpec)` builds a spec and returns every mistake in it, one
message each, in the words `sliderule check` prints them. A rule with no reason, two rules with one
ID and a blank or multi-line piece of prose are all in the list. A one-line test with
`SpecValidation.Validate(new ArchitectureSpec())` proves that the spec's rules are valid without
loading a solution. A rule pack's own tests use it to prove that the rules its methods declare are
valid, as [Rule packs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md#rule-packs)
describes.

## Diagnostics

| ID | Meaning |
|---|---|
| `SR1000` | A rule or scope ID is declared twice in one method. |
| `SR1001` | A rule ID is under a scope's reserved `{scope-id}/` prefix. |
| `SR1005` | A prose argument, such as a `Because`, `Fix` or `Purpose`, is blank. |
| `SR1006` | A prose argument spans more than one line. |
| `SR1008` | A rule or scope ID is malformed. The convention is `area/rule-name`. |
| `SR1010` | A layer name is declared twice in one method. |
| `SR1012` | An `arch.Member(type, name)` has a blank name. |
| `SR1016` | A pattern, name or affix is blank. |
| `SR1017` | A namespace pattern can never match: a `*` comes before its trailing `.*`. |
| `SR1024` | A project name or glob on `arch.Projects` is blank. |
| `SR1025` | A target framework on `MustOnlyTarget` is blank. |
| `SR1026` | A counterpart name template has no `{Name}` placeholder. |
| `SR1030` | A `Citation` is not an absolute `http` or `https` URL. |
| `SR1031` | A layer name is blank. |
| `SR1093` | The analyzer failed, or was given another version's contract and checked no literal. Neither is about your spec. |

All 15 are ordinary warnings, so `.editorconfig` severities, `#pragma` and `NoWarn` apply. A
build that treats warnings as errors will fail on them. To keep the early warning without the break,
exclude the IDs:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);SR1000;SR1001;SR1005;SR1006;SR1008;SR1010;SR1012;SR1016;SR1017;SR1024;SR1025;SR1026;SR1030;SR1031;SR1093</WarningsNotAsErrors>
```

Loading the spec validates a suppressed literal like any other, and the spec fails validation with
the same sentence. To turn the analyzer off for a project, leave its assets out of the reference:

```xml
<PackageReference Include="SlideRule" Version="..." ExcludeAssets="analyzers" />
```

The analyzer needs a compiler on Roslyn 4.0.1 or later: the .NET 6 SDK and Visual Studio 2022, or
newer. Under an older compiler the package removes its analyzer and its completion provider from
the build, and the project compiles as before, with no warning.

Editors differ in how they show an analyzer's warnings. Visual Studio, VS Code with C# Dev Kit and
Rider underline the literal as you type. With ReSharper enabled in Visual Studio the message shows on
mouseover, without the underline. With the ReSharper extension in VS Code each warning is listed
twice, once under its `SR` code and once under ReSharper. ReSharper reports compiler and analyzer
warnings as inspections of its own. That extension has no setting that turns the second listing off.

## Names offered as you write

The package also includes a completion provider. Inside a string literal that names a namespace or a
type, it offers what the projects your spec references declare, one dotted segment at a time. Type
the opening quote of `arch.Namespace("")` and the top-level namespaces appear, each with a `.*` form
for the whole subtree. Type a dot and the next segment follows.

`Named` offers simple type names. The filters `Implementing`, `DerivedFrom`, `AttributedWith` and
`Returning`, the `Must*` verbs of the first three, and `MustAcceptParameter` take a type's full name. They
offer the types each one can match, spelled as a report prints them, generic parameters included.
Names from the projects your spec references sort above everything else the compilation can see.

Rider ranks its own completions above Roslyn's, so these appear there once "Always prefer Roslyn
completions" is on, under Settings | Editor | Inspection Settings | Roslyn Analyzers. ReSharper in
Visual Studio hides them too. Under ReSharper | Options | Environment | IntelliSense | General,
either "Visual Studio" or "Custom IntelliSense" with C# set to Visual Studio brings them back. The
second costs you ReSharper's own completion in C# and in no other language. The first request of a
session can come back without these names, because an editor loads a project's providers in the
background. Ask again and they are there.

## What checks and renders

This package is the contract, the analyzer for a spec's own literals, and the completion provider.
The other three packages check a codebase, and the tool also renders the rules:

- [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli), the
  `sliderule` global tool: `check`, `render`, `explain`, `status`, `graph`, `baseline`, `hook`, and
  the MCP server (`sliderule mcp`).

- [`SlideRule.Analyzers`](https://www.nuget.org/packages/SlideRule.Analyzers), the build analyzer,
  which reports a broken rule as a compiler warning at the offending code.

- [`SlideRule.Xunit`](https://www.nuget.org/packages/SlideRule.Xunit), the
  adapter that runs every rule in the spec as an individually named xUnit test.

The tool's `baseline` command writes the baseline files, and the tool, the build analyzer and the
adapter each read them.

## Documentation

- [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md)
- [Command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md)
- [The language reference](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md)
- [Repository and full README](https://github.com/andypgray/sliderule)

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
