# Command-line reference

`sliderule` is the command installed by the `SlideRule.Cli` .NET global tool package. This page lists each command's help as the tool prints it, followed by details about its behavior. Commands appear in root-help order, with subcommands after their group. Shared topics follow, from solution and spec resolution to environment variables.

[Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) covers installation and machine requirements. [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md) covers rule authoring, and [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists the full vocabulary. [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md) covers CI, reducing baselines and upgrades. Help blocks use invariant-culture output at 100 columns; a terminal wraps help to its own width.

## Commands

Commands resolve solutions through the argument, `SLIDERULE_SOLUTION_PATH`, a settings file or a walk up the directory tree. Commands that use a spec take `--spec`, the settings file's spec or the convention, in that order. See [Solution and spec resolution](#solution-and-spec-resolution) for the search rules. A committed [settings file](#the-settings-file) lets you omit both paths. `init` uses the argument, environment variable and walk up, without reading settings, and rejects an existing settings file.

A positional token beginning with `-` produces `Unrecognized option '<token>'.` during parsing. This applies to solution paths, `explain` rule IDs and subcommand arguments, including tokens after `--`. The exit code is 2, except for the `hook` verb, which reports its own errors with exit 1. Write a path beginning with a dash as `./-name`.

```text
Description:
  SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for
  coding agents.

Usage:
  sliderule [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  init <solution>               Scaffold the solution's first spec project: write it under arch/,
                                add it to the solution, write the sliderule.json naming both, build
                                it, and run the first check. Refuses before writing anything where
                                a spec project or a sliderule.json already exists.
  check <solution>              Evaluate rules against a target solution; a project that fails to
                                load, or whose NuGet packages did not resolve, fails the run (exit
                                2) unless --allow-workspace-diagnostics is passed.
  explain <rule-id> <solution>  Print a rule's because, fix, citation, exemplar, posture payload,
                                and linked prose; or, with --type or --path, which layers, rules
                                and scopes claim a type, and why.
  render <solution>             Render the managed AGENTS.md block(s) from the spec, and
                                sliderule.model.json where --model is passed or the file exists; a
                                project that fails to load, or whose NuGet packages did not
                                resolve, refuses the command (exit 2) unless
                                --allow-workspace-diagnostics is passed.
  baseline                      Grandfather, shrink, add one attributed exception to, or rename the
                                ratcheted baselines. init, accept-reductions and add refuse (exit
                                2) when a project fails to load or its NuGet packages did not
                                resolve, unless --allow-workspace-diagnostics is passed; rename
                                reads only the spec and the baseline files.
  status <solution>             Report per-rule burndown and promotion suggestions; red rules never
                                fail the run, but a project that fails to load or to restore does
                                (exit 2) unless --allow-workspace-diagnostics is passed.
  graph <solution>              Summarize the codebase: projects, declared vs observed project
                                references, namespace inventory, and grouped external references.
                                Needs no spec; a project that fails to load or to restore refuses
                                the survey (exit 2) unless --allow-workspace-diagnostics is passed.
  mcp <solution>                Run the MCP stdio server bound to a solution and spec.
  hook <solution>               Run as a Claude Code hook: read the event's payload on stdin, check
                                the working tree when an agent's turn ends, and answer in Claude
                                Code's exit codes. 0 lets the stop stand, with any tripwire
                                warnings on stdout as context for the agent; 2 refuses it, with the
                                report on stderr; 1 is SlideRule's own error, reported on stderr
                                without blocking.
  install-hook <solution>       Merge the hook entries and the baseline deny rules into
                                .claude/settings.json at the root of the git work tree, or in the
                                working directory outside one. Every other setting is kept, and so
                                is a group already on the same event and matcher that runs
                                sliderule hook. A run with nothing to add leaves the file
                                untouched.
```

### `init`

```text
Description:
  Scaffold the solution's first spec project: write it under arch/, add it to the solution, write
  the sliderule.json naming both, build it, and run the first check. Refuses before writing
  anything where a spec project or a sliderule.json already exists.

Usage:
  sliderule init [<solution>] [options]

Arguments:
  <solution>  Solution file, or a directory to search, or omitted to walk up from the working
              directory. A sliderule.json is never read: one already in the solution's directory or
              above it, up to the repository root, refuses the run.

Options:
  --name <name>                  The spec project's name, its folder and its namespace. Defaults to
                                 the solution's name plus .Architecture; a namespace segment 'Arch'
                                 refuses (CS0118).
  --framework <framework>        The spec project's target framework. Defaults to the highest .NET
                                 Framework one on an estate that targets only .NET Framework,
                                 otherwise net10.0, or the pinned SDK's netN.0 under a global.json
                                 that pins an SDK below 10.
  --output <dir>                 The folder the spec project's own folder goes in. Defaults to
                                 arch/ beside the solution.
  --from-graph                   Also write a layer per product project and, for each, a rule that
                                 it must not reference the projects it is not connected to either
                                 way, each with a reason saying it was recorded from the graph and
                                 is yours to rewrite. Without it the spec has no rules.
  --allow-workspace-diagnostics  Survey and check the partial model even when some projects fail to
                                 load or to restore, instead of refusing with exit 2.
  -?, -h, --help                 Show help and usage information
```

`init` creates the first spec project from the csproj and class templates embedded in the [`derive_spec` recipe](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#drafting-a-spec-with-derive_spec). For `MyApp.sln`, it writes `arch/MyApp.Architecture/MyApp.Architecture.csproj` and `ArchitectureSpec.cs` in the solution directory. `ArchitectureSpec.Define` is empty unless you pass `--from-graph`. Use `--name` to change the project name and namespace, and `--output` to change the parent folder.

It runs `dotnet sln <solution> add <csproj>`, then writes a [settings file](#the-settings-file) naming the solution and spec project. That file goes at the containing git work-tree root, or in the solution directory outside a work tree. It then builds the spec with `dotnet build` and runs `check` with it, returning the check's exit code. Restore the solution first. The initial survey uses the same loading rules as `graph`; failed loads or restores stop it with exit 2 unless you pass `--allow-workspace-diagnostics`.

The csproj references `SlideRule` at the running tool's informational version before the first `+`. `sliderule --version` prints the full version, which can include a `+<commit>` suffix. The scaffold sets `ManagePackageVersionsCentrally` and `IsPackable` to `false`, and clears an inherited `TargetFramework` before setting `TargetFrameworks`. These settings prevent inherited central versioning, package validation or framework settings from causing NU1008, NU1101 or NETSDK1005. It also sets `GenerateDocumentationFile` to `true` and adds CS1591 to `NoWarn`, so neither an inherited documentation setting (CS1591) nor IDE0005 enforced in the build (EnableGenerateDocumentationFile) fails it. An applicable `.editorconfig`'s `file_header_template` becomes `//` comment lines above the spec class; blank or `unset` values add no header.

Without `--framework`, a solution targeting only .NET Framework gets its highest framework, such as `net48`. Other solutions get `net10.0`, subject to the nearest `global.json` at or above the solution directory. If it pins an SDK below 10, the target is that SDK's `netN.0`, unless `rollForward` is `major` or `latestMajor`. In this case, a pin below SDK 6 exits 2 because the class requires C# 10.

`init` exits 2 before writing any file in these cases:

- The solution is a `.slnf` solution filter.
- A `sliderule.json` exists between the solution directory and the work-tree root, including both endpoints.
- The spec folder already exists, or an ancestor up to the work-tree root contains a `.csproj`, `.vbproj` or `.fsproj` that would compile the class.
- A solution project already references `SlideRule` by package or project reference. The error names that project.
- A namespace segment is `Arch`, which hides the `Arch` type used by `Define` and causes CS0118.

After writing begins, a failed `dotnet sln add` or build prints the command's output on stderr and exits 2. The error includes commands to finish by hand. A second `init` also exits 2 before writing, because the spec folder or settings file from the first attempt remains.

`--from-graph` records `Enforce` rules using `MustNotReference`. A product project must be a solution member declaring at least one type. It must have no package reference starting with `Microsoft.NET.Test.Sdk`, `xunit`, `NUnit`, `MSTest` or `TUnit`. Each product project gets a layer and rule only if another product project is unconnected to it. The rule forbids references to all such projects. Connection means either project transitively reaches the other through declared references, inferred references or observed project edges. References along existing paths therefore break no recorded rule, and the fixed layers do not cover projects added later. A generated comment identifies `sliderule init --from-graph` as the source. Each `Because` reads `Recorded from the project graph on <yyyy-MM-dd>: no reference either way then. Replace this with the reason the boundary exists.` With no unconnected pair, it writes no rules and prints `no rule recorded: every two product projects are connected one way or the other`.

`init` finishes by printing next steps to write rules and rebuild. It also prints `claude mcp add --scope project sliderule -- sliderule mcp <solution>` to register the [MCP server](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server), and `sliderule install-hook` to add the [hook entries](https://github.com/andypgray/sliderule/tree/main/hooks). Run those commands to complete agent setup; `init` performs neither registration.

### `check`

```text
Description:
  Evaluate rules against a target solution; a project that fails to load, or whose NuGet packages
  did not resolve, fails the run (exit 2) unless --allow-workspace-diagnostics is passed.

Usage:
  sliderule check [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                           A built spec DLL, or a csproj that is a member of the
                                          target solution. Omit to use the convention.
  --json                                  Emit the machine-readable JSON document instead of
                                          human-readable output.
  --diff-base <diff-base>                 A git ref; files changed since it are checked against
                                          quarantined and cautioned scopes (the scope tripwire) —
                                          warnings only, never failures.
  --allow-workspace-diagnostics           Check against the partial model even when some projects
                                          fail to load or to restore, instead of failing the run
                                          with exit 2.
  --sarif <sarif>                         Write a SARIF 2.1.0 report to <path> (GitHub code
                                          scanning et al.); human/--json output is unchanged.
  --annotations <path>                    Write GitHub Actions workflow commands to <path>, one
                                          ::error per red violation site and one ::warning per
                                          check warning, for a step to print; grandfathered sites
                                          are left out, and human/--json output is unchanged.
  --annotations-summary <path>            Write a Markdown job summary to <path>: the verdict and a
                                          table of the findings --annotations writes, for a step to
                                          append to $GITHUB_STEP_SUMMARY; human/--json output is
                                          unchanged.
  --rules <rules>                         Check only the rules whose ID matches these globs
                                          (semicolon-separated, '*' allowed and it spans '/'); the
                                          report, the summary counts and the 0/1 verdict then cover
                                          that subset alone. A filter matching no rule refuses the
                                          run (exit 2) and lists the available rule IDs.
  --grain <full|overview|skeleton|index>  How much of the --json report to keep, each rung coarser
                                          than the one before it and never narrower: full (the
                                          default); overview, every rule and violation with each
                                          violation's sites replaced by their count; skeleton,
                                          every rule with its prose, status, baseline and warnings
                                          and its violations replaced by their count; index, a
                                          verdict per rule ID with the prose, the NuGet audit
                                          advisories and the workspace diagnostics elided
                                          (packageAuditCount and workspaceDiagnosticCount say how
                                          many) and every trust stamp kept, which is the menu
                                          --rules globs pick from.
  --no-cache                              Bypass the persisted caches (extraction fragments and the
                                          build capture): always load and extract fresh, and write
                                          nothing back.
  --binlog <binlog>                       A .binlog from a real build of this solution on this
                                          machine. Replays the captured structure instead of a
                                          design-time build; the capture persists, and later runs
                                          replay it automatically while structurally valid.
  --timings                               Write per-phase wall times and the cache verdict to
                                          stderr after the report; stdout and every written report
                                          are unchanged.
  -?, -h, --help                          Show help and usage information
```

Without `--diff-base`, quarantine and caution tripwires are skipped. The reason is `Tripwire: no diff context — run 'sliderule check --diff-base <ref>' to check changed files against this quarantined scope.` Caution tripwires use `cautioned scope` in that message. With a ref, git lists files committed since it plus changes against HEAD, including untracked files. Each tripwire warns once per changed file declaring a type in its scope, and the tripwire rule passes. These [warnings](#warnings), `quarantinedScopeTouched` and `cautionedScopeTouched`, change neither rule status nor exit code. An unresolved ref exits 2 with `--diff-base could not resolve changed files: …`. A pull-request checkout must include enough history to resolve its base ref.

See [SARIF](#sarif) for `--sarif`, [GitHub annotations](#github-annotations) for `--annotations` and `--annotations-summary`, and [Grains](#grains) for `--grain`. [Caching](#caching) covers `--binlog` and `--no-cache`. [Wrong and smaller models](#wrong-and-smaller-models) explains what `--allow-workspace-diagnostics` accepts.

`--timings` writes the run's wall-clock time per phase to stderr after the report, in seconds: `startup` (from process start to the command), `resolve`, `msbuild`, `cache read`, `workspace load`, `spec resolve`, `spec load`, `extraction`, `cache write`, `check` and `report`, then `total`. A phase that did not run is left out, so a cache hit has no `workspace load` line unless `--binlog` named a log to replay. A phase nested in another is not counted in its parent, and the phases sum to at most `total`. The last line gives the [cache](#caching) verdict (`hit`, `partial`, `miss` or `disabled`) and how many of the solution's projects the run extracted, as in `cache: hit, 0 of 13 projects extracted`. Stdout, the `--json` document and every report file are the same with or without the flag. The first run after a build, a tool install or a spec rebuild pays a one-time warm-up spread across the phases, so discard it before comparing runs.

### `explain`

```text
Description:
  Print a rule's because, fix, citation, exemplar, posture payload, and linked prose; or, with
  --type or --path, which layers, rules and scopes claim a type, and why.

Usage:
  sliderule explain [<rule-id> [<solution>]] [options]

Arguments:
  <rule-id>   The rule ID to explain (a post-desugar ID, e.g. legacy/billing/containment). Omit it
              with --type or --path.
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>   A built spec DLL, or a csproj that is a member of the target solution. Omit to
                  use the convention.
  --no-cache      Bypass the persisted caches (extraction fragments and the build capture): always
                  load and extract fresh, and write nothing back.
  --type <name>   Explain a type instead of a rule: which layers, rules and scopes claim it, and
                  why. A full name as check reports it, or a simple name.
  --path <path>   Explain every type a file or directory declares instead of a rule, absolute or
                  relative to the solution.
  -?, -h, --help  Show help and usage information
```

`explain <rule-id>` prints a `<rule-id> (<posture>)` header, then `sentence:`, `because:`, `citation:`, `fix:` and `exemplar:` fields where present. `Migrate` adds `from:`, `policy:` and `baseline:`. Scope rules add applicable `scope:`, `boundary:`, `baseline:`, `dragons:` and `dragons-doc:` fields. Use the post-desugar ID printed by `check`; scopes generate `<scope-id>/containment` or `<scope-id>/tripwire`. The MCP tool `arch_explain` uses the same runner and returns the same text. Its selectors are `ruleId`, `type` and `path`.

Pass exactly one of a rule ID, `--type` or `--path`. Other combinations exit 2 with `Pass exactly one of a rule ID, --type and --path.` With either option, a lone positional argument names the solution. `--type` first matches the full name printed by `check`, preferring a declared type over an external type with that name. Otherwise it matches every declared type with the given simple name. An unknown name exits 2; matching name fragments produce up to 10 suggestions under `Closest full names`. `--path` explains every type declared in a file or directory, using an absolute path or one relative to the solution.

Each answer lists the layers, rules and scopes that claim the type. Indented traces identify namespace globs, declaring projects or container registrations, with an `at <file>:<line>` line for each registration site. `operand of` lists rules naming the type in an operand, such as `MustNotInject(arch.Registered(Lifetime.Scoped))`. Each entry identifies its role as `target operand`, `allow-list operand`, `membership` or `among operand`. `excepted by` lists layers, rules and scopes whose selection admitted the type before their own `Except` removed it, with the matching trace. Other selection filters do not qualify.

A declared type with no claim, including operand claims, gets `claimed by: no layer, rule or scope`. External framework or package types require full names. They are outside layer, scope and rule subjects, but targets and allow-lists can name them under `operand of`, as in `MustNotCatch(typeof(Exception))`. An external type with no operand claim or other trace gets `claimed by: nothing; it is declared outside the solution, where no layer, scope or rule subject reaches, and no rule names it as an operand`.

Type and path queries extract through the [cache](#caching), which `--no-cache` bypasses. Failed loads, failed restores or missing project files produce a caveat naming the affected projects above an otherwise successful answer. That answer still exits 0.

### `render`

```text
Description:
  Render the managed AGENTS.md block(s) from the spec, and sliderule.model.json where --model is
  passed or the file exists; a project that fails to load, or whose NuGet packages did not resolve,
  refuses the command (exit 2) unless --allow-workspace-diagnostics is passed.

Usage:
  sliderule render [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                        A built spec DLL, or a csproj that is a member of the target
                                       solution. Omit to use the convention.
  --allow-workspace-diagnostics        Render from the partial model even when some projects fail
                                       to load or to restore, instead of refusing the command with
                                       exit 2.
  --model                              Write sliderule.model.json, the built model as data, beside
                                       the root AGENTS.md. Needed once: any later render refreshes
                                       a model file that exists.
  --diagram <diagram>                  Also render two Mermaid diagrams into <path>'s managed
                                       block: the codebase graph (projects and their cross-project
                                       references; solid = observed, dotted = declared but
                                       unobserved), then the spec's law (the places it names, what
                                       they must not reference, and the debt it grandfathers). The
                                       graph draws the projects the solution file declares, so a
                                       project a ProjectReference dragged into the build is not
                                       drawn; `graph` lists every loaded project and says which is
                                       which.
  --diagram-only <diagram-only>        Draw only the declared projects matching these name globs
                                       (semicolon-separated, '*' allowed) — for legibility on a
                                       large solution, not to keep a foreign project out. Scopes
                                       the codebase survey fence only, never the law fence. With
                                       --diagram.
  --diagram-exclude <diagram-exclude>  Drop the declared projects matching these name globs
                                       (semicolon-separated, '*' allowed) — for legibility on a
                                       large solution, not to keep a foreign project out. Scopes
                                       the codebase survey fence only, never the law fence. With
                                       --diagram.
  --check                              Write nothing: compare every file this render would write
                                       with the one on disk, name each that differs, and exit 1 if
                                       any does. For CI, in place of a render followed by git diff.
  --no-cache                           Bypass the persisted caches (extraction fragments and the
                                       build capture): always load and extract fresh, and write
                                       nothing back.
  -?, -h, --help                       Show help and usage information
```

`render` updates `AGENTS.md` in the solution directory, plus a card in each scope's directory and in the directory of each layer with rules of its own. Cards placed in the same directory merge into one file. Text outside the managed markers is preserved byte for byte. [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files) explains which agents read these files. If a `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` exists in or above the solution directory and none imports the root `AGENTS.md`, stderr warns about the nearest one. Claude Code reads those files in place of `AGENTS.md` by default.

`render` creates and updates files but never deletes them. Delete obsolete cards after a spec change removes their placement; `render --check` does not report those files. It compares only files the current render would write, printing `current` or `stale` for each. A missing card at a newly required location is stale too. It writes no files and exits 1 if any target is stale.

The build analyzer reads `sliderule.model.json` next to the root `AGENTS.md`. Render writes it when `--model` is passed, the settings file sets `render.model` to `true`, or the file already exists. Failed loads or missing declared project files block model-file writes even with `--allow-workspace-diagnostics`; failed restores alone do not. An explicit model request, through the flag or settings, then exits 2 before writing any files. An automatic refresh leaves the existing model unchanged, warns and continues with the other targets. A [settings file](#the-settings-file) can also request a diagram through `render.diagram`. A command-line `--diagram` replaces that entire entry, including its `only` and `exclude` lists.

### `baseline`

```text
Description:
  Grandfather, shrink, add one attributed exception to, or rename the ratcheted baselines. init,
  accept-reductions and add refuse (exit 2) when a project fails to load or its NuGet packages did
  not resolve, unless --allow-workspace-diagnostics is passed; rename reads only the spec and the
  baseline files.

Usage:
  sliderule baseline [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  init <solution>               Grandfather each uncaptured ratcheted rule's current violations
                                into its baseline. Always extracts fresh, because a rule it cannot
                                see would be recorded as zero debt.
  accept-reductions <solution>  Remove baseline entries whose violations no longer occur and lower
                                recorded site counts; never adds. Always extracts fresh, because a
                                violation it cannot see would be deleted as fixed.
  add <solution>                Grandfather exactly one currently observed violation of a captured
                                rule, with attribution: an edge (--source with --target) or a shape
                                (--subject).
  rename <solution>             Move a renamed rule's baseline section from its old rule ID to its
                                new one, with every entry kept and re-sealed. Reads the spec and
                                the baseline files, never the code.
```

A baseline grandfathers a ratcheted rule's recorded violations. Unrecorded violations fail, as does a recorded source/target pair whose site count exceeds its allowance. Only `Migrate` rules and `Quarantine` containment rules have baselines; `add` and `rename` reject other rules with exit 2. The default path is `arch/baselines/<rule-id>.json` under the solution directory, with each `/` becoming a directory. Quarantine containment uses `arch/baselines/<scope-id>/containment.json`. `.Baseline(path)` overrides this location; relative paths resolve against the solution directory. Commit baselines with the spec. The line-per-entry layout makes removing one paid-off violation a one-line diff.

Baseline files are line-oriented JSON. Each entry's `seal` covers the rule ID and the entry's fields, including count and reason. Reformatting or CRLF line endings preserve validity; editing those fields fails the integrity check. The error reads `Baseline file '<path>' failed its integrity check: the entry '<entry>' under rule '<rule-id>' does not match its seal.` Commands reading that file exit 2. The `hook` verb maps this failure to its own error, exit 1, which Claude Code treats as non-blocking. The recovery hint directs you to restore a hand-edited line from version control. Resolve merge conflicts by keeping whole entry lines from either side; the merged file needs no re-sealing command. Deleting whole entries or sections is accepted, and any violations they grandfathered fail the next check. After capture, shrink a baseline with `baseline accept-reductions`; grow it only with `baseline add`, one attributed entry at a time.

Baselines can turn red rules green without code changes, so `baseline` is yours to run. The MCP server's five tools are read-only and cannot write one. The [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny the command to an agent through `Bash(sliderule baseline*)`, `Bash(*dnx SlideRule.Cli* baseline*)`, `PowerShell(sliderule baseline*)` and `PowerShell(*dnx SlideRule.Cli* baseline*)`.

#### `baseline init`

```text
Description:
  Grandfather each uncaptured ratcheted rule's current violations into its baseline. Always
  extracts fresh, because a rule it cannot see would be recorded as zero debt.

Usage:
  sliderule baseline init [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                  A built spec DLL, or a csproj that is a member of the target
                                 solution. Omit to use the convention.
  --allow-workspace-diagnostics  Write baselines from the partial model even when some projects
                                 fail to load or to restore, instead of refusing the command with
                                 exit 2.
  -?, -h, --help                 Show help and usage information
```

`baseline init` captures only rules without a baseline section. Before capture, it reports each section filed under an ID absent from the spec. Both `init` and `accept-reductions` always extract fresh and take no `--no-cache` option.

#### `baseline accept-reductions`

```text
Description:
  Remove baseline entries whose violations no longer occur and lower recorded site counts; never
  adds. Always extracts fresh, because a violation it cannot see would be deleted as fixed.

Usage:
  sliderule baseline accept-reductions [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                  A built spec DLL, or a csproj that is a member of the target
                                 solution. Omit to use the convention.
  --allow-workspace-diagnostics  Write baselines from the partial model even when some projects
                                 fail to load or to restore, instead of refusing the command with
                                 exit 2.
  -?, -h, --help                 Show help and usage information
```

#### `baseline add`

```text
Description:
  Grandfather exactly one currently observed violation of a captured rule, with attribution: an
  edge (--source with --target) or a shape (--subject).

Usage:
  sliderule baseline add [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                  A built spec DLL, or a csproj that is a member of the target
                                 solution. Omit to use the convention.
  --rule <rule>                  The ratcheted rule ID to add the entry to.
  --because <because>            Why this entry is grandfathered — recorded in the baseline entry
                                 (single line).
  --source <source>              The referencing type of the edge to grandfather — a full type name
                                 or 'T:' symbol ID — or the project a package, framework or
                                 property violation names.
  --target <target>              The referenced type of the edge to grandfather — a full type name
                                 or 'T:' symbol ID, or a banned member's full name
                                 (System.DateTime.Now) or member symbol ID (P:System.DateTime.Now),
                                 or the project's package, target framework, or property with its
                                 framework (Serilog, net48, Nullable@net8.0).
  --subject <subject>            The offending type of the shape violation to grandfather — a full
                                 type name or 'T:' symbol ID.
  --allow-workspace-diagnostics  Write baselines from the partial model even when some projects
                                 fail to load or to restore, instead of refusing the command with
                                 exit 2.
  --no-cache                     Bypass the persisted caches (extraction fragments and the build
                                 capture): always load and extract fresh, and write nothing back.
  -?, -h, --help                 Show help and usage information
```

`--rule` and a non-blank, single-line `--because` are required, although help does not mark them required. Pass exactly one entry form, either `--source` with `--target`, or `--subject`. Missing or conflicting inputs exit 2 before the workspace loads. The rule must already have a baseline section; otherwise the error directs you to run `sliderule baseline init` first. Adding an already recorded violation prints `entry already baselined`, replaces its reason and records its current site count. SARIF repeats the stored reason as `grandfathered: <because>` in both the result message and its external suppression's justification.

#### `baseline rename`

```text
Description:
  Move a renamed rule's baseline section from its old rule ID to its new one, with every entry kept
  and re-sealed. Reads the spec and the baseline files, never the code.

Usage:
  sliderule baseline rename [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>             A built spec DLL, or a csproj that is a member of the target solution.
                            Omit to use the convention.
  --from <from> (REQUIRED)  The rule ID the baseline section is filed under: the rule's old ID,
                            which the spec no longer declares.
  --to <to> (REQUIRED)      The rule's new ID: a ratcheted rule in the spec with no baseline
                            section yet.
  -?, -h, --help            Show help and usage information
```

`baseline rename --from <old> --to <new>` moves a section to the new rule's baseline file. It preserves entry identities, site counts and reasons, then re-seals each entry under the new ID. An emptied source file is deleted. The destination must be a ratcheted rule in the spec without a baseline section, and the old ID must be absent from the spec. `status` lists orphaned sections after its summary, including their old IDs, paths and entry counts. Each line suggests `sliderule baseline rename --from <old-id> --to <new-id>` for you to complete. If a ratcheted rule fails without a captured baseline while an orphan exists, `check` raises `orphanedBaseline`. Its rename hint supplies the failing rule's ID as `--to`.

### `status`

```text
Description:
  Report per-rule burndown and promotion suggestions; red rules never fail the run, but a project
  that fails to load or to restore does (exit 2) unless --allow-workspace-diagnostics is passed.

Usage:
  sliderule status [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>                  A built spec DLL, or a csproj that is a member of the target
                                 solution. Omit to use the convention.
  --json                         Emit the machine-readable JSON burndown document instead of
                                 human-readable output.
  --allow-workspace-diagnostics  Report the burndown from the partial model even when some projects
                                 fail to load or to restore, instead of exiting 2 after rendering
                                 it.
  --no-cache                     Bypass the persisted caches (extraction fragments and the build
                                 capture): always load and extract fresh, and write nothing back.
  --binlog <binlog>              A .binlog from a real build of this solution on this machine.
                                 Replays the captured structure instead of a design-time build; the
                                 capture persists, and later runs replay it automatically while
                                 structurally valid.
  -?, -h, --help                 Show help and usage information
```

`status` reports per-rule burndown and exits 0 even over red rules. Errors exit 2. Failed loads or restores also produce exit 2 after the burndown, unless `--allow-workspace-diagnostics` is passed. A `Migrate` rule gets `0 remaining; promotable to Enforce (baseline is empty)` only after a captured baseline is fully reduced. It must have no grandfathered violations, new violations or entries awaiting `baseline accept-reductions`, and the run must have measured the rule as written. Quarantine containment never gets a promotion suggestion. `status --json` writes the same burndown document returned by `arch_status`.

Under each rule with grandfathered entries left, `status` prints an `oldest entry:` line: the oldest entry's age in days, the author of the commit that recorded it, and that commit's date, as in `oldest entry: 32 days, recorded by Sam Lee on 2026-10-09`. The dates come from the git history of the JSON files under the solution's directory and of each baseline file the spec declares elsewhere in the repository, read along the current branch's first-parent line. An entry keeps its date when `baseline accept-reductions` lowers its count, when `baseline rename` moves it to another file, and when its file moves or is reformatted. It gets a new date when it is removed and later added back, and when a merge brings it to the branch. Entries with no date are counted at the end of the line, as in `, 2 of unknown age`. When no entry has a date, the line gives the reason: `age unknown (no git history)` when git is missing or the solution is not in a repository with a commit, `(shallow clone)` for an entry in the oldest commit a shallow clone fetched, and `(not committed)` for an entry the working tree has and HEAD does not. A CI job that should show the ages needs the full history, `fetch-depth: 0` on `actions/checkout`. The ages never change the exit code, and `check` does not read them.

In `status --json`, each ratcheted rule's `ratchet.entries` lists its remaining entries, the dated ones oldest first. Each has `source` and `target`, or `subject`, then `recordedAt` and `recordedBy`, or `ageUnknown` (`noHistory`, `shallowClone` or `notCommitted`) when it has no date. `ratchet.oldest` has the oldest dated entry's `recordedAt` and `recordedBy`. Both keys are omitted when no entry remains.

### `graph`

```text
Description:
  Summarize the codebase: projects, declared vs observed project references, namespace inventory,
  and grouped external references. Needs no spec; a project that fails to load or to restore
  refuses the survey (exit 2) unless --allow-workspace-diagnostics is passed.

Usage:
  sliderule graph [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --json                                  Same as --format json.
  --format <text|json|mermaid>            What to write: text, the human survey (the default);
                                          json, the machine-readable survey document; or mermaid,
                                          the projects and their references as one Mermaid fence,
                                          as render --diagram draws them, which needs no spec.
  --allow-workspace-diagnostics           Survey the partial model even when some projects fail to
                                          load or to restore, instead of refusing with exit 2.
  --no-cache                              Bypass the persisted caches (extraction fragments and the
                                          build capture): always load and extract fresh, and write
                                          nothing back.
  --binlog <binlog>                       A .binlog from a real build of this solution on this
                                          machine. Replays the captured structure instead of a
                                          design-time build; the capture persists, and later runs
                                          replay it automatically while structurally valid.
  --grain <full|overview|skeleton|index>  How much of the survey to keep, each rung coarser than
                                          the one before it and never narrower: full (the default);
                                          overview, the whole survey with each project's namespace
                                          inventory elided; skeleton, the structural spine, every
                                          project with its frameworks, packaging facts, declared
                                          project references and type count and the observed
                                          project edges, with its package references elided and the
                                          external references, multiply declared types and shadowed
                                          types each reported as a count; index, the project
                                          roster, every project with its solution membership and
                                          type count alone and the observed edges, NuGet audit
                                          advisories and workspace diagnostics each reported as a
                                          count, which is the menu --projects globs pick from.
  --projects <projects>                   Survey only the projects matching these name globs
                                          (semicolon-separated, '*' allowed); references in both
                                          directions are kept, so an edge can name a project
                                          outside the scope. Under --format mermaid the filter
                                          scopes the diagram as render's --diagram-only does: only
                                          projects the solution file declares are drawn, and an
                                          edge only when both its ends are. A filter matching no
                                          project refuses the survey (exit 2) and lists the
                                          available names.
  -?, -h, --help                          Show help and usage information
```

`graph` surveys the codebase without a spec and takes no `--spec` option. It lists project references, types declared by multiple projects, and type names declared both by a project and an external assembly, including packages or frameworks. `--json` and `--format json` produce the document returned by `arch_graph`. Combining `--json` with another format exits 2. Both JSON and text support the detail levels under [Grains](#grains). `--format mermaid` produces the codebase fence from `render --diagram` for the same project globs, without its caption. It draws only solution-declared projects and keeps an edge only when both ends are drawn. A grain coarser than `full` exits 2 because Mermaid has no grain. Inside the fence, `%%` comments name failed projects accepted by `--allow-workspace-diagnostics`, projects left unchecked by a solution filter, and unsupported projects. A clean load adds no comments. A `--projects` filter matching no project exits 2 and lists available names. Failed loads or restores also exit 2 unless allowed; the codebase's contents never fail the survey.

### `mcp`

```text
Description:
  Run the MCP stdio server bound to a solution and spec.

Usage:
  sliderule mcp [<solution>] [options]

Arguments:
  <solution>  Solution file, a directory to search, or omitted to walk up from the working
              directory.

Options:
  --spec <spec>   A built spec DLL, or a csproj that is a member of the target solution. Omit to
                  use the convention.
  -?, -h, --help  Show help and usage information
```

Configure an MCP client to launch this command, as [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server) describes. With stdin attached to a terminal, `mcp` prints `sliderule mcp is an MCP stdio server; it is started by an MCP client, not interactively.` It then prints client-configuration guidance and exits 2. An unresolved solution argument also exits 2 before the server starts or completes a handshake. With no argument, failed discovery starts an unbound server whose tool calls report that discovery failure. An applicable `sliderule.json` that is malformed or names a missing solution still causes exit 2 while the request is built.

The server keeps the solution loaded between calls unless `SLIDERULE_DISABLE_WARM_WORKSPACE` is `true`. Every tool request bypasses the persisted extraction cache, which the server neither reads nor writes. It exits after `SLIDERULE_IDLE_TIMEOUT_MINUTES` without a tool call, 30 minutes by default. On Windows, it also exits when its launching process exits, unless `SLIDERULE_DISABLE_PARENT_WATCH` is `true`. Other systems rely on stdin closing. See [Environment variables](#environment-variables) for these settings.

### `hook`

```text
Description:
  Run as a Claude Code hook: read the event's payload on stdin, check the working tree when an
  agent's turn ends, and answer in Claude Code's exit codes. 0 lets the stop stand, with any
  tripwire warnings on stdout as context for the agent; 2 refuses it, with the report on stderr; 1
  is SlideRule's own error, reported on stderr without blocking.

Usage:
  sliderule hook [<solution>] [options]

Arguments:
  <solution>  Solution file, or a directory to search, relative to the working tree the hook fires
              for. Omit it to use the settings file, or the walk up from that tree.

Options:
  --spec <spec>              A built spec DLL, or a csproj that is a member of the target solution,
                             relative to the working tree the hook fires for. Omit it to use the
                             settings file, or the convention.
  --diff-base <diff-base>    The git ref the check's scope tripwires list changed files from. The
                             hook reads it on an edit, a hand run, or a stop with no snapshot of
                             the tree (no state, or no answer from git). Any other stop passes the
                             check the changed code files it listed itself. Warnings only, never
                             failures. [default: HEAD]
  --max-rounds <max-rounds>  How many consecutive continuations one prompt may get from a red rule
                             or a warning. Past it the hook reports the red rule without blocking,
                             and the turn ends. [default: 3]
  -?, -h, --help             Show help and usage information
```

The hook uses Claude Code's exit codes. Exit 0 permits the stop, exit 2 blocks it with the report on stderr, and exit 1 reports SlideRule's own error without blocking. The [exit-code contract](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#the-exit-code-contract) compares these with the CLI's [Exit codes](#exit-codes). `--diff-base` defaults to `HEAD`; `--max-rounds` defaults to 3 and rejects values below 1. [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-turn-end-hook) explains the hook's place in a session. Use [`install-hook`](#install-hook) to configure it, or follow [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) to edit settings by hand.

### `install-hook`

```text
Description:
  Merge the hook entries and the baseline deny rules into .claude/settings.json at the root of the
  git work tree, or in the working directory outside one. Every other setting is kept, and so is a
  group already on the same event and matcher that runs sliderule hook. A run with nothing to add
  leaves the file untouched.

Usage:
  sliderule install-hook [<solution>] [options]

Arguments:
  <solution>  Solution file, or a directory to search, written into each hook command relative to
              the repository root. Omit it when the hook finds the solution from the root by
              itself, through sliderule.json or the walk up; the run refuses when it would not.

Options:
  --spec <spec>   A built spec DLL, or a csproj that is a member of the target solution, written
                  into each hook command relative to the repository root. Omit it to use the
                  settings file, or the convention.
  -?, -h, --help  Show help and usage information
```

`install-hook` uses the CLI's exit codes and needs no workspace load. It merges the embedded [settings snippet](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) into `.claude/settings.json`, including its baseline deny rules. The snippet runs `sliderule hook` on `UserPromptSubmit` with a 30-second timeout, and on `Stop` and `SubagentStop` with 600-second timeouts. Each entry gives the command and its arguments separately, as `"command": "sliderule"` and `"args": ["hook"]`, so Claude Code starts it without a shell. Claude Code reads a hook's `args` from version 2.1.139 on. Installation targets the git work-tree root containing your working directory, or your working directory outside a work tree. Missing directories and files are created. Existing settings retain their values and order; the merge removes no settings. Invalid JSON or an incorrect object or array shape where the merge must write exits 2, naming the file. A blank file counts as empty.

An existing group counts as installed when its event and matcher match and one command parses as the `sliderule hook` verb itself, whether it is written as a command with `args` or as a single command line. Absent matchers match each other; `install-hook` and `hook --help` commands do not qualify. A qualifying group keeps its timeout and arguments. Deny rules match by exact string and produce no output when already present. The report prints `kept <event>: <command>` or `added <event>: <command>` for hook entries, plus `added permissions.deny: <rule>` for added deny rules. A changed file ends with `wrote <path>`. With no additions, the report ends with `nothing to change in <path>`, preserving the file's bytes and modification time. Paths in these messages are relative to your working directory, so installation from the root reports `.claude/settings.json`. A write uses 2-space indentation, LF line endings and a final newline, replacing the previous formatting.

Any supplied solution or `--spec` path is appended to each new entry's `args`, one element per argument, relative to the work-tree root and with `/` separators. Paths beginning with `-` get a `./` prefix. No shell reads the arguments, so every other path is written unchanged, including one with spaces or shell metacharacters. A path holding `${` fails, because Claude Code replaces a placeholder such as `${CLAUDE_PROJECT_DIR}` inside a hook's arguments. Before writing, installation checks solution discovery from the root with the arguments it would write, through `sliderule.json` or the walk up. It does not validate the spec. If discovery fails, it exits 2 with `Nothing was written to <path>: the hook runs from <root> and would find no solution there.` Omit both paths when discovery already works from the root.

## Solution and spec resolution

A command chooses its solution in this order:

1. A non-blank solution argument. An existing directory starts a search; any other value names a solution file.
2. A non-blank `SLIDERULE_SOLUTION_PATH`, read as a solution file.
3. A search starting at the working directory.

Relative argument and environment paths resolve against the working directory. A missing argument file produces `Solution file not found: <path>`. A missing environment file produces `SLIDERULE_SOLUTION_PATH points to a file that does not exist: <path>`.

A search walks upward and stops at the first directory containing `sliderule.json` or exactly one solution file. It climbs past directories with several solutions. Solution files are `.sln`, `.slnx` and `.slnf`; filters count only where the same directory has no `.sln` or `.slnx`. A directory argument uses this same search, including settings files. An explicit solution file or the environment variable takes precedence over settings; settings take precedence over plain solution discovery.

If the search reaches the filesystem root without a match, it reports the first directory it passed with several solutions. The message begins `Multiple solution files found in '<directory>':` and lists those files. Otherwise it reports `No .sln, .slnf or .slnx file found in '<directory>' or any parent directory.` Any solutions immediately below the starting directory follow under `Solution files one level down:`. That list skips `bin`, `obj`, `.git`, `.vs`, `node_modules` and `packages`, quotes at most 5 entries and counts the rest. Both failures end with `Pass the solution as the argument (<command> <solution>, or in your MCP client config's args), or set SLIDERULE_SOLUTION_PATH to it.`

The search never descends. From a repository root, pass a solution under `src/` explicitly or name it in a root settings file. A solution one level down appears in the failure's suggestions, but is not selected.

The spec comes from `--spec`, then the applying settings file's `spec`, then the convention. `--spec` takes a built DLL or a solution-member `.csproj`. A DLL that a project declared in the solution builds counts as that project, so the check leaves the project out and compares its sources as it does for the `.csproj`. That includes a DLL built in another configuration. Under a binlog replay, the DLL must be under the project's own `bin` directory, or under `artifacts` when the project builds to an artifacts layout. When two declared projects build the same DLL, the error begins `--spec '<value>' is the built output of more than one project in the solution; pass the spec project's csproj instead:` and lists them. A missing DLL produces `--spec '<value>' was not found. Pass a built spec DLL or a solution-member csproj.` An unknown project produces `--spec '<value>' is not a project in the solution.`

The convention selects the single C# project declared in the solution that references `SlideRule.dll`. A project brought in only through `ProjectReference` is not a candidate; a multi-targeted project counts once. On a clean load, zero candidates produces `No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.` The message also counts the C# projects considered. Several candidates produces `Multiple spec projects found; pass --spec to disambiguate:` with one line per candidate. If the load was incomplete or otherwise unclean, the zero-candidate error names that problem instead. A failed load or restore can hide the spec's reference too.

Only `init` builds a spec project. Other commands load its last built output. Without one, the error begins `The spec project '<name>' has no built output` and ends `Build the solution first (dotnet build).`

Every run that resolves a spec project compares its sources with the build it loads. A difference does not stop the run. It prints `warning: the spec project '<name>' differs from its last build ('<file>' has changed since it was built); this run used the rules that build holds. Rebuild it: dotnet build <csproj>` on stderr. The parenthesis can instead report `'<file>' no longer exists`, `'<file>' was added after it was built` or `'<file>' was written after it was built`. Paths are solution-relative.

Under `--json`, the stderr line omits `warning:`. Check and status documents include `staleSpec`, with `project` (the csproj path), `file`, `reason` (`changed`, `missing`, `added` or `newer`) and `comparison` (`checksum` or `fileTime`). These paths use forward slashes. `explain` and the MCP tool `arch_context` also print the sentence on stdout above their answer, without the prefix. The hook treats a stale spec as a warning, returning the line to the agent on a clean stop and retaining it in a red report.

Checksum comparison needs a portable PDB embedded in the DLL or stored next to it with a matching ID. At least one source listed in the PDB must exist locally. SlideRule compares the recorded SHA-1 or SHA-256 checksums, reporting changed or missing sources. A checkout, a touch or an edit reverted to the original bytes does not count. A `.cs` file under the project directory that the build did not compile counts as added only when written after the DLL.

Without a usable portable PDB, or when none of its sources exists locally, comparison uses file times. A source written after the DLL then counts as newer. Generated sources under the project's `bin` and `obj` build-output directories, nested projects' sources, the project file and its imports are excluded. A DLL that no declared project builds names no project and is never compared. `explain <rule-id>` and `baseline rename` load a DLL spec without opening the solution. They compare it when an earlier run's cache records the project that builds it, and in the MCP server once its workspace is loaded. Before then they do not compare it, and neither does `explain --no-cache`.

### The settings file

For a selected solution, SlideRule checks the nearest `sliderule.json` at or above its directory. Its spec and render settings apply only when it names that same solution. If it names another, no settings apply; the search does not continue farther up. This also applies when discovery stops at a nested directory's single solution.

All commands except `init` use settings resolution, including `mcp`, `hook` and `install-hook`; `graph` uses it without needing a spec. `init` never reads settings and rejects an existing file found between the solution directory and the repository root. A malformed settings file fails before loading, with exit 2. An applying file naming a missing solution produces `<file> names a solution that does not exist: <path>. Fix its 'solution', or pass the solution as the argument.`

The file is JSON with comments and trailing commas allowed. Every relative path resolves against the settings file's own directory.

| Key | What it sets |
|---|---|
| `solution` | the solution file; required |
| `spec` | the spec csproj or built spec DLL; omit for the convention |
| `render.model` | `true` adds `sliderule.model.json` to every render |
| `render.diagram.path` | the file `render` draws into; required in `render.diagram` |
| `render.diagram.only` | project-name globs to draw |
| `render.diagram.exclude` | project-name globs to leave out |

`solution` must be a non-blank string. The optional `spec` names a solution-member csproj or built DLL. `render.model` takes `true` or `false`; `true` requests the model file on every render, as `--model` does. `render.diagram` requires `path`; its optional `only` and `exclude` values are arrays of non-blank project-name globs. It supplies the diagram target when no `--diagram` is passed.

A top-level `$schema` is accepted and ignored, for an editor's schema pointer. Any other unknown key fails with `<file>: unknown key '<key>'; <where> takes <known keys>.`, so a misspelt key is reported instead of ignored. [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#next-steps) shows a first settings file.

## Warnings

A check warning never changes its rule's status or the check's exit code. It can accompany a passed or failed rule. The human report uses `FAIL` for failed rules, `skip` for skipped rules, `pass` for passes without warnings and `warn` for passes with warnings. `check --json` lists warnings under each rule with camelCase `kind`, `message` and an optional `hint`. [SARIF](#sarif) uses warning-level results for warnings naming a file, and invocation configuration notifications for warnings about the spec.

| Kind | Raised when |
|---|---|
| `inertTarget` | a forbidden target matched no types |
| `deadOperand` | a list pattern matched no types, or an `Except` removed nothing |
| `deadExemplar` | a rule's exemplar matched no declared type |
| `orphanedBaseline` | a ratchet with no baseline fails, and a section has an old ID |
| `quarantinedScopeTouched` | a changed file declares a type in a quarantined scope |
| `cautionedScopeTouched` | a changed file declares a type in a cautioned scope |

`inertTarget` means a forbidden-set dependency verb's entire target matched no types, so the rule cannot fire. Check the target or confirm that the forbidden code does not exist yet. An absent `typeof` target raises no warning, and `MustOnly` verbs never raise this kind.

`deadOperand` identifies a list pattern that matched no types. It can be a forbidden target among live targets, an allow-list pattern, a membership, a reside verb's glob or project name, or part of `among:`. The rule was not measured as written. Such a rule is never suggested for promotion, and `baseline init` skips it.

`deadOperand` also identifies an `Except` that removes nothing from the selection it is written on, which the sentence still prints. It is raised for an exception that names a specific type, namespace, project, layer, exact name or folder. An exception that describes a kind, such as `arch.Types.ThatAreStatic()`, is raised only when the selection contains a package's type the kind cannot see. The message quotes both: ``This rule is stricter than written: its exception "types named `SqlException`" removes nothing from "types in `System.Data.SqlClient`".`` An exception from a subject or a forbidden target makes the rule stricter than written. One from an allow-list or a membership makes it partly inert, so `baseline accept-reductions` leaves its unmatched entries alone. `arch.Types` contains only the types the solution declares, so to except a package's type, name it with `typeof(...)` or `arch.Namespace(...).Named(...)`. [The grammar](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#41-reference-universe-per-position) gives each position's register.

`deadExemplar` means part of the exemplar matched no solution-declared type, or the exemplar could not be resolved. It affects neither verdict nor counts, and is suppressed on solution-filter runs.

`orphanedBaseline` appears once per undeclared baseline section when a ratcheted rule fails without a captured baseline. Its hint gives the `sliderule baseline rename` command to move that section. When the rule was measured in full, it also suggests `sliderule baseline init` if the rule is new.

The two scope warnings need `check --diff-base`. They identify a changed file declaring a type in the scope. The quarantine message is `Changed file '<file>' is inside quarantined scope '<scope>' — does the task actually require editing dragon territory? Dragons: sliderule explain <rule-id>.` The caution message is `Changed file '<file>' is inside cautioned scope '<scope>' — read the dragons before editing: sliderule explain <rule-id>.`

## Exit codes

| Code | Meaning |
|---|---|
| `0` | the run succeeded, and `check` found no red rule |
| `1` | `check` found a red rule, or `render --check` a stale file |
| `2` | SlideRule's own error: a refusal, a usage error, an incomplete model |

Commands that do not evaluate rules exit 0 on success. `status` exits 0 even over red rules, and 2 when the incomplete-model gate refuses. `init` returns its final check's exit code. Apart from that delegated check, only `check` and `render --check` return 1. Exit 2 also covers parse errors and bugs.

`sliderule hook` uses Claude Code's exit codes instead: 0 proceeds, 2 blocks and 1 reports a non-blocking error. A command line that fails to parse under `hook` is its own non-blocking error, not the CLI's exit 2.

Other parse failures exit 2 and print the error on stderr. Root help follows when no command was named, the command was mistyped or an unknown option appeared at the root. A group without its subcommand, such as `sliderule baseline`, prints that group's help. Errors and accompanying help both use stderr, leaving stdout empty.

Cancellation by Ctrl+C or a termination signal prints `Cancelled.` on stderr and exits 2 if the run stops within System.CommandLine's termination grace. SlideRule uses the default grace of 2 seconds in System.CommandLine 2.0.14. If the run outlives it, System.CommandLine terminates the process with exit 130 after Ctrl+C or 143 after a termination signal.

## Documents and versions

Each SlideRule JSON document below begins with `schemaVersion`, except the hook document, which follows Claude Code's unversioned format.

| Document | Written by | `schemaVersion` |
|---|---|---|
| check report | `check --json`, `arch_check` | 3 |
| status burndown | `status --json`, `arch_status` | 2 |
| graph survey | `graph --json` or `--format json`, `arch_graph` | 1 |
| baseline file | `baseline` | 2 |
| model file | `render --model` | 2 |
| hook document | `hook` | unversioned |

A version changes when the document's shape changes. Added keys and new values of existing enums, such as `caution` or `cautionedScopeTouched`, are additive and retain the version, except in the model file. Optional keys are omitted when null, preserving the earlier bytes for documents that need none of the additions. At grains that include them, the check report writes `violations`, `sites`, `warnings` and `workspaceDiagnostics` even when empty.

The model reader rejects unknown keys, because ignoring a selection constraint could widen the selection. Later renders also refresh an existing model file without `--model`. The baseline reader accepts versions 1 and 2; the next baseline command that writes a version-1 file upgrades it to 2.

Every JSON document uses LF line endings and exactly one trailing LF on every operating system. The same document therefore has the same bytes on Windows and Linux. The hook's own stderr lines, relayed reports and `additionalContext` use LF too.

Check, status and graph documents include the load's NuGet audit advisories under `packageAudit`, with one entry per distinct advisory. Each has `message`, containing NuGet's text, and `projects`, naming the reporting projects; `projects` is omitted when none is known. Audit advisories never mark the model incomplete.

The hook document is one JSON object on the hook's stdout. It contains `hookSpecificOutput`, with `hookEventName` and `additionalContext`, plus an optional `systemMessage` for Claude Code to show the user. When a clean check warned, `hook` writes the report as `additionalContext` and names the event it answers:

| Event | When `hook` writes the document |
|---|---|
| `PostToolUse` | a checked edit came back clean with a warning |
| `Stop` | an agent's turn ended clean with a warning |
| `SubagentStop` | a worker's turn ended clean with a warning |

A hand run uses `PostToolUse`. The hook also writes a document when it discovers that an earlier stop passed unchecked because its check never completed. That notice appears in both `additionalContext` and `systemMessage`; the event is the one that found it, `UserPromptSubmit` or `Stop`.

## Grains

`check --json` and `graph --json` accept `--grain`; `arch_check` and `arch_graph` accept `grain`. Names are case-insensitive, and a blank or missing value means `full`. Each grain retains a subset of the preceding grain's detail while answering about the same rules and projects. Use `--rules` or `--projects` to narrow the subject. Coarsened documents include a `grain` key; full documents omit it.

| Grain | `check --json` drops | `graph --json` drops |
|---|---|---|
| `full` | none; the whole report stays, sites included | none; the whole survey stays, namespace inventories included |
| `overview` | each violation's sites, keeping their count | each project's namespace inventory |
| `skeleton` | each rule's violations, keeping their count | package references; external references and shared type names, as counts |
| `index` | rule prose; audit advisories and diagnostics, as counts | frameworks, packaging, project references; edges, audit advisories and diagnostics, as counts |

Check reports retain `siteCount` and `violationCount` when their arrays disappear. At `index`, omitted prose is `sentence`, `because`, `fix`, `citation`, `exemplar` and each warning's `hint`. `packageAuditCount` and `workspaceDiagnosticCount` replace the corresponding non-empty arrays. Every grain retains the trust stamps and summary counts.

For graph surveys, the shared-type counts at `skeleton` describe multiply declared and shadowed types. At `index`, each project retains `name`, `solutionMember`, `types` and `generated` where present; observed edges become a count. Every rule ID and project name remains available at `index` for the `--rules` and `--projects` filters.

Over MCP, an over-budget `arch_check` or `arch_graph` response is tried at successively coarser grains until a whole document fits. If even `index` exceeds the budget, the response is truncated. See [the response budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget).

## SARIF

`check --sarif <path>` atomically writes a UTF-8 SARIF 2.1.0 log with one run, one invocation and one trailing LF. Human and `--json` output are unchanged.

The driver is named `SlideRule`. It lists every rule in the run in model order, including passes and skips; `--rules` limits that list to the selected rules. Each descriptor uses the generated sentence as `shortDescription` and `Because` as `fullDescription`. Scope tripwires have no sentence, so their short description is omitted. The `help` combines `Fix`, exemplar types with `Exemplar: <type> (<file>:<line>)`, and `See <citation>.` The citation also becomes `helpUri`. A rule's `defaultConfiguration` level is `error`; a scope tripwire's is `warning`.

Each violation site produces one result at its file and start line. Its URI combines the solution directory's path below the git work-tree root with the site's solution-relative path. Outside git, URIs resolve from the solution directory. All use base ID `SRCROOT`; files on another drive use absolute URIs. Fingerprints keep the solution-relative path.

| Result | `level` | `baselineState` |
|---|---|---|
| a site of a red rule | `error` | `new`, or `updated` for a grown pair |
| a grandfathered site | `note` | `unchanged` |
| a check warning that names a file, such as a tripwire touch | `warning` | `new` |

File warnings have no line number. A tripwire names the changed file; an orphaned section names its baseline file. A grandfathered result includes an `external` suppression whose justification is `grandfathered: <because>`, or `grandfathered in <baseline file>` without an entry reason. The message repeats that attribution in parentheses because code scanning displays the message and ignores the suppression. A violation without a site, including an empty subject or rule error, produces no result but still fails the check through its exit code.

The invocation records NuGet audit advisories and workspace diagnostics as warning-level tool-execution notifications. Missing project files, failed loads and failed restores add error-level notifications. Solution-filter narrowing and unsupported projects add warning-level notifications. `executionSuccessful` is false when the incomplete-model gate will exit 2.

`inertTarget`, `deadOperand` and `deadExemplar` name no file. They become warning-level tool-configuration notifications, with their message and associated rule ID, rather than results. A locationless result would cause code scanning to reject the upload. The [GitHub annotations](#github-annotations) and job summary still include these warnings.

Each result has a `partialFingerprints` entry named `slideRuleViolationIdentity/v1`, formatted as `v1|<source>|<target>|<subject>|<path>|<ordinal>`. Violation results use their identity slots and the site's zero-based ordinal among that violation's sites in the same solution-relative file. File warnings use the same format with ordinal 0 and the warning's key, when present, in the target slot. Line numbers are excluded, so moving code around a site leaves its fingerprint unchanged. The [SARIF section](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#as-sarif) of SlideRule on itself shows a grandfathered result from a real run.

## GitHub annotations

`check --annotations <path>` atomically writes GitHub Actions workflow commands, creating the directory as needed. Print the file in a workflow step to emit one annotation per finding. Every red edge violation produces an `::error` at each site; a shape violation uses its first site, matching the human report's placement. Each has `title=<rule-id>`. A violation without a site produces an error without a file.

Every check warning produces an `::warning`, with its named file and no line, or no file when the warning names none. Grandfathered sites are excluded from both annotations and the summary. The `file=` property uses an absolute path with forward slashes. Messages and properties use the same escaping as `@actions/toolkit`.

`check --annotations-summary <path>` writes Markdown for `$GITHUB_STEP_SUMMARY`. It starts with `## SlideRule check`, followed by the human report's summary line and the incomplete-model caveat when that gate fails the run. A `| Level | Rule | Where | Finding |` table lists the same findings as annotations. The table stops before the summary exceeds 900 KiB, including its footer. If findings were omitted, the footer counts them, explains GitHub's size limit and ends `The check report in the job log lists every finding.`

The [GitHub Action](https://github.com/andypgray/sliderule/blob/main/action.yml) appends the summary file to `$GITHUB_STEP_SUMMARY`. It also requests and prints annotations when `annotate` is `true`, the default. Both options are used only when the installed command's `check --help` lists `--annotations-summary`.

## Caching

`check`, `explain`, `render`, `status`, `graph`, `init`, `baseline add` and `hook` read and write a persisted extraction cache. `init` uses it for both its survey and first check; the hook uses it for its check. `baseline init` and `baseline accept-reductions` always extract fresh and have no `--no-cache` option. They use absence as evidence: an unseen rule could be recorded as zero debt, or an unseen violation deleted as fixed. `baseline add` may use cached facts because it records a violation the run observed. `baseline rename` reads only the spec and baseline files, extracts no code and has no `--no-cache` option. When every project is clean, cached fragments are merged without loading the workspace. A run needing a spec also needs a recorded resolution for that spec. `explain <rule-id>` and `baseline rename` read no code, so they replay a recorded resolution, and print the load warnings recorded with it, even when some projects need extraction.

The source digest excludes whitespace, line endings and comments, except the generated-code banner's yes/no verdict. Adding or removing that banner re-extracts the project. Other whitespace or comment edits reuse the facts with adjusted line numbers, provided each old line's code remains on one new line. Splitting it across lines re-extracts the project. After a code edit, a temporary solution filter loads the projects needing extraction and their transitive project references. Changes inside member bodies, non-constant initializers or top-level statements re-extract only their own project. Direct and transitive dependants are re-extracted when the edited project's declaration surface changes; private declarations count too.

A filtered cache load reuses the last whole load's spec resolution and the load results for omitted projects. A whole load is required if the requested spec resolution is absent, a dirty project is not a member the solution builds, or the solution format cannot be expressed in a filter. It is also required if loading rejects the filter or misses a dirty project, finds any failed or missing project, or reports a restore failure or workspace diagnostic absent from the record. Content or existence changes to structural inputs invalidate the whole cache. These inputs include the solution file, a filter's referenced solution, project files and `project.assets.json`. They also include `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` and `global.json` in each project's directory or any parent directory. A previously absent probe file appearing counts. A different SlideRule informational version, including its commit, also invalidates it. Cache read or validation failures fall back to a cold run. The default root is .NET's local application data directory followed by `SlideRule/cache`:

| OS | Default cache root |
|---|---|
| Windows | `%LOCALAPPDATA%\SlideRule\cache` |
| Linux | `$XDG_DATA_HOME/SlideRule/cache`, else `~/.local/share/SlideRule/cache` |
| macOS | `~/Library/Application Support/SlideRule/cache` |

Each solution has one directory under that root. Its name is the solution filename without its extension, a hyphen, and the first 16 hexadecimal characters of its canonical full path's SHA-256. Canonicalization resolves symlinks and folds case where the operating system ignores case. `SLIDERULE_CACHE_DIR` replaces the root; a blank value keeps the default. The cache is disposable local derived data. Deleting it costs one cold extraction, which writes it back.

`check`, `status` and `graph` accept `--binlog`. Unless `--no-cache` is passed, replay copies the log to `capture.binlog` with a `capture.json` manifest in the solution's cache directory. Later `check`, `status`, `graph` and `hook` runs replay a stored capture when they need a workspace. `explain`, `render`, `baseline` and `init` never replay one. Captures are keyed on structure; replay reads source text from disk, so code edits remain visible. A complete extraction-cache hit skips stored replay. A stale, unreadable or other-version capture produces one `warning:` line on stderr when a workspace is needed, then a design-time build. The stale message is `warning: build capture is stale ('<path>' no longer matches the capture); running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.` A stored copy that fails during replay also warns and falls back. An explicit `--binlog` that is missing or cannot replay exits 2, naming the cause and quoting the reader error's first line where available. Storing a capture also exits 2 for a solution filter, a structural file newer than the log, missing coverage of a project the solution builds, or an undeclared project in the log.

`--no-cache` is available on `check`, `explain`, `render`, `status`, `graph` and `baseline add`. It neither reads nor writes extraction fragments, and neither replays nor stores a build capture. The run loads and extracts fresh, leaving both caches unchanged. Combined with `--binlog`, it replays the supplied log without storing it.

## Wrong and smaller models

A missing declared project file, failed project load or unresolved NuGet restore makes the model incomplete. `check`, `render`, `graph`, `status`, `init`, `baseline init`, `baseline accept-reductions` and `baseline add` exit 2 and name the affected projects. `--allow-workspace-diagnostics` opts into the partial model. `baseline rename` reads no code and has no such gate. `status` renders its burndown before exiting 2. When produced, check, status and graph JSON documents include `modelIncomplete: true` regardless of opt-in. Failed loads appear in `failedProjects`; a complete load omits these keys. Graph normally stops before producing its document unless the opt-in is passed. When a `global.json` pins an SDK the .NET host cannot resolve, the run stops before any project loads. The message names the pin and quotes the host, and the opt-in does not apply to it.

A declared project file missing from disk, perhaps in an uninitialised submodule, appears under `missingProjects` in those documents. Its refusal block comes before failed loads and restore failures, with the remedy `No restore or build creates a project file: initialise the submodule or check out the tree that holds it, generate it, or remove its entry from the solution`. The same opt-in applies.

A project with unresolved packages can load completely while missing the edges its package references would produce. The check's refusal explains that a package rule would then be measured against a model that never saw the package. This covers restores that failed or never ran. Restore failures include non-audit errors in the assets file's `logs` and, for SDK-style projects, an absent assets file. SDK-style restores write `project.assets.json`, normally under `obj/`. Non-SDK-style .NET Framework projects are not marked failed for that file's absence. NuGet audit advisories (`NU19xx`) never count as restore failures, even when promoted to errors. These projects appear under `restoreFailedProjects`, take the same opt-in and have a separate refusal block directing you to `dotnet restore`. Failed loads direct you to `dotnet build`.

In the xUnit adapter, an incomplete model fails `Workspace_LoadedCompletely` with all causes named, and every rule case skips. Override `AllowWorkspaceDiagnostics` to `true` to obtain rule verdicts. The completeness test then skips, still naming the affected projects.

A solution filter narrows the model without making it incomplete or triggering that gate. `check`, `status`, `graph` and `arch_context` answer over the subset. Check, status and graph documents list the declared projects left unchecked as `uncheckedProjects`. `explain --type` and `explain --path` print that list above their answer, as `check` does above its text report. `baseline init`, `baseline accept-reductions` and `render` exit 2 before writing when projects remain unchecked, because their lasting files would describe unmeasured code. Run them against the referenced solution. `baseline add` can proceed.

Unchecked projects are determined by what the load checked, so a filter whose references pull in the whole solution lists none. Baselines, render targets, `arch_context` paths, diff resolution and reported paths resolve against the referenced solution's directory, even when the filter is elsewhere. In a narrowed run, a rule whose subject matches no types is skipped with a reason naming the filter and the missing measurement. A subject anchored on a project SlideRule never surveys still fails; an unfiltered run also fails an empty subject. The xUnit adapter retains the filtered run's rule verdicts and skips `Workspace_LoadedCompletely`, naming unchecked projects.

`unsupportedProjects` lists projects SlideRule cannot survey, each with its reason. These include other languages such as F#, VB and SQL, shared projects, C# projects excluded by the solution's default build configuration, and Web Site entries. Such projects contribute no violations. Shared code is read within importing projects. A configuration-excluded project stays outside the model even when referenced by a built project; replay keeps it if the recorded build compiled it. A Web Site has no project file and is listed by its solution name. A rule anchored on an unsupported project, such as `arch.Project("Legacy")` for a VB project, selects no types and fails. Its hint identifies the unsurveyed project instead of suggesting a spelling correction. A forbidden target anchored only on such a project gets the same hint with its inert warning.

The check document's `multiTargetedProjects` lists projects compiled for several frameworks. A shared type's members and hierarchy follow the first framework that declares it; references are combined from every framework.

## Framework host selection

A design-time load of a non-SDK-style project uses Roslyn's .NET Framework build host, which requires Windows. It needs a Visual Studio or Build Tools installation containing `MSBuild\Current\Bin\MSBuild.exe`. A `--binlog` replay opens no `MSBuildWorkspace` and starts no build host, including when the log came from .NET Framework `MSBuild.exe`.

On Windows, SlideRule discovers installations through `vswhere` with `-all -prerelease -products *`. It chooses the newest installed Visual Studio 2019, 2022 or 2026 (majors 16, 17 and 18). If none is installed, it chooses the newest other version and describes it as outside the tested VS 2019/2022/2026 envelope. The selected installation supplies the Framework host's MSBuild. SlideRule's in-process engine normally uses the .NET SDK's MSBuild, falling back to the selected installation's path if SDK registration fails. Workspace load failures include `MSBuild for this run: ...` on stderr. Set `SLIDERULE_VS_INSTALL_PATH` to the installation root containing `MSBuild\Current\Bin` to choose it explicitly. A nonexistent root or missing `MSBuild.exe` fails with a message naming the variable and the probed path.

## Environment variables

SlideRule reads the variables below. The `SLIDERULE_*` variables configure SlideRule; the others belong to Claude Code or the operating system. Blank, negative or unparsable idle-timeout values keep the default, as do hook-timeout values that are not a positive whole number. The disable flags accept `true` in any case; parent watching runs only on Windows. The file-log level accepts Serilog or Microsoft names, with unrecognised values using the default. The hook state directory replaces the temporary directory. Home and Claude configuration paths locate user `CLAUDE.md` files, which are excluded from the instruction files `render` checks for an import of its block.

| Variable | Set by | What it does |
|---|---|---|
| `SLIDERULE_SOLUTION_PATH` | you | names the solution when no argument does |
| `SLIDERULE_VS_INSTALL_PATH` | you | chooses the [Framework build host](#framework-host-selection)'s install |
| `SLIDERULE_CACHE_DIR` | you | moves the [cache](#caching) root |
| `SLIDERULE_IDLE_TIMEOUT_MINUTES` | you | idle minutes before the MCP server exits; 30 by default, 0 never |
| `SLIDERULE_DISABLE_PARENT_WATCH` | you | `true` keeps the MCP server running after its launcher exits; Windows only |
| `SLIDERULE_DISABLE_WARM_WORKSPACE` | you | `true` makes the MCP server load the solution on every call |
| `SLIDERULE_LOG_LEVEL` | you | the MCP server's log level, `Warning` by default |
| `SLIDERULE_HOOK_STATE_DIR` | you | where `hook` keeps its per-session state |
| `SLIDERULE_HOOK_TIMEOUT` | you | the seconds your client gives `hook` before killing it; 600 by default |
| `SLIDERULE_DISABLE_WARM_HOOK` | you | `true` makes `hook` check in its own process, never through a live MCP server |
| `CLAUDE_CODE_SESSION_ID` | Claude Code | tags the MCP server's log lines |
| `CLAUDE_PROJECT_DIR` | Claude Code | the project directory `hook` checks |
| `MAX_MCP_OUTPUT_TOKENS` | Claude Code | sizes the MCP [reply budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget) |
| `CLAUDE_CONFIG_DIR` | Claude Code | locates your own `CLAUDE.md`, which `render` never counts |
| `USERPROFILE` | the operating system | your home directory on Windows |
| `HOME` | the operating system | your home directory elsewhere |

The MCP server writes daily rolling `sliderule-mcp-<date>.log` files in `logs` next to the default cache root (`%LOCALAPPDATA%\SlideRule\logs` on Windows). It retains the 7 newest files; `SLIDERULE_CACHE_DIR` does not move them. The source-checkout launcher, `hooks/mcp-launch.sh`, reads `SLIDERULE_MCP_BUILD_OUTPUT` and `SLIDERULE_MCP_RUN_ROOT`; the product itself does not. [hooks/README.md](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#why-a-source-checkout-needs-the-launcher) documents both.
