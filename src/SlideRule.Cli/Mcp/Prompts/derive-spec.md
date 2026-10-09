# Derive an architecture spec for an existing codebase

Your goal is a **compiling SlideRule spec that states this codebase's architecture honestly,
as it is today**: the target rules new code must follow (`Enforce`), the known debt being
worked off (`Migrate`), the dragons nothing new may reference (`Quarantine`), and the dragons
that stay open to callers (`Caution`). A legacy codebase's
architecture is partly descriptive, not prescriptive — a spec that only states the ideal is
useless on day one, and a spec that launders the mess into law is worse. The postures exist so
you never have to choose between the two.

Work the steps in order. Do not skip the curation gate.

## What this recipe is — and is not (binding, not a disclaimer)

- **This server does not infer the architecture.** There is no "derive" command. *You* derive
  a proposal from evidence; the deterministic checker validates every claim; the human decides
  what becomes law. A spec nobody confirmed is worse than no spec — the rendered agent context
  claims to be provably true, so every rule in it must be something the team actually stands
  behind.
- **Violations during this flow are data, not failures.** You will deliberately author rules
  the codebase violates, precisely to measure the violations. A red `arch_check` mid-derive
  means the evidence pass is working.
- Everything here uses the read-only tools (`arch_graph`, `arch_check`, `arch_explain`,
  `arch_status`) or their identical CLI verbs (`sliderule graph|check|explain|status`, add
  `--json` for the same documents). Prefer the tools: they are already bound to the solution,
  the workspace stays warm between calls, and their narrowing parameters (`overview`,
  `skeleton` and `index` for grain, `projects` for scope on the survey, `rules` on the check)
  keep a big solution's answer a complete document rather than a truncated one. The CLI verbs are for when a document belongs in a
  file. They take the solution path as their first argument — `sliderule graph MyApp.sln
  --json` — or walk up from the working directory when omitted (`explain` differs: its rule
  ID comes first, the solution second — `sliderule explain area/rule MyApp.sln`). The two
  ratchet mutations that end the flow — `sliderule baseline init` and the commit — belong
  to the human. A session launched from the MCP registry manifest has no `sliderule` command:
  there, prefix every CLI verb in this recipe with `dotnet dnx SlideRule.Cli@... --yes --`,
  run from outside any `global.json` that pins an SDK below 10.

## 0. Discover stated intent first

Before proposing anything, look for architecture the team already wrote down:

- Existing architecture tests (ArchUnitNET, NetArchTest) — these are rules someone already
  ratified; carry them over rather than re-deriving them.
- Analyzers or lint config enforcing boundaries; `Directory.Build.props` conventions.
- ADRs, `docs/architecture*`, wiki exports checked into the repo.
- `AGENTS.md` / `CLAUDE.md` / Cursor rules prose about layers or "do not touch" zones.
- Solution folders and project naming — they encode intended grouping.

**Already-stated intent wins over inference.** Where a document and the code disagree, that
disagreement is not noise — it is a posture decision waiting for step 5 (the document
describes the target; the code is the debt).

## 1. Survey the estate

Call `arch_graph` (CLI: `sliderule graph MyApp.sln --json`). It needs no spec — it is the
one tool that works before any spec exists — and returns the codebase as extraction sees it.

It does need the solution **restored and built**. If a project fails to load, the call returns an
error naming it instead of a survey: a map missing whole projects would send the whole derivation
down a wrong path, and there is no later step that would catch it. Restore and build, then retry.
Survey the partial model deliberately (`allowWorkspaceDiagnostics: true`) only if some project
genuinely cannot be made to load, and then treat every conclusion below as provisional.

- `projects[]` — each project's declared `projectReferences`, type count, and exact namespace
  inventory with type counts. The namespace inventory is your raw material for layer globs.
  `solutionMember: false` marks a project a `ProjectReference` dragged into the workspace that
  the solution file does not declare — a passenger, not part of the estate you are writing law
  for, so keep it out of your layer globs. An absent key means membership could not be read.
  `targetFrameworks` lists every framework the project declares, ordinal-ordered by framework
  name — extraction's own order, never the csproj's — normalized to the short moniker, so a
  project predating the SDK reads `net48` beside everything else. A type several frameworks
  declare takes its facts from the first of them in that order, and `factsFollow` names the
  first framework to win one; it is absent when the frameworks share no type (every type then
  keeps its own framework's facts) and for every project that compiled once. Read both before
  writing a rule whose subject is a multi-targeting project: a rule about what its shared types
  reference sees every framework; their members and hierarchy come from one framework alone.
  `isPackable`, `locksPackages` and `packageReferences[]` are the artifact facts, and they are
  evaluated rather than read off the project file. The SDK makes a project packable without
  anybody writing it down, and a lock-file policy is regularly declared once in a props file
  above the whole solution. `packageReferences[]` is what the project declares, never the
  transitive closure. An absent key means no answer: nothing evaluated the project, or — for
  `isPackable` alone — the project is outside the SDK's pack machinery and has none.
  `privatePackageReferences` lists the ones whose `PrivateAssets` is `all` (analyzers and build
  tooling, which reach neither the project's package nor a project referencing it), absent when
  there are none; `MustReferenceNoPackages()` passes them.
  `generated` qualifies a type count — on the project, and on each namespace — with how many of
  those types a generator emitted; it is absent when none were. **A namespace whose `generated`
  equals its `types` is wholly generator output: never make it a layer glob and never anchor a
  rule on it.** A compiled view tier collects hundreds of such types under one namespace nobody
  typed, and naming it would aim your law at code no one can fix. Where a project's `generated`
  is a large share of its `types`, narrow the project noun with `.Authored()`.
- `projectEdges[]` — **observed** project→project references (distinct type pairs), and only
  references the code declares: a project reaching a type it compiles itself is not an edge,
  however extraction attributed that type (see `multiplyDeclaredTypes[]` below). Compare
  against the declared references: a declared reference with no observed edge is a dead
  reference (note it for the human; it is cleanup evidence, not a rule). An observed edge you
  did not expect is the interesting kind.
- `externalEdges[]` — external references grouped by namespace root. Scan for the classic
  dangerous externals: `System.Data` (inline SQL), `System.Web` (HttpContext-era coupling),
  direct driver namespaces, and anything the team says it is migrating away from.
- `multiplyDeclaredTypes[]` — the types more than one project declares, from one file compiled
  into several (a `<Compile Include>` link, shared source) or from a copy in each (a polyfill).
  Each entry names the type, every project declaring it, and the one whose facts and project
  attribution it follows. Read this **before** anchoring a subject on a project:
  `arch.Project(...)` on any declarer selects the type, and its facts answer from the declarer
  the entry names. The key is absent when the solution has none, which is the common case.
- `shadowedTypes[]` — the full names a project declares that a referenced assembly also supplies:
  a stand-in written under a package's own namespace, a polyfill under a BCL one. The name means
  two types, and each reference reaches whichever the referencing project bound, so a rule naming
  the type reaches both while `arch.Project(...)` over the declaring project reaches only the
  declaration. Each entry names the type, that project, the supplying assemblies, and the projects
  binding the assembly instead. Absent when the solution has none.
- `unsupportedProjects[]` — the projects the solution declares that this survey does not cover,
  each with its reason. A project in another language (an `.fsproj`, a `.vbproj`, a `.sqlproj`) is
  not in `projects[]`, contributes no edges, and can never violate a rule you write. A shared
  project (`.shproj`) is not in `projects[]` either, but its `.projitems` are compiled into every
  project that imports it, so its code is already surveyed under those names. Read this **first**,
  before treating `projects[]` as the estate: without it the roster is simply shorter than the
  solution and nothing says so, and a spec derived from it silently cannot reach whatever ships
  from those projects. Absent when the survey covers every declared project. Unlike the two
  coverage keys above it is never elided — it is bounded by the solution, not the codebase.

The document's keys, exactly (camelCase; an optional field is absent, never null):

```text
projects[]              { name, solutionMember?, targetFrameworks?, factsFollow?, isPackable?, locksPackages?, projectReferences[], packageReferences[], privatePackageReferences?, types, generated?, namespaces[]{ namespace, types, generated? } }
projectEdges[]          { source, target, references }
externalEdges[]         { source, targetNamespaceRoot, references }
multiplyDeclaredTypes[] { type, declaredBy[], factsFollow }
shadowedTypes[]         { type, declaredBy, suppliedBy[], boundFromAssemblyBy[] }
unsupportedProjects[]   { project, reason }
```

Grain is a ladder, and an over-budget survey walks down it by itself rather than coming back
cut. At overview grain — `grain: "overview"`, or the server's own first step — the document
stamps `"grain": "overview"` and elides each project's `namespaces`; every project, edge and
external row survives. At skeleton grain — `grain: "skeleton"`, or the server's second step when
the overview is still too big — it stamps `"grain": "skeleton"` and drops `externalEdges[]`
and `multiplyDeclaredTypes[]` and `shadowedTypes[]` too, reporting how many rows went as
`externalEdgeCount`, `multiplyDeclaredTypeCount` and `shadowedTypeCount`; each project's
`packageReferences[]` and `privatePackageReferences` go with them, being the build-side twin of the
external rows. The
projects and their edges stay. At index grain — `grain: "index"`, the ladder's floor — it stamps
`"grain": "index"` and keeps the roster alone: every project's `name`, `solutionMember` and
`types`, with its `projectReferences`, framework pair and packaging pair gone and
`projectEdges[]` reported as `projectEdgeCount`. That is the list `projects` globs match, so a
survey that degrades this far hands you the argument for the next call. `unsupportedProjects[]`
survives every rung whole, having no count key at all, and so does a project's own `generated`,
riding its row; the framework pair (`targetFrameworks`, `factsFollow`) and the packaging pair
(`isPackable`, `locksPackages`) ride it the same way down to skeleton.
Only the per-namespace `generated` goes with the inventory that carries it — so at any grain
you can still see which projects are mostly generator output, and drop to full grain to see
which namespaces. Read the stamp: a survey with no `grain` is the complete one. An absent
coverage key with no count beside it means the solution has none; the count key is what tells
elision from absence.

Scope is the other axis. `projects` (name globs) narrows the survey and stamps
`projectsScope`; edges keep both directions, so a scoped `projectEdges[]` can name a project
outside the roster, and a `multiplyDeclaredTypes[]` entry survives when any of its declarers is
in scope. A `shadowedTypes[]` entry survives on either end too — its declaring project, or any
project binding the assembly. `unsupportedProjects[]` does not scope either: it is a fact about
the load rather than about the roster. On a solution too big to survey whole even at index
grain, scope is the knob left — grain has nowhere further to go, and the index document you
are holding names every project you can scope to.

From the survey, write down **hypotheses, not conclusions**:

- Candidate layers: coherent namespace subtrees or project groups (Domain-shaped, Web-shaped,
  Infrastructure-shaped). Either can be a layer — a layer is defined by namespace globs or by a
  selection, so a project is one. Keep them disjoint: overlapping layers double-count evidence.
- Candidate direction rules: which layer should never reference which (the `projectEdges`
  matrix tells you which of those are already true and which are aspirational).
- Candidate conventions: naming patterns the inventory suggests (interfaces, handlers,
  controllers, repositories).
- Candidate dragon zones: "Legacy"/"Old"/"V1" names, a facade-shaped surface guarding a blob,
  areas with no tests that everything fears. If a spec project already exists in the solution
  it will appear in the survey like any other project — ignore it (and in a source-checkout
  setup, the SlideRule contract library and its types appear too; ignore both — see step 2).

## 2. Scaffold the spec project

Create a small class library for the spec (convention: an `arch/` folder next to the
solution):

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <!-- net48 and netstandard2.0 default to C# 7.3; the spec class below needs C# 10. -->
        <LangVersion>latest</LangVersion>
        <!-- Stages package assemblies into the build output so `check` can load typeof() targets
             that live in NuGet packages; harmless when every target is a project type or pattern. -->
        <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
        <!-- The spec pins its own SlideRule version, so this project builds the same whether or not
             the repository manages package versions centrally. Without it, a repository carrying a
             Directory.Packages.props fails the restore with NU1008. -->
        <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="SlideRule" Version="..." />
    </ItemGroup>
</Project>
```

The four SlideRule packages ship one version in lockstep: reference the version
`sliderule --version` prints. Where the repository manages package versions centrally, leave its
`Directory.Packages.props` alone — the property above opts this one project out, so the spec keeps
its own pin and the central file needs no entry for it.

**Pick the TFM by one rule: the spec project must be able to reference the product projects
it will `typeof()`.** On a `net48` estate, the spec targets `net48` — the SlideRule contract
library is `netstandard2.0` precisely so that works. Several frameworks go in the plural
`<TargetFrameworks>` element. Where referencing a product project is awkward, you do not need
it: namespace-pattern targets (`arch.Namespace("System.Data.*")`) need no compile-time reference
at all, and matching is by full name.

`typeof()` targets must also be **accessible** to the spec assembly: anchoring an `internal`
type fails the spec build with CS0122. Every set-valued position has a spelling that needs no
load, so reach for one before reaching for accessibility: dependency-verb targets take namespace
patterns, a `BoundaryOnlyVia` facade takes `arch.Types.Named("BillingFacade")`, and
`Implementing` / `DerivedFrom` / `AttributedWith` each take the fully-qualified name as a string,
matching byte-identically to their `typeof` twin. Where the type must genuinely be reflected —
an `arch.Member(typeof(X), nameof(X.M))` anchor — have the product project grant
`[InternalsVisibleTo("MyApp.Architecture")]` (or the csproj
`<InternalsVisibleTo Include="MyApp.Architecture" />`) and rebuild.

```csharp
using SlideRule;

namespace MyApp.Architecture;

public sealed class ArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
    }
}
```

Name the project — and with it its root namespace — so the **last segment is not `Arch`**: inside
a namespace ending in `.Arch`, the simple name `Arch` binds to that namespace rather than to the
`Arch` type `Define` takes, and the spec does not compile (CS0118, verbatim below).
`MyApp.Architecture` is safe, and so is the `arch/` folder — only the namespace segment collides.

Add it to the solution (`dotnet sln add arch/MyApp.Architecture/MyApp.Architecture.csproj`) and build.
Spec discovery is by convention: **the unique solution project that references
`SlideRule.dll`** — via the package or via a project reference. As a solution member,
the spec project is excluded from the checked universe — its own types never trip your rules.

**Expect `dotnet sln add` to rewrite more of a `.sln` than the one project it adds.** The CLI
unions its default platforms (`x64`, `x86`) into the solution's configuration list, then writes the
project-configuration table out complete: an `ActiveCfg`/`Build.0` pair per project per
configuration × platform combination. On a dozen-project solution that is a hundred-plus inserted
lines for the one project you added. The rewrite is legitimate, not damage: those mappings are what
make the spec project, and every other member, build under every combination the solution declares.
Do not revert it to hand-write a narrower entry mapping only `Debug|Any CPU` and `Release|Any CPU`.
The project registers either way (`dotnet sln list` shows it), but a solution build under any other
declared combination skips a project with no mapping for it — MSBuild names it in an `MSB4121`
warning and still exits 0, so the build stays green while `check` reads a stale spec assembly, or
none at all.

**Source-checkout setups only**: `dotnet sln add` follows project references, so it may also
add the SlideRule contract library to the solution — and **solution membership decides
whether its types come under your rules**. A contract library the solution declares is checked
like any other member; one that only rides in as a project reference of the spec counts as the
spec's private plumbing and stays out of the checked universe. It appears in the survey either
way, because `arch_graph` is spec-less and excludes nothing. Ignore it there, and scope broad
subjects to your product namespaces
(`arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")`, not bare
`arch.Types.OfKind(...)`) so convention rules never bite the tooling. A `PackageReference`
setup has none of this — the package is a metadata reference, not a project.

Test projects need the same subject-scoping care in ANY setup: a test project sharing the
product root namespace (`MyApp.Tests.*` inside `MyApp.*`) is solution-declared, so
`.InNamespace("MyApp.*")` subjects include its types — fakes and test helpers then pollute
naming, member, and hierarchy rules. Anchor product-wide subjects on `arch.Project("MyApp")`
instead; a project noun never crosses project boundaries. When the product is several projects,
name them as one union — `arch.AnyOf(arch.Project("MyApp"), arch.Project("MyApp.Data"))` — rather
than a namespace cone minus an `.Except`: the union names exactly what you mean, while the cone
also sweeps every other solution member that happens to sit inside it.

Errors you may see, verbatim, and what they mean:

- `error NU1008: The following PackageReference items cannot define a value for Version:
  SlideRule. Projects using Central Package Management must define a Version value on a
  PackageVersion item.` — NuGet's, not this tool's, so nothing in it names SlideRule as the cause.
  The repository has a `Directory.Packages.props` at or above the spec project and the
  `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` line is missing from the
  spec csproj. Put it back rather than adding a `PackageVersion` entry to the central file: the
  spec's pin belongs to SlideRule and moves when the tool does, not with the repository's own
  dependencies.
- `error CS0118: 'Arch' is a namespace but is used like a type`, beside a CS0535 that the spec
  class does not implement `IArchitectureSpec.Define(Arch)` — the compiler's, not this tool's, so
  nothing in either names the cause. The spec project's namespace ends in `.Arch`, and inside that
  namespace the simple name `Arch` is the namespace itself, shadowing the contract type. Rename the
  project and namespace so the last segment is not `Arch` (the scaffold's `MyApp.Architecture` shape);
  qualifying `SlideRule.Arch` in the signature also compiles, but leaves the shadow armed
  for every file the spec grows.
- `No spec project found: no solution project references SlideRule.dll. Pass --spec
  to name one.` — the spec project is not in the solution yet (`dotnet sln add`), or you need
  an explicit `--spec`. The line below it says how many C# projects the workspace held: a count
  far short of the solution's is the real finding, not the missing spec project.
- `No spec project found: N projects failed to load, so a project that references
  SlideRule.dll may be among them:` — followed by every failed project, uncapped. The
  one that would have matched may be among the casualties, and `--spec` cannot repair a load,
  so the remedy is the one the refusal names: restore and build, then retry.
- `No spec project found: NuGet packages did not resolve for N projects, so a project that
  references SlideRule.dll may have failed to resolve it:` — the quieter break: every
  named project loaded completely, but without its package references, so a reference to the
  contract library resolves to nothing and convention discovery cannot see it. Restore, then
  retry. One broken tree can raise this and the previous refusal at once — they compose, each
  naming its own projects.
- `No spec project found: the workspace did not load cleanly, so a project that references
  SlideRule.dll may have failed to resolve it:` — followed by the diagnostics, up to
  three of them: load problems that blame no project in particular, which is why this one says
  "may" — that is all it knows. The spec project may well be there, its reference to the
  contract library hidden by whatever the diagnostics describe. Restore and build, then retry.
  `--spec` cannot help here — an unresolved reference is unresolved whichever project you name.
- `Multiple spec projects found; pass --spec to disambiguate:` — more than one project
  references the contract library; the listed lines name each one's `.csproj`, so pass yours.
  A project that multi-targets is listed once: its frameworks are one spec project.
- `The spec project '…' has no built output … Build the solution first (dotnet build).` —
  the CLI and this server **never build**; build before every check, or the results are stale.
- `The spec assembly '…' failed to load its dependency '…' while running Define().` — a
  `typeof()` anchor names a type whose assembly the loader cannot reach. The message says which
  of two situations you are in, so read its second line rather than reaching for the setting.
  If the assembly is a **NuGet package**, `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
  in the spec csproj (the scaffold above carries it) plus a rebuild stages it. If it is a **.NET
  shared framework** — anything reached through `<FrameworkReference Include="Microsoft.AspNetCore.App" />`,
  such as an MVC `ControllerBase` or a `Microsoft.WindowsDesktop.App` type — **no build setting
  can help**: that assembly is never staged into a spec's output and the tool's own host does not
  carry it. Anchor those by string instead — `.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`
  renders exactly like the `typeof()` form and needs no assembly load — or target a namespace
  pattern.

If discovery still cannot find your spec project, do not stall: pass
`--spec path/to/YourSpec.csproj` to every verb and continue — nothing downstream depends on
convention discovery.

## 3. Draft candidate rules — all as `Enforce`, all of them hypotheses

Turn every step-1 hypothesis into a rule. Do not pre-judge postures — the checker assigns the
evidence in step 4; postures come in step 5. **Author the already-true directions too**: every
layer pair the survey's edge matrix shows clean becomes an Enforce candidate — the cheapest
law you will ever get. A derive that only writes rules about problems under-produces law.

Define each layer by whatever honestly says where it is. A namespace cone where the namespaces are
disciplined; `arch.Project("MyApp.Web")` where the unit of architecture is the assembly, or where the
survey's `multiplyDeclaredTypes[]` or a shared `RootNamespace` says the namespaces do not separate
the projects; a refinement — `core.InNamespace("MyApp.Core.Model.*")` — where one cone nests inside
another and a glob would swallow it. A layer names exactly what its definition names and checks
identically to it, so the choice costs nothing at the checker and buys the row, the card and the
collective voice.

Give each layer a `.Purpose(prose)`: one sentence on what the layer is for, lifted from the step 0
prose that already says it (the ADR, the wiki page, the `AGENTS.md` paragraph). It renders into the
layer's module-map row and its card, so the sentence that rotted in a document now sits beside the
definition it describes and re-renders with it.

```csharp
Layer domain = arch.Layer("Domain", "MyApp.Domain.*").Purpose("Domain holds the order and customer model.");
Layer web    = arch.Layer("Web",    arch.Project("MyApp.Web")).Purpose("Web is the HTTP surface: controllers and the views they serve.");

arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(web))
    .Because("Candidate: Domain looked UI-agnostic in the survey.");

arch.Rule("data-access/no-inline-sql")
    .Enforce(web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
    .Because("Candidate: externalEdges shows System.Data reached from Web.");
```

- One hypothesis per rule — a rule is one sentence; compound requirements are multiple rules.
- IDs are `area/rule-name`, matching `^[a-z0-9-]+(/[a-z0-9-]+)*$`. They are permanent handles
  (baseline keys, violation citations), so name them as if they will survive — they will.
- `Because` is **required** and will not pass spec build when blank. During drafting a
  candidate note is fine ("Candidate: …"); by step 6 every kept rule needs the real reason.
- Prefer **namespace-pattern targets** for external bans. A bare `typeof(SomeType)` target
  that does not exist in the codebase passes silently (absence is the win condition), so a
  typo'd type name looks like success; a pattern target that matches nothing raises an inert
  warning — the misspelling check comes free.
- Naming/shape candidates come straight from the inventory: interfaces `MustHavePrefix("I")`,
  handler types `MustHaveSuffix("Handler")`, `MustBeSealed()` where the convention looks
  intended. Member-level conventions are candidates too:
  `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async")` where the
  codebase names its async methods `*Async`.
- Token-presence candidates: where the codebase's `Task`-returning methods already accept a
  `CancellationToken`, the convention is one line —
  `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken))`.
  Matching is declaration-level and exact (a `CancellationToken?` or `params CancellationToken[]`
  parameter is a different declared type and does not count); whether a body *flows* the token
  is call-site analyzer territory, not this rule's.
- DI-construction candidates: a type resolved through a container or a discovery registry may be
  *referenced* but must not be *constructed* — `arch.Types.Except(arch.Type<HandlerRegistry>())
  .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>)))` where a family of types
  (handlers, commands, services) is meant to arrive via one sanctioned root, so a stray `new`
  bypasses discovery. `.Except` the composition root that legitimately `new`s them.
- DI-lifetime candidates: where the composition root registers services with
  `AddSingleton`/`AddScoped`/`AddTransient` (or `TryAdd*`, `AddHostedService`, `AddDbContext`,
  `AddHttpClient<TClient>`), the captive-dependency rule is one line —
  `arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped),
  arch.Registered(Lifetime.Transient))`. Registrations made by assembly scanning, factory
  internals, or framework defaults are not seen, so an empty-subject failure here means the
  registrations live outside the recognized calls — drop the rule rather than guessing.
- Anchor a rule on the `Layer` handle wherever one exists (`tools.MustHaveSuffix("Tools")`,
  not `arch.Types.InNamespace("MyApp.Tools.*").MustHaveSuffix("Tools")`, and `web.MustNot…`
  rather than a bare `arch.Project("MyApp.Web").MustNot…` where a Web layer names that
  project). The two check identically, but render's per-directory local-rules card is keyed on
  the layer-anchored subject — the anonymous twin emits no card in that layer's directory, so
  agents editing there never see the rule locally, and it earns no module-map row and no
  purpose either.
- Family candidates: where the estate has a family of like units — plugins, modules, bounded
  contexts, one project or one layer each — the law that every unit keeps to itself is one rule
  over the family, not one per unit. `arch.Each(arch.Projects.Matching("MyApp.Plugin.*"))
  .MustNotReferenceEachOther()` says no plugin reaches another;
  `arch.Each(dispatch, tracking, invoicing).Except(<each module's Contracts cone>)
  .MustOnlyBeReferencedByItself()` says each module's internals are reached only from its own
  module; `arch.Each(a, b, c).MustNotHaveCircularReferences()` says the units may reference each
  other but never in a circle — the law for peers whose order nobody has stated, and the first
  law to write on an estate before its order is known (layers only: MSBuild already forbids a
  circle among projects). On a family, "itself" and "their own" mean the cell — the whole layer
  or project the type sits in, as declared — so the `MustOnly*` verbs read the same way per
  cell, and the survey's edge matrix over the family's projects is the evidence. One rule, one
  ID, one baseline; write one rule per cell only where each cell needs its own `Because` or `Fix`.
- Dragon-zone candidates are the one exception to "all as Enforce": a scope has no Enforce
  form, so draft them as `arch.Scope(id).Quarantine(...)` directly (step 5 shows the full shape
  of both scope postures). The scope's containment violations arrive in step 4 alongside every
  other rule's evidence, and they are what decides in step 5 whether the scope stays a
  `Quarantine` or becomes a `Caution`.

Build the spec. Spec-build validation reports **every** error at once (missing `Because`,
malformed IDs, dangling rules, blank prose) — fix them in one pass.

## 4. Check = the evidence pass

Run `arch_check` (CLI: `sliderule check MyApp.sln --json`; exit 1 is expected — violations
are the data). For each rule, read `rules[]` — keyed by `id` (`ruleId` is SARIF's spelling,
not this document's), camelCase and exact like the step-1 key map:

- `violations[]` with `sites[]` — the real edges, each with `file:line`. One violation per
  offending type pair; multiple reference sites between the same pair ride together in
  `sites[]` — and the baseline records how many, so a grandfathered pair that grows is red.
  This is your edge summary at rule precision. Beside them, `violationCount` and
  `siteCount` are present at every grain — script against the counts (step 5 reads the
  violation count); the arrays are their expansion and are elided at coarser grain.
- The **authoring signals** are about your selection, not the code. A violation of kind
  `emptySubject` ("The subject selection matched no solution-declared types.") fails the rule;
  the warning "This rule is inert: its target selection matched no types." leaves a rule that
  passes forever. Its twins name one dead pattern: "This rule is partly inert…" a target,
  "…stricter than written…" an allow-list or a membership, "…not measured as written…" an
  `among:` part. Each carries a `hint` (a `hint:` line in the report) with the cure read off the
  selection that came up empty — a glob, a project name and a `typeof` anchor each fail
  differently — so apply the hint rather than guessing, and where an inert target names a layer
  that genuinely declares no types yet, decide before keeping the rule.
- `modelIncomplete: true` on the document, with `failedProjects` and `restoreFailedProjects`
  naming the casualties, means the check ran on a partial model: a rule that passed was **not
  observed**, not obeyed, and an inert target may be an edge that was never extracted. The
  document arrives either way; the CLI also exits 2 unless `--allow-workspace-diagnostics` is
  passed. Restore and build the named projects before reading any verdict as evidence.
- An **error result** (not a violations document) means the run itself failed — an
  unresolvable or unbuilt spec, or spec validation errors. Fix, rebuild, rerun.

Iterate selections until the failures that remain are *genuine* — real edges, real
nonconforming names — against a count you can read off the document: per rule, its
`emptySubject` violations plus its inert and partly inert warnings. Re-check while that count reaches a
new low; when two consecutive re-checks leave a rule's count at its best so far, stop working
that rule and carry it to step 6 as `undecided`, while every other rule keeps iterating on its
own count. No derivation has needed the bound yet; it is there so that none can run away.
Iterating is cheap over MCP: the server holds the workspace warm and reconciles your edits per
call, so a re-check after the first load answers in milliseconds; one-shot CLI runs pay a
workspace load each time (a clean tree with a valid extraction cache skips it). On a big
solution, narrow instead of leaving: `rules` takes rule-ID globs (`rules: "data-access/*"`; the
CLI twin is `--rules`), so the evidence pass can walk the draft area by area with every
response a complete document, and `arch_explain` returns one rule whole. You never have to page
a report out to read it entire: over your client's budget it comes back whole at a coarser
grain rather than cut, down to `index` — every rule ID with its verdict and violation count,
which is the menu those globs pick from.

## 5. Assign postures from the evidence

For each surviving rule, the violation count decides the honest posture:

- **Zero violations → `Enforce`.** The codebase already obeys — on a complete model only: a
  check stamped `modelIncomplete` has *not observed* the rule, and a rule not observed can never
  be called already-obeyed. Making it law costs nothing and protects it from the next change.
  Keep the rule exactly as drafted; upgrade `Because` to the real rationale.
- **Violations, and the team wants the target → `Migrate`.** Rewrite as a pair:

  ```csharp
  arch.Rule("data-access/no-inline-sql")
      .Migrate(
          from: "Controllers reach System.Data directly (legacy inline-SQL style).",
          to: web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
      .Because("Repository pattern for testability — ADR-012.")
      .Fix("Inject the repository.")
      .Exemplar(arch.Types.Named("OrdersRepository"));
  ```

  `from:` is **descriptive prose about the OLD pattern, stated factually** — never
  aspiration, never blame. The current violations become the grandfathered baseline in
  step 7; new code in the old pattern goes red from then on. The boy-scout policy defaults to
  `MigrateIfSmall` (override with `.WhileYoureThere(...)`), and the baseline path defaults to
  `arch/baselines/<rule-id>.json` — omit `.Baseline(...)` unless the team wants it elsewhere.

- **A region with no target state, which nothing new may reference → `Quarantine`.** No one will
  fix it; the enforceable thing is the boundary:

  ```csharp
  arch.Scope("legacy/billing")
      .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
      .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
      .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
               "Nightly reconciliation depends on this. Do not normalize.")
      .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
  ```

  **List the facade implementation type(s) in `BoundaryOnlyVia` alongside the interface** —
  the composition root's DI registration references the concrete type, and forgetting it puts
  that registration red on day one. Omit `BoundaryOnlyVia` entirely for a hermetic quarantine.

  **The boundary takes selections, not only types**, so a facade the spec cannot compile against
  is still law: `.BoundaryOnlyVia(arch.Types.Named("IBillingFacade", "BillingFacade"))` for an
  `internal` facade or one in a project the spec does not reference,
  `.BoundaryOnlyVia(arch.Namespace("MyApp.Legacy.Billing.Contracts.*"))` where the surface is a
  region. The formula does not care whether the surface is inside the scope or outside it, so a
  legacy region's one sanctioned *caller* is spelled the same way. Write that caller into the
  boundary rather than grandfathering it, or the rendered law will call your sanctioned entry
  point debt to burn down while the dragons prose says the opposite.

  `Dragons` must carry three things: what the code does, **which weirdness is structural**
  (the behavior a "fix" would break), and the sanctioned interaction surface. "Don't touch,
  it's bad" is not dragons prose — agents still have to call into this code.

  A Quarantine desugars to two checkable rules under the scope ID: `{id}/containment` (red on any
  new reference into the scope not via the facade; existing inbound references get
  grandfathered in step 7) and `{id}/tripwire` (a diff-aware warning; it reports as *skipped*
  in `check` runs without `--diff-base` — expected, not a bug).

- **Structural weirdness that new callers are welcome to → `Caution`.** The decision rule between
  the two scope postures is whether anything new may reference the code: nothing new may reference
  it → `Quarantine`; new callers welcome → `Caution`. A shared utility, a God class, a native-interop
  layer that is ordinary public API: containing it would grandfather every legitimate caller as debt
  and red the next one, and the card is still worth having.

  ```csharp
  arch.Scope("interop/native-signatures")
      .Caution(arch.Namespace("MyApp.Native.*"))
      .Dragons("Every DllImport here mirrors the C header byte for byte, and a wrong width is silent " +
               "memory corruption rather than a compile error. Add signatures beside the existing ones " +
               "and keep every marshalling attribute; call through the wrappers, never the imports.")
      .Because("The native layer is public API for the whole solution; the compiler checks none of it.");
  ```

  `Dragons` carries the same three things a quarantine's does. A Caution desugars to one checkable
  rule, `{id}/tripwire`: nothing is ever red, a change set touching the scope draws a warning under
  `--diff-base` (and reports as *skipped* without it), and `render` puts the dragons on the scope's
  directory as a card. A caution that later gains a facade becomes a `Quarantine` under the same ID.

- **Nothing the team will stand behind → drop the rule.** An unratified rule in the spec is
  exactly the stale-doc problem this tool exists to kill.

Before moving on, check the invariant those four bullets exist to leave true: **`Enforce`
carries no baseline**, so `baseline init` cannot grandfather it and a red Enforce rule
stays red for good. Every rule that still has violations must leave this step re-postured to
`Migrate`, fenced inside a `Quarantine`, dropped, or named in the handoff as work the team is
choosing to do now. An unexplained red is the one outcome the flow cannot recover from,
because the reader cannot tell it from a mistake they made.

## 6. The curation gate — **do not guess**

Stop. Present every proposed rule to the human with its evidence, one row per rule: ID,
proposed posture, the rule sentence, violation count, one example `file:line`, and your draft
`Because`. The human accepts, edits, or drops **each rule individually**. Conflicts you found
in step 0 (doc says X, code does Y) are decided here, not by you. A rule the step-4 loop could
not make match takes `undecided` in the posture column, and in place of its evidence
`subject could not be made to match; human to resolve`: it is never dropped for the human and
never kept silently.

**Author nothing final until every rule is decided**, and never write a `Because` the human
would not say in a design review — it renders into the generated context and into every
violation message as the team's stated rationale.

## 7. The human baselines the remainder

With the curated spec built and checked, the remaining reds are exactly the acknowledged debt:
each Migrate rule's current violations and each quarantined scope's existing inbound references.
Grandfather them:

```
sliderule baseline init MyApp.sln
```

**`baseline init` is run by the human, never by you.** The baseline is the team's signature
on its debt — an attributed, reviewable artifact, not an agent convenience. It captures every
uncaptured ratcheted rule's *current* violations in one pass (a rule whose selection matched
nothing, or that holds a dead target pattern, is skipped and named), writing one entry per line
under `arch/baselines/` (paths resolve against the solution root) — everything red right now,
including code written five minutes ago; day
zero is the one moment "current" and "accepted" coincide, which is why curation comes first.
From then on the ratchet holds: baselined violations pass, new ones go red, and the file only
shrinks (`baseline accept-reductions`). One precision worth knowing: entries key
subject×target pairs, not sites — every reference site between one pair rides in a single
entry that records how many sites it grandfathered, and `status` counts pairs. A new pair goes
red, and so does a grandfathered pair that gains sites past its recorded count.

When a failing rule was left `Enforce`, `baseline init` says so rather than staying quiet: a notice
names each rule failing with no baseline to capture, with its violation count (on a spec with
no ratcheted rules at all, it leads with `nothing to capture`). Enforce is the one posture a
baseline cannot absorb, so every rule in that list is either a step-5 miss — go back and
re-posture, fence, or drop it — or work the team is choosing to do now, which the handoff
must say.

Re-run `arch_check`. If step 5 ratcheted every violating rule, expect exit 0,
`rulesFailed: 0`, with the grandfathered counts visible. If reds were left deliberately, the
re-check still exits 1 and those rules — and only those — are what remains: state each one
with its violation count in the outcome report, so the reader can tell a chosen red from a
mistake they made. `arch_status` now shows the per-rule burndown — the numbers the team
watches shrink.

## 8. Render and commit

Have the human run `sliderule render MyApp.sln`: it writes the managed block into the root `AGENTS.md`
(everything outside the markers is preserved byte-for-byte) and drops a second managed
`AGENTS.md` into each scope's directory (the dragons card) and into each layer's
directory when rules are anchored on that layer (the local-rules card). If a `CLAUDE.md`,
`.claude/CLAUDE.md` or `CLAUDE.local.md` sits in the solution directory or above it (a
`~/.claude/CLAUDE.md` of the user's own does not count), Claude Code reads that instead of
`AGENTS.md`, and `render` warns: propose a `CLAUDE.md` beside `AGENTS.md` holding the line
`@AGENTS.md`, and leave that edit to the human. Then commit —
spec project, `arch/baselines/**`, and the rendered
`AGENTS.md` files — as **one reviewable diff**: the reviewer sees the proposed law, the
acknowledged debt, and the generated context in a single change. On a multi-configuration `.sln`
the largest hunk in that diff is often the solution file itself: the configuration-table expansion
from step 2, which is mechanical, expected, and not to be trimmed by hand.

Where the human wires `check` into CI, say that the pipeline's invocation wants
`--diff-base <the pull request's base ref>`: without one every scope's tripwire is skipped rather
than clean, so the code fenced or flagged because it is dangerous to edit is the one thing CI never
mentions. The checkout has to be deep enough for that ref to resolve.

Close with a receipt in exactly this shape, one key per line, so a reader — or a script — finds
each claim where it expects it:

```text
specBuild: ok
evidencePass: complete
check: exit 1, rules 31, passed 2, failed 27, skipped 2, violations 51
postures: enforce 12 / migrate 6 / quarantine 2 / caution 1
leftRed: layering/no-ghost: 3
dropped: naming/handlers: no owner would stand behind it
undecided: data/legacy-sql: subject could not be made to match; human to resolve
curation: pending
baseline: not run (human)
render: not run (human)
```

The `check:` line transcribes the last document's exit code and its `summary` (`rulesChecked`,
`rulesPassed`, `rulesFailed`, `rulesSkipped`, `violations`) — a transcription, never a recount.
`evidencePass` reads `incomplete (failedProjects: …; restoreFailedProjects: …)` whenever that
document was stamped `modelIncomplete`. `leftRed`, `dropped` and `undecided` repeat their key
once per rule, `dropped` with the reason, and read `none` when there is nothing to list. The
receipt keeps three claims apart — that the spec builds, that the evidence pass ran on a
complete model, and that a human curated — and none implies another, so the last three lines
stay as written: you stop at the gate and cannot witness your own curation, and the two write
verbs are the human's. Beside the receipt, list each Migrate rule's current violation count and
the dragons you documented.

---

## Authoring reference (condensed)

A spec is one class implementing `IArchitectureSpec` with one method `Define(Arch arch)`, and
three statement forms: definitions, rules, scopes.

`Define` may delegate: selections and constraints are ordinary values, so a method can build and
return them. A spec of a dozen rules stays one method. Past roughly a hundred lines or three
areas, make `Define` the table of contents — the layers and any shared selections, then one call
per area in reading order — with each area a private static method taking `arch` and only the
layers it governs, and the constants an area cites declared beside its method. Nothing the
checker sees changes: call order is declaration order, and every rule still captures its own
line.

<!-- sliderule:begin -->
*Generated from the shipped API by the test suite: each name and its overloads by reflection, each fragment by the sentence renderer over an example operand. Do not edit by hand.*

**Type subjects**

| Noun | Overloads | Fragment |
|---|---|---|
| `arch.Types` |  | Types |
| `arch.Layer` | `(string, Selection)` · `(string, string, …)` | The Domain layer |
| `arch.Namespace` | `(string)` | Types in `MyApp.Legacy.Billing.*` |
| `arch.Project` | `(string)` | Types in project `MyApp.Web` |
| `arch.Type` | `(Type)` · `<T>()` | `SqlConnection` |
| `arch.Registered` | `()` · `(Lifetime)` | Singleton-registered types |
| `arch.AnyOf` | `(Selection, …)` · `(Type, …)` | Types in `MyApp.Tracking.*` or `MyApp.Billing.*` |
| `arch.Each` | `(Layer, …)` · `(ProjectSelection)` | Each of the Host, Adapter and Pack layers |
| `arch.Member` | `(() => …)` · `(Type, string)` · `(x => …)` | `DateTime.Now` |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.InNamespace` | `(string)` | Types in `MyApp.Web.*` |
| `.OfKind` | `(TypeKind)` | Interfaces |
| `.WithSuffix` | `(string)` | Types named `*Controller` |
| `.WithPrefix` | `(string)` | Types named `Legacy*` |
| `.WithNameMatching` | `(string)` | Types whose name matches `*Repo*` |
| `.Named` | `(string, …)` | Types named `OrdersRepository` |
| `.Implementing` | `(Type)` · `(string)` · `<T>()` | Types implementing `IHandler<T>` |
| `.DerivedFrom` | `(Type)` · `(string)` · `<T>()` | Types derived from `ControllerBase` |
| `.AttributedWith` | `(Type)` · `(string)` · `<T>()` | Types attributed with `[ApiController]` |
| `.Except` | `(Selection, …)` · `(Type, …)` | Types, except `HandlerRegistry` |
| `.Where` | `(Func<ITypeInfo, bool>, string)` | Types that are records |
| `.Authored` | `()` | Authored types |
| `.ThatArePublic` | `()` | Public types |
| `.ThatAreInternal` | `()` | Internal types |
| `.ThatArePrivate` | `()` | Private types |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustNotReference` | `(Selection, …)` · `(Type, …)` | must not reference the Web layer |
| `.MustOnlyReference` | `(Selection, …)` · `(Type, …)` | must reference only `SqlConnection` (external packages are not constrained by this rule) |
| `.MustOnlyReferenceItself` | `()` | must reference only itself (external packages are not constrained by this rule) |
| `.MustNotBeReferencedBy` | `(Selection, …)` · `(Type, …)` | must not be referenced by `SqlConnection` |
| `.MustOnlyBeReferencedBy` | `(Selection, …)` · `(Type, …)` | must be referenced only by `SqlConnection` |
| `.MustOnlyBeReferencedByItself` | `()` | must be referenced only by itself |
| `.MustNotReferenceEachOther` | `()` | must not reference the others |
| `.MustNotHaveCircularReferences` | `()` | must not have circular references with the others |
| `.MustNotUse` | `(() => …, …)` · `(Member, …)` | must not use `DateTime.Now` |
| `.MustNotConstruct` | `(Selection, …)` · `(Type, …)` | must not construct `SqlConnection` |
| `.MustNotInject` | `(Selection, …)` · `(Type, …)` | must not inject `SqlConnection` |
| `.MustNotCatch` | `(Selection, …)` · `(Type, …)` | must not catch `InvalidOperationException` |
| `.MustNotCatchUnfiltered` | `(Selection, …)` · `(Type, …)` | must not catch `Exception` without a `when` filter |
| `.MustNotSwallow` | `(Selection, …)` · `(Type, …)` | must not swallow `Exception` |
| `.MustNotThrow` | `(Selection, …)` · `(Type, …)` | must not throw `InvalidOperationException` |
| `.MustOnlyThrow` | `(Selection, …)` · `(Type, …)` | must throw only `InvalidOperationException` |
| `.MustNotExpose` | `(Selection, …)` · `(Type, …)` | must not expose `SqlConnection` |
| `.MustResideInNamespace` | `(string)` | must reside in `MyApp.Web.*` |
| `.MustResideInProject` | `(string)` | must reside in project `MyApp.Web` |
| `.MustBelongTo` | `(Selection, …)` | must belong to the Domain layer or the Web layer |
| `.MustHaveExactlyOneCounterpart` | `(Selection, string)` | must have exactly one counterpart named `I{Name}` among interfaces |
| `.MustBeRegistered` | `()` | must be registered |
| `.MustHaveSuffix` | `(string)` | must be named `*Handler` |
| `.MustHavePrefix` | `(string)` | must be named `I*` |
| `.MustHaveNameMatching` | `(string)` | must have a name matching `*Repo*` |
| `.MustImplement` | `(Type)` · `(string)` · `<T>()` | must implement `IBillingFacade` |
| `.MustDeriveFrom` | `(Type)` · `(string)` · `<T>()` | must derive from `ControllerBase` |
| `.MustBeAttributedWith` | `(Type)` · `(string)` · `<T>()` | must be attributed with `[ApiController]` |
| `.MustNotImplement` | `(Type, …)` · `(string, …)` · `<T>()` | must not implement `IBillingFacade` or `IHandler<T>` |
| `.MustNotDeriveFrom` | `(Type, …)` · `(string, …)` · `<T>()` | must not derive from `ControllerBase` |
| `.MustNotBeAttributedWith` | `(Type, …)` · `(string, …)` · `<T>()` | must not be attributed with `[ApiController]` or `[Serializable]` |
| `.MustBeSealed` | `()` | must be sealed |
| `.MustBeStatic` | `()` | must be static |
| `.MustBeAbstract` | `()` | must be abstract |
| `.MustBePublic` | `()` | must be public |
| `.MustBeInternal` | `()` | must be internal |
| `.Must` | `(Func<ITypeInfo, bool>, string)` | must keep type names at or under 40 characters |

**Member subjects**

| Projection | Overloads | Fragment |
|---|---|---|
| `.Members` |  | Members of types |
| `.Methods` |  | Methods of types |
| `.Properties` |  | Properties of types |
| `.Fields` |  | Fields of types |
| `.Events` |  | Events of types |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.WithSuffix` | `(string)` | Methods of types named `*Async` |
| `.WithPrefix` | `(string)` | Methods of types named `Get*` |
| `.WithNameMatching` | `(string)` | Methods of types whose name matches `*Handler*` |
| `.Returning` | `(Type, …)` · `(string, …)` on `.Methods` | Methods of types returning `Task` |
| `.AttributedWith` | `(Type)` · `(string)` · `<T>()` | `[ApiController]`-attributed methods of types |
| `.ThatAreStatic` | `()` | Static fields of types |
| `.Where` | `(Func<IMemberInfo, bool>, string)` | Methods of types that are async |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustHaveSuffix` | `(string)` | must be named `*Async` |
| `.MustHavePrefix` | `(string)` | must be named `Get*` |
| `.MustHaveNameMatching` | `(string)` | must have a name matching `*Async` |
| `.MustBePublic` | `()` | must be public |
| `.MustBeInternal` | `()` | must be internal |
| `.MustBePrivate` | `()` | must be private |
| `.MustBeStatic` | `()` | must be static |
| `.MustBeAbstract` | `()` | must be abstract |
| `.MustBeVirtual` | `()` | must be virtual |
| `.MustBeAttributedWith` | `(Type)` · `(string)` · `<T>()` | must be attributed with `[ApiController]` |
| `.MustNotBeAttributedWith` | `(Type, …)` · `(string, …)` · `<T>()` | must not be attributed with `[ApiController]` or `[Serializable]` |
| `.MustAcceptParameter` | `(Type)` · `(string)` · `<T>()` on `.Methods` | must accept a parameter of type `CancellationToken` |
| `.MustBeGetOnly` | `()` on `.Properties` | must be get-only |
| `.MustBeReadonly` | `()` on `.Fields` | must be readonly |
| `.Must` | `(Func<IMemberInfo, bool>, string)` | must return a Task |

**Project subjects**

| Noun | Overloads | Fragment |
|---|---|---|
| `arch.Projects` |  | Projects |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.Named` | `(string, …)` | Project `MyApp.Web` |
| `.Matching` | `(string, …)` | Projects matching `MyApp.*` |
| `.Packable` | `()` | Packable projects |
| `.Except` | `(ProjectSelection)` | Projects, except project `MyApp.Tests` |
| `.Where` | `(Func<IProjectInfo, bool>, string)` | Projects that target net8.0 |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustOnlyTarget` | `(string, …)` | must target only `netstandard2.0` |
| `.MustReferenceNoPackages` | `()` | must reference no NuGet packages other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen) |
| `.MustLockPackages` | `()` | must lock package restore |
| `.MustNotBePackable` | `()` | must not be packable |
| `.Must` | `(Func<IProjectInfo, bool>, string)` | must produce a package |

<!-- sliderule:end -->

**Nouns and adjectives** — `arch.Layer(name, glob, ...)` and `arch.Layer(name, selection)` both
define a layer; the selection form (`arch.Project("MyApp.Web")`, say) names exactly what the
selection names. Its optional `.Purpose(prose)` trailer is one sentence on what the layer is for,
rendered into the module map and the layer's card. `arch.AnyOf(a, b, ...)` is the way to say
"these four projects" in one subject; adjectives apply to the union, not through it, and
every operand must match at least one type in subject
position, so a typo'd operand fails loudly. `arch.Each(...)` is a family, one rule over a partition
into cells, each cell a declared layer or a project. It may stand only as a rule subject, adjectives
on it narrow every cell, and the `MustOnly*` reference verbs and the two leaf verbs read "itself" and
"their own" as the cell the type sits in, so `arch.Each(a, b, c).MustOnlyReference(core)` lets each
cell reach itself and Core, never a sibling. `arch.Registered(Lifetime.Singleton)` names service and
implementation alike, and `arch.Registered()` is any lifetime;
`arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped),
arch.Registered(Lifetime.Transient))` is the captive-dependency rule. `arch.Member(typeof(X),
nameof(X.M))` matches by declaring type and member name, so one ban covers every overload. The
expression forms `arch.Member<X>(x => x.M)` and `arch.Member(() => X.M)` verify the pairing at
compile time, and `MustNotUse` takes static lambdas bare (`MustNotUse(() => DateTime.Now)`); none of
them can anchor a compiler-inlined `const`, `enum` member or literal, which take the `typeof`/`nameof`
form. `.Named(name, ...)` is the exemption form: `.Except(arch.Types.Named("SystemClock"))` renders
", except types named `SystemClock`,", and a type the spec project can reference goes straight into
`.Except(typeof(X), ...)`. `.WithPrefix(s)`, `.WithNameMatching(glob)` and `.Where(pred, description:)`
narrow by name or by predicate. `.AttributedWith(attributeType)` reads declared attributes only, on
types and on members alike. `.Authored()` drops generated types on two signals: `[GeneratedCode]` on
the type or its container, or declared only in files a source generator emitted or that open with an
`<auto-generated>` banner. `.ThatArePublic()` / `.ThatAreInternal()` / `.ThatArePrivate()` read
declared accessibility, and `.Except(arch.Types.ThatArePrivate())` is how a subject drops private
nested types.

**Verbs** — `MustNotBeReferencedBy` and `MustOnlyBeReferencedBy` are the inbound pair, and
`MustOnlyBeReferencedByItself()` on a family is the modular-monolith law in one line. `MustNotCatch`
bans every `catch` clause naming the target; `MustNotCatchUnfiltered` reaches only the clauses that
carry no `when` filter, and `MustNotSwallow` only those that neither filter nor end their block in a
`throw`. `MustNotExpose` bans surfacing a type on the public API. `MustResideInNamespace(glob)` and
`MustResideInProject(name)` are the residence pair (one project name, and any declarer satisfies it),
and `MustBelongTo(membership, ...)` is the coverage verb over several memberships (layers, projects,
namespaces), with deliberately no `typeof` membership.
`MustHaveExactlyOneCounterpart(among: selection, named: "I{Name}")` demands, per subject, exactly one
type in `among:` whose name is the template with `{Name}` replaced by the subject's simple name; zero
and several are both red. Write `among:` and `named:` as named arguments at every call site, and union
candidate homes with `arch.AnyOf`. `MustBeRegistered()` is the completeness half beside
`MustNotInject`'s shape half; membership is `arch.Registered()`'s exactly, so a registration the
checker cannot see reds a correctly registered type. `MustImplement`, `MustDeriveFrom` and
`MustBeAttributedWith` take one anchor; `MustNotImplement`, `MustNotDeriveFrom` and
`MustNotBeAttributedWith` take one or more and red a subject matching any of them.
`MustHaveNameMatching(glob)` is the glob form beside the suffix and prefix verbs. The shape verbs
`MustBeStatic`, `MustBeAbstract`, `MustBePublic` and `MustBeInternal` carry C# declaration semantics:
a static class is neither sealed nor abstract, and an interface is abstract.

**Member and project subjects** — The member verbs reuse the type-side names, with four of their own:
`MustBePrivate` and `MustBeVirtual` have no type-side twin, `MustBeGetOnly()` chains only off
`.Properties` and is strict about the declaration (an `init`-only setter is a setter, so it reds), and
`MustBeReadonly()` chains only off `.Fields` (a `const` field satisfies it, const being readonly's
superset). `.ThatAreStatic()` prefixes the subject head ("static fields of ..."). The flagship:
`web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` — *"Methods of types in `MyApp.Web.*`
returning `Task` must be named `*Async`."* `arch.Projects` names the solution's projects as build
artifacts, not sets of types (`arch.Project(name)` is the unrelated type noun).
`.Matching(glob, ...)` reads `*` over one token, because a project name carries no dot-segment
structure, and `.Packable()` is the projects an evaluation reported packable.
`MustOnlyTarget(tfm, ...)` is strict, with no external exemption, because a project's frameworks are
a closed set; `MustLockPackages()` reads `RestorePackagesWithLockFile`; `MustNotBePackable()` makes
an internal project's opt-out checkable, since the SDK defaults `IsPackable` on. Every project fact
is evaluated rather than parsed, so a violation points at the declaration that won, often a
`Directory.Build.props` above the project, and a fact nothing evaluated passes. The flagship:
`arch.Projects.Matching("MyApp.*").Except(arch.Projects.Named("MyApp.Cli")).MustNotBePackable()` —
*"Projects matching `MyApp.*`, except project `MyApp.Cli`, must not be packable."*

The **string overload** on every hierarchy, attribute and member type anchor position — the three
adjectives, the `Must[Not]Implement` / `Must[Not]DeriveFrom` / `Must[Not]BeAttributedWith`
verbs, and `.Returning` / `MustAcceptParameter` on a member subject — is the escape hatch
for a type the spec project cannot compile against, and it renders
byte-identically to the `typeof()` form. Reach for it whenever a reference on the spec project
would be the only reason to add one, and *always* for a **.NET shared framework** type such as an
MVC `ControllerBase`, which no build setting can stage into a spec's output (see the load-failure
message in step 2). It anchors fine either way: only an external **subject** carries a shallow
hierarchy, while a declared subject's base chain and interface closure are walked through metadata,
so `arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` selects your controllers. A
string always names the **definition**, so it reads like the open-generic `typeof` form
(`"IHandler<T>"` matches every construction) and a constructed spelling matches nothing.

The generic twins (`<T>` beside a `typeof` form, and the lambda anchors beside a `typeof`/`nameof`
one) are pure sugar and reify identically; a twin needs the same compile-time reference the
`typeof` does, so where you cannot have one, use the string overload rather than reaching for
`<T>`. An **open** generic has no type-argument form, so it stays `typeof`
(`Implementing(typeof(IHandler<>))`, `.Returning(typeof(Task<>))`), and `.Returning` has no `<T>`
twin at all. An anchor list is all-`typeof` or all-string and never a mix, so a return-type list
that reaches for a shared-framework type is spelled all-string, the open generic written with its
declared type-parameter names exactly as a report prints it:
`.Returning("System.Threading.Tasks.Task<TResult>", "Microsoft.AspNetCore.Mvc.IActionResult")`.
A `Selection` such as `arch.Type<X>()` is not a `Type`.

**Postures** — `arch.Rule(id).Enforce(constraint)` · `arch.Rule(id).Migrate(from:, to:)`
[`.Baseline(path)`] [`.WhileYoureThere(MigrationPolicy.MigrateIfSmall | AlwaysMigrate |
NeverMigrate)`] · `arch.Scope(id).Quarantine(selection)` [`.BoundaryOnlyVia(selections... | types...)`]
[`.Dragons(prose)` / `.DragonsDoc(path)`] [`.Baseline(path)`] · `arch.Scope(id).Caution(selection)`
[`.Dragons(prose)` / `.DragonsDoc(path)`].

**Trailers** — `.Because(prose)` required everywhere; `.Fix(prose)` optional (for containment
it is auto-derived from the facade list); `.Citation(uri)` optional on `Enforce` and `Migrate`
rules — the canonical page the reason rests on, an absolute `http`/`https` URL;
`.Exemplar(selection)` the type to copy, which `check` verifies.

**Escape hatches** — descriptions are required parameters and must be non-blank. `Where`
descriptions are relative clauses continuing the noun ("whose name contains a digit"); `Must`
descriptions are bare-infinitive phrases completing "must …" ("keep type names at or under 40
characters"). The predicate sees `ITypeInfo`: `Name`, `Namespace`, `Kind`, `ProjectName`,
`Accessibility`, `IsSealed`, `IsStatic`, `IsAbstract`, `IsRecord` (how record rules are
written), `FilePaths`, attributes, base type, interfaces.

**Namespace patterns** (dot-segment aware, case-sensitive) — trailing `.*` = the subtree
*including the namespace itself*; interior `*` = exactly one segment; partial `Legacy*` =
within one segment, never crossing a dot; lone `*` = everything. So `MyApp.Domain.*` matches
`MyApp.Domain` and `MyApp.Domain.Orders` but not `MyApp.DomainX`.

**Semantics worth knowing** — "reference" means a source-level type reference; "use" means a
source-level member access; "construct" means a source-level object creation (`new`, including
target-typed `new()`); "inject" means a source-level constructor-parameter dependency (primary
constructors included); "catch" means a source-level `catch` clause (a bare `catch` counts as
`System.Exception`; whether the clause spells a `when` filter, and whether its block ends in a
`throw`, are recorded beside it, so a ban can reach the unfiltered ones — or the swallowing
ones — alone); "throw" means a source-level `throw` of the thrown
expression's static type (bare rethrows `throw;` are not recorded); "expose" means a type named in a public signature position (a public member's return, parameter, or property/field/event type) of an externally visible type: the checker records all seven edge kinds. A construction ban keys the
(source, constructed) type pair (overload-indifferent) and is honest about reflection — a DI
*registration* mints only a type reference, never a construct edge, so a container-resolved type is
not caught; a factory lambda that genuinely `new`s the type IS caught, so `.Except` the sanctioned
composition root or baseline the edge. `Registered` membership comes only from source-visible
registration calls (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/
`AddDbContext`/`AddHttpClient<TClient>`) — assembly scanning, factory-lambda internals, and
framework defaults are invisible; an empty `Registered` subject fails loud (check the visibility
boundary before blaming the code), while an empty `MustNotInject` operand is the win condition
and never warns. Member bans are
source-visibility bans, not runtime-dispatch bans — a ban on a concrete member does not catch
calls through an interface-typed receiver, nor the reverse. Member subjects range over the
declared members of solution-declared types (accessors, constructors, operators, indexers,
and compiler-generated members are excluded; external types carry no member inventory), and
an empty member subject fails the rule exactly like an empty type subject. Type subjects
range over solution-declared types; targets also reach external (BCL/NuGet) types. `MustOnlyReference` constrains solution-declared targets only (external
packages are exempt, and the rendered sentence says so) and allows the subject implicitly, after
any `Except` it spells — list what a layer may reach BEYOND itself, and reach for
`MustOnlyReferenceItself()` where that list is empty. `MustOnlyBeReferencedBy` reads its subject
the same way. On a family (`arch.Each`) both read the subject per cell — the whole layer or
project a type sits in, as declared — so a cell's own `Except`-ed types still count as its own,
and `MustOnlyBeReferencedByItself()` is the leaf where the source list is empty. `MustOnlyThrow` is stricter
still: external thrown types ARE constrained (no type must throw a BCL exception), so its
sentence carries no exemption; `MustNotThrow` is its ban twin, and a spec may carry either or
both. All five exception verbs match their operands exactly — banning `Exception` never flags a
narrower catch or a derived throw, which is the good state — and the three catch verbs key the
same (source, caught) edge, so a baseline entry means the same thing under any of them.
`Implementing`/`DerivedFrom`
are transitive with type-argument substitution; an open generic (`typeof(IHandler<>)`)
matches any construction. `AttributedWith` sees declared attributes only. The `MustNot*`
hierarchy verbs share these three matchers, negated per subject over the anchor list (a
subject reds iff it matches ANY anchor). Hierarchy
adjectives never match external types (their hierarchy is not extracted). Closed generics are
refused in reference positions — ban the open definition and/or the argument type.

## Where to go deeper

- `arch_explain <rule-id>` (CLI: `sliderule explain`) — any rule's because / citation / fix / exemplar /
  posture payload, including desugared `{scope-id}/containment` and `{scope-id}/tripwire` children.
- `arch_context <path>` — the architecture scope cards covering a directory (a quarantined or
  cautioned scope's dragons, a layer's local rules).
- `sliderule status` — the burndown after baselining.
- [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) — the canonical
  fluent-language spec: the table above with each member's pinned semantics, sentence assembly and
  the validation catalog. When a rule needs a form not shown here, look there before improvising.
- The generated `AGENTS.md` block is the always-on summary; this recipe's output is what
  keeps it true.
