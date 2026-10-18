# Three of the four postures on one codebase: Meridian

One codebase has three postures (the law it keeps, the debt it works off, and the module it quarantines). It is the example where the spec meets code that does not yet match its own target. That codebase is Meridian, a freight-forwarding monolith caught mid-migration. Meridian does not use the fourth posture, `Caution`, because its one dangerous module has a gateway to call through. Containing the module still leaves every caller a sanctioned way in.

An agent dropped into a repository reads the files around its task and copies what it finds. In Meridian most of what it finds is the pattern being retired. The spec below names the target patterns and the grandfathered ones. The agent reads the spec's rules as words before it writes, and the build enforces the same rules after.

## The app

Meridian handles bookings, rating, customs, invoicing, and dispatch through eight controllers. Six of them (Shipments, Rates, Invoices, Customs, Drivers, and Manifests) open a `SqlConnection` and run inline SQL. The same six read the wall clock straight from `DateTime.Now` or `DateTime.UtcNow` for cutoffs, demurrage days, and ETA stamps. The other two (Bookings and Quotes) have already moved to constructor-injected repositories and an `IClock`. That six-to-two split is the point of the example. The majority pattern is the one being retired, and an agent reads a majority as house style.

[ARCHITECTURE.md](ARCHITECTURE.md) has two drawings of Meridian. One shows the projects and their references, the other the places the spec names and the references it forbids, and `render --diagram` writes both.

Behind `IClearanceGateway`, the `Meridian.Clearance` module validates container numbers. Its `ContainerCheckDigit` computes the ISO 6346 check digit, the calculation that decides whether `CSQU3054383` is a real container number or a typo. That module is quarantined.

In all, 33 current violations are grandfathered: 12 inline-SQL references, 7 wall-clock reads, 13 `Task`-returning methods missing the `Async` suffix, and 1 reach into the quarantined module. A reference here is one type naming another, so a controller that names `SqlCommand` on several lines counts once, with each line a site. The app builds and runs; the whole thing reads in about ten minutes. It is shaped like the systems SlideRule is built for, which are long-lived, business-critical, and too important to rewrite on a whim.

## The spec

The architecture is seven statements of ordinary C# in [arch/Meridian.Architecture/MeridianArchitectureSpec.cs](arch/Meridian.Architecture/MeridianArchitectureSpec.cs). Five are written there, and two come from `DotNetGuidance`, a shared rule pack. The pack is an ordinary class library that ships only in this repository, and the spec project has a project reference to it. Each statement has a posture and a reason, and most have a fix. (The quarantined scope becomes two checked rules, a containment rule and a tripwire, so `check` reports eight.)

| Rule | Posture | What it requires |
|---|---|---|
| `layering/domain-independent` | Enforce | Domain must not reference Web |
| `naming/controllers` | Enforce | controllers are named `*Controller` |
| `data-access/no-inline-sql` | Migrate | controllers must not open `SqlConnection` |
| `time/inject-clock` | Migrate | Web must not read `DateTime.Now` / `UtcNow` |
| `naming/async-suffix` | Migrate | authored `Task`/`ValueTask`-returning methods end in `Async` (from the pack) |
| `di/no-buildserviceprovider` | Enforce | no `BuildServiceProvider` while configuring (from the pack) |
| `clearance/engine` | Quarantine | reach the module only via `IClearanceGateway` |

Three rules are already true, so they are law (`Enforce`). Three describe debt with a target, so they ratchet (`Migrate`). Their current violations are grandfathered, and anything new is red. One walls off a module with no target state (`Quarantine`).

The two pack rules show both halves of that split from one source. Both are canonical .NET guidance, and the posture is Meridian's call. The two calls are in the same `Define` as the local rules:

```csharp
DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web),
    PackPosture.Migrate("Repository and controller methods return Task without the Async suffix."),
    "Rename the method to end in Async and update its callers; see the interface and its implementation together.");

DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);
```

The same `naming/async-suffix` is `Enforce` in the [Interchange example](../Meridian.Interchange/), where the codebase already keeps it. A pack provides the rule and its reason, and the spec that takes the rule gives it a posture, `Enforce` or `Migrate`.

## What the agent reads

`sliderule render` writes this block into [AGENTS.md](AGENTS.md) from the spec. CI re-renders it on each push to `main` and on each pull request, and fails on any diff. The context an agent reads is therefore the spec the build enforces.

```markdown
### Rules
- `layering/domain-independent` — The Domain layer must not reference the Web layer. Domain holds the booking and rate model the rest of the system depends on; it must not reach up into the web tier.
- `naming/controllers` — Types derived from `ControllerBase` must be named `*Controller`. Request handlers are found by their `*Controller` name — by routing and by agents reading the code; keep the convention total.
- `di/no-buildserviceprovider` — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`. Calling BuildServiceProvider while configuring services builds a second container with its own singletons, a duplicate-instance trap. See <https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines>.

### Migrations
- `data-access/no-inline-sql` — Some existing code here still follows the OLD pattern: Controllers open SqlConnection and run inline SQL directly. That is grandfathered debt, not house style. New code must follow: Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`. Data access behind a repository can be tested and swapped; SQL in the request path cannot. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
- `time/inject-clock` — Some existing code here still follows the OLD pattern: Code reads the ambient clock directly. That is grandfathered debt, not house style. New code must follow: Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`. Cutoffs, demurrage, and ETA stamps read from the wall clock cannot be tested at a fixed instant; an injected IClock makes the moment an input. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
- `naming/async-suffix` — Some existing code here still follows the OLD pattern: Repository and controller methods return Task without the Async suffix. That is grandfathered debt, not house style. New code must follow: Methods returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` declared in authored types in the Domain or Web layers must be named `*Async`. Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited. See <https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap>. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.

### Quarantined scopes
- `clearance/engine` — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`. The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it. Sanctioned surface: `IClearanceGateway`, `ClearanceGateway`.
```

## Three ways an agent goes wrong here

Each of the failure modes here has a posture that answers it. The `sliderule check` output below is real, cut to the rule that went red, and the first block also keeps the summary line.

### The statistical prior

Six of the eight controllers open a `SqlConnection`. An agent reading them infers that direct SQL is how Meridian does data access, and writes its next controller the same way. The rendered Migration text counters that prior in the agent's context ("that is grandfathered debt, not house style. New code must follow..."), and the ratchet enforces it.

Add an inline-SQL method to `BookingsController`, one of the two migrated controllers, and `check` goes red on the two new references while the 12 grandfathered ones stay quiet:

```text
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Data access behind a repository can be tested and swapped; SQL in the request path cannot.
  fix: Move the SQL into a repository.
  exemplar: BookingRepository (src/Meridian.Web/Data/BookingRepository.cs:6)
  src/Meridian.Web/Controllers/BookingsController.cs:77 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:78 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  grandfathered: 12 (baselined; run 'sliderule status' for burndown)

Checked 8 rules: 6 passed, 1 failed, 1 skipped (2 violations, 0 warnings).
```

The message gives the rule ID, the reason, the fix, a type to copy, and the exact `file:line`. Revert the method and `check` is green again. New code in the old pattern is red, and the existing debt stays on the baseline.

The build analyzer attached to every example build reports the same sites. With the inline-SQL method in place, `dotnet build examples/Meridian/Meridian.slnx` warns `SR2002` at the two new sites and still exits 0. CI builds the examples with the analyzer's IDs promoted to errors, as [Rolling out the build analyzer](../../OPERATING.md#rolling-out-the-build-analyzer) shows, so there the warning fails the build.

### The helpful refactor

`ContainerNumberValidator` is public, so an agent tidying the code can call it directly and delete an "unnecessary" hop through `IClearanceGateway`. The quarantined scope's containment rule stops that, because the only sanctioned way into `Meridian.Clearance` is the gateway. Reach past it and the reference is red, with the fix naming the surface to use:

```text
FAIL clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  because: The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.
  fix: use `IClearanceGateway`
  src/Meridian.Web/Controllers/BookingsController.cs:75 — Meridian.Web.Controllers.BookingsController references Meridian.Clearance.ContainerNumberValidator
  grandfathered: 1 (baselined; run 'sliderule status' for burndown)
```

One reach into the module already exists (`CustomsController` news up the validator), and it is grandfathered on the record. Every new one is red.

### Misreading structural weirdness

The ISO 6346 check-digit table looks broken. It assigns A=10, B=12, C=13, and skips a value every so often, so K=21 is followed by L=23. An agent that "corrects" the gap to make the table contiguous breaks the check digit for every real container number in the system. This is the weirdness a `Quarantine` scope can only document, because agents still have to call into the code. `render` writes this card into the module's own directory, next to the file:

```markdown
## Quarantined scope `clearance/engine`

This directory contains the quarantined `clearance/engine` scope: types in `Meridian.Clearance.*`. Here be dragons: do not spread references into it.

Dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are structural — linearizing the table breaks every real container number. Call in only through IClearanceGateway.
```

The gaps are the standard. The letter values skip every multiple of 11 (11, 22, 33), which is why the table is not contiguous. The dragons prose explains what the code does, which part is structural, and how to call into it. The tripwire prints that prose when a turn edits the module.

## The burndown

Because the Migrate and Quarantine baselines are counted, `sliderule status` reports what is left to work off:

```text
pass layering/domain-independent
pass naming/controllers
pass data-access/no-inline-sql (migrate) — 12 grandfathered remaining (31 sites), 0 new, 0 fixed awaiting acceptance
pass time/inject-clock (migrate) — 7 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass naming/async-suffix (migrate) — 13 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass di/no-buildserviceprovider
pass clearance/engine/containment (quarantine) — 1 grandfathered remaining (2 sites), 0 new, 0 fixed awaiting acceptance
skip clearance/engine/tripwire (tripwire) — diff-aware; run 'sliderule check --diff-base <ref>'
Checked 8 rules: 7 passed, 0 failed, 1 skipped. Burndown: 33 grandfathered remaining (53 sites), 0 fixed awaiting acceptance.
```

Move a controller onto a repository and its grandfathered count drops. When a Migrate rule reaches zero and `sliderule baseline accept-reductions` has emptied its baseline, `status` reports it as promotable to `Enforce`.

## Run it yourself

SlideRule ships as a .NET global tool (solution-loading commands need a .NET 10 SDK):

```bash
dotnet tool install -g SlideRule.Cli
```

From a checkout of this repository, build the example and check it:

```bash
dotnet build examples/Meridian/Meridian.slnx
sliderule check examples/Meridian/Meridian.slnx
```

`check` exits 0 here, because every current violation is on the baseline. `sliderule status` prints the burndown above. `sliderule render examples/Meridian/Meridian.slnx` regenerates the `AGENTS.md` files and the model file from the spec, and adding `--diagram examples/Meridian/ARCHITECTURE.md` also redraws the [ARCHITECTURE.md](ARCHITECTURE.md) drawings. Introduce one of the violations from this page and `check` exits 1 with the message shown.

## In the agent's loop

The rendered block steers an agent's first attempt, and a hook makes the same rules catch a wrong one before a reviewer sees it. Wired as a Claude Code `Stop` hook, `sliderule hook` runs the check when the agent tries to end its turn. A new violation goes back to the agent on stderr as the reason the turn is not over. The agent reads the rule and corrects the code before it returns the work. Add the inline-SQL method from [the statistical prior](#the-statistical-prior) to `BookingsController`, and the hook blocks the stop with exit 2. The hook's stderr gives the failing rule (its passing siblings in the report omitted here):

```text
FAIL data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  because: Data access behind a repository can be tested and swapped; SQL in the request path cannot.
  fix: Move the SQL into a repository.
  exemplar: BookingRepository (src/Meridian.Web/Data/BookingRepository.cs:6)
  src/Meridian.Web/Controllers/BookingsController.cs:77 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlConnection
  src/Meridian.Web/Controllers/BookingsController.cs:78 — Meridian.Web.Controllers.BookingsController references Microsoft.Data.SqlClient.SqlCommand
  grandfathered: 12 (baselined; run 'sliderule status' for burndown)

Checked 8 rules: 7 passed, 1 failed, 0 skipped (2 violations, 0 warnings).
```

The hook's check reads the files the turn changed, which runs the quarantined scope's tripwire as well, so its summary counts no skip. The exemplar line points at `BookingRepository`, and `IBookingRepository` already has the query. The correction is a two-line call through the injected repository. This repository keeps `.claude/` local, so the [`hooks/`](hooks/) directory contains the hook config as a snippet to paste into `.claude/settings.json`. The [storyboard](hooks/storyboard.md) there walks one task from the brief to the agent's self-correction, with real captured output.

## From here

The clean-architecture example, [`Meridian.Quoting`](../Meridian.Quoting/), is a greenfield subsystem where every rule is `Enforce` from day one, with no baseline to work off. The clock ratchet is law there as `time/injected-clock`, the same ban on `DateTime.Now` and `DateTime.UtcNow`. The inline-SQL ratchet's target is the shape Quoting was built in, where the use cases reach data only through repository interfaces. The async-suffix ratchet has no counterpart there.

[ADOPTING.md](ADOPTING.md) walks the other direction and shows how this spec was derived from Meridian's code, one real command at a time. It is the day-one flow for a codebase that already exists.
