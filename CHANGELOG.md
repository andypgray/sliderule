# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- The MCP server instructions open with one whole sentence, and so does the banner an unbound server puts
  above them, so a client that shows only the first line or retrieves the text by search reads a summary
  rather than a lead-in. The instructions also drop three notes that the tool descriptions already carry:
  that violations are data, that an over-budget document coarsens its own grain, and that a partial or
  filtered model is stamped on the answer. A client puts the instructions into every session that registers
  the server, and they are now a quarter shorter.
- Every JSON document the CLI writes, and every MCP reply built from one, now uses LF line ends on Windows
  as it already did elsewhere, so a run writes the same bytes on every operating system. The hook's report
  on stderr and its `additionalContext` use LF too. A JSON reader sees no difference; a tool comparing raw
  bytes on Windows sees each CRLF become LF.
- The `derive_spec` recipe's step 3 now asks each `candidate:` line for the rules the finished spec keeps,
  or for the evidence behind a `none:` answer. A `none:` reading "not evaluated" leaves the question
  unanswered. The example receipt no longer lists a dropped rule on a candidate line.
- A command line that does not parse now gets System.CommandLine's "Did you mean" suggestions for a mistyped
  command or option, on stderr. Stdout stays empty and the exit code stays 2.

### Fixed

- An MCP tool call that leaves out a required parameter, such as `arch_context` with no `path`, is now
  refused with a message that names the missing parameter and lists the ones the tool takes, and the server
  logs nothing. Before, the call failed with the SDK's own binding message and was logged as an unexpected
  tool failure.
- An MCP tool call that names a tool the server does not have, such as `Arch_Context` for `arch_context`,
  now fails with the protocol's invalid-params error, `Unknown tool: 'Arch_Context'`. Before, the server
  answered with a tool error result carrying the same text and logged the call as an unexpected tool
  failure.
- An `arch_check` or `arch_graph` reply within one or two characters of the client's response budget now
  comes back whole at a coarser grain. Before, the grain was chosen without counting the document's final
  line end, so the reply was then cut at the budget into JSON that would not parse.
- The MCP server, started on a `.slnf` solution filter, now reloads the workspace when the solution the
  filter names changes. Before, adding or removing a project there went unseen until something else
  forced a reload, so `uncheckedProjects` and `unsupportedProjects` could describe the solution as it was.
- A baseline file holding two lines for one violation, as a merge that kept both sides' line does, now
  grandfathers the smaller of their site counts and keeps a reason either line carries, and `baseline`
  writes the pair back as one line. Before, both lines were written back, and which count applied depended
  on which line came first.
- A `git` process that SlideRule starts and that outlives its 30-second limit now ends the call with the
  timeout error, or with the caller's cancellation, even when part of its process tree cannot be killed.
  Before, the error from the partial kill replaced either one.
- Two test classes deriving `ArchRuleTests<TSpec>` over one spec, with different `SolutionPath` or
  `ExcludeProjectName` values, now each check their own solution. Before, every class over that spec got
  the run of whichever class's test ran first, so a test could pass against the wrong solution.
- An error raised outside every command's own handling now exits 2 with SlideRule's error message, or under
  `hook` is reported as the hook's non-blocking error. Before, it printed a stack trace and exited 1, the
  code `check` uses for a red rule.
- A cancelled `check` or `baseline` run now stops before its next rule, and a cancelled CLI run, MCP tool
  call or xUnit adapter run stops extracting. Before, the checker evaluated every remaining rule and
  extraction went on to the last project. A cancelled MCP call also leaves the server's warm session as it
  was. Before, the next call could move the cancelled walk's sites a second time.
- A workspace load that fails before it returns, such as a cancelled load or an unreadable `.slnf` filter,
  now shuts down the out-of-process build host it started. Before, the build host outlived the call.
- The workspace code the xUnit adapter runs in a test process no longer resumes its awaits on a
  synchronization context the test host installed. Before, a host that blocked on the adapter's work under
  such a context could deadlock.
- Extracting a field or property initializer now takes time in proportion to its size. Before, each method
  call, object creation, `?.` call and type name in it, such as a type argument, a cast or a qualified name,
  bound the whole initializer again, so an initializer of 400 rows took 28.8 s where it now takes 0.3 s. The build analyzer, the xUnit adapter and every command and MCP tool
  that reads the code run this extraction, and what it extracts is unchanged.

## [0.2.0] - 2026-10-18

### Added

- `MustTarget(framework)` states a framework floor: every selected project must target the framework,
  among any others it targets. A project that does not is one violation, sited at the declaration of its
  framework list and keyed by the framework, and a project nothing evaluated passes.
- `MustOnlyReferencePackages(pattern, ...)` states a package allow-list: every package a project
  declares must match one of the patterns, private references aside. `MustNotReferencePackages(pattern,
  ...)` bans the packages its patterns match, private references included, so
  `MustNotReferencePackages("*")` forbids every package reference. Patterns are globs matched ignoring
  case, as NuGet compares package identifiers, and each package a rule objects to is one violation at
  its own declaration, keyed by the package. A blank pattern is reported as `SR1032`.
- `IPackageReferenceInfo.Frameworks` names the target frameworks whose build declares a package, so a
  `.Must` predicate can state a law about one framework's packages, such as backports taken for
  `netstandard2.0` alone. `graph` marks a package only some of a project's frameworks declare, as
  `(netstandard2.0 only)`, and `graph --json` and `arch_graph` list those packages in a
  `packageFrameworks` key.
- `MustSetProperty(name, value)` states that every selected project sets an MSBuild property to a value.
  Set means declared in the solution, in the project file, a `Directory.Build.props` above it or a props
  file imported from inside the solution; a value only the SDK defaults does not count. Each target
  framework's build is judged on its own, and each one where the property is unset or holds another value
  is a violation keyed `property:{name}@{framework}`. `IProjectInfo.Properties` lists the properties the
  solution sets for a project, each an `IProjectPropertyInfo` carrying its value, its framework and the
  file and line that set it, for `.Must` predicates. A blank name or
  value is reported as `SR1033`. `baseline add --target` takes the property item, such as
  `Nullable@net8.0`.
- Member selections narrow by declared accessibility at all six levels: `ThatArePublic()`,
  `ThatAreInternal()`, `ThatAreProtected()`, `ThatArePrivate()`, `ThatAreProtectedInternal()` and
  `ThatArePrivateProtected()`, each matching its level exactly and reading as a prefix ("protected fields
  of types"). `MustBeProtected()` states that every selected member is declared `protected`, so
  `.Fields.ThatArePrivate().MustBeProtected()` over a base class's namespace reds each field an inheritor
  cannot reach.
- `IMemberInfo.IsOverride` reports a member declared `override`, and `IMemberInfo.IsSealed` a
  `sealed override`, for `.Where` and `.Must` predicates.
- `MustBeOverridable()` states that a derived type can override every selected member: one declared
  `virtual` or `abstract`, or an `override` not declared `sealed`, passes. `MustBeVirtual` reds an open
  override; this verb does not, so a law that public service methods stay overridable holds over a
  hierarchy that overrides them.
- `.Named(name, ...)` on a member selection keeps the members with exactly those names, every overload
  of a method included, and reads as a prefix ("`Main` methods of types named `Program`"). Where it
  narrows a rule's subject, each name no member carries fails the rule naming it, as a type `Named` list
  does. A blank name is reported as `SR1016`.
- `.Except(memberSelection, ...)` on a member selection removes every member the given member selections
  name, so `arch.Types.Methods.Except(arch.Types.Named("Program").Methods.Named("Main"))` exempts one
  entry point from a naming law without exempting its type. Each exclusion draws from types of its own,
  several remove what any one names, and one of another single kind than the selection, such as fields
  taken from methods, removes nothing and is refused when the spec is loaded.
- `.Under(glob, ...)` narrows a type selection to the types declared under a folder or file of the
  solution, such as `arch.Types.Under("src/Widgets")` or `arch.Types.Under("src/*/Internal")`, for a
  codebase whose folders are its only structure. Globs are relative to the solution directory with `/`
  between segments, `*` within a segment and `**` across any number of them, and case is ignored on every
  operating system. A glob names everything under it, and a type is under it when any file declaring it
  is. Each glob a rule's subject names is checked on its own, as `Named` names are, and a malformed glob
  is reported as `SR1035`.
- Seven calls take a list a spec computed, such as one a helper method built: `arch.AnyOf` over
  selections or over types, `arch.Projects.Named` and `.Matching`, member `.Named` and `.Except`, and
  `MustNotUse` over members. Each selects what the comma-separated form selects given the same operands,
  and reads the list once, when it is called. A `List<Layer>` passes to `arch.AnyOf` and a
  `List<MethodSelection>` to member `.Except` as they are. An empty list is reported when the spec is
  loaded, naming the call and what it would have done, and a null list or a null entry in it throws.
  A model file holding an empty list for any of these is refused.
- The extraction cache starts over once after this upgrade, because each project's evaluation now records
  the properties the solution sets, and each member whether it overrides.
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
- `.WithSuffix`, `.WithPrefix` and `.WithNameMatching` on a member selection now read as a prefix on the
  members: `arch.Types.Methods.WithSuffix("Async")` renders "`*Async` methods of types", where it rendered
  "Methods of types named `*Async`". The old sentence was also what `arch.Types.WithSuffix("Async").Methods`
  renders, which selects every method of the types named `*Async`, so two rules with different verdicts
  read alike. What each selects is unchanged.
- A member subject with `.Returning` or a member `.Where` now puts those clauses on the members and names
  the types after "declared in": `web.Methods.Returning(typeof(Task))` renders "Methods returning `Task`
  declared in types in `MyApp.Web.*`", where it rendered "Methods of types in `MyApp.Web.*` returning
  `Task`". `arch.Types.Fields.Where(p, "that are X")` rendered "Fields of types that are X", the sentence
  `arch.Types.Where(q, "that are X").Fields` also renders for the fields of different types; it now
  renders "Fields that are X declared in types". A member subject without either clause still reads
  "methods of types …", and what each selects is unchanged.
- `MemberSelection`, `MethodSelection`, `FieldSelection`, `PropertySelection` and `ProjectSelection` moved
  from `SlideRule.Fluent` to the `SlideRule` namespace, so a helper method that takes or returns one
  compiles under `using SlideRule;` alone. Every member whose signature names one of them changed with
  it, among them `Selection.Members` and `Selection.Events`, the member adjectives `ThatAreStatic` and
  `AttributedWith`, the member verbs `MustBePublic`, `MustBeInternal`, `MustBePrivate`, `MustBeStatic`,
  `MustBeAbstract`, `MustBeAttributedWith`, `MustNotBeAttributedWith`, `MustHavePrefix`, `MustHaveSuffix`,
  `MustHaveNameMatching`, `MustAcceptParameter`, `MustBeGetOnly` and `MustBeReadonly`, and the project
  vocabulary's `Packable`, `MustLockPackages` and `MustNotBePackable`. Source that calls them compiles
  unchanged, but a spec project built against 0.1.x must be rebuilt: until it is, loading it is refused
  with a message that says it was built against an older package and names the move. A
  `using SlideRule.Fluent;` kept only for these types can go.
- The `derive_spec` recipe's first step now has the agent search where a project records its
  decisions: its issues, pull requests and discussions, release notes and upgrade guides,
  first-party templates and sibling repositories, and API-approval tests. It reads the README,
  CONTRIBUTING, `.editorconfig` and analyzer configuration whole, quotes each statement that becomes
  a rule from the raw file (with `curl` or `gh api`) rather than from a fetch tool's summary, and
  reads docs written for another major version at the tag or commit the code is at.
- Before the `derive_spec` recipe calls a red rule debt, the agent re-reads its statement from raw
  text, dates it against the code, and opens a sample of the rule's sites. A site the code marks as
  deliberate, with a comment, a `#pragma`, a vendored-file banner or a maintainer's pull request,
  becomes an `.Except` on an `Enforce` rule rather than `Migrate` debt. A statement the code or its
  maintainers contest is dropped for you to decide, and its receipt line reads `dropped: <rule>:
  contested: <evidence>`. Dragons prose is checked against the code or its history before it is
  written.
- A project's frameworks, packability, restore lock policy or package references declared in a props file
  it imports from elsewhere in the solution, such as a sibling `build/` directory, are now cited at that
  file's line, as the properties the same file sets already were. They were cited at line 1 of the project
  file. A violation about any of them moves to the new site; baseline keys do not change.

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

[Unreleased]: https://github.com/andypgray/sliderule/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/andypgray/sliderule/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/andypgray/sliderule/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/andypgray/sliderule/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/andypgray/sliderule/releases/tag/v0.1.0
