# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `status` prints an `oldest entry:` line under each rule with grandfathered entries left, giving the oldest entry's
  age in days, the author of the commit that recorded it and that commit's date, read from the git history of the
  solution's baseline files. An entry keeps its date through `accept-reductions`, `baseline rename`, a moved file and
  a reformat. A shallow clone, a solution outside git and an uncommitted entry read as unknown, and the exit code
  does not change. `status --json` and `arch_status` list every remaining entry under `ratchet.entries` with
  `recordedAt` and `recordedBy`, or `ageUnknown`, and the oldest dated one under `ratchet.oldest`. The baseline
  format and `check` are unchanged.
- `render` prints a warning on stderr when Codex would cut the files it writes short. By default Codex reads
  32 KiB of the `AGENTS.md` files from the repository root down to the directory it was started in, cuts the
  file that crosses the limit and skips the rest. The warning names the root `AGENTS.md` when it is over the
  limit on its own, and otherwise the card whose total with the files above it is largest, and gives the
  size and the fixes.
- `render` prints a warning on stderr for each `AGENTS.override.md` next to a file it writes. Codex reads
  that file in place of `AGENTS.md` in its directory, even when it is empty, so its sessions never see the
  managed block or the card there.
- `sliderule install-hook` merges the hook entries and the `baseline` deny rules from
  `hooks/settings.snippet.json` into `.claude/settings.json` at the root of the git work tree. Other settings
  stay as they are, a group on the same event and matcher that already runs `sliderule hook` is left alone,
  and a run that finds every entry present does not write the file. Each entry gives `sliderule` and its
  arguments separately, so Claude Code starts the hook without a shell, and a solution or `--spec` passed to the
  installer is added to those arguments. When the hook would find no solution from that root, it exits 2 before writing
  the file. `init` now ends by naming this command in place of a link to the snippet.
- `check` warns, as `deadOperand`, when an `Except` that names a specific type, namespace, project, layer, exact
  name or folder removes nothing from the selection it is written on. An exception that describes a kind (a shape,
  a name pattern, a predicate) warns only when that selection holds a package's type the kind cannot see. The
  message quotes the exception and that selection. An exception from a subject or a forbidden target makes the
  rule stricter than written, so `baseline init` skips it. One from an allow-list or a membership makes the rule
  partly inert, so a type the sentence forbids no longer passes unremarked. Each name, glob and union part of a
  live exception is read on its own, and the check covers type, member and project selections and exemplars. A
  package's type is excepted by `typeof(...)` or `arch.Namespace(...).Named(...)`, since `arch.Types` holds only
  the types the solution declares, and the hint says so.
- `sliderule hook` sends a stop's check to a live `sliderule mcp` server when one is running for the same
  solution and spec, from the same build, with the same environment and `global.json`. The server reads every
  file the turn changed before it checks, and the report matches a check in the hook's own process byte for
  byte. Otherwise the hook checks in its own process, as before. Both processes first remove the shell's own
  variables and list each `PATH` directory once. A hook entry `install-hook` writes starts without a shell, so
  it matches a server the client starts directly. On Windows, an entry written as one command line runs through
  Git's bash, and no directly started server serves its stops. On this repository, whose server
  starts through Git's `sh`, a stop after a one-line edit took about a fifth as long through the server as without it. `SLIDERULE_HOOK_TIMEOUT` gives the hook your
  client's timeout where it is not Claude Code's 600 seconds, and `SLIDERULE_DISABLE_WARM_HOOK` keeps every stop
  in the hook's own process.
- `check --timings` writes the wall-clock time of each phase of the run to stderr after the report, from
  resolution and the MSBuild registration through the cache read, the workspace load, spec resolution and
  loading, extraction and the cache write to the check and the report, then the total and the time the process
  took to start. Its last line gives the cache verdict and how many of the solution's projects the run extracted.
  Stdout, the `--json` document and every report file are the same with or without it.
- `.Carrying(...)`, `MustCarry(...)` and `MustNotCarry(...)` read an attribute declared on a type or inherited
  from a base type, as reflection's `GetCustomAttributes(inherit: true)` and ASP.NET Core read it. An attribute
  on a base counts when the attribute class's own `[AttributeUsage]` leaves `Inherited` true, the default, and
  bases from packages and the framework count too. "Management controllers carry `[Authorize]`" is one rule
  where it took two: `MustDeriveFrom` the base and the base `MustBeAttributedWith` the attribute.
  `AttributedWith` and `Must[Not]BeAttributedWith` still read the type's own declaration only. Each form takes
  a `typeof`, a string and a generic argument. The build analyzer leaves the three to `check`, because a base
  another project declares reaches a build as that project's assembly, which does not carry every attribute
  its source declares. The root `AGENTS.md` block adds a `carry =` line to its glossary when a rule uses them.
  A 0.3.0 build analyzer refuses a `sliderule.model.json` that holds one.

### Changed

- The CLI and the MCP server run on the newest .NET runtime installed, 10 or later, where they ran on .NET 10
  whenever it was installed. Where no `global.json` pins an SDK, the MSBuild they load solutions with in process is
  therefore the newest SDK's. Without `--framework`, `init` gives the spec project the newest `netN.0` the product
  projects target, at least `net10.0` and at most the runtime the tool runs on, with the platform suffix the
  projects share at its highest version (`net11.0-windows`), so a spec on a .NET 11 estate can reference its
  projects, and sets `EnableWindowsTargeting` on a Windows framework so the spec builds off Windows. The
  `derive_spec` recipe gives the same framework rule.
- A project whose C# version or source generators the tool's C# compiler cannot read now refuses the check as a
  failed restore does: `check` exits 2 and names it under `compilerLagProjects`, and the xUnit adapter's
  `Workspace_LoadedCompletely` fails. `--allow-workspace-diagnostics` overrides it. Such a project used to lose that
  code from the model with nothing reported. The extraction cache's format changes, so the first run after the
  update extracts every project again.
- The xUnit adapter's `Workspace_LoadedCompletely` failure, and its skip under `AllowWorkspaceDiagnostics`, list
  the messages the load reported beneath the projects, under `The workspace load reported:`. These are the lines
  `check` prints as warnings: why a project failed to load, the NuGet error a restore recorded, the C# version a
  project asked for, the source generator the compiler could not load. NuGet audit advisories are left out. The
  test report used to name only the projects.
- `explain <rule-id>` and `baseline rename` open no workspace when the cache records the spec's resolution, even
  after a source edit leaves some projects to re-extract. They read only the spec, and the record holds only what
  determines which project builds it. A convention or `.csproj` spec used to load the edited projects first.
- `explain <rule-id>` and `baseline rename` with a spec passed as a built DLL print the load warnings the cache
  recorded on stderr, as they do for a `.csproj` or convention spec.
- A run that replays a saved build capture reads the binlog once. The capture records each C# project's
  references to projects in other languages, which the replay used to read from the binlog a second time. As
  after any upgrade, a capture saved by an earlier version is not replayed: each run falls back to a
  design-time build until a run with `--binlog` captures the build again.
- A replayed `--binlog` or saved build capture lists `compilerLagProjects` as a design-time build does.
  A project's C# version is the one its recorded compiler call passed, an alias is
  measured against the compiler that call ran, and source generators are loaded as on a design-time build.
  The capture records each compiler call and stamps the compiler, so a capture saved by an earlier version is
  not replayed, and the first check after the update extracts every project again.
- The root `AGENTS.md` block lists each rule as its ID, its sentence and its citation. A rule's `Because`
  prints on the layer and scope cards, in `explain`, in the MCP server's `arch_explain` and in a failed
  rule's `check` output. The root block loads into every agent session, and the reasons were most of its
  size. This repository's root file falls from 37,999 bytes to 20,104, below the 32 KiB Codex reads by
  default.
  A Migrate rule keeps its whole counter-prior paragraph and a quarantined scope keeps its sanctioned
  surface. The cards are unchanged.
- The `derive_spec` recipe's list of errors answers three more that its spec project can meet. NU1101 or a
  401 NU1301 on SlideRule means no source the repository configures serves the package. The fix is a
  `nuget.config` beside the spec csproj that adds nuget.org, mapping SlideRule to it only where the
  repository already maps its sources, and the agent tells the human that the spec restores from nuget.org.
  NU1015 from versionless references that a `Directory.Build.targets` adds survives a `Remove` in the spec
  csproj, because targets import after the project body, so the recipe sets
  `<ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>` there. An `IDE` code-style error on a
  spec file, not only IDE0073, gets the fix its message names, such as an expression-bodied `Define` for
  IDE0022.
- The `derive_spec` recipe's `attributes` question names `MustCarry`, `MustNotCarry` and `.Carrying` for an
  attribute a base passes down, in place of the two-rule spelling it taught.
- The root `AGENTS.md` block's `registered =` glossary line now also appears when the `Registered` noun stands
  only in a layer's definition, which the block prints.

### Fixed

- A spec project targeting `net11.0` loads and checks on a machine that also holds the .NET 10 runtime, where it
  failed with "Could not load file or assembly 'System.Runtime, Version=11.0.0.0'" and a remedy that did not apply.
  A spec built for a newer .NET than the runtime the tool runs on is refused before its types load, with a message
  naming both versions and the runtime to install.
- The CLI, the MCP server and the xUnit adapter now read C# 15 code and run the .NET 11 SDK's source
  generators. They carry Microsoft.CodeAnalysis 5.12.0, up from 5.9.0, which read C# 15 as C# 14 and could not
  load a generator built against a newer compiler. A union was left out of the model, and on a machine whose
  newest SDK is 11 a Razor project's compiled views were missing, with nothing reported. A union is now a struct
  that references each of its case types, as the build analyzer reports it.
- C# 14 extension members now reach the model. An access to an extension property, to a method declared in an
  `extension(…)` block or to an extension indexer references the static class that declares it, and the
  property and method accesses are also member uses, so a `MustNotReference` or `MustNotUse` rule now catches
  them, where it used to pass. `s.M()` and `Ext.M(s)` are one use of one member. The code inside an extension
  member is that class's code, and the class's members now include its block members, an instance method with
  its receiver as the first parameter as a classic extension method has. A spec over code that declares
  extension blocks can turn red where it was green.
- A registration wrapper declared in an `extension(IServiceCollection services)` block is now followed, so the
  registrations made through it are recorded at each call, whether the call is `services.AddX<T>()` or
  `Ext.AddX<T>(services)`. A wrapper in a block with type parameters is still not followed.
- `arch.Member(typeof(Ext), "Name")` now names an extension property, so `MustNotUse` can ban one, and a call to
  its accessor written as `Ext.get_Name(s)` is the same use as `s.Name`. Validation used to refuse the anchor.
  `arch.Member(typeof(Ext), "get_Name")` is refused instead, and `arch.Member(() => Ext.get_Name(s))` anchors the
  property. The compiler refuses an extension property in an expression anchor (CS9296).
- `--spec` given the DLL a declared project builds now checks the solution as `--spec` given that project's
  `.csproj` does. The spec project is left out of the check and its sources are compared with its build, on a
  cold run, a cache hit and in the MCP server. The DLL form used to check the spec project's own code against
  its rules and never reported a stale spec. A DLL counts as a project's when it has the project's assembly name
  and sits under the project's `bin` or `artifacts` output root, or under its project directory when its output
  has neither. A DLL that two declared projects build is refused with both projects named.
- `explain <rule-id>` and `baseline rename` given the DLL a declared project builds now report a stale spec,
  still without opening the solution. They report it once an earlier run's cache records which project builds
  the DLL, and `arch_explain` reports it once the server has loaded its workspace. They used to report none for a
  DLL spec.
- Every command and MCP tool that loads the solution now refuses when a `global.json` pins an SDK the .NET
  host cannot resolve. The refusal names the pinned version and the file, quotes the host, and gives the
  ways out: the repository's own instruction when the pin supplies one, installing that SDK or
  updating the pin, and putting a dotnet root that has it first on `PATH`. The tools used to answer
  `An exception of type System.InvalidOperationException was thrown: Error while calling hostfxr function
  hostfxr_resolve_sdk2. Error code: -2147450725 Detailed error: ...`, and the CLI printed a stack trace. On
  Linux and macOS, a command or the MCP server started from a directory under such a pin now refuses the
  same way before MSBuild registers, where it printed a stack trace and the server stopped at startup.
- On Linux and macOS, the MSBuild note printed next to a load failure now reads
  `MSBuild for this run: MSBuildLocator default (the .NET SDK's MSBuild).` It said vswhere had found no
  Visual Studio and suggested `SLIDERULE_VS_INSTALL_PATH`, but vswhere runs only on Windows and the variable
  works only there.
- A run that reads the extraction cache (`check`, `graph`, `status` and the rest), and a running MCP server,
  now detect an edit to a Visual Basic project
  that a C# project references. They used to keep the edges and the load warning from before the edit, and a
  `.vb` file added to such a project went unseen. Any change to that project's files now costs one full run.
- `--binlog` now replays a build in which a C# project references a Visual Basic project. The replay used to
  stop with `The given key '0' was not present in the dictionary.`
- A source file under a `bin` or `obj` directory with no project file beside it, such as `Sub/bin/X.cs`, now
  counts as a source, as the .NET SDK compiles it. The caches used to miss an add or an edit there, and the
  stale-spec warning missed an edit to one in the spec project. A nested project's own `bin` and `obj` still
  count as build output.
- The spec project that `init` writes and the `derive_spec` recipe gives now builds in a repository that
  enforces IDE0005 in the build. It turned documentation off to avoid CS1591, and the build fails a project
  that enforces IDE0005 without documentation, with `EnableGenerateDocumentationFile`. It now sets
  `GenerateDocumentationFile` to `true` and adds CS1591 to `NoWarn`, so IDE0005 also runs on the spec files.
- A registration wrapper whose body registers a type constructed over its own type parameter, such as
  `services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<TOptions>, ...<TOptions>>())`,
  is now recorded at each call, in the calling project, and `explain --type` names the call. It used to be
  recorded once, at the wrapper's body. Such a wrapper with no calls now registers nothing, and a call that
  passes an array for the type argument the service is built over registers nothing either, so `Registered()`
  can hold fewer types than before. When only the implementation is built over that argument, the call
  registers the service with the implementation unnamed.
- `status` and `baseline` now give each role its own cure when a rule's dead patterns hold more than one
  role, such as a dead membership beside an exception that removes nothing. Both named every pattern and
  then cured them all in the first one's words. `status` said `until each dead operand matches or is
  removed`, though the exception has to remove something, and `baseline` said only what the membership was
  meant to hold. A rule whose dead patterns all hold one role reads as before, except that `baseline`
  counts a pattern the rule writes twice once, as the names before it do: `what it was meant to spare`, where
  it said `what they were meant to spare` after naming one exception.

## [0.3.0] - 2026-11-04

### Added

- `sliderule init` scaffolds a solution's first spec project. It writes the spec project the `derive_spec`
  recipe publishes under `arch/`, with an empty `ArchitectureSpec`, adds it to the solution, writes a
  `sliderule.json` naming both, builds the spec project and runs the first check. It picks the target
  framework from the product projects and any `global.json` pin, follows the repository's
  `file_header_template`, and refuses before writing anything where the solution already has a spec project
  or a `sliderule.json`. `--from-graph` also writes a layer per product project and, for each, a rule that it
  must not reference the projects it is not connected to, each with a reason that says it was recorded from
  the project graph. [The `init` reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#init) has
  the details, and path 1 of
  [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#path-1-rules-you-write)
  now starts with it.
- `SlideRule.Guidance.DotNet`, a new package: Microsoft's .NET guidance as compiler warnings, with no spec,
  model file or tool. Reference it and the next build reports. Each rule has its own ID, `SR3001` to
  `SR3008`, links to the Microsoft page it rests on, and takes a severity in `.editorconfig`. `SR3007`
  (`async/accept-cancellation`) ships at Info, which an editor shows and a build does not count. The two async
  rules report a signature where it is declared and leave alone overrides and interface implementations, so
  a missing token or suffix is reported once. Both leave alone xUnit, NUnit and MSTest test methods, and the
  async-suffix rule also leaves alone the public methods of controllers and SignalR hubs. A project whose own
  `sliderule.model.json` declares one of these rules keeps it, and the package does not report that rule.
  The package is one analyzer assembly with no dependencies, so it needs neither the contract nor the build
  analyzer at any version.
- `MustBeRecords()` requires every type in a selection to be declared as a record, a record class or a record
  struct, and `.ThatAreRecords()` narrows a selection to the records. A record rule no longer needs a
  `Must(t => t.IsRecord, ...)` predicate, so it is written to the model file and the build analyzer reports it.
  `.ThatAreRecords()` composes with `OfKind`: "record structs in `MyApp.*`".
- `.ThatAreSealed()`, `.ThatAreStatic()` and `.ThatAreAbstract()` narrow a type selection by its declared
  shape, reading the same facts as `MustBeSealed`, `MustBeStatic` and `MustBeAbstract`. A rule about concrete
  classes leaves the other two shapes out with
  `.OfKind(TypeKind.Class).Except(arch.Types.ThatAreAbstract(), arch.Types.ThatAreStatic())`, which reads
  "classes, except abstract types or static types" and, unlike a `Where` predicate, reaches the build.
- `.ThatAreOverrides()` and `.ThatImplementInterfaces()` narrow a member selection to the members whose
  signature another declaration owns: those declared `override`, and those that implement a member of an
  interface their type implements, a package's interface included. As payloads of a member `.Except`, they
  let a parameter or naming rule report the declaration that owns a signature rather than every member
  that follows it: "methods of types in `MyApp.*`, except overriding methods of types or
  interface-implementing methods of types, must accept a parameter of type `CancellationToken`".
- `IMemberInfo.IsInterfaceImplementation` reports a member that implements an interface member, for
  `.Where` and `.Must` predicates.
- A GitHub Action, `andypgray/sliderule`, for a pull-request job: it installs the tool at the version you
  name, runs `check` against the pull request's base branch, annotates each finding on its line and lists
  them all in the job summary, uploads the SARIF file to code scanning even when the check fails, and runs
  `render --check`. The annotations and the summary need no upload. The upload skips a private repository
  unless `upload-sarif` is `true`, for one with GitHub Code Security, and `annotate: false` leaves the
  annotations to code scanning. Your job checks out the full history and builds the solution first. The
  action is versioned by this repository's release tags, and the
  [operations guide](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#ci) shows the job.
- `check --annotations <path>` writes GitHub Actions workflow commands, an `::error` at each line a red
  violation is reported on and a `::warning` for each check warning, which a step prints to annotate the
  code without a code scanning upload. Grandfathered sites are left out, and a violation with no site is an
  error with no file. `check --annotations-summary <path>` writes the same findings as a Markdown job
  summary under the report's closing line, cut short of GitHub's 1 MiB limit with a count of what it left
  out.
- A spec edited and not rebuilt is reported. Every command that loads the spec compares the spec project's
  sources with the build it runs, and when they differ prints a `warning:` line naming the project and the
  file, with the command that rebuilds it. The check and status documents, and so `arch_check` and
  `arch_status`, carry the same fact as `staleSpec`, and `explain`, `arch_explain` and `arch_context` print
  the sentence above their answer. Where the build has a portable PDB, the comparison is of
  the checksums it records for each source, so a checkout, a touch or an edit made and undone does not count;
  without one, a source written after the build does. The turn-end hook hands the warning to the agent on a
  clean stop, so it can rebuild, and keeps it in the report on a red one. A spec passed to `--spec` as a DLL
  names no project and is not compared.
- `graph --format mermaid` draws the projects and their references as one Mermaid fence, the drawing
  `render --diagram` commits, with no spec. It draws the projects the solution file declares, and with
  `--projects` it keeps an edge only when both of its ends are drawn. When the load left projects out,
  `%%` comment lines inside the fence say which and why. `--format text` and `--format json` are the
  existing outputs, and `--json` stays as the short form of the second. `arch_graph` takes the same choice
  as `format`, `json` by default or `mermaid`; a fence over the response budget is an error that names
  `projects`, because a cut would leave it unclosed.
- A stop whose check never answered, because Claude Code killed the hook at its timeout, is reported. `hook`
  marks each stop's check before it starts and clears the mark once it has answered. The next prompt, or the
  main agent's next stop, finds a mark left behind and tells the agent and the user that the stop passed
  unchecked. Before, such a stop passed and neither of them heard about it.

### Changed

- The SlideRule assemblies are strong-named, all with one key, so their identity now includes a public key
  token, `f1ab2c1d070c9fc6`. That includes the three assemblies a test project takes from `SlideRule.Xunit`:
  the adapter itself and the `SlideRule.Extraction` and `SlideRule.Roslyn` assemblies it carries. A signed
  .NET Framework spec project no longer reports CS8002 on its reference to `SlideRule`, and builds under
  `TreatWarningsAsErrors` with no opt-out, so the `derive_spec` and `init` scaffold no longer suppresses it.
  A spec built against an earlier, unsigned release still loads and checks.
- `arch.Registered`, `MustBeRegistered` and `MustNotInject` see registrations made through a generic
  extension method the solution declares, such as `services.AddUnique<IFoo, Foo>()`, at each place it is
  called, with that call's types and its lifetime: a `ServiceLifetime` argument, or the parameter's default.
  This holds when the extension is declared in one project and called from another, when it calls
  another such method, and in any namespace, `Microsoft.Extensions.DependencyInjection` included.
  A `ServiceDescriptor` handed to the collection
  (`services.Add(ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), ServiceLifetime.Scoped))`,
  `TryAddEnumerable(ServiceDescriptor.Singleton<IFoo, Foo>())`, `new ServiceDescriptor(...)`) is a
  registration too. A codebase that registers this way gets more registered types, so a captive-dependency
  rule has more subjects and can report violations it did not before. The rendered glossary line names
  the new reach.
- The same holds for such an extension method on a builder that exposes the collection as a property, such
  as `builder.AddNotificationHandler<TNotification, THandler>()` over a builder whose `Services` property is
  the `IServiceCollection`. A descriptor kept in a local variable that is never reassigned and then handed to
  the collection counts, and so does a subclass of `ServiceDescriptor` declared in the same project whose
  constructor passes its parameters straight to the base constructor. Entries a builder adds to collections
  of its own, rather than to the service collection, are still not seen.
- `explain --type` and `arch_explain` give the file and line of each container registration that puts a type
  in an `arch.Registered` selection, below the line naming its role and lifetime. A registration whose type
  is inferred from its argument, such as `services.AddSingleton(instance)` in a test project, is listed like
  any other, so a type that is unexpectedly a singleton can be traced to the call that made it one. Where a
  rule names the type in one of its operands, such as the scoped end of a captive-dependency ban, the answer
  lists that rule under `operand of` with the operand's role, framework types included.
- The `derive_spec` recipe's attributes question says that only attributes declared on a type count. For an
  attribute a shared base class declares, it writes two rules: the types derive from the base, and the base
  has the attribute. It also warns that a concrete intermediate base class is a subject like the types below
  it. [GRAMMAR §5.2](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#52-adjectives-reduced-relative-clauses)
  states the same for `AttributedWith` and `MustBeAttributedWith`.
- Visual Studio 2026 is in the tested range with 2019 and 2022, so a machine whose only Visual Studio is 2026
  no longer reports its MSBuild as outside the tested envelope. On a machine with both 2022 and 2026, projects
  are now loaded with 2026; set `SLIDERULE_VS_INSTALL_PATH` to an install root to choose another.
- The stamp `check` and `status` print over projects outside the model opens "2 projects were not surveyed:",
  as does the SARIF notification, and `graph` heads the list "Projects this survey does not cover:". Both
  used to say the solution declares these projects, which a run from a binary log can contradict: a project
  the build compiled in another language is listed whether the solution names it or not. The `status` stamp
  no longer says these projects never contribute violations, because one the default configuration skips
  contributes once that configuration builds it.

### Fixed

- `check --sarif` writes each file's URI relative to the git repository's root, where GitHub code scanning
  resolves a relative path, rather than relative to the solution's directory. For a solution below the root,
  code scanning put every alert on a path missing the solution's directory. Outside a git repository the URIs
  stay relative to the solution's directory. The fingerprints keep the solution-relative path, so an alert for
  a solution at the repository root is unchanged.
- `check --sarif` no longer writes a result without a location. GitHub code scanning rejects the whole upload
  over one such result, so a spec with a single inert rule, a pattern that matched nothing or an exemplar that
  matched nothing got no alerts at all, red violations included. Those warnings are about the spec and name no
  file, so they are now configuration notifications on the run's invocation, each naming its rule. Code
  scanning raises no alert for them. The annotations and the job summary still list them.
- The `derive_spec` recipe's spec csproj now clears an inherited singular `<TargetFramework>`. A
  `Directory.Build.props` that set one decided the spec project's build while its own `<TargetFrameworks>`
  list decided the restore, and the first build failed with NETSDK1005.
- A project the solution's default build configuration does not build, mapped with no `Build.0` line in a
  `.sln` or with `Project="false"` in a `.slnx`, no longer refuses `check`, `graph` and the other commands as
  a project whose packages did not resolve. A restore of the solution skips such a project, so the advice to
  restore could not end the refusal. The project is now left out of the model, as the build leaves it out
  even for a project that references it, and `unsupportedProjects` lists it with the reason. A binary log
  describes what its build compiled: a project such a build skipped is listed the same way, and one it
  compiled, as a `Release` build can where the configurations differ, is in the model as one of the
  solution's projects. A spec project left out this way is refused with the configuration as the reason:
  `--spec` naming its csproj says the configuration does not build it, and a run with no `--spec` lists the
  projects the load left out below `No spec project found`.
- A run from a binary log lists the shared projects and the `.sqlproj`, `.vcxproj` and `.dcproj` entries
  the solution declares in `unsupportedProjects`, as a load does. Before, it listed only the projects whose
  compiler calls it set aside as not C#.
- A Web Site entry in a `.sln`, whose path is a folder or a URL, is listed in `unsupportedProjects` by the
  name the solution gives it. Before, it appeared only as a workspace diagnostic saying that no project file
  was found, at a path made by joining the URL onto the solution directory.
- A solution with no spec project that declares a shared project, a `.sqlproj` or a `.dcproj` now gets the
  `No spec project found: no solution project references SlideRule.dll` refusal, with the projects the load
  left out listed below it. Before, the workspace's failure to open such an entry counted as a load problem,
  and the refusal said to restore and build the solution, which could not end it. The workspace's line for the
  entry stays in `workspaceDiagnostics`.
- A C# project's `ProjectReference` to a `.vcxproj`, `.sqlproj`, `.esproj` or other project in another
  language that the solution does not declare no longer refuses a run with no spec project as a load that did
  not load cleanly. The referenced project is listed in `unsupportedProjects` as not a C# project, and counts
  in the "projects were not surveyed" line. Before, the workspace's failure to open it was the only trace of
  it, and the refusal said to restore and build the solution, which could not end it.
- A project the solution declares whose file is not on disk, such as one in a git submodule that was never
  initialised, is refused as a missing project file rather than as a project that failed to load. The refusal
  says to initialise the submodule or check out the tree, generate the file, or remove the entry from the
  solution. Before, it said to restore and build the solution, which no restore or build could end. The check,
  status and graph documents list such a project as `missingProjects`, beside `failedProjects`, and SARIF
  reports it in a notification of its own. A cached run notices when the file appears.
- A rule whose subject or target is a project the run does not survey, such as an SDK-style VB project, a
  shared project or one the default configuration does not build, gets a hint that names the project and says
  the run does not survey it. Before, the hint said a project is named by its csproj file name, which sent the
  reader after a typo that was not there. The rule still fails, under a solution filter too, because no run
  ever surveys such a project.
- A run from a binary log lists a `.vcxproj`, `.sqlproj`, `.esproj` or other project in another language that a
  C# project references and the solution does not declare, as a load does, read off the build's own
  evaluation of the C# project in the log. Before, `unsupportedProjects` and the "projects were not surveyed"
  line read one shorter on a replay than on a load of the same tree.
- A `.slnx` build rule that spells the platform `AnyCPU` now applies at `Any CPU`, and one that spells it
  `Any CPU` applies where the solution declares `AnyCPU`, as MSBuild reads them. Before, a project such a rule
  leaves out of the default build was surveyed as one the build compiles.
- The spec project in the `derive_spec` recipe sets `<IsPackable>false</IsPackable>`, so it restores in a
  repository whose `Directory.Build.props` turns on package validation for every project. Without the
  setting, restore looks for the spec project's own package at the baseline version and fails with NU1101.
  The recipe's list of errors now includes NU1101 and its fix.
- The `derive_spec` recipe's spec project builds under more of the settings a repository's
  `Directory.Build.props` applies to every project. It names its framework in `<TargetFrameworks>`, so a
  framework list set above it no longer fails the build with NETSDK1005. It sets
  `<GenerateDocumentationFile>false</GenerateDocumentationFile>`, so generated documentation does not report
  CS1591 on the spec class, which under `TreatWarningsAsErrors` was a failed build. The
  recipe now asks for the spec files in the repository's own style, its header included, and its list of
  errors adds IDE0073, NU1100 under package source mapping and NU1004 under locked restore, each with its fix.
- On Linux, a reference from a C# project into an SDK-style Visual Basic project is checked, as it is on
  Windows. Before, the MSBuild workspace on Linux loaded the VB project without its `/define:` symbols, so its
  `My` template failed to compile, the C# compilation never received its assembly, and a rule about
  references into the VB project's types could not report a violation there. Where a C# project's reference
  to a project in another language still does not reach its compilation, such as a VB project with a compile
  error, the run prints a `warning:` line naming both project files.

## [0.2.1] - 2026-10-26

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
- A load now writes one warning for each C# project that declares a `Reference` the build could not
  resolve, such as a `HintPath` DLL missing from disk, naming the references and saying that edges to
  those assemblies are absent from the model. The build drops such a reference before the compiler sees
  it, so before, the load read as complete while types from that assembly bound to nothing. The warning
  covers unconditioned `Reference` items on both load paths, MSBuild and binlog replay, counts a reference
  as resolved when any target framework received it, and appears in `--json` output's diagnostics. It
  changes no verdict.
- A load through MSBuild whose build names an analyzer or source generator file missing from disk now
  writes one notice with the number of analyzer references it stripped, saying that any code they
  generate is absent from the model. Before, the MSBuild path dropped such a reference without a word; the
  binlog replay already wrote this notice.

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
  bound the whole initializer again, so an initializer of 400 rows now extracts about 96 times faster.
  The build analyzer, the xUnit adapter and every command and MCP tool that reads the code run this
  extraction, and what it extracts is unchanged.
- `--diff-base` and `arch_check`'s `diffBase` now count a file moved since the ref as changed at both its
  old and its new path, as `hook` already did. Before, git's rename detection listed it only at the new
  path, so a scope tripwire on the directory it left did not fire.
- `arch_check` with an empty `diffBase` now treats it as absent, as `arch_explain` already did. Before,
  the empty string was passed to git and the call failed.
- `SLIDERULE_LOG_LEVEL` set to a comma-separated list now falls back to Warning, like any other value it
  does not recognise. Before, the listed levels combined into one that turned logging off.
- `MAX_MCP_OUTPUT_TOKENS=1` no longer makes every non-empty tool reply fail. Before, it gave a budget of
  zero characters, and truncating a reply to it threw.
- A quarantine whose boundary names or contains its own scoped region is now listed under "Not drawn in
  full" in the law diagram. Before, its scope box became its own parent, and the box and its rule vanished
  from the diagram.

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

[Unreleased]: https://github.com/andypgray/sliderule/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/andypgray/sliderule/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/andypgray/sliderule/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/andypgray/sliderule/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/andypgray/sliderule/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/andypgray/sliderule/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/andypgray/sliderule/releases/tag/v0.1.0
