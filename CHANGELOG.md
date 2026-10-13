# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- A check after an edit, which is what `hook` runs when an agent's turn ends, loads only the
  projects it re-extracts and the projects they reference instead of the whole solution. A project
  that references an edited one, directly or through another, is re-extracted only when the edit
  changed a declaration, even a private one, and an edit inside member bodies, non-constant
  initializers or top-level statements re-extracts only its own project. The cache write-back
  re-stamps only the documents that changed, and the output is byte for byte what a cold run prints.

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
- **Self-hosting.** This repository's own spec governs its code. CI runs every read verb against it,
  and `render` keeps fifteen committed artifacts here current.

[Unreleased]: https://github.com/andypgray/sliderule/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/andypgray/sliderule/releases/tag/v0.1.0
