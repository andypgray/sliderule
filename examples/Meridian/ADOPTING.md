# Adopting SlideRule on an existing codebase

This page replays the derivation of a real spec, one real command at a time.

You have a working codebase and no architecture spec. You start by surveying what is there, then scaffold a spec project and draft candidate rules. You run those rules to gather evidence, and you assign each one a posture from it. A rule is `Enforce` or `Migrate`, and a scope is `Quarantine` or `Caution`. Then you baseline the acknowledged debt and render the agent context. Meridian, the worked instance, is a freight-forwarding monolith of three projects and eight controllers. The walk keeps its shape at 3 projects or 300, so every step works the same on a larger solution.

This is the [derive-spec.md](../../src/SlideRule.Cli/Mcp/Prompts/derive-spec.md) recipe applied to real code (the MCP server serves the same text to agents as the `derive_spec` prompt). The recipe's step 0, which this walk skips, is a search for architecture the team already wrote down, such as architecture tests and ADRs. On your solution, start there. The [README](README.md) in this directory shows the destination, with the finished spec table, the rendered block, the failure-mode demos, and the burndown board. This page covers the steps that lead there. To run the whole thing yourself against a scratch copy, jump to [Try it yourself](#try-it-yourself).

## What you'll do

1. Survey the estate
2. Scaffold the spec project
3. Draft candidate rules
4. Check as the evidence pass
5. Assign postures from the evidence
6. Curate, then baseline
7. Render and commit

Hold onto two things before you start. First, the violations you see during the derivation are data. You will write rules your code breaks on purpose, because the count of what breaks is the measurement you assign the posture from. A red check in the middle of this flow means the evidence pass is working. Second, SlideRule surveys, checks and renders, and you make every decision. The curation, the `baseline init`, and the commit belong to you.

This recipe was proven before any example existed. A cold agent, given only the recipe and SlideRule's own test fixture, walked it end to end. The agent produced a compiling spec, and the curated result became a committed test fixture. The walk also caught a real defect in spec discovery. The page you are reading is that same walk, run against Meridian and written down.

## Step 1: survey the estate

On your solution, restore or build first, then start with the one verb that needs no spec. `sliderule graph` reads the code from source the way `check` does and returns projects, namespace inventories, project-to-project edges, and external references. On a large estate, this is how you find the layers without opening every file. The edge matrix shows what references what, and the namespace inventory is the raw material for layer globs. Read all of it as hypotheses to test.

```text
$ sliderule graph examples/Meridian/Meridian.slnx --json
{
  "schemaVersion": 1,
  "solution": "Meridian.slnx",
  "projects": [
    {
      "name": "Meridian.Clearance",
      "solutionMember": true,
      "targetFrameworks": [
        "net10.0"
      ],
      "isPackable": false,
      "locksPackages": false,
      "projectReferences": [],
      "packageReferences": [],
      "types": 5,
      "namespaces": [
        {
          "namespace": "Meridian.Clearance",
          "types": 5
        }
      ]
    },
    {
      "name": "Meridian.Domain",
      "solutionMember": true,
      "targetFrameworks": [
        "net10.0"
      ],
      "isPackable": false,
      "locksPackages": false,
      "projectReferences": [],
      "packageReferences": [],
      "types": 7,
      "namespaces": [
        {
          "namespace": "Meridian.Domain",
          "types": 7
        }
      ]
    },
    {
      "name": "Meridian.Web",
      "solutionMember": true,
      "targetFrameworks": [
        "net10.0"
      ],
      "isPackable": false,
      "locksPackages": false,
      "projectReferences": [
        "Meridian.Clearance",
        "Meridian.Domain"
      ],
      "packageReferences": [
        "Microsoft.Data.SqlClient"
      ],
      "types": 18,
      "namespaces": [
        {
          "namespace": "(global)",
          "types": 1
        },
        {
          "namespace": "Meridian.Web.Controllers",
          "types": 8
        },
        {
          "namespace": "Meridian.Web.Data",
          "types": 4
        },
        {
          "namespace": "Meridian.Web.Models",
          "types": 5
        }
      ]
    }
  ],
  "projectEdges": [
    {
      "source": "Meridian.Web",
      "target": "Meridian.Clearance",
      "references": 4
    },
    {
      "source": "Meridian.Web",
      "target": "Meridian.Domain",
      "references": 23
    }
  ],
  "externalEdges": [
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "Microsoft.Data",
      "references": 35
    },
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "System.Data",
      "references": 8
    },
    {
      "source": "Meridian.Web",
      "targetNamespaceRoot": "System.Threading",
      "references": 7
    }
  ]
}
exit: 0
```

The type counts are Clearance 5, Domain 7, and Web 18. Web splits across `Controllers` (8 types), `Data` (4), `Models` (5), and one type at global scope. Two candidate layers fall out immediately, `Meridian.Domain.*` and `Meridian.Web.*`. The `projectEdges` show Web reaching Domain (23) and Clearance (4), and no edge runs the other way. Domain never references Web, so that direction goes on the list. Eight types under `Controllers` is a naming hypothesis (`*Controller`).

The clue is in the external edges, where Web reaches `Microsoft.Data` 35 times and `System.Data` 8, the shape of inline SQL. Grouped per project, though, the survey cannot yet show how many of those land in controllers (a question for `check`). `Task` lives under the `System.Threading` root, and Web's async methods return it. The wall clock is invisible at this altitude, a hypothesis to test later. Clearance has five types of its own behind one thin inbound edge, the module-behind-a-facade shape. That shape makes Clearance the candidate dragon zone, a module whose hidden hazards the spec should warn about.

The capture is trimmed to three external roots, the two SQL-shaped ones plus `System.Threading`. It drops the framework roots (`Microsoft.AspNetCore` at 47, `Microsoft.Extensions` at 20), Web's `System` and `System.Collections` rows, and every row whose source is Clearance or Domain.

## Step 2: scaffold the spec project

On your solution, the spec is a small class library in an `arch/` folder next to the solution file. There is no registration step. Discovery is by convention, and the spec is the one solution project that references the SlideRule contract assembly. The spec project is itself a member of the solution. `check` excludes that project from the universe it inspects, so the spec's own types never trip your rules.

Here is Meridian's spec project, exactly as committed:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
        <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\..\..\src\SlideRule\SlideRule.csproj" />
        <ProjectReference Include="..\..\..\..\src\SlideRule.Packs.DotNet\SlideRule.Packs.DotNet.csproj" />
        <ProjectReference Include="..\..\src\Meridian.Clearance\Meridian.Clearance.csproj" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    </ItemGroup>

</Project>
```

The `RestoreLockedMode` line takes effect only where a `packages.lock.json` is next to the project file, and this project has none. OpenSSF Scorecard's pinned-dependencies check reads the setting from each project file, so this repository declares it in every one. Your spec project can leave it out unless you commit a lock file next to it.

The other line to explain is `CopyLocalLockFileAssemblies`. It stages NuGet package assemblies (here SqlClient) into the spec's build output, where `check` looks for the `typeof()` targets that live in those packages. Strictly, the spec does not need the line. The loader also reads the spec's `.deps.json` and will resolve a package assembly out of the machine's NuGet folder. The line is insurance that makes the spec's output self-contained, so a check does not depend on what a given machine happens to have restored. It is harmless when every target is a project type or a namespace pattern.

The line cannot reach a **.NET shared framework**. An ASP.NET Core `ControllerBase`, or anything else arriving through `<FrameworkReference>`, is never staged into a class library's output. Such a type is not in the NuGet folder either, so a `typeof()` anchor on one fails however the spec project is built. Anchor those by string instead. `.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` renders exactly like the `typeof()` form and loads no assembly at all.

Of the three project references, the first is the SlideRule contract library, which your own spec takes as the `SlideRule` package. The third, `Meridian.Clearance`, lets the spec name the clearance gateway's types with `typeof()`. The second is a rule pack, `DotNetGuidance`. A pack is an ordinary class library of static methods that declare rules onto your `Arch`, and you call the ones you want. A pack rule applies only when your spec calls its method, so this reference on its own adds no rules. Step 3 is where the calls go in.

`DotNetGuidance` is not a package you can install. It ships inside the SlideRule repository as a working example. That is why the pack's reference is a relative path into `src/`, which resolves only in a source checkout. On your solution the pack is one you write, and its [README](../../src/SlideRule.Packs.DotNet/README.md) is the shape to copy. What the steps below do with `DotNetGuidance` is what your spec will do with yours.

The class starts empty and grows in step 3:

```csharp
using SlideRule;

namespace Meridian.Architecture;

public sealed class MeridianArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        // The rules arrive in step 3.
    }
}
```

Add it to the solution, then build it before you check. In a source checkout the add step prints three lines, quoted here as the .NET SDK printed them:

```text
$ dotnet sln examples/Meridian/Meridian.slnx add examples/Meridian/arch/Meridian.Architecture/Meridian.Architecture.csproj --solution-folder arch
Project `arch\Meridian.Architecture\Meridian.Architecture.csproj` added to the solution.
Project `..\..\src\SlideRule\SlideRule.csproj` added to the solution.
Project `..\..\src\SlideRule.Packs.DotNet\SlideRule.Packs.DotNet.csproj` added to the solution.
exit: 0
```

In a source checkout like this repository, `dotnet sln add` follows the spec's project references transitively. The command also adds the SlideRule contract library and the rule pack (the second and third `added` lines above). The recipe's step 2 lists the errors you may see in that setup, verbatim, and [Try it yourself](#try-it-yourself) below shows the exact removal. A published-package setup references SlideRule as a `PackageReference` and avoids all of this.

## Step 3: draft candidate rules

On your solution, turn every hypothesis from the survey into a rule, and draft them all as `Enforce`. Do not pre-judge the posture; the check in the next step supplies the evidence you assign it from. Write the already-true directions too, the ones the edge matrix showed clean. A clean direction made law is the cheapest rule you will ever own. Candidate `Because` notes are fine at this stage; you will upgrade them once the rules are real. The one exception is a dragon zone. A boundary has no `Enforce` form, so you draft it as a `Quarantine` scope directly. In step 5 you decide whether it stays one or becomes a `Caution`.

Before you write any of them, check what you can take instead. Canonical .NET guidance is the same in every codebase, so those rules belong in a pack, written once with their reasons and citations attached. Every spec that takes one afterwards spends a line on it. This walk takes those rules from `DotNetGuidance`. On your solution the first pack is yours to write, and it starts paying at the second spec that calls it. Take the ones that could plausibly apply to your estate and leave the rest. You opt out of a rule by not calling its method. Draft the taken rules as `Enforce` too, for the same reason as the ones you write. Step 4 shows which of them your code already obeys.

Meridian's survey produced four direction-and-convention candidates plus one quarantined scope, and two rules come from the pack. One is the `Async` naming convention, taken because Web and Domain are full of `Task`-returning methods. The other is the `BuildServiceProvider` antipattern, taken because the app has a composition root worth guarding.

```csharp
using Meridian.Clearance;
using Microsoft.Data.SqlClient;
using SlideRule;
using SlideRule.Packs.DotNet;

namespace Meridian.Architecture;

public sealed class MeridianArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "Meridian.Domain.*");
        Layer web = arch.Layer("Web", "Meridian.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Candidate: Domain looked self-contained in the survey; nothing should pull it up into Web.");

        arch.Rule("naming/controllers")
            .Enforce(arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase").MustHaveSuffix("Controller"))
            .Because("Candidate: the eight types under the Controllers namespace look like MVC controllers, so the rule selects them by their base type.");

        arch.Rule("data-access/no-inline-sql")
            .Enforce(arch.Namespace("Meridian.Web.Controllers.*")
                .MustNotReference(typeof(SqlConnection), typeof(SqlCommand)))
            .Because("Candidate: externalEdges shows Microsoft.Data.SqlClient reached straight from controllers.");

        arch.Rule("time/inject-clock")
            .Enforce(web.MustNotUse(
                arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
            .Because("Candidate: the Web layer looks like it reads the ambient clock directly.");

        DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web), PackPosture.Enforce);

        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);

        arch.Scope("clearance/engine")
            .Quarantine(arch.Namespace("Meridian.Clearance.*"))
            .BoundaryOnlyVia(typeof(IClearanceGateway), typeof(ClearanceGateway))
            .Dragons("ISO 6346 check digit: the letter-value table skips every multiple of 11 " +
                     "(A=10, B=12 … U=32); the gaps are structural — linearizing the table breaks " +
                     "every real container number. Call in only through IClearanceGateway.")
            .Because("Candidate: a self-contained clearance module already sits behind a gateway facade.");
    }
}
```

That is five candidate rules you wrote plus two you took. The quarantined scope expands into two checkable rules (a containment rule and a diff-aware tripwire), so `check` will report eight. The controllers rule anchors `ControllerBase` by string, which is the shared-framework case step 2 flagged. The spec project cannot reference that type at all. The rule therefore names it by fully-qualified name, and the check resolves it out of the codebase it is reading. Notice the clock rule bans `DateTime.Now` and `DateTime.UtcNow` across the whole Web layer with no exception yet. The draft states the blunt hypothesis, and the check in the next step finds the one type whose job is to read the clock.

The two pack calls have no `Because` here. The pack supplies one for each rule, with a learn.microsoft.com citation attached, and each reason reads the same in every codebase that takes the rule. What you choose is the posture and the selections. The naming rule selects the two layers, so it reaches Domain's interfaces and leaves out the top-level statements in `Program.cs`.

The CLI never builds your code. It reads the code from source and runs the spec from the spec project's last build, so rebuild the spec project after each change to the spec, or the check runs the old rules.

## Step 4: check as the evidence pass

On your solution, run `check`. Exit 1 is the expected outcome here, because the reds are the data you came for. Every violation arrives with its rule ID, its reason, and the exact `file:line` of each offending site. The counts are the measurements you assign a posture from in the next step.

```text
$ sliderule check examples/Meridian/Meridian.slnx
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Candidate: externalEdges shows Microsoft.Data.SqlClient reached straight from controllers.
  src/Meridian.Web/Controllers/CustomsController.cs:26 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/CustomsController.cs:27 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/CustomsController.cs:28 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
  src/Meridian.Web/Controllers/CustomsController.cs:29 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/CustomsController.cs:32 — Meridian.Web.Controllers.CustomsController references Microsoft.Data.SqlClient.SqlCommand
FAIL time/inject-clock — The Web layer must not use `DateTime.Now` or `DateTime.UtcNow`.
  because: Candidate: the Web layer looks like it reads the ambient clock directly.
  src/Meridian.Web/Controllers/CustomsController.cs:16 — Meridian.Web.Controllers.CustomsController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/DriversController.cs:15 — Meridian.Web.Controllers.DriversController uses System.DateTime.Now
  src/Meridian.Web/Controllers/InvoicesController.cs:34 — Meridian.Web.Controllers.InvoicesController uses System.DateTime.Now
  src/Meridian.Web/Controllers/InvoicesController.cs:35 — Meridian.Web.Controllers.InvoicesController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/ManifestsController.cs:14 — Meridian.Web.Controllers.ManifestsController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/RatesController.cs:15 — Meridian.Web.Controllers.RatesController uses System.DateTime.UtcNow
  src/Meridian.Web/Controllers/ShipmentsController.cs:15 — Meridian.Web.Controllers.ShipmentsController uses System.DateTime.UtcNow
  src/Meridian.Web/Data/SystemClock.cs:7 — Meridian.Web.Data.SystemClock uses System.DateTime.UtcNow
FAIL naming/async-suffix — Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
  because: Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited.
  citation: https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Rename the method to end in Async.
  src/Meridian.Domain/IBookingRepository.cs:5 — Meridian.Domain.IBookingRepository.Add()
  src/Meridian.Domain/IBookingRepository.cs:7 — Meridian.Domain.IBookingRepository.Get()
  src/Meridian.Domain/IQuoteRepository.cs:5 — Meridian.Domain.IQuoteRepository.Add()
  src/Meridian.Domain/IQuoteRepository.cs:7 — Meridian.Domain.IQuoteRepository.Get()
  src/Meridian.Domain/IRateCardRepository.cs:5 — Meridian.Domain.IRateCardRepository.GetForLane()
  src/Meridian.Web/Controllers/BookingsController.cs:18 — Meridian.Web.Controllers.BookingsController.Create()
  src/Meridian.Web/Controllers/QuotesController.cs:15 — Meridian.Web.Controllers.QuotesController.Create()
  src/Meridian.Web/Controllers/QuotesController.cs:39 — Meridian.Web.Controllers.QuotesController.Get()
  src/Meridian.Web/Data/BookingRepository.cs:8 — Meridian.Web.Data.BookingRepository.Add()
  src/Meridian.Web/Data/BookingRepository.cs:29 — Meridian.Web.Data.BookingRepository.Get()
  src/Meridian.Web/Data/QuoteRepository.cs:8 — Meridian.Web.Data.QuoteRepository.Add()
  src/Meridian.Web/Data/QuoteRepository.cs:30 — Meridian.Web.Data.QuoteRepository.Get()
  src/Meridian.Web/Data/RateCardRepository.cs:8 — Meridian.Web.Data.RateCardRepository.GetForLane()
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
FAIL clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  because: Candidate: a self-contained clearance module already sits behind a gateway facade.
  fix: use `IClearanceGateway`
  src/Meridian.Web/Controllers/CustomsController.cs:52 — Meridian.Web.Controllers.CustomsController references Meridian.Clearance.ContainerNumberValidator
  src/Meridian.Web/Controllers/CustomsController.cs:53 — Meridian.Web.Controllers.CustomsController references Meridian.Clearance.ContainerNumberValidator
  hint: no baseline captured for this rule; run 'sliderule baseline init' to grandfather existing violations
skip clearance/engine/tripwire
  skipped: Tripwire: no diff context — run 'sliderule check --diff-base <ref>' to check changed files against this quarantined scope.

Checked 8 rules: 3 passed, 4 failed, 1 skipped (34 violations, 0 warnings).
exit: 1
```

The two direction-and-naming rules pass with zero violations, which already settles their posture. The same goes for one of the two pack rules, `di/no-buildserviceprovider`, which is green on the first run. The four that fail are the evidence. `data-access/no-inline-sql` reports 12 violations across six controllers, with 31 reference sites in the full output. Of those, the capture above shows CustomsController's five and drops the other five controllers. The inline-SQL result answers the survey's open question. The `Microsoft.Data` references do land in controllers, and in six of the eight.

`time/inject-clock` reports eight sites, shown in full, and one of them is the answer to step 3's setup. That site is `src/Meridian.Web/Data/SystemClock.cs:7`, in a type whose job is to read the clock. `naming/async-suffix` reports 13, shown in full. Five are interface declarations in Domain, and eight are implementations and actions in Web. That is the shape of a convention the codebase never adopted rather than one it drifted from. `clearance/engine/containment` reports one inbound reference the gateway does not sanction, with the hint that no baseline exists yet. The tripwire reports `skip`, as expected, because a plain `check` takes its changed files only from a `--diff-base` ref, and this run gives none. In all, the check reports 34 located, named violations, ready to price.

Notice that the pack rules were checked exactly like the ones written by hand. A pack ships a rule and its reason, and the check reports whether your code obeys it.

## Step 5: assign postures from the evidence

On your solution, you assign each rule's posture from its violation count. Zero violations means the code already obeys, so the rule becomes `Enforce` law at no cost. Violations plus a target state the team actually wants means `Migrate`. The current sites are grandfathered, and new code goes red.

A region with no target state, code the team plans to leave as it is, is a scope. What you want from its future callers decides which kind. Where new code must not reference it, the scope is `Quarantine`. A `Quarantine` scope enforces the boundary, grandfathers the reaches that already exist, and documents what is dangerous inside. Where new callers are welcome (a shared utility or a public interop layer), the scope is `Caution`. Containing it would grandfather every legitimate caller as debt and fail the next caller. A `Caution` scope therefore keeps the dragons card and never goes red. A rule the team will not stand behind gets dropped, because an unratified rule is the stale doc SlideRule exists to kill.

Meridian's evidence assigns cleanly:

| Rule | Evidence | Posture |
|---|---|---|
| `layering/domain-independent` | 0 violations | Enforce |
| `naming/controllers` | 0 violations | Enforce |
| `data-access/no-inline-sql` | 12 pairs, 6 controllers | Migrate |
| `time/inject-clock` | 8 clock reads | Migrate |
| `naming/async-suffix` | 13 unsuffixed methods | Migrate |
| `di/no-buildserviceprovider` | 0 violations | Enforce |
| `clearance/engine` | 1 non-facade inbound | Quarantine |

The three zero-violation rules become law. The inline-SQL rule has 12 violations (six controllers, each referencing both `SqlConnection` and `SqlCommand`), and the team wants repositories, so it is a `Migrate`. The majority pattern is the one being retired, and the two already-migrated controllers show the target. The clock rule has eight sites and the same shape, so `Migrate` again, with one refinement. The naming rule has 13 and the team wants the convention, so `Migrate` as well. The single non-facade reach into Clearance has no target state (the ISO 6346 table stays as it is). New code should reach Clearance only through the gateway, so the scope is `Quarantine`.

The two pack rules come from the same source on the same run and land on opposite sides of that line. `di/no-buildserviceprovider` is `Enforce`, and `naming/async-suffix` is `Migrate`. In the [Interchange example](../Meridian.Interchange/) the identical `naming/async-suffix` is `Enforce`, because that codebase already keeps it. A pack therefore gives you the rule and its reason and leaves the posture to you, since only your own check measures your code.

The refinement comes from the evidence, where the draft flagged `SystemClock.cs:7` alongside the seven controller reads. `SystemClock` implements `IClock` as the one sanctioned seam that reads the wall clock, so every other type can take the time from `IClock`. The blunt draft rule surfaced the seam. The curated rule keeps it by adding `.Except(arch.Types.Named("SystemClock"))`, which leaves seven grandfathered controller reads and one type doing its job. That signal is authoring feedback, so the fix goes in the rule. An empty subject, or a glob that matches no type, is the same kind of feedback.

## Step 6: curate, then baseline

On your solution, this step is the curation gate, and every decision in it is yours. Go rule by rule, and accept, edit, or drop each one. Then upgrade every `Because` from a candidate note into the reason a reviewer would sign in a design review. That text renders into the agent context and into every violation message as the team's stated rationale. Only then run `baseline init`, which grandfathers exactly what is red at that moment. Curation comes first because day zero is the one moment when current and accepted are the same set. The baseline is also your signature on the debt.

For Meridian the curation is a small set of edits against the draft (the finished result is [the committed spec](arch/Meridian.Architecture/MeridianArchitectureSpec.cs)):

- Each layer gains a `Purpose` sentence, which `render` writes into the agent context. `layering/domain-independent` and `naming/controllers` stay `Enforce` and gain a real `Because`, and the first also gains a `Fix`.
- The two data rules move from `Enforce` to `Migrate`. Each gains a factual `from:` line describing the old pattern ("Controllers open SqlConnection and run inline SQL directly"; "Code reads the ambient clock directly"). Each also gains a real `Because`, a `Fix`, and an `Exemplar` naming the type to copy (`BookingRepository` for the SQL, `BookingsController` for the clock). `check` resolves each exemplar to a file and line.
- The clock rule gains the `.Except(SystemClock)` refinement from step 5 and names its two members with lambdas (`() => DateTime.Now`), which render the same.
- The naming rule moves from `PackPosture.Enforce` to `PackPosture.Migrate` with its own `from:` line ("Repository and controller methods return Task without the Async suffix"). The rule also gains a `Fix` override. Renaming an interface method means moving its implementations with it, and the pack's generic hint does not mention that. The rule's `Because` is untouched, since that reason was never Meridian's to write.
- `di/no-buildserviceprovider` is left exactly as drafted. It was green on the evidence pass, and the pack's own reason, citation and fix already cover it, so it needs no curation.
- The quarantined scope keeps its dragons prose and gains its real `Because`.

Curating a taken rule is the same job as curating one you wrote, minus the part the pack already did. The reason is universal, so it ships in the pack. The posture and the remediation are local, so they stay yours.

With the curated spec built, `baseline init` captures the debt:

```text
$ sliderule baseline init examples/Meridian/Meridian.slnx
data-access/no-inline-sql: captured 12 grandfathered violations.
wrote arch/baselines/data-access/no-inline-sql.json
time/inject-clock: captured 7 grandfathered violations.
wrote arch/baselines/time/inject-clock.json
naming/async-suffix: captured 13 grandfathered violations.
wrote arch/baselines/naming/async-suffix.json
clearance/engine/containment: captured 1 grandfathered violation.
wrote arch/baselines/clearance/engine/containment.json
exit: 0
```

`baseline init` writes four baselines, one per ratcheted rule, holding 12 inline-SQL references, 7 clock reads, 13 unsuffixed methods, and 1 inbound reach. Re-run `check` and the same code is green, because every red is now grandfathered:

```text
$ sliderule check examples/Meridian/Meridian.slnx
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
pass time/inject-clock — Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`.
pass naming/async-suffix — Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
skip clearance/engine/tripwire
  skipped: Tripwire: no diff context — run 'sliderule check --diff-base <ref>' to check changed files against this quarantined scope.

Checked 8 rules: 7 passed, 0 failed, 1 skipped (0 violations, 0 warnings).
exit: 0
```

Each of the four ratcheted passes also prints its grandfathered count (12, 7, 13, and 1); those sub-lines are dropped above. The check exits 0 with zero live violations, and the debt is on the record. These are the only two `check` fences on this page (the draft red and the post-baseline green). The curated-but-unbaselined run in between is the one `baseline init` captured. From here `sliderule status` prints the per-rule burndown, and the [README's status board](README.md#the-burndown) shows what it reports as the numbers fall.

The rule IDs you commit here are the keys of those files. If you rename a rule later, its section stays filed under the old ID. `check` then reports its grandfathered sites as new violations and names the old section in a warning. Run `sliderule baseline rename --from <old-id> --to <new-id>` in the same commit as the rename. It moves the section with every entry in it and never reads the code, so it cannot grandfather anything new. `sliderule status` lists any baseline section that no rule in the spec names.

## Step 7: render and commit

On your solution, `render` turns the spec into agent-readable context. It writes one managed block into the root `AGENTS.md`. It also writes a scoped card into each scope's directory (the dragons). A layer with rules anchored on it gets a card in its directory too (the local rules). Everything outside the managed markers is preserved byte for byte, so `render` is safe to run over hand-written files.

```text
$ sliderule render examples/Meridian/Meridian.slnx
unchanged AGENTS.md
unchanged src/Meridian.Domain/AGENTS.md
unchanged src/Meridian.Web/AGENTS.md
unchanged src/Meridian.Clearance/AGENTS.md
unchanged sliderule.model.json
exit: 0
```

Read that output carefully, because on your codebase the first `render` writes these files. Here every line reports `unchanged` because the committed example already contains what `render` writes. CI runs `render --check` over this example and fails the build on any file a render would change. The last line is the model file, `sliderule.model.json`, which the [build analyzer](../../src/SlideRule.Analyzers/README.md) reads. Your first `render` writes it only when you pass `--model`, and every later `render` keeps it current. The block itself is Meridian's spec in the words an agent reads before it writes. The [README shows what it contains](README.md#what-the-agent-reads), so this page does not repeat it.

If a `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` is in your solution directory or above it, Claude Code reads it instead of `AGENTS.md`, and `render` warns. Your own `~/.claude/CLAUDE.md` does not count. A `CLAUDE.md` next to `AGENTS.md` that contains the line `@AGENTS.md` brings the block back.

Then commit the spec project, `arch/baselines/**`, the rendered `AGENTS.md` files, and the model file if you asked for one, all in one reviewable diff. In one commit, a reviewer reads the proposed law, the acknowledged debt, and the generated context that will keep them true.

## Try it yourself

Everything above is reproducible against a scratch copy of this repository. Work in a throwaway worktree so your checkout stays clean:

```bash
git worktree add ../meridian-sandbox
```

To reset to day zero inside the sandbox, delete `examples/Meridian/arch/` and `examples/Meridian/sliderule.model.json`. Then remove the `Meridian.Architecture` entry (and the now-empty `arch` folder) from `examples/Meridian/Meridian.slnx`. Keep all four `AGENTS.md` files, because `render` only rewrites the managed blocks between its markers, and the root file's hand-written preamble cannot be regenerated. Build the example solution (`dotnet build examples/Meridian/Meridian.slnx`), then walk steps 1 through 7. At step 7, pass `--model` to `render` once, so it writes the model file again.

Every example build attaches the [build analyzer](../../src/SlideRule.Analyzers/README.md), which reads the nearest `sliderule.model.json` at or above each project. Until step 7 writes the example's own, the nearest one is this repository's root model file, whose rules select no Meridian code.

One substitution applies throughout. This repository is a source checkout, so each `sliderule <verb> …` line above runs as `dotnet run --no-build --project src/SlideRule.Cli -- <verb> …` from the repository root. Build the CLI first with `dotnet build src/SlideRule.Cli`. To use the `sliderule` command directly, install the global tool per the [README](README.md#run-it-yourself).

The walk hits two SDK behaviors, and neither affects what `check` reports. First, `dotnet sln add` follows the spec's project references and adds both the SlideRule contract library and the rule pack to the solution. Remove the two of them and confirm four projects remain:

```bash
dotnet sln examples/Meridian/Meridian.slnx remove src/SlideRule/SlideRule.csproj
dotnet sln examples/Meridian/Meridian.slnx remove src/SlideRule.Packs.DotNet/SlideRule.Packs.DotNet.csproj
```

Each `remove` path is relative to your working directory. A path that matches no project is reported and ignored, so re-run `dotnet sln examples/Meridian/Meridian.slnx list` and expect exactly four projects. Second, `dotnet sln add` reformats the solution file, so a little `Meridian.slnx` churn in your final diff is expected.

At the end, the spec and the baselines match the committed files byte for byte, the per-entry content seals included. In the sandbox the only change left is that solution-file formatting, as `git status` reported it after one such walk:

```text
$ git status --porcelain
 M examples/Meridian/Meridian.slnx
exit: 0
```

The spec project is absent from the diff because you finished it to match the committed one. The four baselines, the model file and the four `AGENTS.md` files are absent because the walk reproduced them exactly. The same code and the same spec produced the same evidence and the same files, so the recipe's commands are deterministic.
