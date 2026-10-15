# SlideRule.Analyzers

`SlideRule.Analyzers` is the [SlideRule](https://github.com/andypgray/sliderule) build
analyzer. It reports a broken architecture rule as a compiler warning at the offending code, in the
editor as you type and in plain `dotnet build` output. It runs the same extractor and checker as
`sliderule check`, over the one project being compiled.

`sliderule check` stays the gate, and the analyzer reports the same finding earlier. A person sees
the squiggle in the file they are editing. An agent sees the rule, its reason and its fix in the
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
same. The package is a development dependency, so the packages you publish do not depend on it.

A repository that restores with lock files needs them refreshed once. The new reference is missing
from each `packages.lock.json` it reaches, so a locked restore fails with `NU1004`. Run
`dotnet restore --force-evaluate` over the solution after adding the reference, and commit the
changed lock files with it.

The analyzer cannot run your spec, because the spec project builds after the projects it describes.
It reads the rules from a file instead. Write that file once, and commit it:

```
sliderule render --model
```

`render` writes `sliderule.model.json` next to the solution file, and every later `render` refreshes
it. In CI, `render --check` fails on a stale copy exactly as it does on a stale `AGENTS.md` block. A
repository with a `sliderule.json` settings file can ask for the model file there instead, with
`"render": { "model": true }`.

The package finds the nearest `sliderule.model.json` at or above each project, and the baseline files
under `arch/baselines/` next to it. To use a model file kept somewhere else, set the
`SlideRuleModelFile` MSBuild property to its path. The file must still be named `sliderule.model.json`,
and the baselines then come from `arch/baselines/` next to it. A baseline your spec keeps somewhere
else is one item:

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

a new reference from `OrderService` to a controller still compiles, and the build prints this warning:

```
MyApp.Domain/OrderService.cs(9,9): warning SR2001: layering/domain-independent: MyApp.Domain.OrderService references MyApp.Web.HomeController. The Domain layer must not reference the Web layer. Because: Domain is UI-agnostic; transaction boundaries live in services. Fix: Define an abstraction in Domain and implement it in Web. (https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md#diagnostics)
```

The build adds the link to this page, and the project file in brackets after it.

A rule can also name a type to copy with `.Exemplar(...)`. The warning then adds
``Exemplar: types named `OrdersRepository`.`` after the fix, worded as the rule reads. The build does
not look the type up. `sliderule check` prints the file and line that declare it, and warns when it no
longer exists.

## Projects on packages.config

A `packages.config` project gets no build assets from a package on its own. Its project file needs
the import that passes the model file to the compiler, and the analyzer assemblies:

```xml
<Import Project="..\packages\SlideRule.Analyzers.x.y.z\build\SlideRule.Analyzers.targets"
        Condition="Exists('..\packages\SlideRule.Analyzers.x.y.z\build\SlideRule.Analyzers.targets')" />
<ItemGroup>
  <Analyzer Include="..\packages\SlideRule.Analyzers.x.y.z\analyzers\dotnet\cs\*.dll" />
</ItemGroup>
```

Both are needed. With the analyzers and no import, the build reports `SR2090` and leaves every rule
unchecked, because the import is what finds `sliderule.model.json`.

## What the build checks, and what it leaves to check

A compiler sees one project at a time, and many rules can be decided from there. A layer must not
reference another. A type must not be constructed, injected, thrown or caught. A member must not be
used, and a method must accept a parameter. A type must have an attribute or a name suffix. Other
rules need the whole solution, such as a rule over a family of layers, a circle of references, a
dependency-injection registration, or anything that reads a project file. The build reports the
first kind and leaves the second to `sliderule check`. A rule with a `Where` or `Must` predicate is
not among the model file's rules, so the analyzer leaves it to `check` and the xUnit adapter. The
file lists every rule it leaves out under `omitted`, with the reason. When `render` writes the model
file, it prints the split rule by rule, with the reason for each rule it left to `check`.

How a layer is defined, and where its rule names it, decide which kind that rule is. A layer
defined by namespace is decided in the build wherever it appears. A layer defined by
`arch.Project(...)` is decided in the build wherever its rule asks about one reference at a time.
That covers the subject of a rule, what a `MustNot...` verb bans, and the allow-list of a
`MustOnly...` verb. An allow-list entry is asked about the project whose copy of a type the
reference reached. That is the copy the compiler bound, so one project's build has the exact answer.

Inside an `.Except(...)`, a layer defined by `arch.Project(...)` is left to `check`, because
`.Except(...)` drops a type from a set whichever project compiled it. A file compiled into two
projects is declared by both, and one project's build sees only its own declaration. So a name that
spares a type in `check` cannot spare it in the build. A rule that asks which project a type resides
in, or which layer it belongs to, is left to `check` however its layers are defined.

The build never reports a violation that `check` would not. Where one project's view cannot be sure,
the analyzer stays silent, and a referenced type it cannot fully resolve is left out of the verdict.
The reverse does not hold, so `check` can still fail after a build with no warnings.

A `Migrate` rule or a `Quarantine` is checked against its captured baseline. Violations already in
the baseline are not reported, and a new violation is a warning. When a pair in the baseline gains
sites, the build reports each site with the count the baseline recorded and the count it found. A
ratcheted rule whose baseline the build was not given is left unchecked, because checking it
would report every violation the baseline exists to grandfather. The build names that rule once, as
`SR2092`, and only in a project where it would have reported.

## Diagnostics

| ID | Meaning |
|---|---|
| `SR2001` | A rule is broken: an `Enforce` rule, or a quarantine's containment. |
| `SR2002` | A `Migrate` rule has a violation missing from its baseline, or a pair that grew. |
| `SR2090` | No `sliderule.model.json` at or above the project, so no rule was checked. |
| `SR2091` | A model file or a baseline file could not be read. The warning is reported in that file. |
| `SR2092` | A ratcheted rule was not checked, because the build was given no baseline for it. |
| `SR2093` | The analyzer failed in this project, or was given another version's contract and checked no rule. Neither is about your code. |

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
of the property. To promote the six IDs to errors in CI, pass them to `-warnaserror` separated by
commas, as [Rolling out the build analyzer](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#rolling-out-the-build-analyzer)
shows.

A `#pragma warning disable` or a `[SuppressMessage]` silences a warning in the build.
`sliderule check` ignores suppressions and still reports the violation, so a suppression gives up
only the early warning.

## Cost, and the tier

The analyzer runs a full extraction of the project inside the compiler, and that extraction is nearly
all of its cost. These figures come from a large open-source web application. In a command-line
build the compile step of its largest project went from 58.1 s to 85.6 s, and a class library's from
13.9 s to 16.4 s. In the editor, re-analysing one file after an edit took 0.7 ms at the median and
33 ms at the 95th percentile, over the same project's 1,174 authored files. Over its 803 generated
Razor files the median was 15 ms.

Reading the model file adds a fixed cost of about a quarter of a second, even to a project with no
rule to check. The compiler server that `dotnet build` uses by default keeps what it read until the
file changes, so a project compiled after that read skips it. A build with
`-p:UseSharedCompilation=false` runs each project in a compiler process of its own, so every project
pays it.

One property, `SlideRuleTier`, chooses where the analyzer reports:

```xml
<SlideRuleTier>build</SlideRuleTier>
```

- `live`, the default, reports in the file being edited as you type, and in the build.
- `build` reports once per project at the end of its compilation. It is a command-line mode, and
  [What each editor shows](#what-each-editor-shows) covers the editors under it. It costs more,
  because it runs on one thread where `live` analyses several documents at once. The same largest
  project compiled in 104.8 s under `build`.
- `off` turns the analyzer off for a project, or for a configuration.

The value is case-insensitive, and an unknown value counts as `live`. Set it on the command line
(`-p:SlideRuleTier=off`) for an inner-loop build. Leave it unset in CI and in the builds a coding
agent runs, which is where the warning gets read.

## What each editor shows

Under `live`, `SR2001` and `SR2002` squiggle in the file being edited and clear when the edit is
reverted. `SR2090`, `SR2092` and `SR2093` describe the build as a whole. They appear only after a
build, with full-solution analysis on or off, and never as a squiggle.

- Visual Studio analyses the open documents. "Entire solution" under Tools, Options, Text Editor, C#,
  Advanced, Run background code analysis for, shows a closed file's finding too. With ReSharper on,
  the warning appears on mouseover and its squiggle is hidden. To bring the squiggle back, clear "Hide
  only those Visual Studio squiggles that duplicate ReSharper code analysis highlightings" under
  ReSharper, Options, Environment, Editor, Visual Studio Features. The option hides more than its
  name suggests, and JetBrains tracks that as
  [RSRP-492566](https://youtrack.jetbrains.com/issue/RSRP-492566).
- VS Code with C# Dev Kit analyses the open documents. Set
  `dotnet.backgroundAnalysis.analyzerDiagnosticsScope` to `fullSolution` to reach closed files. With
  the JetBrains extension on, each warning is listed twice.
- Rider runs the analyzer with "Enable Roslyn Analyzers" on, which is the default. "Include Roslyn
  Analyzers in Solution-Wide Analysis" reaches closed files.

Under `build`, Visual Studio and VS Code show no warning in the editor while background analysis
covers only the open documents, the default in both. Visual Studio lists the warning once a build
runs, and clears it from the Error List as soon as the file is edited. With background analysis
set to the whole solution (the two settings above), both editors run each project's
end-of-compilation check as a build would. The warnings then squiggle and are listed before any
build, and in Visual Studio they stay listed through an edit. Set `SlideRuleTier` to `off` to keep
the warnings out of either editor.

Rider shows no warning in the editor under `build`, even with Solution-Wide Analysis and its Roslyn
option on. After a build the warning appears in the build output only, with no squiggle and no entry
in the Problems window. Use `live` where Rider users need to see the warning while they edit.

VS Code applies a new `SlideRuleTier` value once the file that sets it is saved in the editor. After
an edit made outside VS Code, run Reload Window from the command palette.

## Requirements

- **A compiler on Roslyn 4.0.1 or later:** the .NET 6 SDK and Visual Studio 2022, or newer. An older
  compiler cannot load the analyzer, so the package removes itself from that build and the project
  compiles as before, with no warning.
- **A committed `sliderule.model.json`,** written by a `sliderule` tool of the same version as
  this package. A file with a newer `schemaVersion` is reported as `SR2091`, and the rules go
  unchecked until the package is updated. One with an older `schemaVersion` is reported the same
  way until `render --model` rewrites it.

A project that references both this package and `SlideRule` needs the two at the same version. Each
package includes its own copy of the contract assembly, and at two versions a build gives both
analyzers the same copy. The analyzer that gets the other version's copy skips its checks and
reports `SR2093` here, or `SR1093` from the contract package, naming both versions.

## Writing the spec

You write the spec against the [`SlideRule`](https://www.nuget.org/packages/SlideRule) contract
package. The [`SlideRule.Cli`](https://www.nuget.org/packages/SlideRule.Cli) global tool checks it,
renders it for coding agents, and writes the model file this package reads. SlideRule's packages
ship one version in lockstep, so keep the analyzer at the tool's version.

## License

[MIT](https://github.com/andypgray/sliderule/blob/main/LICENSE)
