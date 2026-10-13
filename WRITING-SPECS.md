# Writing specs

This page is for someone writing a spec's rules after the first one, which [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) shows how to make. It covers what a rule is made of, its two postures, and the two kinds of scope. It also shows how a rule picks the code it covers, and how to see what a rule will do before it gates anyone. Every code example is taken from a spec committed in this repository.

[GRAMMAR.md](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md) is the language reference. [Its vocabulary table](https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md#5-vocabulary-v1-closed-every-member-ships-with-pinned-fragments) lists every noun, adjective and verb, with the sentence fragment each one renders.

## The spec class

A spec is one class that implements `IArchitectureSpec`. Its `Define(Arch arch)` method declares the layers first, then the rules and scopes about them. A layer is a named region of the code. `arch.Layer(name, pattern)` defines one by namespace pattern, and `arch.Layer(name, arch.Project("MyApp.Web"))` defines one by project. `.Purpose(...)` adds a sentence that `render` writes into the Layers list of the `AGENTS.md` block and onto the layer's card. A purpose alone places no card, because a layer gets one only when a rule has the layer as its subject. The Meridian example declares its two layers with a purpose each:

```csharp
Layer domain = arch.Layer("Domain", "Meridian.Domain.*")
    .Purpose("Domain holds the booking and rate model and the ports the web tier depends on.");
Layer web = arch.Layer("Web", "Meridian.Web.*")
    .Purpose("Web is the HTTP surface of the monolith: the controllers, their request and response models, " +
             "and the data access behind them.");
```

A rule about a layer reads in the layer's name ("The Domain layer must not reference the Web layer"). Declare a layer for each region your architecture already names, and use a plain selection, such as `arch.Namespace("MyApp.Web.Controllers.*")`, for a subject only one rule needs.

`Define` can call methods of its own, and a large spec reads better split by concern: this repository's spec calls one private method per area. Loading the spec validates all of it, across methods and classes, and reports every error in one pass.

## The parts of a rule

A rule is one statement. It starts with `arch.Rule(id)` and has a posture with its constraint, a reason, and up to three optional trailers:

| Part | Required | What it does |
|---|---|---|
| the ID | yes | names the rule in reports, in `explain` and in its baseline |
| the posture | yes | `Enforce(...)` or `Migrate(from:, to:)` |
| `Because` | yes | one line of reason, rendered for agents and printed in every report |
| `Fix` | no | what to do instead, printed under a failed rule and in each build warning |
| `Citation` | no | an absolute `http` or `https` URL the reason rests on |
| `Exemplar` | no | a type to copy, printed with its `file:line` under a failed rule |

The Quoting example cites the Microsoft naming guidelines for its interface rule:

```csharp
arch.Rule("naming/interfaces")
    .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("Meridian.Quoting.*").MustHavePrefix("I"))
    .Because("The `I` prefix is how a reader and an agent tell a port from its implementation at a glance; the convention stays total so the distinction is reliable.")
    .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces");
```

An ID must match `^[a-z0-9-]+(/[a-z0-9-]+)*$`, and the convention is `area/rule-name`. The ID also keys the rule's baseline, so when a ratcheted rule's ID changes, `sliderule baseline rename` moves its baseline to the new one. A citation becomes the rule's help link in SARIF and a `citation:` line in reports. In the `AGENTS.md` block and the cards, it follows the reason as `See <url>.`, and the fix and exemplar do not appear. An exemplar never affects the verdict, and `check` warns when it matches no type.

## Enforce and Migrate

`Enforce` is the law: every violation fails, including the ones that predate the rule. Use it for what the code already keeps.

`Migrate(from:, to:)` is a ratchet. `from:` describes the old pattern in words, and `to:` is the constraint the code is moving towards. Until you capture the rule's baseline, every violation fails. The ones present at the capture are then grandfathered, and any new violation fails:

```csharp
arch.Rule("data-access/no-inline-sql")
    .Migrate(
        from: "Controllers open SqlConnection and run inline SQL directly.",
        to: arch.Namespace("Meridian.Web.Controllers.*")
            .MustNotReference(typeof(SqlConnection), typeof(SqlCommand)))
    .Because("Data access behind a repository can be tested and swapped; SQL in the request path cannot.")
    .Fix("Move the SQL into a repository.")
    .Exemplar(arch.Types.Named("BookingRepository"));
```

`sliderule baseline init` captures the baseline into `arch/baselines/<rule-id>.json` under the solution directory, and you commit the file with the spec. `.Baseline(path)` puts it somewhere else. Fixed sites leave it through `sliderule baseline accept-reductions`, and only `sliderule baseline add` grows it, one attributed entry at a time. `status` suggests promoting the rule to `Enforce` once its baseline is empty.

`.WhileYoureThere(...)` takes a `MigrationPolicy` and sets what the rendered context asks an agent to do with a grandfathered site it touches. The default, `MigrateIfSmall`, asks it to migrate a site it is already editing when the migration is small. `AlwaysMigrate` asks for the migration in every change that touches a site, and `NeverMigrate` asks it to leave the sites for a coordinated migration. All three tell the agent never to grow the debt.

## What a rule covers

A rule's subject is a selection of types, built from a starting set and narrowed with filters:

| Selection | Selects |
|---|---|
| `arch.Types` | every type the solution declares |
| `arch.Namespace("MyApp.Web.*")` | the types in a namespace pattern |
| a layer | the types its definition selects |
| `arch.Registered(Lifetime.Singleton)` | the types a container registration names |
| `.InNamespace(pattern)` | the part of a selection in a namespace |
| `.Named("A", "B")` | the types with these simple names |
| `.Except(selection)` | a selection minus another |
| `arch.AnyOf(a, b)` | the union of selections |
| `.DerivedFrom(...)`, `.Implementing(...)` | types by base type or interface |
| `.AttributedWith(...)`, `.OfKind(...)` | types by attribute or by kind |
| `.Authored()` | the types a person wrote, without generated code |

Namespace patterns are case-sensitive and match whole dot-separated segments. A trailing `.*` covers the namespace and every namespace below it, so `MyApp.Domain.*` includes `MyApp.Domain` itself. An interior `*` segment matches exactly one segment, a `*` inside a segment (`Legacy*`) matches within that segment, and a lone `*` matches everything. A pattern with a `*` before its trailing `.*`, such as `MyApp.*.Controllers.*`, can never match, so loading the spec reports it as an error. The contract package's analyzer flags it as `SR1017` while you type.

A type the spec can reference is named with `typeof()`, and one it cannot is named by its full name as a string. Only the filters and verbs about base types, interfaces, attributes and member types take a string, so the reference verbs reach such a type through a namespace pattern. The Meridian spec has no reference to ASP.NET Core, so it names the controller base class as a string:

```csharp
arch.Rule("naming/controllers")
    .Enforce(arch.Types.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase").MustHaveSuffix("Controller"))
    .Because("Request handlers are found by their `*Controller` name — by routing and by agents reading the code; keep the convention total.");
```

`.Methods`, `.Properties`, `.Fields`, `.Events` and `.Members` turn a type selection into a selection of members, for rules about signatures and shapes. In this repository's own spec, `repo` is `arch.Namespace("SlideRule.*")`, and `McpServerToolAttribute` is the attribute's full name as a string:

```csharp
arch.Rule("mcp/tools-accept-cancellation")
    .Enforce(repo
        .Methods.AttributedWith(McpServerToolAttribute)
        .MustAcceptParameter<CancellationToken>())
    .Because("Tool calls run inside one long-lived server process; a tool method without a " +
             "CancellationToken cannot honor a client cancel and holds the idle watchdog open.")
    .Citation("https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/cancellation")
    .Fix("Add a trailing CancellationToken parameter (default it) and flow it into the runner.");
```

`arch.Projects` selects projects, for rules about project files: target frameworks, package references, lock files.

```csharp
arch.Rule("packaging/core-netstandard-only")
    .Enforce(arch.Projects.Named("SlideRule").MustOnlyTarget("netstandard2.0"))
    .Because("netstandard2.0 is the one TFM a net48 spec project and the net10 host can both load; " +
             "Core is the contract every spec references, so its TFM is the product's reach.")
    .Fix("Anything needing a newer API belongs in SlideRule.Roslyn or the CLI, never Core.");
```

`arch.Each(...)` declares a family, one rule over several layers, and each layer in it is a cell. In the Operations example below, `dispatch`, `tracking` and `invoicing` are layers, one per module. `MustOnlyBeReferencedByItself` checks each cell against its own allow-list, the cell itself, so a reference into a module must come from inside it. The `Except` takes each module's `Contracts` namespace out of the subject, so any module may reference another module's contracts:

```csharp
Selection dispatchContracts = arch.Namespace("Meridian.Operations.Dispatch.Contracts.*");
Selection trackingContracts = arch.Namespace("Meridian.Operations.Tracking.Contracts.*");
Selection invoicingContracts = arch.Namespace("Meridian.Operations.Invoicing.Contracts.*");

arch.Rule("modules/internals")
    .Enforce(arch.Each(dispatch, tracking, invoicing)
                 .Except(dispatchContracts, trackingContracts, invoicingContracts)
                 .MustOnlyBeReferencedByItself())
    .Because("Every module is reached only through its Contracts surface, so the dispatch board and roster, the milestone store and log, and the invoice assembler and reconciler all stay swappable; a reference into any of them from another module turns a private implementation detail into a contract its owner can no longer revise without breaking a caller.")
    .Fix("Depend on the module's `Contracts` type (`IDispatchBoard`, `ITrackingLog` or `IInvoiceRun`), not on its internals.");
```

The family verbs `MustNotReferenceEachOther`, `MustOnlyBeReferencedByItself` and `MustNotHaveCircularReferences` judge the cells against one another. A family can also take a project selection, as `arch.Each(arch.Projects.Matching("Nop.Plugin.*"))`, where each project is a cell. Either form stands only as a rule's subject. A type that belongs to two cells fails the rule, which names the type and both cells. `MustNotHaveCircularReferences` takes a family of layers alone, because the build rejects a circular project reference.

`arch.Registered(Lifetime.Singleton)` selects the service and implementation types a container registration in the source names with that lifetime, and `arch.Registered()` takes any lifetime. The registrations it sees are calls to `AddSingleton`, `AddScoped` and `AddTransient`, their `TryAdd` forms, `AddHostedService`, `AddDbContext`, `AddDbContextPool` and `AddHttpClient` on an `IServiceCollection`. Registrations made any other way are not seen, including keyed-service and raw `ServiceDescriptor` registrations and those made by assembly scanning, factory internals and framework defaults. This repository's own spec uses it on both sides of `MustNotInject`, so a singleton that injects a scoped or transient service fails the rule.

`.Where(predicate, description:)` filters a selection by any condition C# can state, and `Must(condition, description:)` states a constraint the vocabulary has no verb for. The `description:` is how the rule reads, so both require it. A rule with either one is left out of the model file the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) reads, so the analyzer leaves it to `check` and the xUnit adapter.

A rule whose subject matches no types fails with `The subject selection matched no solution-declared types.`, so a mistyped pattern shows up on the first check.

## What a constraint counts

The verbs count facts in source code, as the glossary at the top of a rendered `AGENTS.md` block defines the ones its spec uses:

| Verb family | Counts |
|---|---|
| `MustNotReference`, `MustOnlyReference` | a type named in source |
| `MustNotUse` | a member access, such as `DateTime.Now` |
| `MustNotConstruct` | a `new`, target-typed `new()` included |
| `MustNotInject` | a constructor parameter, primary constructors included |
| `MustNotCatch`, `MustOnlyThrow` | a `catch` clause, or the type a `throw` throws |
| `MustBeRegistered` | a type named in a container registration |
| `MustNotExpose` | a type in a public member's signature |

Other verbs test the shape and name of a type or a member (`MustBeSealed`, `MustHaveSuffix`, `MustAcceptParameter`) or a project's properties (`MustOnlyTarget`, `MustLockPackages`). The vocabulary table lists every one, with its overloads.

## Scopes

A scope marks code that needs care. Its dragons are the notes on why the code is fragile and how to call into it, written with `Dragons(...)` or linked as a longer document with `DragonsDoc(path)`. `render` puts them on a card in the scope's directory, and `explain` and the MCP server's `arch_context` return them. Every scope needs a `Because`, and dragons in one of the two forms.

`Quarantine` contains its code. Its containment rule fails a reference into the scope from outside it. `baseline init` records the references there today, and from then on only a new one fails. `BoundaryOnlyVia(...)` names the sanctioned way in, and a quarantine without it is hermetic:

```csharp
arch.Scope("clearance/engine")
    .Quarantine(arch.Namespace("Meridian.Clearance.*"))
    .BoundaryOnlyVia(typeof(IClearanceGateway), typeof(ClearanceGateway))
    .Dragons("ISO 6346 check digit: the letter-value table skips every multiple of 11 " +
             "(A=10, B=12 … U=32); the gaps are structural — linearizing the table " +
             "breaks every real container number. Call in only through IClearanceGateway.")
    .Because("The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.");
```

A quarantine declares two rules, `{scope-id}/containment` and `{scope-id}/tripwire`. Every scope reserves its own ID and every ID beneath it, so loading the spec reports a rule that takes one. The tripwire warns when a change touches the scope. It reads the change set from `check --diff-base <ref>`, or from the files a turn changed when the agent hook runs the check. A `check` without `--diff-base` reports the tripwire skipped.

`Caution` marks code that stays open to new callers. It declares the tripwire alone, so a cautioned scope warns and never fails. Here `model` is the Model layer the spec declared earlier:

```csharp
arch.Scope("model/prose-fragments")
    .Caution(model)
    .Dragons("Every node here declares the prose fragment the renderers assemble into its sentence, and " +
             "the fragments are position-blind: one phrase serves subject and reference position, so a " +
             "fragment never closes its own parenthetical — the closing comma belongs to the junction " +
             "(SentenceRenderer.EndsOpen and CloseBefore). A strict verb renders its strictness by the " +
             "absence of the external-packages caveat; do not add one. A changed fragment moves its pin " +
             "in the same commit.")
    .Because("Every reader in Core reaches into Model and every renderer assembles its fragments, so " +
             "nothing here can be fenced; the hazard is an edit that reads as tidying.");
```

## The spec project

A rule that names a type with `typeof()` needs a spec project that can reference that type's project, so the projects a spec names determine its target framework. The contract package is `netstandard2.0`, so a spec project can target anything from `net48` up. A spec project on `net48` or `netstandard2.0` compiles at C# 7.3 unless it sets `<LangVersion>latest</LangVersion>`.

A .NET shared-framework type, such as an ASP.NET Core or WPF type, is never copied into a class library's output. A type that exists only on .NET Framework, such as one from `System.Web`, cannot load in the tool's .NET host. Name either by string or by namespace pattern. A spec that names NuGet package types with `typeof()` sets `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`, which copies the package assemblies next to the spec DLL, where the checker loads them from. Without it, `check` fails to load the spec and names the setting.

In a repository on central package management, the spec project can set `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` and pin its own `SlideRule` version, as the scaffold of the [`derive_spec` recipe](https://github.com/andypgray/sliderule/blob/main/CODING-AGENTS.md#drafting-a-spec-with-derive_spec) does. It then builds the same way in any repository.

## Rule packs

A rule pack is a static class whose methods declare rules on the spec's `Arch`, so several specs share one definition of a rule. The pack supplies the reason and the citation, and the spec chooses the posture and the subject. [`SlideRule.Packs.DotNet`](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Packs.DotNet/README.md) is a worked example with nine .NET rules, and the Meridian and Interchange examples take rules from it. It is not published as a package and targets `net10.0`, so copy its source or write a pack of your own. `SpecValidation.Validate(new MySpec())` returns every mistake in the spec, one message each, in the words `sliderule check` prints them, and an empty list proves a pack's rules valid without loading a solution.

## Checking a spec as you write it

The contract package's analyzer reports a malformed ID, pattern or prose argument as a warning on the literal while you type. Its completion provider offers the namespaces and type names of the projects your spec references. The [package README](https://github.com/andypgray/sliderule/blob/main/src/SlideRule/README.md) lists both.

`sliderule explain <rule-id>` prints a rule as it renders, with its reason, fix, citation, exemplar and posture. `explain --type <name>` and `explain --path <file>` show which layers, rules and scopes claim a type, and why. Run one after adding a layer or rule to see whether its selection reaches the types you meant.

`check` also reports, as [warnings](https://github.com/andypgray/sliderule/blob/main/CLI.md#warnings), what a verdict alone cannot show. Three point at the spec: a forbidden target that matched no types (`inertTarget`), a pattern in a rule's list that matched none (`deadOperand`), and an exemplar that matched no type (`deadExemplar`).

`sliderule render` writes the rules into `AGENTS.md` and the cards. A render's diff shows the sentence each new rule renders and the directories that gain a card. Build the spec project first, because `render` and `check` run the spec from its last build.
