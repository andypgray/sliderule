# A reference architecture held to its own book: eShopOnWeb

eShopOnWeb is Microsoft's reference application for the e-book *Architecting Modern Web Applications with ASP.NET Core and Azure*, now maintained by NimblePros. [A public fork](https://github.com/andypgray/eShopOnWeb/tree/sliderule-showcase-1) adopts SlideRule on it end to end, and is not affiliated with NimblePros or Microsoft. It adds a spec derived from the book and the repository's own docs, a baseline of today's departures from it, and agent context. It also adds a Claude Code hook, an MCP server registration, a CI check and the build analyzer. The application code is unchanged. [Every change from upstream](https://github.com/andypgray/eShopOnWeb/compare/f21d6233b851897371c25c5e7ffa67bb8552dba8...sliderule-showcase-1) is in the compare view.

The other examples run on Meridian, a fiction built in this repository. This one runs on code you may already know. The fork is pinned at the tag `sliderule-showcase-1`. CI fetches that commit, re-runs every command output on this page with the current build of SlideRule, and compares every quoted file with the fork's.

## How eShopOnWeb describes itself

The book's chapter on common web application architectures describes the Clean Architecture the sample follows:

> the Application Core has no dependencies on other application layers.

It allows one exception, for wiring up dependency injection:

> For the purposes of this sample, the simplest approach is to allow the UI project to reference the Infrastructure project (but developers should limit actual references to types in the Infrastructure project to the app's composition root).

([Common web application architectures](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures))

The repository's own [architecture page](https://github.com/NimblePros/eShopOnWeb/blob/f21d6233b851897371c25c5e7ffa67bb8552dba8/docs/explore/architecture.md) states the direction of dependencies:

> Business concepts and domain model concerns are kept in the ApplicationCore project, which other projects depend on.

When a contributor asked which packages the core may take, the sample's maintainer answered in [issue 581](https://github.com/dotnet-architecture/eShopOnWeb/issues/581) on the original Microsoft repository:

> If the package brings with it a dependency on UI concerns, like Microsoft.AspNetCore, then no (because see above).

The build checks these statements only as far as the project references go, and those let any Web type name an Infrastructure type.

## How the spec was derived

The spec was written by one fresh Claude Code session with Claude Opus 5.5. It ran SlideRule 0.2.1 and followed the `derive_spec` recipe the MCP server serves, as given. The session read the book chapter and the repository's docs, and surveyed the solution with `arch_graph`. It drafted rules, each with a reason naming its evidence, and stopped at the curation gate. Its receipt is [committed in the fork](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/arch/derive_spec-receipt.md). These lines are from its close:

```text
specBuild: ok
evidencePass: complete
check: exit 1, rules 22, passed 15, failed 6, skipped 1, violations 40
postures: enforce 15 / migrate 6 / quarantine 0 / caution 1
leftRed: none
curation: pending
baseline: not run (human)
render: not run (human)
```

The `check` line is the draft's verdict over the fork's first commit before any baseline, so each `Migrate` rule in it fails. Curation came next, as a separate commit. It dropped four rules, added the project rule below, rewrote each reason without the draft's "Candidate:" prefix, and gave each rule the source that states it. The baseline and the render are the human's steps, so the recipe leaves them to the person who owns the repository.

## The spec

The spec is [`arch/EShopOnWeb.Architecture/ArchitectureSpec.cs`](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/arch/EShopOnWeb.Architecture/ArchitectureSpec.cs). Each application project under `src/` except the Aspire host is a layer, and the layering rules state which may reference which. The book's composition-root exception is written into the subject of the rule it qualifies:

```csharp
        Selection webRoot = arch.AnyOf(
            web.InNamespace("Microsoft.eShopWeb.Web.Configuration.*"),
            web.Named("Program", "ServiceCollectionExtensions", "WebApplicationExtensions"));
```

Only types in that root may name Infrastructure types. The fork chose `Migrate` for the rule, because today's code names Infrastructure types outside that root:

```csharp
        arch.Rule("layering/web-infrastructure-via-composition-root")
            .Migrate(
                from: "Identity pages, controllers and the basket component name ApplicationUser (and ManageController UserNotFoundException) from Infrastructure.Identity.",
                to: web.Except(webRoot).MustNotReference(infrastructure))
            .Because("The UI project references Infrastructure so that it can wire up dependency injection, and references to Infrastructure types belong in the app's composition root. Most of today's sites name ApplicationUser, the ASP.NET Core Identity user type, which is declared in Infrastructure.Identity.")
            .Citation(EBookArchitecture);
```

## Where the code and the docs disagree

`ApplicationCore.csproj` declares a project reference to `BlazorShared`, the contracts project the Blazor admin client shares with the API. No source file in ApplicationCore uses it. BlazorShared references BlazorInputFile, which depends on Microsoft.AspNetCore.Components, so the reference brings ASP.NET Core into the build of the core and of every project that references it. The maintainer's answer above rules that out. The fork states the rule over the project file itself:

```csharp
        arch.Rule("layering/core-declares-no-blazor-shared")
            .Migrate(
                from: "ApplicationCore.csproj declares a project reference to BlazorShared that no ApplicationCore source uses. Infrastructure and PublicApi use BlazorShared types through it.",
                to: arch.Projects.Named("ApplicationCore")
                    .Must(project => !project.ProjectReferences.Contains("BlazorShared"),
                        description: "declare no project reference to BlazorShared"))
            .Because("A project reference is a dependency even when no code uses it. This one adds BlazorShared and the Blazor packages it references to the build of every project that references the Application Core. The maintainer's rule for the core is that a package bringing UI concerns, like Microsoft.AspNetCore, does not belong there.")
            .Citation(CoreDependenciesAnswer);
```

Before the baseline, `check` fails the rule with this block:

```text
FAIL layering/core-declares-no-blazor-shared — Project `ApplicationCore` must declare no project reference to BlazorShared.
  because: A project reference is a dependency even when no code uses it. This one adds BlazorShared and the Blazor packages it references to the build of every project that references the Application Core. The maintainer's rule for the core is that a package bringing UI concerns, like Microsoft.AspNetCore, does not belong there.
  citation: https://github.com/dotnet-architecture/eShopOnWeb/issues/581
  ApplicationCore
  hint: no baseline captured for this rule; run 'sliderule baseline init' to grandfather existing violations
```

Infrastructure and PublicApi use BlazorShared types today, and they reach the project only through this reference. Removing it is a change to three project files, so the fork chose `Migrate` and baselined the one site. `explain` prints the rule with its source and the migration's starting point:

```text
layering/core-declares-no-blazor-shared (migrate)
  sentence: Project `ApplicationCore` must declare no project reference to BlazorShared.
  because: A project reference is a dependency even when no code uses it. This one adds BlazorShared and the Blazor packages it references to the build of every project that references the Application Core. The maintainer's rule for the core is that a package bringing UI concerns, like Microsoft.AspNetCore, does not belong there.
  citation: https://github.com/dotnet-architecture/eShopOnWeb/issues/581
  from: ApplicationCore.csproj declares a project reference to BlazorShared that no ApplicationCore source uses. Infrastructure and PublicApi use BlazorShared types through it.
  policy: MigrateIfSmall
  baseline: arch/baselines/layering/core-declares-no-blazor-shared.json
```

Three of the other migrations keep Infrastructure types inside the composition roots, and the fourth covers Infrastructure's use of BlazorShared. Most of the composition-root sites name `ApplicationUser`, the ASP.NET Core Identity user type, which Infrastructure declares and the Identity pages and the user endpoints use directly.

## What an agent reads now

Upstream has merged pull requests from coding agents, including Copilot's. In the fork, `sliderule render` writes a managed block into the fork's [`AGENTS.md`](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/AGENTS.md), and a card into each governed project's directory. Claude Code, Copilot and the other agents that read `AGENTS.md` see each migration with its old pattern and the rule new code follows:

```markdown
### Migrations
- `layering/core-declares-no-blazor-shared` — Some existing code here still follows the OLD pattern: ApplicationCore.csproj declares a project reference to BlazorShared that no ApplicationCore source uses. Infrastructure and PublicApi use BlazorShared types through it. That is grandfathered debt, not house style. New code must follow: Project `ApplicationCore` must declare no project reference to BlazorShared. A project reference is a dependency even when no code uses it. This one adds BlazorShared and the Blazor packages it references to the build of every project that references the Application Core. The maintainer's rule for the core is that a package bringing UI concerns, like Microsoft.AspNetCore, does not belong there. See <https://github.com/dotnet-architecture/eShopOnWeb/issues/581>. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
```

The fork also commits [`.claude/settings.json`](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/.claude/settings.json), whose `Stop` hook runs `sliderule hook` when a session's turn ends and returns a red check to the agent, and [`.mcp.json`](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/.mcp.json), which registers the MCP server. [CODING-AGENTS.md](../../CODING-AGENTS.md) explains both.

## CI and the build

The fork's [Architecture workflow](https://github.com/andypgray/eShopOnWeb/blob/sliderule-showcase-1/.github/workflows/architecture.yml) builds the solution with the build analyzer's diagnostics as errors, runs `check` with SARIF output for code scanning, and fails when a rendered file is out of date. On a pull request, `check` compares against the base branch:

```bash
            sliderule check --diff-base "origin/$BASE_REF" --sarif sliderule.sarif
```

The solution declares one project SlideRule cannot read, the Docker Compose project. Run without a base, `check` prints a stamp naming it above the report. Here is that stamp and the report's summary line:

```text
1 project the solution declares was not surveyed:
  docker-compose.dcproj — not a C# project
The verdict below covers only the projects this product can read, so a clean result here is not a clean solution.

Checked 19 rules: 18 passed, 0 failed, 1 skipped (0 violations, 0 warnings).
```

`status` prints the burndown, one line per rule:

```text
1 project the solution declares was not surveyed:
  docker-compose.dcproj — not a C# project
The burndown below counts only the projects this product can read: these contribute no violations and never will, so every count reads low by whatever they hold.

pass layering/core-independent
pass layering/core-declares-no-blazor-shared (migrate) — 1 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass layering/blazor-shared-independent
pass layering/blazor-admin-client-only
pass layering/service-defaults-independent
pass layering/infrastructure-not-ui
pass layering/infrastructure-not-blazor-shared (migrate) — 2 grandfathered remaining (8 sites), 0 new, 0 fixed awaiting acceptance
pass layering/web-not-public-api
pass layering/public-api-not-web
pass layering/web-infrastructure-via-composition-root (migrate) — 11 grandfathered remaining (26 sites), 0 new, 0 fixed awaiting acceptance
pass layering/public-api-infrastructure-via-composition-root (migrate) — 16 grandfathered remaining (18 sites), 0 new, 0 fixed awaiting acceptance
pass construction/ui-no-infrastructure-instantiation (migrate) — 3 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass externals/core-no-ef
pass externals/core-no-aspnetcore
pass externals/ui-no-ef
pass naming/interfaces-prefix-i
pass naming/repository-implements-irepository
pass members/entity-fields-private
skip infrastructure/ef-migrations/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'
Checked 19 rules: 18 passed, 0 failed, 1 skipped. Burndown: 33 grandfathered remaining (55 sites), 0 fixed awaiting acceptance.
```

The skipped line is the fork's `Caution` scope over the EF Core migrations, whose tripwire runs only against a diff.

## Run it yourself

The commands need git and a .NET 10 SDK. They clone the fork at its tag, install the release the fork pins, build the solution and check it:

```bash
git clone --branch sliderule-showcase-1 https://github.com/andypgray/eShopOnWeb.git
cd eShopOnWeb
dotnet tool install -g SlideRule.Cli --version 0.2.1
dotnet build eShopOnWeb.slnx
sliderule check
sliderule explain layering/core-declares-no-blazor-shared
```

`check` exits 0 on the committed baselines. `sliderule status` prints the burndown above.

## From here

- [Day-one adoption on an existing codebase](../Meridian/ADOPTING.md) walks the same derive flow step by step on Meridian.

- [Three of the four postures on one codebase](../Meridian/) shows `Migrate` debt burning down, with `Enforce` and `Quarantine` in the same spec.

- [The agent loop, closed by a hook](../Meridian/hooks/) follows one agent task from a red check to a fix.
