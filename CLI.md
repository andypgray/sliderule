# Command-line reference

`sliderule` is the command SlideRule installs as a .NET global tool, and this page is its reference. Each command's help appears as the tool prints it, followed by what the help has no room for. The sections after the commands cover exit codes, the JSON documents, grains, SARIF, caching and the environment variables SlideRule reads.

[Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) covers the install and what the machine needs, and [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) covers the rules a spec can state. The help on this page is printed at a width of 100 columns, and a terminal wraps it to its own.

## Commands

Every command that takes a solution finds it the same way. An argument names a solution file, or a directory to search. Without one, a command reads `SLIDERULE_SOLUTION_PATH`, and when that is unset too it walks up from the working directory. The walk stops at the first directory that holds a `sliderule.json` or exactly one solution file. The spec comes from `--spec`, then from the `sliderule.json` that names the solution. Otherwise it is the one project in the solution that references `SlideRule`. The settings file is shown in [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#next-steps).

A token that begins with `-` where a solution goes, or where `explain` takes its rule ID, is refused as `Unrecognized option '<token>'.` Write a path that begins with a dash as `./-name`. A `--` in front of it does not help, because the refusal applies after `--` as well.

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

Without `--diff-base` every tripwire reports skipped, which is not the same as clean. A pull-request check therefore passes the pull request's base ref, and the checkout has to be deep enough for that ref to resolve.

Four options have a section of their own below. `--sarif` is under [SARIF](#sarif), `--grain` under [Grains](#grains), and `--binlog` and `--no-cache` under [Caching](#caching). What `--allow-workspace-diagnostics` accepts is under [Wrong and smaller models](#wrong-and-smaller-models).

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

`explain` prints one rule whole, and `arch_explain` returns the same text over MCP. The rule ID is the one `check` prints. A rule a scope contributes carries the scope's ID in front of its own, such as `<scope-id>/containment`.

With `--type` or `--path` in place of the rule ID, `explain` answers which layers, rules and scopes claim a type, and why. `--type` takes a full name as `check` prints it, or a simple name, which explains every type that has it. An unknown name is refused with the closest full names. `--path` explains every type declared in a file or under a directory. A lone positional argument beside either option is the solution.

Under each claim, indented lines give the facts that admitted the type, such as the glob its namespace matched or the project that declares it. The `excepted by` group lists the rules and scopes whose `Except` removed the type, with the reason the type matched it. A rule that drops the type for any other reason is not listed. A type that nothing claims gets the line `claimed by: no layer, rule or scope`. This form extracts the solution as `check` does, so it uses the [extraction cache](#caching). A project that fails to load is named at the top of the answer, and the command still exits 0. `arch_explain` takes `type` or `path` for the same answer.

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

`render` writes the managed block into the `AGENTS.md` in the solution's directory. It also writes a card into each scope's directory and into the directory of each layer a rule governs. Everything outside a block's markers stays as it was. The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-context-files) page says which agents read these files, and why `render` names a `CLAUDE.md` on stderr when it finds one.

The build analyzer reads the model file `--model` writes. A `sliderule.json` can ask for the model file or the diagram without the option, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#next-steps) shows. Under `--check`, each file a render would write is listed as `current` or `stale`. That includes a card the spec now places where none is committed.

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

A baseline records a ratcheted rule's current violations, so that only new ones fail. The ratcheted rules are the `Migrate` rules and each `Quarantine` scope's containment. A rule's baseline sits in `arch/baselines/<rule-id>.json` under the solution directory, unless the rule names a path of its own. Commit the files `baseline` writes along with the spec.

`baseline` is yours to run. A baseline turns a red rule green without fixing anything, so the MCP server has no tool that writes one, and the [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny it to an agent.

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

`status` lists a baseline section filed under an ID the spec no longer declares, and `check` names the rename that would move it.

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

The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#connecting-the-mcp-server) page covers the client config, how the server binds a solution, and its tools. A solution argument that does not resolve exits 2 before the handshake. Without an argument, the server starts even when it cannot bind a solution.

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
  --diff-base <diff-base>    The git ref the check's scope tripwires read changed files from when
                             no turn record stands: an edit, a hand run, or a stop with no state. A
                             stop with one reads the files the turn changed. Warnings only, never
                             failures. [default: HEAD]
  --max-rounds <max-rounds>  How many consecutive continuations one prompt may get from a red rule
                             or a warning. Past it the hook reports the red rule without blocking,
                             and the turn ends. [default: 3]
  -?, -h, --help             Show help and usage information
```

The [exit-code contract](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#the-exit-code-contract) maps the hook's exit codes onto the ones under [Exit codes](#exit-codes). The [Coding agents](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-turn-end-hook) page shows where the hook fits, and `hooks/` holds the settings to paste.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | the run succeeded, and `check` found no red rule |
| `1` | `check` found a red rule, or `render --check` a stale file |
| `2` | SlideRule's own error: a refusal, a usage error, an incomplete model |

`status` exits 0 over red rules, because a burndown is its answer. A command line that does not parse exits 2 with the parse error on stderr. One that names no command also prints the root help on stderr, which keeps stdout empty for a caller that parses it. A run cancelled with Ctrl+C or a termination signal prints `Cancelled.` and exits 2 when it stops within System.CommandLine's two-second grace. A run that outlives the grace is ended by System.CommandLine, which exits 130 after Ctrl+C and 143 after a termination signal.

## Documents and versions

Every JSON document a command writes carries a `schemaVersion` at its top, except the hook document, whose shape is Claude Code's.

| Document | Written by | `schemaVersion` |
|---|---|---|
| check report | `check --json`, `arch_check` | 3 |
| status burndown | `status --json`, `arch_status` | 2 |
| graph survey | `graph --json`, `arch_graph` | 1 |
| baseline file | `baseline` | 2 |
| model file | `render --model` | 2 |
| hook document | `hook` | unversioned |

A version moves when a key is removed, renamed or given a new meaning, which a reader written for the old shape would misread. A key added for something new does not move it, except in the model file, whose reader refuses a key it does not know. The key is absent wherever there is nothing to say, so a document with nothing new in it is byte for byte what it was. The human-readable reports carry no version and can gain lines in any release. A baseline reader still accepts the version before the current one, and any write upgrades the file.

The hook document is the `hookSpecificOutput` object Claude Code reads from a hook's stdout. `hook` writes it when a clean check warned, with the report as `additionalContext`, and names the event it answers:

| Event | When `hook` writes the document |
|---|---|
| `PostToolUse` | a checked edit came back clean with a warning |
| `Stop` | an agent's turn ended clean with a warning |
| `SubagentStop` | a worker's turn ended clean with a warning |

## Grains

`check --json` and `graph --json` take `--grain`, and `arch_check` and `arch_graph` take `grain`. Each rung keeps less than the one before it, and every rung answers about the same rules and projects. `--rules` and `--projects` narrow the subject instead. A coarsened document carries a `grain` key naming its rung, and a full one carries none.

| Grain | `check --json` drops | `graph --json` drops |
|---|---|---|
| `full` | nothing | nothing |
| `overview` | each violation's sites, keeping their count | each project's namespace inventory |
| `skeleton` | each rule's violations, keeping their count | package references; external references and shared type names, as counts |
| `index` | rule prose; audit advisories and diagnostics, as counts | frameworks, packaging, project references; edges, audit advisories and diagnostics, as counts |

`index` lists every rule ID and every project name, which are what `--rules` and `--projects` take, so it is the menu to narrow from. Over MCP the server steps a document down these rungs until it fits, as [the response budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget) explains.

## SARIF

`check --sarif <path>` writes the check as SARIF 2.1.0, the format GitHub code scanning reads. The file holds one run. Its driver lists every rule in the spec's order, with the generated sentence, the `Because`, the `Fix` followed by each exemplar type's file and line, and the `Citation` as the help link. A rule reports at `error` by default, and a scope's tripwire at `warning`.

Each violation site is one result, at its file and line relative to the solution directory:

| Result | `level` | `baselineState` |
|---|---|---|
| a site of a red rule | `error` | `new`, or `updated` for a grown pair |
| a grandfathered site | `note` | `unchanged` |
| a check warning, such as a tripwire touch | `warning` | `new` |

A grandfathered site also carries an external suppression naming what grandfathered it. Its message repeats that attribution, since code scanning shows the message and ignores the suppression. A violation with no site, such as a rule whose subject matched nothing, adds no result and still fails the run. Workspace diagnostics and an incomplete model are reported as notifications on the run's invocation.

Each result's `partialFingerprints` carries `slideRuleViolationIdentity/v1`, built from the violation's source, target and subject, the file, and the site's ordinal within that file. Code that moves around a site leaves the key alone, so one violation stays one alert across runs. The [SARIF section](https://github.com/andypgray/sliderule/blob/main/SELF-CHECK.md#as-sarif) of SlideRule on itself shows a grandfathered result from a real run.

## Caching

`check`, `explain`, `render`, `status`, `graph` and `baseline add` keep what they extract in a persisted cache, one directory per solution. When nothing has changed, the next run builds the model from the cache without loading the workspace. When some projects have changed, it extracts only those again. A change to the solution's structure, or a cache in an older format, starts over.

| OS | Default cache root |
|---|---|
| Windows | `%LOCALAPPDATA%\SlideRule\cache` |
| Linux | `$XDG_DATA_HOME/SlideRule/cache`, else `~/.local/share/SlideRule/cache` |
| macOS | `~/Library/Application Support/SlideRule/cache` |

Each solution gets a directory under the root, named after the solution file with a short hash of its full path. `SLIDERULE_CACHE_DIR` moves the root. The cache is local derived data, so deleting it costs one cold run.

A `--binlog` capture is kept in the solution's cache directory.

## Wrong and smaller models

A project that fails to load is treated as a wrong model rather than a smaller one. `check`, `render`, `graph`, `status` and every `baseline` subcommand but `rename` exit 2 and say which projects failed, and `--allow-workspace-diagnostics` opts into the partial model. A project whose NuGet packages did not resolve is the same wrong model, reached with no load failure. It loads completely, and only the edges its package references would have produced are missing, so a rule about a package reports itself inert and the run goes green. That covers a restore that ran and failed and one that never ran at all. An SDK-style project writes `obj/project.assets.json` every time it restores, so not having one is a fact about that project, while a non-SDK-style .NET Framework project never writes one and is left alone. The unresolved case gates on the same terms and takes the same opt-out. Its refusal names those projects separately and sends you to `dotnet restore`, because a project that loaded and a project that never did ask for different repairs. The xUnit adapter answers the same way in test dress: one named test fails carrying both causes, every rule case skips, and an `AllowWorkspaceDiagnostics` override opts in.

A solution filter is the opposite case: the model is smaller, and still right. `check`, `status`, `graph` and the `arch_context` tool still answer, stamping which declared projects the filtered run never checked, and the JSON documents carry them as `uncheckedProjects`. A green over a subset can no longer read as a green over the solution. The verbs that read absence as evidence refuse instead. `baseline init`, `baseline accept-reductions` and `render` exit 2, because the files they would write vouch for projects the run never measured, and the fix they name is to run against the solution the filter references. The narrowing is measured from what actually loaded, not from the filter's text. A selection whose references pull in the whole solution narrows nothing and prints nothing. The xUnit adapter keeps every rule verdict and skips `Workspace_LoadedCompletely`, naming what was not checked. Committed baselines, render targets, `arch_context` paths, diff resolution and the reported project paths all resolve against the solution the filter references, never against the filter's own directory. A filter kept apart from its solution answers with the same paths as the solution itself. A rule whose whole selection lives in the unchecked projects reports as skipped, naming the filter, so an empty selection does not read as a failure. The fail-on-empty defaults stand untouched for unfiltered runs.

## Framework host selection

Loading a non-SDK-style project, and replaying a binary log from .NET Framework `MSBuild.exe`, both run the .NET Framework build host that Roslyn ships. That host needs Windows and a Visual Studio or Build Tools install carrying `MSBuild\Current\Bin\MSBuild.exe`, which the tool finds with `vswhere`.

Where several are installed, it prefers Visual Studio 2019 or 2022. Where neither is installed, it takes the newest it found and names it on stderr, beside any load failure. Set `SLIDERULE_VS_INSTALL_PATH` to an install root, the parent of `MSBuild\Current\Bin`, to choose one yourself. A spec project that targets `net48` needs none of this and builds anywhere.

## Environment variables

SlideRule reads the variables below. The ones Claude Code or the operating system sets are read to fit in with the host, and configure nothing of SlideRule's.

| Variable | Set by | What it does |
|---|---|---|
| `SLIDERULE_SOLUTION_PATH` | you | names the solution when no argument does |
| `SLIDERULE_VS_INSTALL_PATH` | you | chooses the [Framework build host](#framework-host-selection)'s install |
| `SLIDERULE_CACHE_DIR` | you | moves the [cache](#caching) root |
| `SLIDERULE_IDLE_TIMEOUT_MINUTES` | you | idle minutes before the MCP server exits; 30 by default, 0 never |
| `SLIDERULE_DISABLE_PARENT_WATCH` | you | `true` keeps the MCP server running after its launcher exits |
| `SLIDERULE_DISABLE_WARM_WORKSPACE` | you | `true` makes the MCP server load the solution on every call |
| `SLIDERULE_LOG_LEVEL` | you | the MCP server's log level, `Warning` by default |
| `SLIDERULE_HOOK_STATE_DIR` | you | where `hook` keeps its per-session state |
| `CLAUDE_CODE_SESSION_ID` | Claude Code | tags the MCP server's log lines |
| `CLAUDE_PROJECT_DIR` | Claude Code | the project directory `hook` checks |
| `MAX_MCP_OUTPUT_TOKENS` | Claude Code | sizes the MCP [reply budget](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#the-response-budget) |
| `CLAUDE_CONFIG_DIR` | Claude Code | locates your own `CLAUDE.md`, which `render` never counts |
| `USERPROFILE` | the operating system | your home directory on Windows |
| `HOME` | the operating system | your home directory elsewhere |

The MCP server writes a daily log to `SlideRule/logs`, beside the default cache root. The launcher that runs the server from a source checkout reads two variables of its own, which [hooks/README.md](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#why-a-source-checkout-needs-the-launcher) documents.
