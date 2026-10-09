# SlideRule

`SlideRule` is the spec contract: the fluent builder and reified rule model
an architecture spec is written against. It is the one package a spec project references.

[SlideRule](https://github.com/andypgray/sliderule) is a .NET architecture checker that enforces
one C# spec and renders the same rules for coding agents. The check runs at the command line, in CI
and in agent hooks. Agents read the rules in a managed `AGENTS.md` block, per-directory rule cards
and MCP query tools. A rule is either the law (`Enforce`) or a ratchet over a counted baseline
(`Migrate`). A scope marks "here be dragons" code. `Quarantine` contains it, so a new reference into
the scope fails. `Caution` gives dragons that stay open to new callers the same card and tripwire,
without the containment.

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
The tooling discovers it by convention, as the unique solution project that references this
package. The spec project itself is excluded from the checked universe. So is any library the spec
references that the solution does not declare, since only the spec pulls it in.

The four SlideRule packages ship one version in lockstep. Reference the version your installed
`sliderule` tool prints from `sliderule --version`. A spec project that also references
`SlideRule.Analyzers` keeps both on that version. Each package carries a copy of this
contract. At two versions, one of the two analyzers is handed the other's copy and reports `SR1093`
or `SR2093` instead of checking.

Pick the spec project's target framework by one rule. It must be able to reference the product
projects it will `typeof()`. This package is `netstandard2.0` with zero dependencies, so a spec
project can target anything from `net48` up.

## Mistakes reported as you write

The package carries a Roslyn analyzer beside the contract. A spec project that references it gets
compiler warnings on its string literals for the mistakes a literal alone decides. They appear in
the editor and in `dotnet build`. Each warning sits on the literal and uses the sentence that
loading the spec would print:

```text
arch/MyApp.Architecture/ArchitectureSpec.cs(12,37): warning SR1017: The namespace pattern 'MyApp.*.Controllers.*' has a trailing `.*` subtree operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.
```

Loading the spec still validates it in full, and that validation is the authority. The analyzer
reads compile-time string constants, and it compares IDs and layer names within one method body. A
value built at run time is reported when the spec is loaded, as before. So is an ID declared twice
across two methods or a rule pack.

## Diagnostics

| ID | Meaning |
|---|---|
| `SR1000` | A rule or scope ID is declared twice in one method. |
| `SR1001` | A rule ID sits under a scope's reserved `{scope-id}/` prefix. |
| `SR1005` | A prose argument is blank: a `Because`, a `Fix`, a `Purpose` and the rest. |
| `SR1006` | A prose argument spans more than one line. |
| `SR1008` | A rule or scope ID is malformed. The convention is `area/rule-name`. |
| `SR1010` | A layer name is declared twice in one method. |
| `SR1012` | An `arch.Member(type, name)` has a blank name. |
| `SR1016` | A pattern, name or affix is blank. |
| `SR1017` | A namespace pattern can never match: a `*` sits before its trailing `.*`. |
| `SR1024` | A project name or glob on `arch.Projects` is blank. |
| `SR1025` | A target framework on `MustOnlyTarget` is blank. |
| `SR1026` | A counterpart name template has no `{Name}` placeholder. |
| `SR1030` | A `Citation` is not an absolute `http` or `https` URL. |
| `SR1031` | A layer name is blank. |
| `SR1093` | The analyzer failed, or was handed another version's contract and checked nothing. Neither is about your spec. |

All fifteen are ordinary warnings, so `.editorconfig` severities, `#pragma` and `NoWarn` apply. A
build that treats warnings as errors will fail on them. To keep the early warning without the break,
exclude the IDs:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);SR1000;SR1001;SR1005;SR1006;SR1008;SR1010;SR1012;SR1016;SR1017;SR1024;SR1025;SR1026;SR1030;SR1031;SR1093</WarningsNotAsErrors>
```

A suppressed warning changes nothing at load, where the spec fails validation with the same
sentence. To turn the analyzer off for a project, leave its assets out of the reference:

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

The package carries a completion provider beside the analyzer. Inside a string literal that names a
namespace or a type, it offers what the projects your spec references declare, one dotted segment at
a time. Type the opening quote of `arch.Namespace("")` and the top-level namespaces appear, each with
a `.*` form beside it for the whole subtree. Type a dot and the next segment follows.

`Named` offers simple type names. The anchors that take a definition's full name are
`Implementing`, `DerivedFrom`, `AttributedWith`, `Returning`, `MustAcceptParameter` and the `Must*`
forms beside them. They offer the types each one can match, spelled the way a report prints them,
declared generic parameters included. Names from the projects your spec references sort above
everything else the compilation can see.

Rider ranks its own completions above Roslyn's, so these appear there once "Always prefer Roslyn
completions" is on, under Settings | Editor | Inspection Settings | Roslyn Analyzers. ReSharper in
Visual Studio hides them too. Under ReSharper | Options | Environment | IntelliSense | General,
either "Visual Studio" or "Custom IntelliSense" with C# set to Visual Studio brings them back. The
second costs you ReSharper's own completion in C# and nothing in any other language. The first
request of a session can come back without these names, because an editor loads a project's
providers in the background. Ask again and they are there.

## What checks and renders

This package is the contract, the analyzer over a spec's own literals, and the names offered while
one is written. Nothing in it checks a codebase or renders anything. That is done by:

- [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli), the
  `sliderule` global tool: `check`, `render`, `explain`, `status`, `graph`, `baseline`, and
  the MCP server (`sliderule mcp`).

- [`SlideRule.Xunit`](https://www.nuget.org/packages/SlideRule.Xunit), the
  adapter that runs every rule in the spec as an individually named xUnit test.

Baseline files are theirs too. This package describes a captured baseline, and the two runners
above are what read one off disk and hand it to the check.

## Documentation

- [Repository and full README](https://github.com/andypgray/sliderule)

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
