# Enforce-only clean architecture: Meridian.Quoting

Nine rules hold a four-layer clean architecture, every one `Enforce`, with zero baselines. ArchUnitNET already shows exactly that. The difference is the generated agent context beside the spec. The worked codebase is a greenfield customer-quoting service, the strangler step after the monolith in [`../Meridian/`](../Meridian/) migrated its quoting controllers.

## The subsystem

Four layers run one direction. The module map at the top of [AGENTS.md](AGENTS.md) says what each is for, rendered from the `.Purpose` each layer declares in the spec. The rules hold that shape; the subsystem was born conforming, so there is nothing to ratchet.

Writes go through a command bus that opens a unit of work only around a handler marked `[Transactional]`. `RequestQuoteHandler` writes twice, first to reserve a quote number and then to persist the quote. An unmarked command handler would commit each write on its own. A failure between the two would burn a quote number with no quote to show for it.

Time enters through `IClock`, and `SystemClock` is the single adapter that reads the machine clock. A quote's 14-day validity window is therefore computed from an injected instant, and a test can check the window at a fixed moment.

[ARCHITECTURE.md](ARCHITECTURE.md) draws the four layers as the code actually references them, beside the law the spec holds them to. The law fence is nearly empty, because only two of the nine rules have an arrow to draw.

## The block beside the rules

Three rules from [arch/Meridian.Quoting.Architecture/QuotingArchitectureSpec.cs](arch/Meridian.Quoting.Architecture/QuotingArchitectureSpec.cs):

```csharp
        arch.Rule("layering/application-boundaries")
            .Enforce(application.MustOnlyReference(domain))
            .Because("Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.")
            .Fix("Depend on a port (an interface in Domain or Application) instead of the concrete type; wire the implementation in the Api composition root.");

        arch.Rule("handlers/transactional")
            .Enforce(arch.Types.Implementing(typeof(ICommandHandler<>)).MustBeAttributedWith<TransactionalAttribute>())
            .Because("Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.")
            .Fix("Add `[Transactional]` to the command handler; if the work is read-only, make it a query handler instead.")
            .Exemplar(arch.Type<RequestQuoteHandler>());

        arch.Rule("time/injected-clock")
            .Enforce(arch.Types.InNamespace("Meridian.Quoting.*")
                         .Except(arch.Types.Named("SystemClock"))
                         .MustNotUse(
                             () => DateTime.Now,
                             () => DateTime.UtcNow))
            .Because("A quote's validity window is computed from the current instant; read straight from the wall clock it cannot be tested at a fixed moment, so time enters through IClock and SystemClock is the one adapter that reads the machine clock.")
            .Fix("Take IClock in the constructor and read clock.UtcNow; SystemClock is the one sanctioned wall-clock read.")
            .Exemplar(arch.Type<RequestQuoteHandler>());
```

The exact lines those three produce in [AGENTS.md](AGENTS.md):

```markdown
- `layering/application-boundaries` — The Application layer must reference only the Domain layer (external packages are not constrained by this rule). Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.
- `handlers/transactional` — Types implementing `ICommandHandler<TCommand>` must be attributed with `[Transactional]`. Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.
- `time/injected-clock` — Types in `Meridian.Quoting.*`, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`. A quote's validity window is computed from the current instant; read straight from the wall clock it cannot be tested at a fixed moment, so time enters through IClock and SystemClock is the one adapter that reads the machine clock.
```

Each rendered rule opens with a sentence generated from the fluent call, then carries its `Because` string verbatim. `sliderule render` writes the whole block into `AGENTS.md`. CI re-runs `render` on every push and fails on any diff, so the block an agent reads is provably the spec the build enforces. `layering/application-boundaries` renders a caveat about its own scope. `MustOnlyReference` bounds the Application layer against the other layers, and the parenthetical `(external packages are not constrained by this rule)` says so, because a use case still depends on framework types. The call names only `domain` and the sentence only the Domain layer, because a `MustOnly*` subject may always reach itself. An Application-to-Application reference needs no permission written down.

## Every rule as a named test

The `SlideRule.Xunit` adapter turns each rule into its own xUnit test with the rule ID as the display name. Here is `dotnet test --output Detailed` on the adapter project, captured from a real run. The runner's lines that name the test assembly's path are left out:

```text
passed Meridian.Quoting.ArchTests.QuotingArchitectureTests.Workspace_LoadedCompletely (8s 727ms)
passed handlers/transactional(ruleId: "handlers/transactional") (1ms)
passed time/injected-clock(ruleId: "time/injected-clock") (0ms)
passed naming/controllers(ruleId: "naming/controllers") (0ms)
passed messaging/immutable-messages(ruleId: "messaging/immutable-messages") (0ms)
passed layering/domain-independent(ruleId: "layering/domain-independent") (0ms)
passed persistence/repository-ports(ruleId: "persistence/repository-ports") (0ms)
passed layering/application-boundaries(ruleId: "layering/application-boundaries") (0ms)
passed naming/interfaces(ruleId: "naming/interfaces") (0ms)
passed handlers/naming(ruleId: "handlers/naming") (0ms)

Test run summary: Passed!
  total: 10
  failed: 0
  succeeded: 10
  skipped: 0
  duration: 10s 089ms
```

The one-time MSBuild workspace load bills to whichever case runs first (here `Workspace_LoadedCompletely`, at 8.7 s). The nine rule tests then read the shared verdict in a millisecond or less. The adapter adds that tenth test so the run fails when a project does not load.

Delete the `[Transactional]` attribute from `RequestQuoteHandler` and the test named `handlers/transactional` is the one that fails, with the message:

```text
FAIL handlers/transactional — Types implementing `ICommandHandler<TCommand>` must be attributed with `[Transactional]`.
  because: Every command here mutates the store, and the command bus opens a unit of work only around a handler marked `[Transactional]`; an unmarked command handler would commit each write on its own and leave a half-written quote if it failed midway.
  fix: Add `[Transactional]` to the command handler; if the work is read-only, make it a query handler instead.
  exemplar: RequestQuoteHandler (src/Meridian.Quoting.Application/Handlers/RequestQuoteHandler.cs:13)
  src/Meridian.Quoting.Application/Handlers/RequestQuoteHandler.cs:13 — Meridian.Quoting.Application.Handlers.RequestQuoteHandler
```

That text is byte-identical to the CLI's FAIL block by construction. The adapter and the CLI share one renderer, and a product test pins them equal. Restore the attribute and the test is green.

## One edit, four message components

Layers here are namespace patterns (`Meridian.Quoting.Application.*`), not project references. You cannot break the boundary with a direct `Application -> Infrastructure` project reference. Infrastructure already references Application, and MSBuild rejects the cycle. A namespace violation compiles anyway, and the checker catches what the project graph cannot see.

The agent-failure story is an agent that wires the concrete repository instead of the port. Drop one file into the Api project (which already references Infrastructure, so it compiles) declaring the Application namespace and newing up `InMemoryQuoteRepository`. The layer is the namespace, so the type lands in Application while the file sits in Api, and `check` goes red. Here is the failing rule's block from its report:

```text
FAIL layering/application-boundaries — The Application layer must reference only the Domain layer (external packages are not constrained by this rule).
  because: Use cases depend on the Domain and on abstractions they own, never on a concrete adapter; keeping Infrastructure and Api out of Application is what lets persistence and transport be swapped or faked in a test.
  fix: Depend on a port (an interface in Domain or Application) instead of the concrete type; wire the implementation in the Api composition root.
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:12 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryDatabase
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:16 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryQuoteRepository
  src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs:17 — Meridian.Quoting.Application.Handlers.ExpireQuotesHandler references Meridian.Quoting.Infrastructure.Persistence.InMemoryQuoteRepository
```

`check` exits 1 and the summary reads `Checked 9 rules: 8 passed, 1 failed, 0 skipped (2 violations, 0 warnings)`, so exactly one rule is red. The block carries the ID, the reason, the fix, and the exact `file:line`, and no `grandfathered:` line, because this is day-one law with nothing baselined. That is the contrast with the ratcheted blocks in [`../Meridian/`](../Meridian/), where every current violation sits on a counted baseline. Delete the file, rebuild, and `check` is green again.

## Run it yourself

SlideRule ships as a .NET global tool (solution-loading commands need a .NET 10 SDK):

```bash
dotnet tool install -g SlideRule.Cli
```

The test leg is a package too. A consumer adds `SlideRule.Xunit` to its own test project and derives one class per spec, as `tests/Meridian.Quoting.ArchTests` does here. From a checkout of this repository, build the subsystem and check it:

```bash
dotnet build examples/Meridian.Quoting/Meridian.Quoting.slnx
sliderule check examples/Meridian.Quoting/Meridian.Quoting.slnx
```

`check` exits 0 because every rule holds. `sliderule render` regenerates the `AGENTS.md` block and the [ARCHITECTURE.md](ARCHITECTURE.md) drawings from the spec. `dotnet test examples/Meridian.Quoting/tests/Meridian.Quoting.ArchTests/Meridian.Quoting.ArchTests.csproj` runs the nine named rule tests (add `--output Detailed` for a line per test).

## From here

[`../Meridian/`](../Meridian/) is the monolith that ratchets its debt as `Migrate` rules. Its `time/inject-clock` target is `time/injected-clock` here, the same ban on `DateTime.Now` and `DateTime.UtcNow` with no baseline to grandfather. Its `data-access/no-inline-sql` target is the shape this subsystem was built in, with data access behind repository interfaces. Here `persistence/repository-ports` keeps those interfaces in Domain, and `layering/application-boundaries` keeps the use cases off their implementations. Its `naming/async-suffix` has no counterpart here. Two rules match by name across both specs, domain independence and controller naming, `Enforce` in each.
