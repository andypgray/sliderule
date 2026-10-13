# Command-line reference

`sliderule` is the command SlideRule installs as a .NET global tool, and this page is its reference. Each command's help appears as the tool prints it, followed by what the help has no room for. The topics that span several commands come after the commands, from how a command finds its solution to the environment variables SlideRule reads.

The install and what the machine needs are in [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements). Writing rules is covered in [Writing specs](https://github.com/andypgray/sliderule/blob/main/WRITING-SPECS.md), with the whole vocabulary in [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments). Running the check in CI, working a ratchet down and upgrading are in [Operating SlideRule](https://github.com/andypgray/sliderule/blob/main/OPERATING.md). The help on this page is printed at a width of 100 columns, and a terminal wraps it to its own.

## Commands

Every command that takes a solution finds it, and its spec, the same way, as [Solution and spec resolution](#solution-and-spec-resolution) describes. A repository that commits a [settings file](#the-settings-file) can leave both out of every command.

A token that begins with `-` where a solution goes, or where `explain` takes its rule ID, is rejected with `Unrecognized option '<token>'.` Write a path that begins with a dash as `./-name`. A `--` in front of it does not help, because the refusal applies after `--` as well.

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
```

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
  -?, -h, --help                          Show help and usage information
```

Without `--diff-base` every tripwire reports skipped, which is not the same as clean. A pull-request check therefore passes the pull request's base ref, and the checkout has to be deep enough for that ref to resolve. A tripwire touch is one of the [warnings](#warnings) a check can raise, and a warning never fails the run.

Five options have a section of their own below. `--sarif` is under [SARIF](#sarif), `--grain` under [Grains](#grains), and `--binlog` and `--no-cache` under [Caching](#caching). What `--allow-workspace-diagnostics` accepts is under [Wrong and smaller models](#wrong-and-smaller-models).

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

`explain` prints one rule whole, and `arch_explain` returns the same text over MCP. The rule ID is the one `check` prints. A rule that a scope contributes has the scope's ID in front of its own, such as `<scope-id>/containment`.

With `--type` or `--path` in place of the rule ID, `explain` answers which layers, rules and scopes claim a type, and why. `--type` takes a full name as `check` prints it, or a simple name, which explains every type that has it. An unknown name fails with the closest full names. `--path` explains every type declared in a file or under a directory. With either option, a lone positional argument is the solution.

Under each claim, indented lines give the facts that admitted the type, such as the glob its namespace matched or the project that declares it. The `excepted by` group lists the rules and scopes whose `Except` removed the type, with the reason the type matched it. A rule that drops the type for any other reason is not listed. A type that no layer, rule or scope claims gets the line `claimed by: no layer, rule or scope`. A framework or package type the solution references, named in full, gets the line `claimed by: nothing; it is declared outside the solution, and every selection ranges over the types the solution declares`. This form extracts the solution as `check` does, so it uses the [extraction cache](#caching). A project that fails to load is named at the top of the answer, and the command still exits 0. `arch_explain` takes `type` or `path` for the same answer.

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

`render` writes the managed block into the `AGENTS.md` in the solution's directory. It also writes a card into each scope's directory and into the directory of each layer a rule governs. Everything outside a block's markers stays as it was. The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files) page explains which agents read these files, and why `render` names a `CLAUDE.md` on stderr when it finds one.

`render` adds and updates files, and never deletes one. When a spec change moves a rule, the card in the directory the rule left stays on disk until you delete it, and `render --check` does not report it. Under `--check`, each file a render would write is listed as `current` or `stale`, including a card the spec now places where none is committed.

The build analyzer reads the model file `--model` writes. The model file is never written while a project has failed to load. Under `--allow-workspace-diagnostics`, such a render fails whole when `--model` or the settings file asks for the model file. One that would only refresh an existing model file leaves it as it was, with a warning. A [settings file](#the-settings-file) can ask for the model file or the diagram without the option. A `--diagram` on the command line replaces the settings file's whole `diagram` entry, its `only` and `exclude` lists included.

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

A baseline records a ratcheted rule's current violations, so that only new ones fail. The ratcheted rules are the `Migrate` rules and each `Quarantine` scope's containment. A rule's baseline is the file `arch/baselines/<rule-id>.json` under the solution directory, unless the rule names a path of its own with `.Baseline(path)`. Commit the files `baseline` writes along with the spec.

Each entry in a baseline file is one line with a seal of its own, so an edit by hand is caught. A file whose entry no longer matches its seal fails its integrity check. Every command that reads it then exits 2, naming the file and the entry. Restore that line from version control, and resolve a merge conflict in a baseline file by keeping whole entry lines from either side. A baseline shrinks through `baseline accept-reductions` and grows only through `baseline add`, one attributed entry at a time.

`baseline` is yours to run. A baseline turns a red rule green without fixing anything. The MCP server therefore has no tool that writes one, and the [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny the command to an agent.

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
                                 or 'T:' symbol ID.
  --target <target>              The referenced type of the edge to grandfather — a full type name
                                 or 'T:' symbol ID, or a banned member's full name
                                 (System.DateTime.Now) or member symbol ID (P:System.DateTime.Now).
  --subject <subject>            The offending type of the shape violation to grandfather — a full
                                 type name or 'T:' symbol ID.
  --allow-workspace-diagnostics  Write baselines from the partial model even when some projects
                                 fail to load or to restore, instead of refusing the command with
                                 exit 2.
  --no-cache                     Bypass the persisted caches (extraction fragments and the build
                                 capture): always load and extract fresh, and write nothing back.
  -?, -h, --help                 Show help and usage information
```

The reason given with `--because` is kept in the entry, and SARIF repeats it on the alert for that site.

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

`status` lists a baseline section filed under an ID the spec no longer declares, with the rename that would move it. That rename ends `--to <new-id>` for you to fill in, and `check` names it with the failing rule's ID as `--to`.

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

`status` suggests `Enforce` for a `Migrate` rule whose baseline has reached zero. `--json` writes the burndown document `arch_status` returns.

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
  --json                                  Emit the machine-readable JSON survey document instead of
                                          human-readable output.
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
                                          outside the scope. A filter matching no project refuses
                                          the survey (exit 2) and lists the available names.
  -?, -h, --help                          Show help and usage information
```

`graph` needs no spec, so it is the first command to run on a solution that has none. Besides the projects and their references, it lists each type name that two projects, or a project and a package, both declare. `--json` writes the survey `arch_graph` returns, and `--grain` coarsens it by the rungs under [Grains](#grains).

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

The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server) page covers the client config, how the server binds a solution, and its tools. A solution argument that does not resolve exits 2 before the handshake. Without an argument, the server starts even when it cannot bind a solution. Started at a terminal, with no client on its input, `mcp` prints how to configure one and exits 2.

The server keeps the solution loaded between calls, and never reads or writes the persisted cache. It exits after a stretch with no tool call, and when the process that launched it exits. The variables that change either are listed under [Environment variables](#environment-variables).

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

The [exit-code contract](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#the-exit-code-contract) maps the hook's exit codes onto the ones under [Exit codes](#exit-codes). The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-turn-end-hook) page shows where the hook fits, and [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) has the settings to paste.

## Solution and spec resolution

A command finds its solution in this order:

1. The argument, a solution file or a directory to search.
2. `SLIDERULE_SOLUTION_PATH`, which names a solution file, when the argument is left out.
3. A walk up from the working directory, which stops at the first directory with a `sliderule.json` or exactly one solution file.

The walk climbs past a directory with several solution files, and counts a `.slnf` filter only where no `.sln` or `.slnx` is in the same directory. A walk that reaches the root without a match fails. It names the solution files of the first directory it climbed past, or else any solution one level below where it started. A command run from the repository root therefore finds no solution under `src/` without the argument or a settings file there.

The spec comes from `--spec`, a built DLL or a csproj in the solution. Without it, the spec is the one the settings file names, and otherwise the one project in the solution that references `SlideRule`. A solution with no such project fails with `No spec project found`, and one with several fails with `Multiple spec projects found` and lists them.

A solution file named by the argument or by the environment variable wins over a settings file, and a settings file wins over the plain walk. A directory argument is searched the way the working directory is, settings file included. A solution file chosen any of these ways takes its spec and render settings from the nearest `sliderule.json` above it, and only when that file names it.

### The settings file

A `sliderule.json` at the root of a repository names the solution and the spec once. Every command run below it reads it, and so do the MCP server and the hook. A run inside a nested directory with exactly one solution file of its own stops there instead, and uses the settings file only if it names that solution. It is JSON with comments and trailing commas allowed, and its paths are relative to the file.

| Key | What it sets |
|---|---|
| `solution` | the solution file; required |
| `spec` | the spec csproj or built spec DLL; omit for the convention |
| `render.model` | `true` adds `sliderule.model.json` to every render |
| `render.diagram.path` | the file `render` draws into; required in `render.diagram` |
| `render.diagram.only` | project-name globs to draw |
| `render.diagram.exclude` | project-name globs to leave out |

`$schema` is read and ignored, for an editor's schema pointer. Any other key fails the command, naming the file and the key, so a misspelt key never passes for a setting. [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#next-steps) shows a first file.

## Warnings

A warning is something `check` noticed that does not affect the verdict. It never changes a rule's status or the exit code. In the text report, a rule that passed with a warning is headed `warn`. The JSON report lists each warning under its rule with a `kind`, and SARIF reports it as a `warning`-level result.

| Kind | Raised when |
|---|---|
| `inertTarget` | a forbidden target matched no types |
| `deadOperand` | a pattern in a rule's list matched no types |
| `deadExemplar` | a rule's exemplar matched no declared type |
| `orphanedBaseline` | a ratchet with no baseline fails, and a section has an old ID |
| `quarantinedScopeTouched` | a changed file declares a type in a quarantined scope |
| `cautionedScopeTouched` | a changed file declares a type in a cautioned scope |

An `inertTarget` rule can never fire, so either its target is wrong or what it forbids does not exist yet. A `deadOperand` rule was not measured as written, so `status` never suggests promoting it and `baseline init` skips it. A `deadExemplar` leaves the verdict and the counts as they were. An `orphanedBaseline` warning appears when a ratcheted rule fails with no baseline while a section is filed under a rule ID the spec does not declare. Its hint gives the `baseline rename` that would move the section. The two tripwire kinds need `--diff-base`. A quarantine warning asks whether the change had to edit inside the scope at all, and a caution warning asks that the scope's dragons be read first.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | the run succeeded, and `check` found no red rule |
| `1` | `check` found a red rule, or `render --check` a stale file |
| `2` | SlideRule's own error: a refusal, a usage error, an incomplete model |

`status` exits 0 over red rules, because a burndown is its answer. A command line that does not parse exits 2 with the parse error on stderr. One that names no command also prints the root help, and one that names a group without its subcommand, such as `sliderule baseline`, prints that group's help. Both go to stderr, which keeps stdout empty for a caller that parses it. A run cancelled with Ctrl+C or a termination signal prints `Cancelled.` and exits 2 when it stops within System.CommandLine's two-second grace. A run that outlives the grace is ended by System.CommandLine, which exits 130 after Ctrl+C and 143 after a termination signal.

## Documents and versions

Every JSON document a command writes has a `schemaVersion` at its top, except the hook document, whose shape is Claude Code's.

| Document | Written by | `schemaVersion` |
|---|---|---|
| check report | `check --json`, `arch_check` | 3 |
| status burndown | `status --json`, `arch_status` | 2 |
| graph survey | `graph --json`, `arch_graph` | 1 |
| baseline file | `baseline` | 2 |
| model file | `render --model` | 2 |
| hook document | `hook` | unversioned |

A version moves when a key is removed, renamed or given a new meaning, which a reader written for the old shape would misread. A key added for something new does not move it, except in the model file, whose reader rejects a key it does not know. A key appears only when it has something to report, so a document with no new facts is byte for byte what it was. The human-readable reports have no version and can gain lines in any release. A baseline reader still accepts the version before the current one, and any write upgrades the file.

The check report, the status burndown and the graph survey also list the NuGet audit advisories the load raised, under `packageAudit`. Each advisory is one entry, with NuGet's message and the projects that reported it. An advisory is about a package's published record, so it never marks the model incomplete.

The hook document is the `hookSpecificOutput` object Claude Code reads from a hook's stdout. `hook` writes it when a clean check warned, with the report as `additionalContext`, and names the event it answers:

| Event | When `hook` writes the document |
|---|---|
| `PostToolUse` | a checked edit came back clean with a warning |
| `Stop` | an agent's turn ended clean with a warning |
| `SubagentStop` | a worker's turn ended clean with a warning |

## Grains

`check --json` and `graph --json` take `--grain`, and `arch_check` and `arch_graph` take `grain`. Each rung keeps less than the one before it, and every rung answers about the same rules and projects. `--rules` and `--projects` narrow the subject instead. A coarsened document has a `grain` key naming its rung, and a full one has none.

| Grain | `check --json` drops | `graph --json` drops |
|---|---|---|
| `full` | none; the whole report stays, sites included | none; the whole survey stays, namespace inventories included |
| `overview` | each violation's sites, keeping their count | each project's namespace inventory |
| `skeleton` | each rule's violations, keeping their count | package references; external references and shared type names, as counts |
| `index` | rule prose; audit advisories and diagnostics, as counts | frameworks, packaging, project references; edges, audit advisories and diagnostics, as counts |

`index` lists every rule ID and every project name, which are what `--rules` and `--projects` take, so it is the menu to narrow from. Over MCP the server steps a document down these rungs until it fits, as [the response budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget) explains.

## SARIF

`check --sarif <path>` writes the check as SARIF 2.1.0, the format GitHub code scanning reads. The file contains one run. Its driver lists every rule in the spec's order. Each has the generated sentence, the `Because`, the `Fix` followed by each exemplar type's file and line, and the `Citation` as the help link. A rule reports at `error` by default, and a scope's tripwire at `warning`.

Each violation site is one result, at its file and line relative to the solution directory:

| Result | `level` | `baselineState` |
|---|---|---|
| a site of a red rule | `error` | `new`, or `updated` for a grown pair |
| a grandfathered site | `note` | `unchanged` |
| a check warning, such as a tripwire touch | `warning` | `new` |

A grandfathered site also has an external suppression that quotes its baseline entry's reason, or names the baseline file when the entry has none. Its message repeats that attribution, since code scanning shows the message and ignores the suppression. A violation with no site, such as a rule whose subject matched no types, adds no result and still fails the run. Workspace diagnostics and an incomplete model are reported as notifications on the run's invocation.

Each result's `partialFingerprints` includes `slideRuleViolationIdentity/v1`, built from the violation's source, target and subject, the file, and the site's ordinal within that file. Code that moves around a site leaves the key alone, so one violation stays one alert across runs. The [SARIF section](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#as-sarif) of SlideRule on itself shows a grandfathered result from a real run.

## Caching

`check`, `explain`, `render`, `status`, `graph`, `baseline add` and `hook` keep what they extract in a persisted cache, one directory per solution. `baseline init` and `baseline accept-reductions` always extract fresh. When no file has changed, or an edit changed only whitespace or comments, the next run builds the model from the cache without loading the workspace.

After an edit, a run loads only the projects it re-extracts and the projects they reference, through a temporary solution filter. An edit inside member bodies, non-constant initializers or top-level statements re-extracts only its own project. A project that references an edited one, directly or through another, is re-extracted only when the edit changed a declaration, even a private one.

The cache's record of the last whole load provides the spec's resolution and the load results of the projects the filter leaves out. The run loads the whole solution instead when the record lacks the spec's resolution, or a project to re-extract is not declared in the solution. It does the same when the load rejects the filter, or the filtered load finds a restore failure or workspace diagnostic the record lacks, or any failed project. A change to the solution file, a project file, a restore's `project.assets.json`, or a `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` or `global.json` starts over. So does a cache another SlideRule version wrote.

| OS | Default cache root |
|---|---|
| Windows | `%LOCALAPPDATA%\SlideRule\cache` |
| Linux | `$XDG_DATA_HOME/SlideRule/cache`, else `~/.local/share/SlideRule/cache` |
| macOS | `~/Library/Application Support/SlideRule/cache` |

Each solution gets a directory under the root, named after the solution file with a short hash of its full path. `SLIDERULE_CACHE_DIR` moves the root. The cache is local derived data, so deleting it costs one cold run.

A `--binlog` capture is kept in the solution's cache directory. Later `check`, `status`, `graph` and `hook` runs replay the whole capture, even after an edit. A stored capture can go stale, become unreadable or come from another SlideRule version. A run that then needs the workspace prints a `warning:` line on stderr and runs a design-time build instead. For a stale capture the line is `warning: build capture is stale ('<path>' no longer matches the capture); running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.` A `--binlog` file that cannot be replayed fails the run with exit 2 and names the cause.

A run with `--no-cache` bypasses the cache and any stored capture, loads and extracts fresh, and leaves both as they were.

## Wrong and smaller models

A project that fails to load makes the model wrong, because every rule would be judged without that project's code. `check`, `render`, `graph`, `status` and every `baseline` subcommand except `rename` exit 2 and name the projects that failed. `--allow-workspace-diagnostics` opts into the partial model. With or without it, the check and status documents say `modelIncomplete: true` and list `failedProjects`.

A project whose NuGet packages did not resolve is wrong in the same way, though it loads. Only the edges its package references would have produced are missing. Without the gate, a rule about a package would report itself inert and the run would pass. This covers a restore that failed and one that never ran. An SDK-style project writes `obj/project.assets.json` every time it restores, so a missing one is a fact about that project. A non-SDK-style .NET Framework project never writes one and is left alone. These projects take the same opt-out and are listed as `restoreFailedProjects`. Their refusal lists them in a block of their own and sends you to `dotnet restore`. A project that failed to load is sent to `dotnet build` instead.

The xUnit adapter answers both cases the same way: one named test fails with both causes, every rule case skips, and an `AllowWorkspaceDiagnostics` override opts in.

A solution filter makes the model smaller, and still right. `check`, `status`, `graph` and the `arch_context` tool answer, and the JSON documents list the declared projects the filtered run never checked as `uncheckedProjects`. `explain --type` and `explain --path` answer too, and list those projects above the answer, as `check` does above its text report. A green over a subset then cannot pass for a green over the solution. `baseline init`, `baseline accept-reductions` and `render` exit 2 instead, because the files they would write describe projects the run never measured. Run them against the solution the filter references.

The narrowing is measured from what loaded, so a filter whose references pull in the whole solution counts as the whole solution and lists no unchecked projects. Committed baselines, render targets, `arch_context` paths, diff resolution and the reported paths all resolve against the solution the filter references. A filter kept apart from its solution therefore answers with the solution's own paths. A rule whose whole selection lies in the unchecked projects reports as skipped and names the filter. An unfiltered run still fails a rule whose subject matches no types. The xUnit adapter keeps every rule verdict over a filter and skips `Workspace_LoadedCompletely`, naming what was not checked.

Two more stamps describe a model that is smaller and still right. `unsupportedProjects` names the declared projects SlideRule cannot read, such as F#, VB or SQL projects, each with its reason. No rule can be violated in one. A shared project is listed as well, because its code is read only inside the projects that import it. `multiTargetedProjects` names the projects compiled for several frameworks, each with the framework whose members and hierarchy their shared types follow; their references come from every framework.

## Framework host selection

Loading a non-SDK-style project, and replaying a binary log from .NET Framework `MSBuild.exe`, both run the .NET Framework build host that Roslyn ships. That host needs Windows and a Visual Studio or Build Tools install that includes `MSBuild\Current\Bin\MSBuild.exe`, which the tool finds with `vswhere`.

Where several are installed, it prefers Visual Studio 2019 or 2022. Where neither is installed, it takes the newest it found and names it on stderr, next to any load failure. Set `SLIDERULE_VS_INSTALL_PATH` to an install root, the parent of `MSBuild\Current\Bin`, to choose one yourself. A spec project that targets `net48` needs none of this and builds anywhere.

## Environment variables

SlideRule reads the variables below. You configure it through the `SLIDERULE_*` ones, and it reads the ones Claude Code or the operating system sets only to fit in with its host.

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
| `CLAUDE_CODE_SESSION_ID` | Claude Code | tags the MCP server's log lines |
| `CLAUDE_PROJECT_DIR` | Claude Code | the project directory `hook` checks |
| `MAX_MCP_OUTPUT_TOKENS` | Claude Code | sizes the MCP [reply budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget) |
| `CLAUDE_CONFIG_DIR` | Claude Code | locates your own `CLAUDE.md`, which `render` never counts |
| `USERPROFILE` | the operating system | your home directory on Windows |
| `HOME` | the operating system | your home directory elsewhere |

The MCP server writes a daily log to the `logs` directory next to the default cache root, such as `%LOCALAPPDATA%\SlideRule\logs` on Windows. The launcher that runs the server from a source checkout reads two variables of its own, which [hooks/README.md](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#why-a-source-checkout-needs-the-launcher) documents.
