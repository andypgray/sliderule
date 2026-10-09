# Three of the four postures on one codebase: Meridian

One codebase carries three postures (the law it keeps, the debt it works off, and the module nobody touches). It is the example where the spec meets code that does not yet match its own target. That codebase is Meridian, a freight-forwarding monolith caught mid-migration. Nothing here calls for the fourth posture, `Caution`. Meridian's one dangerous module has a gateway to call through, so containing it costs no legitimate caller anything.

An agent dropped into a repository reads the files around its task and copies what it finds. In Meridian most of what it finds is the pattern being retired. The spec below is how the build tells the agent which patterns are the target and which are grandfathered, in words the agent reads before it writes and the same rules the build enforces after.

## The app

Meridian handles bookings, rating, customs, invoicing, and dispatch. Eight controllers sit at the front of it. Six of them (Shipments, Rates, Invoices, Customs, Drivers, and Manifests) open a `SqlConnection`, run inline SQL, and read the wall clock straight from `DateTime.Now` or `DateTime.UtcNow` for cutoffs, demurrage days, and ETA stamps. Two of them (Bookings and Quotes) have already moved to constructor-injected repositories and an `IClock`. That six-to-two split is the whole point. The majority pattern is the one being retired, which is exactly the arrangement an agent reads as house style.

[ARCHITECTURE.md](ARCHITECTURE.md) draws that shape. It puts the projects and their references beside the law the spec holds them to, and `render` writes both drawings.

Behind `IClearanceGateway`, the `Meridian.Clearance` module validates container numbers. Its `ContainerCheckDigit` computes the ISO 6346 check digit, the calculation that decides whether `CSQU3054383` is a real container number or a typo. That module is quarantined.

Thirty-three current violations are grandfathered: twelve inline-SQL references, seven wall-clock reads, thirteen `Task`-returning methods missing the `Async` suffix, and one reach into the quarantined module. The app builds and runs; the whole thing reads in about ten minutes. It is shaped like the systems the tool is built for, which are long-lived, business-critical, and too important to rewrite on a whim.

## The spec

The architecture is seven statements of ordinary C# in [arch/Meridian.Architecture/MeridianArchitectureSpec.cs](arch/Meridian.Architecture/MeridianArchitectureSpec.cs). Five are written there; two are taken from `DotNetGuidance`, a shared rule pack that is an ordinary class library the spec project references. The pack ships in this repository rather than as a package to install. Each statement carries a posture, a reason, and a fix. (The quarantined scope desugars into two checked rules, so `check` reports eight.)

| Rule | Posture | What it says |
|---|---|---|
| `layering/domain-independent` | Enforce | Domain must not reference Web |
| `naming/controllers` | Enforce | controllers are named `*Controller` |
| `data-access/no-inline-sql` | Migrate | controllers must not open `SqlConnection` |
| `time/inject-clock` | Migrate | Web must not read `DateTime.Now` / `UtcNow` |
| `naming/async-suffix` | Migrate | authored `Task`/`ValueTask`-returning methods end in `Async` (from the pack) |
| `di/no-buildserviceprovider` | Enforce | no `BuildServiceProvider` while configuring (from the pack) |
| `clearance/engine` | Quarantine | reach the module only via `IClearanceGateway` |

Three rules are already true, so they are law (`Enforce`). Three describe debt with a target, so they ratchet (`Migrate`). Their current violations are grandfathered, and anything new is red. One walls off a module with no target state (`Quarantine`).

The two pack rules show the two halves of that split from one source. Both are canonical .NET guidance; the posture is Meridian's call, and the calls sit beside the local rules in the same `Define`:

```csharp
DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web),
    PackPosture.Migrate("Repository and controller methods return Task without the Async suffix."),
    "Rename the method to end in Async and update its callers; see the interface and its implementation together.");

DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);
```

The same `naming/async-suffix` is `Enforce` in the [Interchange example](../Meridian.Interchange/), where the codebase already keeps it. A pack ships the rule and its reason; a codebase decides whether it is law yet.

## What the agent reads

`sliderule render` writes this block into [AGENTS.md](AGENTS.md) from the spec. CI re-renders on every push and fails on any diff, so the context an agent reads is provably the spec the build enforces.

```markdown
### Rules
- `layering/domain-independent` — The Domain layer must not reference the Web layer. Domain holds the booking and rate model the rest of the system depends on; it must not reach up into the web tier.
- `naming/controllers` — Types derived from `ControllerBase` must be named `*Controller`. Request handlers are found by their `*Controller` name — by routing and by agents reading the code; keep the convention total.
- `di/no-buildserviceprovider` — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`. Calling BuildServiceProvider while configuring services builds a second container with its own singletons, a duplicate-instance trap. See <https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines>.

### Migrations
- `data-access/no-inline-sql` — Some existing code here still follows the OLD pattern: Controllers open SqlConnection and run inline SQL directly. That is grandfathered debt, not house style. New code must follow: Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`. Data access behind a repository can be tested and swapped; SQL in the request path cannot. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
- `time/inject-clock` — Some existing code here still follows the OLD pattern: Code reads the ambient clock directly. That is grandfathered debt, not house style. New code must follow: Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`. Cutoffs, demurrage, and ETA stamps read from the wall clock cannot be tested at a fixed instant; an injected IClock makes the moment an input. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.
- `naming/async-suffix` — Some existing code here still follows the OLD pattern: Repository and controller methods return Task without the Async suffix. That is grandfathered debt, not house style. New code must follow: Methods of authored types in the Domain or Web layers returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`. Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited. See <https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap>. If you are already editing a grandfathered site and the migration is small, migrate it; otherwise do not grow the debt.

### Quarantined scopes
- `clearance/engine` — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`. The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it. Sanctioned surface: `IClearanceGateway`, `ClearanceGateway`.
```

## Three ways an agent goes wrong here

Each of the failure modes here has a posture that answers it. The `sliderule check` output below is real, cut to the rule that went red, and the first block also keeps the summary line.

### The statistical prior

Six of the eight controllers open a `SqlConnection`. An agent reading them infers that direct SQL is how Meridian does data access, and writes its next controller the same way. The rendered Migration text counters that prior in the agent's context ("that is grandfathered debt, not house style. New code must follow..."), and the ratchet enforces it.

Add an inline-SQL method to `BookingsController`, one of the two migrated controllers, and `check` goes red on the new site while the twelve grandfathered ones stay quiet:

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

The message carries the rule ID, the reason, the fix, and the exact `file:line`. Revert the method and `check` is green again. New code in the old pattern is red; the existing debt is not.

### The helpful refactor

`ContainerNumberValidator` is public, so an agent tidying the code can call it directly and delete an "unnecessary" hop through `IClearanceGateway`. The quarantined scope's containment rule stops that, because the only sanctioned way into `Meridian.Clearance` is the gateway. Reach past it and the reference is red, with the fix naming the surface to use:

```text
FAIL clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  because: The check-digit table implements a published external standard with no cleaner target shape; contain it behind the gateway rather than change it.
  fix: use `IClearanceGateway`
  src/Meridian.Web/Controllers/BookingsController.cs:75 — Meridian.Web.Controllers.BookingsController references Meridian.Clearance.ContainerNumberValidator
  grandfathered: 1 (baselined; run 'sliderule status' for burndown)
```

One reach into the module already exists (`CustomsController` news up the validator), and it is grandfathered on the record. Every new one is red. The dragons stay contained.

### Misreading structural weirdness

The ISO 6346 check-digit table looks broken. It assigns A=10, B=12, C=13, and skips a value every so often, so K=21 is followed by L=23. An agent that "corrects" the gap to make the table contiguous breaks the check digit for every real container number in the system. This is the weirdness a `Quarantine` scope can only document, because agents still have to call into the code. `render` drops this card into the module's own directory, next to the file:

```markdown
## Quarantined scope `clearance/engine`

This directory holds the quarantined `clearance/engine` scope: types in `Meridian.Clearance.*`. Here be dragons — do not spread references into it.

Dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are structural — linearizing the table breaks every real container number. Call in only through IClearanceGateway.
```

The gaps are the standard. The letter values skip every multiple of 11 (11, 22, 33), which is why the table is not contiguous. The prose says what the code does, which part is structural, and how to interact with it.

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

Move a controller onto a repository and its grandfathered count drops. When a Migrate rule reaches zero, the tool suggests promoting it to `Enforce`.

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

`check` exits 0 here, because every current violation is on the baseline. `sliderule status` prints the burndown above, and `sliderule render` regenerates the `AGENTS.md` block and the [ARCHITECTURE.md](ARCHITECTURE.md) drawings from the spec. Introduce one of the violations from this page and `check` exits 1 with the message shown.

## In the agent's loop

The rendered block steers an agent's first attempt. A hook makes the same rules catch a wrong one before anybody else sees it. Wired as a Claude Code `Stop` hook, `sliderule hook` runs the check when the agent says it is done. A new violation returns on stderr as the reason the turn is not over. The agent therefore reads the rule and corrects the code before handing the work back. Add the inline-SQL method from [the statistical prior](#the-statistical-prior) to `BookingsController` and the hook refuses the stop with exit 2, feeding back the failing rule (its passing siblings in the report omitted here):

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

The hook's check adds `--diff-base HEAD`, which runs the quarantined scope's tripwire as well, so its summary counts no skip. The exemplar line points at `BookingRepository`, and `IBookingRepository` already has the query, so the correction is a two-line call through the injected repository, captured green in the same loop. The paste-in `.claude/settings.json` snippet and the full task-to-self-correction walk are in [`hooks/`](hooks/). The repository keeps `.claude/` local, so the hook config ships as a snippet you paste. The walk is scripted with real captured output in the [storyboard](hooks/storyboard.md).

## From here

The clean-architecture example, [`Meridian.Quoting`](../Meridian.Quoting/), is a greenfield subsystem where every rule is `Enforce` from day one and nothing sits on a baseline. The clock ratchet is law there as `time/injected-clock`, the same ban on `DateTime.Now` and `DateTime.UtcNow`. The inline-SQL ratchet's target is the shape Quoting was built in, where the use cases reach data only through repository interfaces. The async-suffix ratchet has no counterpart there.

[ADOPTING.md](ADOPTING.md) walks the other direction and shows how this spec was derived from Meridian's code, one real command at a time. It is the day-one flow for a codebase that already exists.
