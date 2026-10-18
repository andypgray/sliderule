# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `check --json` and `arch_check` carry a `framework` key on each `MustOnlyTarget` violation, naming
  the framework it reports.
- `baseline add --source <project> --target <package-or-framework>` adds the entry for a project's
  package or target framework. Both ends take the bare name or the identity form, such as
  `project:MyApp.Core` and `package:Serilog`.

### Changed

- A migrated `MustReferenceNoPackages` or `MustOnlyTarget` rule now fails on a package or framework a
  project takes after its baseline was captured. Each package and each framework is a baseline entry of
  its own, keyed from `project:{name}` to `package:{id}` or `framework:{moniker}`, where before one entry
  per project grandfathered every package or framework the project would ever declare.
  `MustOnlyTarget` now reports one violation per framework outside its list, where it reported one per
  project. A baseline file captured before this release keeps working: its one entry per project still
  grandfathers that project's packages or frameworks, `status` counts it as uncounted, and
  `baseline accept-reductions` splits it into one entry per package or framework, each keeping its
  reason. A consumer keyed on SARIF `partialFingerprints` sees each of these rules' alerts re-keyed once;
  GitHub code scanning matches on its own line hash and is unaffected.

## [0.1.2] - 2026-10-15

### Added

- A project row in `graph --json` and `arch_graph` can carry `undeclaredProjectReferences`. It names
  the projects the project compiles against that its declared `projectReferences` do not reach, either
  directly or through a declared project's own references. The key appears only when the declared list
  is short, which means the project file declares a reference the survey could not read. The human
  survey adds the same names to the project's line.

### Changed

- After a stop has checked a linked worktree the turn wrote, `hook` records that worktree at each
  later prompt as well as the session's own tree. A file changed there between turns, by another
  session or a shell, no longer appears in the next turn's tripwire warnings.
- The `derive_spec` recipe's first step now has the agent list the architecture the team already
  wrote down, each statement with its source. To build that list, the agent follows the links the
  README and CONTRIBUTING give to the project's published developer docs. It also writes each rule's
  posture into the spec before your review. The receipt it closes with gains a `statedIntent` key,
  and its `postures` and `leftRed` lines are copied from the last check. New warnings, each at the
  step where its trap is met, cover an SDK pinned below 10, the package references a
  `Directory.Build.props` adds under central package management, MVC action names under the
  async-suffix rule, registrations made in test projects, and the analyzer rollout.

### Fixed

- A project reference written as `$(SolutionDir)\…`, a common spelling in older solutions, now appears in
  the project's declared references: `projectReferences` in `graph` and `arch_graph`, and
  `IProjectInfo.ProjectReferences` in a spec. Each project file is now read with the solution's
  directory defined, as a solution build reads it. Before, such a reference was missing, and a project
  whose references were all spelled this way was reported as declaring none.
- `--binlog` with `--no-cache` no longer checks only part of the solution when the binlog is a build of
  fewer projects than the solution declares. Each project the binlog did not build is now named in a
  warning and in `failedProjects`, and the run refuses as it does for a project that failed to load.
  Without `--no-cache`, such a binlog was already refused when it was saved as the build capture.

## [0.1.1] - 2026-10-14

### Added

- At an agent's stop, `hook` also checks each linked worktree of the project that the turn wrote
  code into with the agent's edit tools, as the transcript the payload names records them. Each of
  those trees is a check of its own, run after the session's tree inside the one hook timeout, and
  a report that covers more than one tree names each. Allow the timeout one check for each tree a
  turn may write.

### Changed

- A check after an edit, which is what `hook` runs when an agent's turn ends, loads only the
  projects it re-extracts and the projects they reference instead of the whole solution. A project
  that references an edited one, directly or through another, is re-extracted only when the edit
  changed a declaration, even a private one, and an edit inside member bodies, non-constant
  initializers or top-level statements re-extracts only its own project. The cache write-back
  re-stamps only the documents that changed, and the output is byte for byte what a cold run prints.

### Fixed

- On Windows, `baseline`, `render` and `check --sarif` no longer fail with "being used by another
  process" when an antivirus scan or another program briefly holds the file they are replacing. The
  write waits up to about three quarters of a second for the hold to end, and still fails at once on
  a read-only file.
- The MCP server instructions, the `arch_graph` description and the `derive_spec` recipe said to
  build before every call. They now say that code is read from source and only the spec project's
  build is read, and the recipe's last step adds `render --model` and the build analyzer.

## [0.1.0] - 2026-10-09

The first release. SlideRule checks a .NET solution against an architecture spec written in C#, and
renders the same rules as context for coding agents.

### Added

- **The spec language** (`SlideRule`). A spec declares layers over projects, namespaces and types,
  then states rules and scopes about them. A rule carries an ID, a posture, a constraint, a reason
  and a fix: `Enforce` fails every violation, and `Migrate` fails only violations its baseline does
  not hold. A scope is either `Quarantine`, which fails a new reference into it, or `Caution`, which
  warns when a change set touches it. Both carry dragons prose that renders onto the scope's
  directory. The constraints cover references, member use, construction, injection, catch and
  throw sites, container registrations, member shape, naming, project properties and package
  references. [GRAMMAR.md](GRAMMAR.md) is the language reference. The package carries a spec
  analyzer that reports a malformed ID, pattern or prose argument as a warning on the literal, with
  IDs `SR1000` to `SR1093`, and completion for the namespaces and types a spec can name.
- **The `sliderule` tool** (`SlideRule.Cli`). `check` gives one pass-or-fail verdict, as text, as
  `--json` or as SARIF 2.1.0 with `--sarif`, and `--diff-base <ref>` fires the scope tripwires for a
  change set. `render` writes the managed `AGENTS.md` block and a rule card in each directory a rule
  or scope places one, plus with `--model` the model file the build analyzer reads; `render --check`
  fails when a committed file would change. `explain` expands a rule ID, or names the layers, rules
  and scopes that claim a type with `--type` or a path with `--path`. `status` reports each
  ratchet's burndown. `baseline` records, shrinks, renames and attributes the grandfathered sites of
  a `Migrate` rule and the existing references into a `Quarantine` scope, and a sealed entry edited
  by hand is rejected. `graph` surveys a solution's projects, namespaces and reference edges without
  a spec. `hook` runs `check` when a coding agent's turn ends and blocks the stop on a red rule.
- **The MCP server** (`sliderule mcp`). Five read-only tools: `arch_check`, `arch_status` and
  `arch_graph` return the CLI's JSON documents byte for byte, `arch_explain` returns the text
  `explain` prints, and `arch_context` returns the cards that cover a path. The `derive_spec` prompt
  walks an agent through writing a first spec. The server keeps the solution loaded across calls and
  checks it against the disk before each answer; it never reads the persisted cache. It is listed in
  the MCP registry as `io.github.andypgray/sliderule`.
- **The xUnit adapter** (`SlideRule.Xunit`). Every rule in the spec runs as an individually named
  test in the consumer's test project, with failure text identical to the CLI's.
- **The build analyzer** (`SlideRule.Analyzers`). It reads the rendered model file and reports a
  broken rule as a warning at the offending code, in the editor and in the build, with IDs `SR2001`
  to `SR2093`.
- **Self-hosting.** This repository's own spec governs its code. CI runs every read command against it,
  and `render` keeps fifteen committed artifacts here current.

[Unreleased]: https://github.com/andypgray/sliderule/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/andypgray/sliderule/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/andypgray/sliderule/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/andypgray/sliderule/releases/tag/v0.1.0
