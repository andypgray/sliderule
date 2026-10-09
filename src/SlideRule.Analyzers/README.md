# SlideRule.Analyzers

`SlideRule.Analyzers` is the [SlideRule](https://github.com/andypgray/sliderule) build
analyzer. It reports a broken architecture rule as a compiler warning at the offending code, in the
editor as you type and in plain `dotnet build` output. It runs the same extractor and checker as
`sliderule check`, over the one project being compiled.

`sliderule check` stays the gate. The analyzer is the same finding, earlier: a person sees the
squiggle in the file they are editing, and an agent sees the rule, its reason and its fix in the
build it was already running.

## Usage

Reference the package from the projects your spec governs. One line in a `Directory.Build.props`
above them reaches all of them:

```xml
<ItemGroup>
  <PackageReference Include="SlideRule.Analyzers" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

Under central package management, a `GlobalPackageReference` in `Directory.Packages.props` does the
same. The package is a development dependency and adds nothing to what your projects ship.

The analyzer cannot run your spec, because the spec project builds after the projects it describes.
It reads the rules from a file instead. Write that file once, and commit it:

```
sliderule render --model
```

`render` writes `sliderule.model.json` beside the solution and refreshes it on every later run, and
`render --check` in CI fails on a stale copy exactly as it does on a stale `AGENTS.md` block. A
repository with a `sliderule.json` can ask for the file there instead, with `"render": { "model": true }`. The package finds
the nearest `sliderule.model.json` at or above each project, and the baseline files under
`arch/baselines/` beside it. A baseline your spec keeps somewhere else is one item:

```xml
<ItemGroup>
  <SlideRuleBaseline Include="$(MSBuildThisFileDirectory)debt/billing.json" />
</ItemGroup>
```

With this rule in the spec:

```csharp
arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(web))
    .Because("Domain is UI-agnostic; transaction boundaries live in services.")
    .Fix("Define an abstraction in Domain and implement it in Web.");
```

a new reference from `OrderService` to a controller fails nothing yet, and prints this:

```
MyApp.Domain/OrderService.cs(9,9): warning SR2001: layering/domain-independent: MyApp.Domain.OrderService references MyApp.Web.HomeController. The Domain layer must not reference the Web layer. Because: Domain is UI-agnostic; transaction boundaries live in services. Fix: Define an abstraction in Domain and implement it in Web. (https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md#diagnostics)
```

The build adds the link to this page, and the project file in brackets after it.

When the rule names a type to copy with `.Exemplar(...)`, the message adds it after the fix, worded
as the rule reads: ``Exemplar: types named `OrdersRepository`.`` The build does not look the type up.
`sliderule check` prints the file and line that declare it, and warns when it no longer exists.

## Projects on packages.config

A `packages.config` project gets no build assets from a package on its own, so its project file
needs two lines: the import that hands the model file to the compiler, and the analyzer
assemblies.

```xml
<Import Project="..\packages\SlideRule.Analyzers.x.y.z\build\SlideRule.Analyzers.targets"
        Condition="Exists('..\packages\SlideRule.Analyzers.x.y.z\build\SlideRule.Analyzers.targets')" />
<ItemGroup>
  <Analyzer Include="..\packages\SlideRule.Analyzers.x.y.z\analyzers\dotnet\cs\*.dll" />
</ItemGroup>
```

Both are needed. With the analyzers and no import, the build reports `SR2090` and checks nothing,
because the import is what finds `sliderule.model.json`.

## What the build checks, and what it leaves to check

A compiler sees one project at a time. Many rules can be decided from there. A layer must not
reference another. A type must not be constructed, thrown or injected. A member must accept a
parameter. Other rules need the whole solution: a rule over a family of layers, a circle of
references, a dependency-injection registration, anything that reads a project file. The build
reports the first kind and leaves the second to `sliderule check`. When `render` writes the model
file it prints the split, rule by rule, with the reason for each rule it left to `check`.

How a layer is defined, and where its rule names it, decide which kind that rule is. A layer
defined by namespace is decided in the build wherever it stands. A layer defined by
`arch.Project(...)` is decided in the build wherever its rule asks it about one reference at a time:
as the subject of a rule, as what a `MustNot...` verb bans, and in the allow-list of a
`MustOnly...` verb. An allow-list entry is asked about the project whose copy of a type the
reference reached, and that is the copy the compiler bound, so one project's build has it exactly.
It is left to `check` inside an `.Except(...)`, which drops a type from a set whichever project
compiled it. A file compiled into two projects is declared by both, one project's build sees only
its own declaration, and a name that spares a type for `check` can spare nothing in the build. A
rule that asks which project a type resides in, or which layer it belongs to, is left to `check`
however its layers are defined.

The build never reports a violation that `check` would not. Where one project's view cannot be sure,
the analyzer says nothing: a referenced type it cannot fully resolve is left out of the verdict
instead of guessed at. The reverse does not hold, so a quiet build is not a passing `check`.

A `Migrate` rule or a `Quarantine` is checked against its captured baseline. Violations the
baseline already holds are silent, and a new violation is a warning. A pair the baseline holds
that has gained sites reports each site, with the count the baseline recorded and the count the
build found. A ratcheted rule whose baseline the build was not given is not checked at all,
because checking it would report everything the baseline exists to hold. The build names that
rule once, and only in a project where it would have reported.

## Diagnostics

| ID | Meaning |
|---|---|
| `SR2001` | A rule is broken: an `Enforce` rule, or a quarantine's containment. |
| `SR2002` | A `Migrate` rule has a violation its baseline does not hold, or a pair that grew. |
| `SR2090` | No `sliderule.model.json` at or above the project, so nothing was checked. |
| `SR2091` | A model file or a baseline file could not be read. The warning sits in that file. |
| `SR2092` | A ratcheted rule was not checked, because the build was given no baseline for it. |
| `SR2093` | The analyzer failed in this project, or was handed another version's contract and checked nothing. Neither is about your code. |

All six are ordinary warnings, so the usual controls apply. `.editorconfig` sets a severity, for the
whole repository or for one folder:

```ini
[*.cs]
dotnet_diagnostic.SR2001.severity = error
dotnet_diagnostic.SR2002.severity = warning
```

A build that treats warnings as errors will fail on them. To keep the early warning without the
break, exclude the IDs:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);SR2001;SR2002;SR2090;SR2091;SR2092;SR2093</WarningsNotAsErrors>
```

On a command line each `;` in that list is written `%3B`, since MSBuild reads a bare one as the end
of the property.

A `#pragma warning disable` or a `[SuppressMessage]` silences a warning in the build and changes
nothing in `sliderule check`, which reads no suppression. What a suppression costs is the
earliness.

## Cost, and the tier

The analyzer runs a full extraction of the project inside the compiler, and that is nearly all of
what it costs. The figures below are from a large open-source web application. In a command-line
build the compile step of its largest project went from 58.1 s to 85.6 s, and a class library's from
13.9 s to 16.4 s. In the editor, re-analysing one file after an edit took 0.7 ms at the median and
33 ms at the 95th percentile over the same project's 1,174 authored files, and 15 ms at the median
over its 803 generated Razor files. A project with nothing to check pays about a quarter of a second,
for reading the model file. The compiler server that `dotnet build` uses by default keeps what it
read until the file changes, and a project compiled after that read skips it. A build with
`-p:UseSharedCompilation=false` runs each project in a compiler process of its own, so every project
pays it.

One property, `SlideRuleTier`, chooses where the analyzer reports:

```xml
<SlideRuleTier>build</SlideRuleTier>
```

- `live`, the default, reports in the file being edited as you type, and in the build.
- `build` reports in the build only, once per project at the end of its compilation. It is a
  command-line mode. Visual Studio and Rider show nothing until a build runs, and clear the build's
  warning from the Error List as soon as the file is edited. It costs more, because it runs on one
  thread where `live` analyses several documents at once. The same largest project compiled in
  104.8 s under `build`.
- `off` turns the analyzer off for a project, or for a configuration.

Set it on the command line (`-p:SlideRuleTier=off`) for an inner-loop build. Leave it unset in
CI and in the builds a coding agent runs, which is where the warning gets read.

In VS Code with C# Dev Kit, `build` shows nothing in the editor while background analysis covers the
open documents, which is the default. With `dotnet.backgroundAnalysis.analyzerDiagnosticsScope` set
to `fullSolution`, that analysis runs each project's end-of-compilation check as a build would, and
the warnings squiggle. Set `off` to keep them out of the editor there. VS Code applies a new value
once the file that sets it is saved in the editor. After an edit made outside VS Code, run Reload
Window from the command palette.

## Requirements

- **A compiler on Roslyn 4.0.1 or later:** the .NET 6 SDK and Visual Studio 2022, or newer. An older
  compiler cannot load the analyzer, so the package removes itself from that build and the project
  compiles as before, with no warning.
- **A committed `sliderule.model.json`,** written by a `sliderule` tool of the same version as
  this package. A file from a newer tool is reported as `SR2091` and nothing is checked until the
  package is updated.
- **The same version as `SlideRule`,** in a project that references both packages. Each
  carries its own copy of the contract assembly, and at two versions a build hands both analyzers the
  same copy. The analyzer given the other version's copy checks nothing and says so, as `SR2093` here
  or `SR1093` from the contract package, naming both versions.

## What each editor shows

`SR2001` and `SR2002` squiggle in the file being edited and clear when the edit is reverted. `SR2090`,
`SR2092` and `SR2093` describe the build as a whole. They appear only after a build, with
full-solution analysis on or off, and never as a squiggle.

- Visual Studio analyses the open documents. "Entire solution" under Tools, Options, Text Editor, C#,
  Advanced, Run background code analysis for, shows a closed file's finding too. With ReSharper on,
  the warning shows on mouseover rather than as a squiggle. To bring the squiggle back, clear "Hide
  only those Visual Studio squiggles that duplicate ReSharper code analysis highlightings" under
  ReSharper, Options, Environment, Editor, Visual Studio Features. The option hides more than its
  name says, which JetBrains tracks as [RSRP-492566](https://youtrack.jetbrains.com/issue/RSRP-492566).
- VS Code with C# Dev Kit analyses the open documents. Set
  `dotnet.backgroundAnalysis.analyzerDiagnosticsScope` to `fullSolution` to reach closed files. With
  the JetBrains extension on, each warning is listed twice.
- Rider runs the analyzer with "Enable Roslyn Analyzers" on, which is the default. "Include Roslyn
  Analyzers in Solution-Wide Analysis" reaches closed files.

## Writing the spec

The spec is authored against the
[`SlideRule`](https://www.nuget.org/packages/SlideRule) contract package, and the
[`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) global tool checks
it, renders it for coding agents, and writes the model file this package reads. SlideRule's
packages ship one version in lockstep; keep the analyzer at the version of the tool.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
