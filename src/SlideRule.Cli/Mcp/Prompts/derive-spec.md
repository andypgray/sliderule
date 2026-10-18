# Derive an architecture spec for an existing codebase

Your goal is a **compiling SlideRule spec that states this codebase's architecture honestly, as it is today**: the target rules new code must follow (`Enforce`), the known debt being worked off (`Migrate`), the dragons nothing new may reference (`Quarantine`), and the dragons that stay open to callers (`Caution`). A legacy codebase's architecture is partly descriptive, not prescriptive — a spec that only states the ideal is useless on day one, and a spec that launders the mess into law is worse. The postures exist so you never have to choose between the two.

Work the steps in order. Do not skip the curation gate. Each step ends in an artifact, named on its **Done when** line, and the receipt you close with transcribes those artifacts.

## What this recipe is — and is not (binding, not a disclaimer)

- **This server does not infer the architecture.** There is no "derive" command. *You* derive a proposal from evidence; the deterministic checker validates every claim; the human decides what becomes law. A spec nobody confirmed is worse than no spec — the rendered agent context claims to be provably true, so every rule in it must be something the team actually stands behind.
- **Violations during this flow are data, not failures.** You will deliberately author rules the codebase violates, precisely to measure the violations.
- Everything here uses the read-only tools, chiefly `arch_graph` and `arch_check`, or their identical CLI commands (`sliderule graph` and `sliderule check`; add `--json` for the same documents). Prefer the tools, which are bound to the solution and keep its workspace warm; the CLI is for a document that belongs in a file. The writes that end the flow — `sliderule baseline init`, `sliderule render --model` and the commit — belong to the human. A session launched from the MCP registry manifest has no `sliderule` command: there, prefix every CLI command in this recipe with `dotnet dnx SlideRule.Cli@... --yes --`, run from outside any `global.json` that pins an SDK below 10.

## 0. Collect stated intent: read, then fetch

Architecture the team already wrote down outranks anything you infer, so collect it before the survey. The artifact is the **stated-intent list**: one line per statement, with its words as written, its source (a path in this repository, or a URL) and, from step 3 on, the rule it became.

1. **Read this repository.** Architecture tests (ArchUnitNET, NetArchTest) are rules someone already ratified: carry them over rather than re-deriving them. Read the README, CONTRIBUTING, `.editorconfig` and any analyzer configuration (`*.ruleset`, `.globalconfig`, `*.DotSettings`) whole, not grepped: a coding standard is stated intent wherever it is written. Then `Directory.Build.props` conventions, ADRs, `docs/architecture*`, wiki exports, `AGENTS.md` / `CLAUDE.md` / Cursor rules prose about layers or "do not touch" zones, and the solution folders and project names, which encode intended grouping.
2. **Fetch what it links.** Open every link the README and CONTRIBUTING give to developer documentation (a docs site, a wiki, an architecture page) and read its architecture pages. A project's published documentation is part of the project wherever it is hosted: reading it is reading this codebase, not leaving it. If no fetch tool is in your list, search for one before deciding you cannot fetch: your client may load tools on request. Read docs at the code's version: a page written for another major version than the code, or one removed from the tree, is read at the tag or commit the code is at, through the forge on a shallow clone (`gh api repos/{owner}/{repo}/contents/{path}?ref=<tag>`). Where a site refuses, read the repository it is built from. Where both fail, or you were told not to use the network, the link goes on the list as `unread`, with the reason.
3. **Search where the project records decisions.** For each topic a rule may cover (layers, dependencies, packages, frameworks, naming, extensibility), search the project's issues, pull requests and discussions (a maintainer's merged or declined PR is a ruling); release notes, changelogs and upgrade guides; first-party templates and sibling repositories (a plugin template, the maintainers' analyzer package); and API-approval or public-surface snapshot tests (a ratified law about the public API). Each statement found goes on the list with its URL.
4. **Quote from raw text.** A statement that becomes a rule is copied from the raw file, never from a fetch tool's summary: a summarising fetch can drop the sentence that carries the law. Use `curl -sL` on the raw URL, and `gh api` for a GitHub file, issue, pull request or discussion.
5. **Keep the source with the statement.** A rule drawn from a page carries its URL as `.Citation(url)` (a scope: in its `Because`), and a layer's `.Purpose` is lifted from the line that describes it.

**Already-stated intent wins over inference, once re-read.** Where a statement and the code disagree, that disagreement is not noise. Before calling the code debt, re-read the statement from raw text with its whole paragraph, and date it against the code: `git log -1 --format=%cs -- <path>` on the statement's file and on the code it governs, or the forge's commit history for a page in another repository or a shallow clone. A statement the code contradicts at every site, with no conforming site, is a probable misreading or a stale statement until the re-read says otherwise. A statement that survives the re-read describes the target and the code is the debt, which step 5 writes as `Migrate` and step 6 puts to the human; one that does not is contested, and step 5 drops it for the human.

**Done when** every source items 1 and 3 name has been looked for and every link is read or listed `unread`. The list becomes the receipt's `statedIntent:` lines; with no statement found and no docs link to list, that is the one line `statedIntent: none`.

## 1. Survey the estate

Call `arch_graph` (CLI: `sliderule graph MyApp.sln --json`). It needs no spec, and the solution **restored**, not built: the code is read from source. A project that fails to load comes back as an error naming it: do what it says and retry, and survey a partial model (`allowWorkspaceDiagnostics: true`) only if some project genuinely cannot load, treating every conclusion below as provisional.

The document's keys, exactly (camelCase; an optional field is absent, never null):

```text
projects[]              { name, solutionMember?, targetFrameworks?, factsFollow?, isPackable?, locksPackages?, projectReferences[], undeclaredProjectReferences?, packageReferences[], privatePackageReferences?, packageFrameworks?[]{ package, frameworks[] }, types, generated?, namespaces[]{ namespace, types, generated? } }
projectEdges[]          { source, target, references }
externalEdges[]         { source, targetNamespaceRoot, references }
multiplyDeclaredTypes[] { type, declaredBy[], factsFollow }
shadowedTypes[]         { type, declaredBy, suppliedBy[], boundFromAssemblyBy[] }
unsupportedProjects[]   { project, reason }
```

Read it in this order:

1. `unsupportedProjects[]` first: the declared projects the survey does not cover, each with its reason. No rule can be violated in one, and nothing else flags the gap.
2. `projects[]`: the namespace inventory is your raw material for layer globs. Keep a `solutionMember: false` row out of your layer globs: a passenger a `ProjectReference` dragged in, not part of the estate you are writing law for. `generated` counts the types a generator emitted. **A namespace whose `generated` equals its `types` is wholly generator output: never make it a layer glob and never anchor a rule on it.** Where a project's `generated` is a large share of its `types`, narrow the project noun with `.Authored()`. `factsFollow` marks a multi-targeting project whose frameworks share types, and `isPackable`, `locksPackages` and `packageReferences[]` are evaluated facts: evidence for `arch.Projects` rules.
3. `multiplyDeclaredTypes[]` and `shadowedTypes[]`, before anchoring a subject on a project: `arch.Project(...)` on any declarer selects a multiply declared type, with the facts of the declarer the entry names, and a shadowed name is two types, of which a rule naming it reaches both and `arch.Project(...)` over the declarer only its own.
4. `projectEdges[]`: the observed references. A declared reference with no observed edge is a dead reference, cleanup for the handoff and not a rule; an observed edge you did not expect is the interesting kind.
5. `externalEdges[]`: scan for the classic dangerous externals — `System.Data` (inline SQL), `System.Web` (HttpContext-era coupling), direct driver namespaces, and anything the team says it is migrating away from.

A survey over your client's budget comes back whole at a coarser `grain` rather than cut: narrow it with `projects` globs instead of paging.

From the survey and the stated-intent list, write down **hypotheses, not conclusions**:

- Candidate layers: coherent namespace subtrees or project groups (Domain-shaped, Web-shaped, Infrastructure-shaped), kept disjoint: overlapping layers double-count evidence.
- Candidate direction rules: which layer should never reference which (the `projectEdges` matrix tells you which of those are already true and which are aspirational).
- Candidate conventions: naming patterns the inventory suggests (interfaces, handlers, controllers, repositories).
- Candidate families: like units — plugins, modules, bounded contexts — that should each keep to themselves.
- Candidate dragon zones: "Legacy"/"Old"/"V1" names, a facade-shaped surface guarding a blob, areas with no tests that everything fears. If a spec project already exists in the solution it will appear in the survey like any other project — ignore it.

**Done when** the hypotheses list is written, with the dead references and any other cleanup noted for the handoff.

## 2. Scaffold the spec project

Create a small class library for the spec (convention: an `arch/` folder next to the solution):

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

The four SlideRule packages ship one version in lockstep: reference the version `sliderule --version` prints. Where the repository manages package versions centrally, leave its `Directory.Packages.props` alone — the property above opts this one project out, so the spec keeps its own pin and the central file needs no entry for it.

**A `Directory.Build.props` above the spec project applies to it too.** Where it adds `PackageReference` items whose versions live in the central file (MinVer, SourceLink, analyzers), the opt-out leaves them versionless, and restore fails with NU1015 naming packages the spec never asked for. Switch them off in the spec csproj with the property the props file conditions them on (one repository spells it `UseBuildTimeTools`), or `<PackageReference Remove="…" />` them there; never turn central management back on.

**Pick the TFM by one rule: the spec project must be able to reference the product projects it will `typeof()`.** On a `net48` estate, the spec targets `net48` — the SlideRule contract library is `netstandard2.0` precisely so that works. Several frameworks go in the plural `<TargetFrameworks>` element. **Read `global.json` before keeping `net10.0`:** a pin below SDK 10 builds the spec project with that SDK, which cannot target `net10.0` (NETSDK1045), so target what the product projects target (`net9.0`, say) and leave the pin alone.

`typeof()` targets must also be **accessible** to the spec assembly: anchoring an `internal` type fails the spec build with CS0122. Every set-valued position has a spelling that needs no load and no reference, so reach for one first: a namespace pattern (`arch.Namespace("System.Data.*")`), `arch.Types.Named("BillingFacade")`, or the fully-qualified name as a string where `Implementing` / `DerivedFrom` / `AttributedWith` take a type. Only where the type must genuinely be reflected — an `arch.Member(typeof(X), nameof(X.M))` anchor — have the product project grant `[InternalsVisibleTo("MyApp.Architecture")]` and rebuild.

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

Name the project — and with it its root namespace — so the **last segment is not `Arch`**: inside a namespace ending in `.Arch`, the simple name `Arch` binds to that namespace rather than to the `Arch` type `Define` takes, and the spec does not compile (CS0118, verbatim below). `MyApp.Architecture` is safe, and so is the `arch/` folder — only the namespace segment collides.

Add it to the solution (`dotnet sln add arch/MyApp.Architecture/MyApp.Architecture.csproj`) and build. Spec discovery is by convention: **the unique solution project that references `SlideRule.dll`** — via the package or via a project reference. As a solution member, the spec project is excluded from the checked universe — its own types never trip your rules.

**Expect `dotnet sln add` to rewrite more of a `.sln` than the one project it adds.** It writes an `ActiveCfg`/`Build.0` pair for every project under every configuration × platform combination, its default `x64` and `x86` included, and that rewrite is legitimate, not damage. Do not trim it back to a hand-written entry mapping only `Debug|Any CPU` and `Release|Any CPU`: a solution build under any other declared combination skips a project with no mapping for it — MSBuild names it in an `MSB4121` warning and still exits 0, so the build stays green while `check` reads a stale spec assembly, or none at all.

**Source-checkout setups only**: `dotnet sln add` follows project references, so it may also add the SlideRule contract library to the solution, and **solution membership decides whether its types come under your rules**: declared, it is checked like any other member; riding in only as the spec's project reference, it is not. The survey lists it either way, so ignore it there.

Errors you may see, verbatim, and what they mean:

- `error NU1008: The following PackageReference items cannot define a value for Version: SlideRule. Projects using Central Package Management must define a Version value on a PackageVersion item.` — NuGet's, not this tool's: the `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` line is missing from the spec csproj. Put it back rather than adding a `PackageVersion` entry to the central file: the spec's pin moves with the tool, not with the repository's own dependencies.
- `error CS0118: 'Arch' is a namespace but is used like a type`, beside a CS0535 that the spec class does not implement `IArchitectureSpec.Define(Arch)` — the compiler's, so neither names the cause: the namespace ends in `.Arch`. Rename the project and namespace; qualifying `SlideRule.Arch` in the signature also compiles, but leaves the shadow armed for every file the spec grows.
- `The spec assembly '…' failed to load its dependency '…' while running Define().` — a `typeof()` anchor names a type whose assembly the loader cannot reach, and the message's second line says which case you are in. A **NuGet package** is staged by the scaffold's `CopyLocalLockFileAssemblies` plus a rebuild. A **.NET shared framework** type, anything reached through `<FrameworkReference Include="Microsoft.AspNetCore.App" />` such as an MVC `ControllerBase`, never is, and **no build setting can help**: anchor it by string (`.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` renders exactly like the `typeof()` form) or by namespace pattern.
- `The spec project '…' has no built output`: nothing here builds — the code is read from source and the spec from its project's last build, so rebuild that project after each spec edit.
- `No spec project found` or `Multiple spec projects found`: the refusal names its cause and its cure, so do what it says; `--spec path/to/YourSpec.csproj` names or picks a spec project, but cannot repair a load or a restore.

**Done when** the spec project builds with no errors. The receipt's `specBuild:` reports the last build, after step 5's edits.

## 3. Draft candidate rules — all as `Enforce`, all of them hypotheses

Turn every step-1 hypothesis and every stated-intent line the language can express into a rule, and write the rule's ID onto the line it came from. Do not pre-judge postures — the checker assigns the evidence in step 4; postures come in step 5. **Author the already-true directions too**: every layer pair the survey's edge matrix shows clean becomes an Enforce candidate — the cheapest law you will ever get. Where a project reference already runs the other way, MSBuild forbids the pair as a cycle, so state the direction as `core.MustOnlyReference(shared)` or `core.MustOnlyReferenceItself()` instead: that also binds the next project added.

Define each layer by whatever honestly says where it is. A namespace cone where the namespaces are disciplined; `arch.Project("MyApp.Web")` where the unit of architecture is the assembly, or where the survey's `multiplyDeclaredTypes[]` or a shared `RootNamespace` says the namespaces do not separate the projects; a refinement — `core.InNamespace("MyApp.Core.Model.*")` — where one cone nests inside another and a glob would swallow it. A layer names exactly what its definition names and checks identically to it, so the choice costs nothing at the checker and buys the row, the card and the collective voice.

Give each layer a `.Purpose(prose)`: one sentence on what the layer is for, lifted from the stated-intent line that already says it. It renders into the layer's module-map row and its card, so the sentence that rotted in a document now sits beside the definition it describes and re-renders with it.

```csharp
Layer domain = arch.Layer("Domain", "MyApp.Domain.*").Purpose("Domain holds the order and customer model.");
Layer web    = arch.Layer("Web",    arch.Project("MyApp.Web")).Purpose("Web is the HTTP surface: controllers and the views they serve.");

arch.Rule("layering/domain-independent")
    .Enforce(domain.MustNotReference(web))
    .Because("Candidate: the architecture page says the model knows nothing of the web tier.")
    .Citation("https://docs.example.org/developer/architecture");

arch.Rule("data-access/no-inline-sql")
    .Enforce(web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
    .Because("Candidate: externalEdges shows System.Data reached from Web.");
```

- One hypothesis per rule — a rule is one sentence; compound requirements are multiple rules.
- IDs are `area/rule-name`, matching `^[a-z0-9-]+(/[a-z0-9-]+)*$`. They are permanent handles (baseline keys, violation citations), so name them as if they will survive — they will.
- `Because` is **required** and will not pass spec build when blank. Write each as a candidate note ("Candidate: …") and leave it that way: the real reason is the human's to give at step 6.
- Anchor a rule on the `Layer` handle wherever one exists (`web.MustNot…`, not `arch.Project("MyApp.Web").MustNot…`): the two check identically, but only the layer-anchored one renders on that layer's card and module-map row.
- Keep product-wide subjects off the test projects: a test project inside the product's root namespace (`MyApp.Tests.*` inside `MyApp.*`) is solution-declared, so an `.InNamespace("MyApp.*")` subject sweeps its fakes and helpers into naming, member and hierarchy rules. Anchor on `arch.Project("MyApp")`, or a union such as `arch.AnyOf(arch.Project("MyApp"), arch.Project("MyApp.Data"))`, rather than a namespace cone minus an `.Except`.

Then draft one candidate for each question the codebase answers yes to, and answer every question on a receipt `candidate:` line under its label:

- **`layering`: Which layer must never reach which?** `domain.MustNotReference(web)`, one rule per pair, for every clean pair no project reference already forbids and every pair a stated-intent line names. Not for peers whose order nobody has stated: that is the family law below.
- **`externals`: Which externals are banned, and from where?** `web.MustNotReference(arch.Namespace("System.Data.*"))`. Use a namespace pattern, not `typeof`: a `typeof` target that does not exist passes silently (absence is the win condition), while a pattern that matches nothing raises an inert warning — the misspelling check comes free. Not for an external the code reaches everywhere on purpose.
- **`naming`: Do names follow a convention?** `arch.Project("MyApp").OfKind(TypeKind.Interface).MustHavePrefix("I")`, handler types `MustHaveSuffix("Handler")`, `MustBeSealed()` where the convention looks intended, after asking which way inheritance runs: virtual members and protected fields mean the contract is derivation, and a seal rule reds the design. Not for a pattern a few types share by accident.
- **`async`: Are async methods named `*Async`?** `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async")`, where the codebase names them so. **On an MVC estate, take the controllers out first** — `web.Except(arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")).Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async")`, or `System.Web.Mvc.Controller` on .NET Framework — because action methods are named for the routes they serve and usually carry no `Async` suffix, and they can be nearly every violation the rule reports.
- **`cancellation`: Do `Task`-returning methods take a `CancellationToken`?** `web.Methods.Returning(typeof(Task), typeof(Task<>)).MustAcceptParameter(typeof(CancellationToken))`, where most already do. Matching is declaration-level and exact (a `CancellationToken?` parameter does not count), and whether a body *flows* the token is not this rule's question.
- **`members`: Which members are banned?** `domain.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow)` where timestamps persist or compare and a clock seam exists; likewise blocking waits on async paths (`arch.Member(typeof(Task), "Wait")`) and ambient configuration (`Environment.GetEnvironmentVariable`) where a settings type exists. Not for a member the code has no alternative to.
- **`attributes`: Must a set of types or actions carry an attribute?** `web.WithSuffix("Controller").MustBeAttributedWith("MyApp.Web.AuthorizeAdminAttribute")`, where a guide prescribes one (authorization, area, antiforgery) or nearly every member of the set carries it; on actions, `web.Methods.AttributedWith("Microsoft.AspNetCore.Mvc.HttpPostAttribute").MustBeAttributedWith(…)`. Not for what a framework convention applies unwritten.
- **`construction`: Must a set of types arrive through one root?** `arch.Types.Except(arch.Type<HandlerRegistry>()).MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>)))`: handlers or services resolved through a container may be *referenced* but never *constructed* outside the root the `.Except` names. Not for plain values the code is meant to `new`.
- **`captive`: Does a singleton capture a shorter-lived service?** `arch.Registered(Lifetime.Singleton).MustNotInject(arch.Registered(Lifetime.Scoped))`. Add `arch.Registered(Lifetime.Transient)` only where the transients are disposable or hold state: a stateless transient held by a singleton is harmless. **Registrations are read from every solution project, test projects included**, and membership is per type, so a test host that registers a product service as a singleton makes it one for this rule. Before calling a hit captive, find where its singleton registration is; where only a test project makes it one, `.Except` that type (or the test project's own types) and say so in the `Because`. A test registration can put a type on the operand side the same way, so check both ends of a hit before calling it captive. Not where registration goes through calls the checker cannot see (Semantics, below): an empty-subject failure here means exactly that, so drop the rule rather than guessing.
- **`family`: Is there a family of like units — plugins, modules, bounded contexts, one project or one layer each?** The law that every unit keeps to itself is one rule over the family, not one per unit. `arch.Each(arch.Projects.Matching("MyApp.Plugin.*")).MustNotReferenceEachOther()` says no plugin reaches another; `arch.Each(dispatch, tracking, invoicing).Except(<each module's Contracts cone>).MustOnlyBeReferencedByItself()` says each module's internals are reached only from its own module; `arch.Each(a, b, c).MustNotHaveCircularReferences()` says the units may reference each other but never in a circle — the law for peers whose order nobody has stated, and the first law to write on an estate before its order is known (layers only: MSBuild already forbids a circle among projects). On a family, "itself" and "their own" mean the cell — the whole layer or project the type sits in, as declared — so the `MustOnly*` verbs read the same way per cell, and the survey's edge matrix over the family's projects is the evidence. One rule, one ID, one baseline; write one rule per cell only where each cell needs its own `Because` or `Fix`. To excuse some types, `.Except` them from the family rather than dropping their cell: a dropped cell is guarded by nothing.
- **`dragons`: Is there a dragon zone?** Dragon-zone candidates are the one exception to "all as Enforce": a scope has no Enforce form, so draft them as `arch.Scope(id).Quarantine(...)` directly (step 5 shows the full shape of both scope postures). The scope's containment violations arrive in step 4 alongside every other rule's evidence, and they are what decides in step 5 whether the scope stays a `Quarantine` or becomes a `Caution`. Not for code that is merely old: a dragon zone is one a change would break in a way the code does not show.

Build the spec. Spec-build validation reports **every** error at once (missing `Because`, malformed IDs, dangling rules, blank prose) — fix them in one pass.

**Done when** the draft spec builds, every hypothesis and every stated-intent line carries a rule ID or a note saying why none, and every question above has its `candidate:` line: the rule IDs it produced, or `none:` with the survey count or statement that answers no. A spec of layering rules alone is not done while another question has no line.

## 4. Check = the evidence pass

Run `arch_check` (CLI: `sliderule check MyApp.sln --json`; exit 1 is expected — violations are the data). Each entry of `rules[]` carries `violationCount`, and at full grain each violation's `file:line` sites.

- The **authoring signals** are about your selection, not the code. A violation of kind `emptySubject` ("The subject selection matched no solution-declared types.") fails the rule; the warning "This rule is inert: its target selection matched no types." leaves a rule that passes forever. Its twins name one dead pattern: "This rule is partly inert…" a target, "…stricter than written…" an allow-list or a membership, "…not measured as written…" an `among:` part. Each carries a `hint` (a `hint:` line in the report) with the cure read off the selection that came up empty — a glob, a project name and a `typeof` anchor each fail differently — so apply the hint rather than guessing, and where an inert target names a layer that genuinely declares no types yet, decide before keeping the rule.
- A document stamped `modelIncomplete: true` ran on a partial model: restore and build the projects its `failedProjects` and `restoreFailedProjects` name before reading any verdict as evidence.
- An **error result** (not a violations document) means the run itself failed — an unresolvable or unbuilt spec, or spec validation errors. Fix, rebuild, rerun.

Iterate selections until the failures that remain are *genuine* — real edges, real nonconforming names — against a count you can read off the document: per rule, its `emptySubject` violations plus its inert and partly inert warnings. Rebuild the spec project before every re-check: the check reads its last build, so a re-check of an unbuilt edit shows no progress. Re-check while that count reaches a new low; when two consecutive re-checks leave a rule's count at its best so far, stop working that rule and carry it to step 6 as `undecided`, while every other rule keeps iterating on its own count. On a big solution, narrow with `rules` globs (`rules: "data-access/*"`; CLI `--rules`). A report over your client's budget comes back whole at a coarser grain, down to `index`: every rule ID with its verdict, the menu those globs pick from. `arch_explain` returns any one rule whole.

**Done when** every rule's signal count is zero or the rule is `undecided`. That pass is the receipt's `undecided:` lines; `evidencePass:` is read off the last whole check.

## 5. Write each posture into the spec, rebuild, re-check

The step-4 counts decide each rule's posture, and the posture goes into the code now, not into a table: **`Enforce` carries no baseline**, so `baseline init` cannot capture an `Enforce` rule, and a red one stays red for good. For each rule:

- **Zero violations → `Enforce`.** The codebase already obeys — on a complete model only: a check stamped `modelIncomplete` has *not observed* the rule. Keep the rule exactly as drafted.
- **Violations → `Migrate`**, the default once the sites are read: it keeps the evidence and costs the team nothing until it decides. Before choosing it, open a sample of the rule's sites: a few, and every site where there are few. A site the code marks as deliberate is an exception, not debt: a comment saying why, a `#pragma`, a vendored-file banner, a maintainer's merged PR. Write it as `.Except(...)` on the subject; where nothing else is red, the rule stays `Enforce`. Only what remains goes to `Migrate`. Rewrite as a pair:

  ```csharp
  arch.Rule("data-access/no-inline-sql")
      .Migrate(
          from: "Controllers reach System.Data directly (legacy inline-SQL style).",
          to: web.WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
      .Because("Repository pattern for testability — ADR-012.")
      .Fix("Inject the repository.")
      .Exemplar(arch.Types.Named("OrdersRepository"));
  ```

  `from:` is **descriptive prose about the OLD pattern, stated factually** — never aspiration, never blame. The current violations become the baseline the human captures in step 7, and new code in the old pattern goes red from then on. The boy-scout policy defaults to `MigrateIfSmall` (override with `.WhileYoureThere(...)`), and the baseline path to `arch/baselines/<rule-id>.json`.

- **A region with no target state, which nothing new may reference → `Quarantine`.** No one will fix it; the enforceable thing is the boundary:

  ```csharp
  arch.Scope("legacy/billing")
      .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
      .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
      .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
               "Nightly reconciliation depends on this. Do not normalize.")
      .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");
  ```

  **List the facade implementation type(s) in `BoundaryOnlyVia` alongside the interface** — the composition root's DI registration references the concrete type, and forgetting it puts that registration red on day one. Omit `BoundaryOnlyVia` entirely for a hermetic quarantine.

  **The boundary takes selections, not only types**, so a facade the spec cannot compile against is still law: `.BoundaryOnlyVia(arch.Types.Named("IBillingFacade", "BillingFacade"))` for an `internal` facade or one in a project the spec does not reference, `.BoundaryOnlyVia(arch.Namespace("MyApp.Legacy.Billing.Contracts.*"))` where the surface is a region. The formula does not care whether the surface is inside the scope or outside it, so a legacy region's one sanctioned *caller* is spelled the same way. Write that caller into the boundary rather than grandfathering it, or the rendered law will call your sanctioned entry point debt to burn down while the dragons prose says the opposite.

  `Dragons` must carry three things: what the code does, **which weirdness is structural** (the behavior a "fix" would break), and the sanctioned interaction surface. "Don't touch, it's bad" is not dragons prose — agents still have to call into this code. Check each claim against the code or its history before writing it: every agent after you reads dragons prose as fact, so a claim the code contradicts is worse than none.

  A Quarantine desugars to two checkable rules under the scope ID: `{id}/containment` (red on any new reference into the scope not via the facade; existing inbound references get grandfathered in step 7) and `{id}/tripwire` (a diff-aware warning, *skipped* without `--diff-base`).

- **Structural weirdness that new callers are welcome to → `Caution`.** The decision rule between the two scope postures is whether anything new may reference the code: nothing new may reference it → `Quarantine`; new callers welcome → `Caution`. Code nothing references by type (migrations, plugins found by reflection) gives containment nothing to fire on, so it takes `Caution`. A shared utility, a God class, a native-interop layer that is ordinary public API: containing it would grandfather every legitimate caller as debt and red the next one, and the card is still worth having.

  ```csharp
  arch.Scope("interop/native-signatures")
      .Caution(arch.Namespace("MyApp.Native.*"))
      .Dragons("Every DllImport here mirrors the C header byte for byte, and a wrong width is silent " +
               "memory corruption rather than a compile error. Add signatures beside the existing ones " +
               "and keep every marshalling attribute; call through the wrappers, never the imports.")
      .Because("The native layer is public API for the whole solution; the compiler checks none of it.");
  ```

  `Dragons` carries the same three things a quarantine's does. A Caution desugars to one checkable rule, `{id}/tripwire`: nothing is ever red, and `render` puts the dragons on the scope's directory as a card. A caution that later gains a facade becomes a `Quarantine` under the same ID.

- **A statement contested by the code or its maintainers → drop the rule**, with a reason opening `contested:` that names the evidence. Contested means stale against the code, applied by its maintainers more loosely than written, or contradicted at every site after step 0's re-read. It goes to the human in the curation table, posture `dropped`, and never to `Migrate`; its `statedIntent:` line still names the rule ID.
- **Nothing the team would stand behind → drop the rule**, with the reason for `dropped:`.
- **Work the team has said it is doing now, or a rule step 4 left `undecided` → leave it as drafted** (`Enforce`, or the scope as written). The team has said so only in a stated-intent line or in the human's own words this session; a red with neither takes the posture its evidence sets, `Migrate`. Which of these reach `leftRed:` is the last check's to say, through the receipt's transcription rule.

Then rebuild the spec project and run the whole check again. That document is what the receipt transcribes, and it is this step's test: every rule it shows with `posture: enforce` and `status: failed` must be one of the last bullet's two kinds. Any other is a miss: re-posture it, fence it or drop it, rebuild, and check again. A different posture you would argue for belongs in the curation table's note; the code carries the one the evidence set, the sampled sites and the re-read statement included.

**Done when** the last whole check passes that test.

## 6. The curation gate — **do not guess**

Stop. Present every rule to the human with its evidence, one row per rule: ID, posture, the rule sentence, violation count, one example `file:line`, your draft `Because`, and a note. The posture column is what the last check document shows for the rule, or `dropped`, or `undecided` — never a proposal; a posture you would argue for instead goes in the note. The human accepts, edits, or drops **each rule individually**. Where a stated-intent line and the code disagree, the human decides, not you. A rule the step-4 loop could not make match takes `undecided` in the posture column, and in place of its evidence `subject could not be made to match; human to resolve`: it is never dropped for the human and never kept silently.

What waits for the human is each `Because`, not the postures. Every kept rule keeps its `Candidate: …` `Because` until the human has decided it, and you never write a `Because` the human would not say in a design review — it renders into the generated context and into every violation message as the team's stated rationale. Postures and selections are already written in the code, as the evidence set them. When the human moves one, edit the spec, rebuild, re-check, and transcribe the receipt again.

**Done when** the table is in front of the human. You cannot witness the curation, so the receipt reads `curation: pending`.

## 7. The human baselines the remainder

With the curated spec built and checked, the remaining reds are the acknowledged debt: each Migrate rule's current violations and each quarantined scope's existing inbound references. The human records them:

```
sliderule baseline init MyApp.sln
```

**`baseline init` is run by the human, never by you.** The baseline is the team's signature on its debt — an attributed, reviewable artifact, not an agent convenience. It captures every uncaptured ratcheted rule's *current* violations in one pass (a rule whose selection matched nothing, or that holds a dead target pattern, is skipped and named), writing them under `arch/baselines/` — everything red right now, including code written five minutes ago; day zero is the one moment "current" and "accepted" coincide, which is why curation comes first. From then on baselined violations pass, new ones go red, and the file only shrinks (`baseline accept-reductions`).

It also names each rule failing with no baseline to capture, and that list must be exactly your `leftRed:` lines. A re-check then expects exit 0, `rulesFailed: 0`, with the grandfathered counts visible; if reds were left deliberately, or a scope is `undecided`, it still exits 1, on those alone.

**Done when** the human has run it. Until then the receipt reads `baseline: not run (human)`.

## 8. Render and commit

Have the human run `sliderule render --model MyApp.sln`: it writes the managed block into the root `AGENTS.md` (everything outside the markers is preserved byte-for-byte), drops a second managed `AGENTS.md` into each scope's directory (the dragons card) and into each layer's directory when rules are anchored on that layer (the local-rules card), and writes `sliderule.model.json` beside the solution: the file the `SlideRule.Analyzers` package reads to report a broken rule as a compiler warning in each governed project's build. If a `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` sits in the solution directory or above it (a `~/.claude/CLAUDE.md` of the user's own does not count), Claude Code reads that instead of `AGENTS.md`, and `render` warns: propose a `CLAUDE.md` beside `AGENTS.md` holding the line `@AGENTS.md`, and leave that edit to the human.

After the curation table, hand the human the next steps as one block; its analyzer line follows [Rolling out the build analyzer](https://github.com/andypgray/sliderule/blob/main/OPERATING.md#rolling-out-the-build-analyzer):

```text
next: after your edits, build the spec project (dotnet build arch/MyApp.Architecture/MyApp.Architecture.csproj): every command below reads its last build
next: sliderule baseline init MyApp.sln
next: sliderule render --model MyApp.sln
next: if render warns that a CLAUDE file shadows AGENTS.md, add a CLAUDE.md beside AGENTS.md holding the line @AGENTS.md
next: commit as one reviewable diff: the spec project, arch/baselines/**, sliderule.model.json, the rendered AGENTS.md files, and the .sln configuration-table expansion from step 2, which is expected and not to be trimmed by hand
next: roll out the build analyzer: add SlideRule.Analyzers at the tool's version to the governed projects (from a Directory.Build.props, or a GlobalPackageReference under central management); where they lock packages, run dotnet restore --force-evaluate and commit the lock files, or the next locked restore fails with NU1004; see https://github.com/andypgray/sliderule/blob/main/OPERATING.md#rolling-out-the-build-analyzer
next: in CI, check with --diff-base <the pull request's base ref>, from a checkout deep enough to resolve it; without it every scope's tripwire is skipped rather than clean, so the code fenced or flagged because it is dangerous to edit is the one thing CI never mentions
```

**Done when** the human has rendered and committed. Until then the receipt reads `render: not run (human)`.

Close with a receipt in exactly this shape, one key per line, so a reader — or a script — finds each claim where it expects it:

```text
statedIntent: CONTRIBUTING.md: "Controllers never open a connection" -> data-access/no-inline-sql
statedIntent: https://docs.example.org/developer/architecture: "the model knows nothing of the web tier" -> layering/domain-independent
statedIntent: unread: https://wiki.example.org/design (site refused; no source repository)
candidate: layering -> layering/domain-independent, layering/no-ghost
candidate: externals -> data-access/no-inline-sql, data/legacy-sql
candidate: naming -> naming/handlers
candidate: async -> naming/async-suffix
candidate: cancellation -> none: 3 of 212 Task-returning methods take a CancellationToken
candidate: members -> time/no-wall-clock
candidate: attributes -> none: no guide prescribes one; 12 of 40 controllers carry [Authorize]
candidate: construction -> none: no container; services are constructed where used
candidate: captive -> none: no registration call in source
candidate: family -> none: no set of like units
candidate: dragons -> legacy/billing, interop/native
specBuild: ok
evidencePass: complete
check: exit 1, rules 23, passed 12, failed 9, skipped 2, violations 70
postures: enforce 14 / migrate 6 / quarantine 2 / caution 1
leftRed: layering/no-ghost: 3
leftRed: data/legacy-sql: 1
dropped: naming/handlers: no owner would stand behind it
dropped: layering/domain-independent: contested: the model references Web at every site, and the page predates the Web project
undecided: data/legacy-sql: subject could not be made to match; human to resolve
curation: pending
baseline: not run (human)
render: not run (human)
```

`check:`, `postures:` and `leftRed:` transcribe one document: the last whole check, run after the final rebuild with no `rules` filter. Every rule in it carries `posture`, `status` and `violationCount` at every grain, so a coarse report serves.

- `check:` is that document's exit code (over MCP, which returns none: 1 when `rulesFailed` is above zero, else 0; the CLI exits 2 first on a document stamped `modelIncomplete`) and its `summary` (`rulesChecked`, `rulesPassed`, `rulesFailed`, `rulesSkipped`, `violations`) — a transcription, never a recount.
- `postures:` counts its `rules[]` by `posture`, each entry once, so the four numbers add up to the `rules` on the `check:` line. A Quarantine scope is two entries (`{id}/containment` and `{id}/tripwire`), a Caution one (`{id}/tripwire`).
- `leftRed:` is every entry with `posture: enforce` and `status: failed`, with its `violationCount`: the reds no baseline can hold. Each is work the team has said it is doing now, or an `undecided` rule; any other sends you back to step 5.
- `evidencePass` reads `incomplete (failedProjects: …; restoreFailedProjects: …)` whenever that document was stamped `modelIncomplete`.
- `statedIntent:` is the step-0 list, `<source>: "<statement>" -> <rule ID, or why none>`. Every developer-docs link appears on at least one line: as a source, as `read: <url> (no architecture stated)`, or as `unread: <url> (<why>)`.
- `candidate:` is step 3's answers, one line per question label, every label once: `<label> -> <rule IDs>`, or `<label> -> none: <what answers no>`.
- `statedIntent`, `leftRed`, `dropped` and `undecided` repeat their key once per entry, `dropped` with the reason (opening `contested:` for a contested statement), and read `none` when there is nothing to list.

The receipt keeps three claims apart — that the spec builds, that the evidence pass ran on a complete model, and that a human curated — and none implies another, so the last three lines stay as written: you stop at the gate and cannot witness your own curation, and the two write commands are the human's. Beside the receipt, list each Migrate rule's current violation count and the dragons you documented.

---

## Authoring reference (condensed)

A spec is one class implementing `IArchitectureSpec` with one method `Define(Arch arch)`, and
three statement forms: definitions, rules, scopes. The full language, with each member's pinned
semantics, is [GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md): look there
before improvising a form not shown here.

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
| `arch.AnyOf` | `(IEnumerable<Selection>)` · `(IEnumerable<Type>)` · `(Selection, …)` · `(Type, …)` | Types in `MyApp.Tracking.*` or `MyApp.Billing.*` |
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
| `.Under` | `(string, …)` | Types under `src/Widgets` |
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
| `.MustNotUse` | `(() => …, …)` · `(IEnumerable<Member>)` · `(Member, …)` | must not use `DateTime.Now` |
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
| `.WithSuffix` | `(string)` | `*Async` methods of types |
| `.WithPrefix` | `(string)` | `Get*` methods of types |
| `.WithNameMatching` | `(string)` | `*Handler*` methods of types |
| `.Named` | `(IEnumerable<string>)` · `(string, …)` | `Main` methods of types |
| `.Returning` | `(Type, …)` · `(string, …)` on `.Methods` | Methods returning `Task` declared in types |
| `.AttributedWith` | `(Type)` · `(string)` · `<T>()` | `[ApiController]`-attributed methods of types |
| `.ThatAreStatic` | `()` | Static fields of types |
| `.ThatArePublic` | `()` | Public methods of types |
| `.ThatAreInternal` | `()` | Internal methods of types |
| `.ThatAreProtected` | `()` | Protected fields of types |
| `.ThatArePrivate` | `()` | Private fields of types |
| `.ThatAreProtectedInternal` | `()` | Protected internal methods of types |
| `.ThatArePrivateProtected` | `()` | Private protected methods of types |
| `.Except` | `(IEnumerable<MemberSelection>)` · `(MemberSelection, …)` | Methods of types, except `Main` methods of types named `Program` |
| `.Where` | `(Func<IMemberInfo, bool>, string)` | Methods that are async declared in types |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustHaveSuffix` | `(string)` | must be named `*Async` |
| `.MustHavePrefix` | `(string)` | must be named `Get*` |
| `.MustHaveNameMatching` | `(string)` | must have a name matching `*Async` |
| `.MustBePublic` | `()` | must be public |
| `.MustBeInternal` | `()` | must be internal |
| `.MustBeProtected` | `()` | must be protected |
| `.MustBePrivate` | `()` | must be private |
| `.MustBeStatic` | `()` | must be static |
| `.MustBeAbstract` | `()` | must be abstract |
| `.MustBeVirtual` | `()` | must be virtual |
| `.MustBeOverridable` | `()` | must be overridable |
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
| `.Named` | `(IEnumerable<string>)` · `(string, …)` | Project `MyApp.Web` |
| `.Matching` | `(IEnumerable<string>)` · `(string, …)` | Projects matching `MyApp.*` |
| `.Packable` | `()` | Packable projects |
| `.Except` | `(ProjectSelection)` | Projects, except project `MyApp.Tests` |
| `.Where` | `(Func<IProjectInfo, bool>, string)` | Projects that target net8.0 |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustOnlyTarget` | `(string, …)` | must target only `netstandard2.0` |
| `.MustTarget` | `(string)` | must target `netstandard2.0` |
| `.MustReferenceNoPackages` | `()` | must reference no NuGet packages other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen) |
| `.MustOnlyReferencePackages` | `(string, …)` | must reference only NuGet packages matching `Microsoft.Bcl.*`, other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen) |
| `.MustNotReferencePackages` | `(string, …)` | must not reference NuGet packages matching `Newtonsoft.Json` (declared references only; transitive dependencies are not seen) |
| `.MustSetProperty` | `(string, string)` | must set `GenerateDocumentationFile` to `true` |
| `.MustLockPackages` | `()` | must lock package restore |
| `.MustNotBePackable` | `()` | must not be packable |
| `.Must` | `(Func<IProjectInfo, bool>, string)` | must produce a package |

<!-- sliderule:end -->

**Nouns and adjectives** — `arch.Layer(name, glob, ...)` and `arch.Layer(name, selection)` both
define a layer. `arch.AnyOf(a, b, ...)` is the way to say
"these four projects" in one subject; adjectives apply to the union, not through it, and
every operand must match at least one type in subject
position. `arch.Each(...)` is a family, one rule over a partition
into cells, each cell a declared layer or a project. It may stand only as a rule subject, adjectives
on it narrow every cell, and `arch.Each(a, b, c).MustOnlyReference(core)` lets each cell reach itself
and Core, never a sibling. `arch.Registered(Lifetime.Singleton)` names service and implementation
alike, and `arch.Registered()` is any lifetime. `arch.Member(typeof(X),
nameof(X.M))` matches by declaring type and member name, so one ban covers every overload. The
expression forms `arch.Member<X>(x => x.M)` and `arch.Member(() => X.M)` verify the pairing at
compile time, and `MustNotUse` takes static lambdas bare (`MustNotUse(() => DateTime.Now)`); none of
them can anchor a compiler-inlined `const`, `enum` member or literal, which take the `typeof`/`nameof`
form. `.Named(name, ...)` is the exemption form: `.Except(arch.Types.Named("SystemClock"))` renders
", except types named `SystemClock`,", and a type the spec project can reference goes straight into
`.Except(typeof(X), ...)`. `.WithPrefix(s)`, `.WithNameMatching(glob)` and `.Where(pred, description:)`
narrow by name or by predicate, and `.Under("src/Widgets")` by folder, relative to the solution directory. `.AttributedWith(attributeType)` reads declared attributes only, on
types and on members alike. `.Authored()` drops generated types on two signals: `[GeneratedCode]` on
the type or its container, or declared only in files a source generator emitted or that open with an
`<auto-generated>` banner. `.ThatArePublic()` / `.ThatAreInternal()` / `.ThatArePrivate()` read
declared accessibility, and `.Except(arch.Types.ThatArePrivate())` is how a subject drops private
nested types.

**Verbs** — `MustNotBeReferencedBy` and `MustOnlyBeReferencedBy` are the inbound pair. `MustNotCatch`
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

**Member and project subjects** — The member verbs reuse the type-side names, with six of their own:
`MustBeProtected`, `MustBePrivate`, `MustBeVirtual` and `MustBeOverridable` (an unsealed override passes it)
have no type-side twin, `MustBeGetOnly()` chains only off
`.Properties` and is strict about the declaration (an `init`-only setter is a setter, so it reds), and
`MustBeReadonly()` chains only off `.Fields` (a `const` field satisfies it, const being readonly's
superset). `.ThatAreStatic()` prefixes the subject head ("static fields of ..."), as does each
accessibility adjective, which on members also has `.ThatAreProtected()`, `.ThatAreProtectedInternal()`
and `.ThatArePrivateProtected()`, each matching its level exactly; `.Named(name, ...)` picks
members by exact name ("`Main` methods of ..."), and `.Except(memberSelection, ...)` exempts members a
name alone cannot: `.Methods.Except(arch.Types.Named("Program").Methods.Named("Main"))`. The flagship:
`web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` — *"Methods returning `Task` declared in
types in `MyApp.Web.*` must be named `*Async`."* `arch.Projects` names the solution's projects as build
artifacts (`arch.Project(name)` is the unrelated type noun).
`.Matching(glob, ...)` reads `*` over one token, because a project name carries no dot-segment
structure, and `.Packable()` is the projects an evaluation reported packable.
`MustOnlyTarget(tfm, ...)` is strict, with no external exemption, because a project's frameworks are
a closed set, and `MustTarget(tfm)` states the floor; `MustLockPackages()` reads
`RestorePackagesWithLockFile`; `MustReferenceNoPackages()` passes only private references, whose
`PrivateAssets` is `all` (analyzers and build tooling), `MustOnlyReferencePackages(glob, ...)` adds an
allow-list to it, and `MustNotReferencePackages(glob, ...)` bans packages, private ones included, both
matching identifiers ignoring case; `MustSetProperty(name, value)` asks that a declaration in the
solution set an MSBuild property, per framework, an SDK default not counting; `MustNotBePackable()`
makes an internal project's opt-out checkable, since the SDK defaults `IsPackable` on. Every project
fact is evaluated rather than parsed, so a violation points at the declaration that won, often a
`Directory.Build.props` above the project, and a fact nothing evaluated passes. The flagship:
`arch.Projects.Matching("MyApp.*").Except(arch.Projects.Named("MyApp.Cli")).MustNotBePackable()` —
*"Projects matching `MyApp.*`, except project `MyApp.Cli`, must not be packable."*

The **string overload** on every hierarchy, attribute and member type anchor position — the three
adjectives, the `Must[Not]Implement` / `Must[Not]DeriveFrom` / `Must[Not]BeAttributedWith`
verbs, and `.Returning` / `MustAcceptParameter` on a member subject — is the escape hatch
for a type the spec project cannot compile against, and it renders
byte-identically to the `typeof()` form. Reach for it whenever a reference on the spec project
would be the only reason to add one, and always for a shared-framework type (step 2). It anchors fine either
way: only an external **subject** carries a shallow hierarchy, while a declared subject's base chain
and interface closure are walked through metadata, so
`arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` selects your controllers. A
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
written), `FilePaths`, attributes, base type, interfaces. On a member subject it sees `IMemberInfo`:
`Name`, `Kind`, `DeclaringType`, `Accessibility`, `IsStatic`, `IsVirtual`, `IsOverride`, `IsSealed`,
`IsAsync`, `IsConst`, `IsReadOnly`, `ReturnTypeFullName`, `Parameters`, attributes, so a constants law
is a `.Where(m => m.IsConst, "that are constants")` away.

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
`AddDbContext`/`AddDbContextPool`/`AddHttpClient<TClient>`) — keyed and raw-descriptor
registrations, assembly scanning, factory-lambda internals, and framework defaults are invisible; an empty `Registered` subject fails loud (check the visibility
boundary before blaming the code), while an empty `MustNotInject` operand is the win condition
and never warns. Member bans are
source-visibility bans, not runtime-dispatch bans — a ban on a concrete member does not catch
calls through an interface-typed receiver, nor the reverse. Member subjects range over the
declared members of solution-declared types (accessors, constructors, operators, indexers,
and compiler-generated members are excluded; external types carry no member inventory). Type subjects
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
matches any construction. The `MustNot*`
hierarchy verbs share these and `AttributedWith`'s matcher, negated per subject over the anchor list (a
subject reds iff it matches ANY anchor). Closed generics are
refused in reference positions — ban the open definition and/or the argument type.
