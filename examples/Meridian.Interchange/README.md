# Microsoft guidance, enforced and cited: Meridian.Interchange

Meridian.Interchange is Meridian's outbound interchange worker, governed by 12 rules, each citing the learn.microsoft.com page it enforces. Every rule pairs a one-sentence reason with a `Citation` naming the Microsoft page behind it. The rendered agent context therefore quotes the guidance with its provenance, and a reviewer can follow the link to the source.

The other examples state house rules. This one states Microsoft's rules and shows its work. An agent dropped into the worker reads the rules before it writes, and each rule cites the canonical page. "Reuse HttpClient" arrives as the documented .NET guideline, with the page behind it. The rule the agent reads is the rule the build enforces, and both include the citation. This page is a cookbook. Each recipe gives the guidance sentence, the spec line that encodes it, and the captured `check` output when the code breaks it. That output is cut to the rules that went red.

The worker belongs to the freight-forwarding fiction shared with [`../Meridian/`](../Meridian/). It is long-lived and business-critical, the transmit path other teams depend on.

## The worker

Meridian.Interchange drains an outbox and transmits booking confirmations, status updates, and customs filings to trading partners (carriers, customs systems, terminals) over HTTP. It is one `Microsoft.NET.Sdk.Worker` project whose modules are namespace subtrees, the shape [`Meridian.Operations`](../Meridian.Operations/) uses.

The parts the rules act on:

- `Partners` contains the `IPartnerClient` family. `CarrierClient`, `CustomsFilingClient` and `TerminalClient` are typed clients whose `HttpClient` comes from `IHttpClientFactory`. `LegacyManifestClient` is the one corner that still blocks.
- `Outbox` contains `OutboxMessage` and the scoped `IOutboxStore`. `Processing` contains the scoped `OutboxProcessor`, which reads its settings from an `IOptionsSnapshot<InterchangeOptions>`, the options type for a scoped consumer.
- `Dispatch` contains `OutboxDispatcher`, a `BackgroundService`. A hosted service is a singleton, so it captures no scoped service itself and delegates each poll to a seam in the composition root.
- `Host` is that composition root. `InterchangeServiceCollectionExtensions` wires every service, and `ScopedDispatchRunner` opens a DI scope per poll and resolves the scoped processor inside it. Construction and service location happen in this namespace alone, which is why three of the rules except it.

The drawings in [ARCHITECTURE.md](ARCHITECTURE.md) are the thinnest pair in the example set. The law fence has arrows for reference and exposure rules between named places. All but one of the rules here use another verb, or a subject the drawing cannot place, so they are listed under the fence.

The crux of the design is the dispatcher, a singleton that must run scoped work. The sanctioned way is an `IServiceScopeFactory` scope, and the resolve inside it (`scope.ServiceProvider.GetRequiredService<IOutboxProcessor>()`) is the service-locator call `di/no-service-locator` bans. Confining both to `Meridian.Interchange.Host.*` keeps the ban total everywhere else while the one sanctioned resolve site is excepted.

## The spec

The spec is one class of ordinary C#, [arch/Meridian.Interchange.Architecture/InterchangeArchitectureSpec.cs](arch/Meridian.Interchange.Architecture/InterchangeArchitectureSpec.cs). Each rule has a posture, a reason, the page it cites and a fix.

| Rule | Posture | Requirement |
|---|---|---|
| `di/construct-via-container` | Enforce | construct partner clients only in the root |
| `http/reuse-httpclient` | Enforce | do not `new HttpClient()` |
| `di/no-service-locator` | Enforce | do not resolve from the provider |
| `di/no-buildserviceprovider` | Enforce | do not call `BuildServiceProvider` |
| `async/no-sync-over-async` | Migrate | do not block on a `Task` or a `ValueTask` |
| `di/hosted-services-scope-their-work` | Enforce | a hosted service captures no scoped service |
| `di/no-captive-dependencies` | Enforce | a singleton injects no scoped or transient service |
| `naming/async-suffix` | Enforce | authored `Task`/`ValueTask`-returning methods end in `Async` |
| `exceptions/no-general-catch` | Enforce | swallow base `Exception` only in a top-level handler |
| `async/accept-cancellation` | Enforce | authored `Task`/`ValueTask`-returning methods accept a `CancellationToken` |
| `persistence/no-mapping-attributes` | Enforce | no `[Table]`/`[ComplexType]` on a persisted type |
| `contracts/no-entity-exposure` | Enforce | do not expose `OutboxMessage` outside the `Outbox` cone |

The code already keeps 11 of them, so they are law (`Enforce`). The remaining rule, `async/no-sync-over-async`, describes the blocking corner as debt with a target, so it ratchets (`Migrate`). The blocking that exists is grandfathered, and new blocking is red.

Of the 12 rules, nine are general .NET guidance, so they come from `DotNetGuidance`, a shared rule pack. The pack is an ordinary class library the spec project references, and it ships inside this repository. On your own solution it is a class library you write. The reason to reuse an `HttpClient` is the same in every codebase, and so is the page behind it. The pack therefore provides each rule's `Because` and `Citation`. This spec picks the posture and the selections. Where remediation needs local words, it overrides the `Fix`. Where one of Meridian's types already shows the fix, it names that type as the `Exemplar`. The three rules whose constraint names one of Meridian's own types (`IPartnerClient`, `IOutboxStore` and `OutboxMessage`) are written out in full. Both forms appear in the same `Define`:

```csharp
DotNetGuidance.ReuseHttpClient(arch, arch.Types, host, PackPosture.Enforce,
    "Take a typed or named client from IHttpClientFactory; the exemplar receives its HttpClient through its constructor.",
    arch.Types.Named("CarrierClient"));

arch.Rule("di/hosted-services-scope-their-work")
    .Enforce(arch.Types.DerivedFrom<BackgroundService>().MustNotReference(typeof(IOptionsSnapshot<>), typeof(IOutboxStore)))
    .Because("A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope; resolve per work item from an IServiceScopeFactory scope.")
    .Citation("https://learn.microsoft.com/dotnet/core/extensions/scoped-service")
    .Fix("Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it.")
    .Exemplar(arch.Types.Named("OutboxDispatcher", "ScopedDispatchRunner"));
```

They reify to the same kind of rule, so the rendered context, the violation message and the citation read the same either way. The [Rule packs](../../WRITING-SPECS.md#rule-packs) section of Writing specs explains the split between a pack and the spec that takes it.

`sliderule render` writes those into the managed block in [AGENTS.md](AGENTS.md), citation and all. CI runs `render --check` over this example and fails on a stale block, so the context an agent reads is the spec the build enforces:

```markdown
- `http/reuse-httpclient` — Types, except types in `Meridian.Interchange.Host.*`, must not construct `HttpClient`. A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers. See <https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines>.
- `di/hosted-services-scope-their-work` — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`. A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope; resolve per work item from an IServiceScopeFactory scope. See <https://learn.microsoft.com/dotnet/core/extensions/scoped-service>.
```

## The burndown

The one `Migrate` rule governs every type, and all of its debt is in `LegacyManifestClient`. The adapter wraps Meridian's own legacy manifest gateway, whose SDK exposes only synchronous entry points. It therefore blocks at three points: serializing the manifest, taking the single-flight gate, and posting it. Those three lines touch three distinct blocking members (`TaskAwaiter<T>.GetResult`, `Task.Wait` and `Task<T>.Result`), so the generated baseline contains three entries. The entries are grandfathered, so `check` exits 0 and `sliderule status` prints what is left to work off:

```text
pass di/construct-via-container
pass http/reuse-httpclient
pass di/no-service-locator
pass di/no-buildserviceprovider
pass async/no-sync-over-async (migrate) — 3 grandfathered remaining, 0 new, 0 fixed awaiting acceptance
pass di/hosted-services-scope-their-work
pass di/no-captive-dependencies
pass naming/async-suffix
pass exceptions/no-general-catch
pass async/accept-cancellation
pass persistence/no-mapping-attributes
pass contracts/no-entity-exposure
Checked 12 rules: 12 passed, 0 failed, 0 skipped. Burndown: 3 grandfathered remaining, 0 fixed awaiting acceptance.
```

The three blocking calls are recorded as debt, and new blocking anywhere in the worker is red on sight. When the legacy SDK exposes an async surface and the corner is rewritten, the entries become fixed awaiting acceptance. After `baseline accept-reductions` empties the baseline, `status` suggests promoting the rule to `Enforce`. The [Working a ratchet down](../../OPERATING.md#working-a-ratchet-down) section of Operating covers the commands that shrink a baseline and the promotion.

## The silent win

`http/reuse-httpclient` is green over a codebase that never breaks it. No type constructs an `HttpClient`. The three typed clients receive theirs from `IHttpClientFactory`, and the legacy adapter takes a named client from the factory in the composition root. The rule's target is absent, so the rule passes by guarding a state that is already clean.

Add one line to `CarrierClient.SendAsync`, the shortcut a socket-exhaustion bug is made of:

```csharp
using var probe = new HttpClient();
```

`dotnet build` still exits 0, because `new HttpClient()` is valid C#, and it prints `SR2001`, the broken-rule warning, at the new line. Every example build attaches the [build analyzer](../../src/SlideRule.Analyzers/README.md), which decides this rule from one project's compilation. This repository's CI promotes the analyzer's warnings to errors, so there the same build fails. `check` exits 1:

```text
FAIL http/reuse-httpclient — Types, except types in `Meridian.Interchange.Host.*`, must not construct `HttpClient`.
  because: A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers.
  citation: https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines
  fix: Take a typed or named client from IHttpClientFactory; the exemplar receives its HttpClient through its constructor.
  exemplar: CarrierClient (src/Meridian.Interchange/Partners/CarrierClient.cs:9)
  src/Meridian.Interchange/Partners/CarrierClient.cs:15 — Meridian.Interchange.Partners.CarrierClient constructs System.Net.Http.HttpClient
```

The message has six components: the rule ID, the `because`, the `citation` naming the Microsoft page, the `fix`, the `exemplar`, and the exact `file:line`. The `exemplar` line points at the file that declares `CarrierClient`, which already receives its client through its constructor. `check` warns if that type is ever renamed away. An agent that reads this failure gets the guideline and its source together, then corrects the code. Revert the line and `check` is green again.

## The captive dependency

`di/no-captive-dependencies` reads two facts the other rules don't: which types are registered with which lifetime, and which types inject which. It states the general captive-dependency antipattern, that a singleton must not inject a scoped or transient service. `di/hosted-services-scope-their-work` reaches the same case only where the type hierarchy reveals it.

It is a pack rule, so the spec names only which singletons it governs and which type already shows the fix:

```csharp
DotNetGuidance.NoCaptiveDependencies(arch, arch.Registered(Lifetime.Singleton), PackPosture.Enforce,
    exemplar: arch.Types.Named("ScopedDispatchRunner"));
```

The worker is green under it, because both singletons take only singleton-safe dependencies. `ScopedDispatchRunner` injects `IServiceScopeFactory`, and `OutboxDispatcher` injects that runner, an `IOptions<InterchangeOptions>` and an `ILogger<OutboxDispatcher>`. `IOptions<T>` is a singleton, unlike the scoped `IOptionsSnapshot<T>` the processor uses, so the scoped work stays behind the scope seam.

`OutboxDispatcher` reaches the rule through `AddHostedService<OutboxDispatcher>`. A hosted service registers as a singleton, so the dispatcher is a singleton-registered type even though no `AddSingleton` names it. Take the scoped `IOutboxStore` in the dispatcher and read the outbox directly, in place of the singleton runner, and the capture is on:

```csharp
internal sealed class OutboxDispatcher(IOutboxStore store, IOptions<InterchangeOptions> options, ILogger<OutboxDispatcher> logger) : BackgroundService
```

The build warns `SR2001` twice for `di/hosted-services-scope-their-work`, once per site, and exits 0. It stays silent on `di/no-captive-dependencies`, because a registration rule needs the whole solution and is left to `check`. `check` exits 1, with two failures on that one edit:

```text
FAIL di/hosted-services-scope-their-work — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`.
  because: A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope; resolve per work item from an IServiceScopeFactory scope.
  citation: https://learn.microsoft.com/dotnet/core/extensions/scoped-service
  fix: Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it.
  exemplar: OutboxDispatcher (src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:15), ScopedDispatchRunner (src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:10)
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:15 — Meridian.Interchange.Dispatch.OutboxDispatcher references Meridian.Interchange.Outbox.IOutboxStore
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:25 — Meridian.Interchange.Dispatch.OutboxDispatcher references Meridian.Interchange.Outbox.IOutboxStore
FAIL di/no-captive-dependencies — Singleton-registered types must not inject scoped-registered types or transient-registered types.
  because: A singleton is created once and holds every dependency it injects for the whole process, so a scoped or transient service injected into it is captured past its lifetime and shared across all callers.
  citation: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines
  fix: Resolve the scoped or transient service per unit of work inside an IServiceScopeFactory scope; take only singleton-safe dependencies in the constructor.
  exemplar: ScopedDispatchRunner (src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:10)
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:15 — Meridian.Interchange.Dispatch.OutboxDispatcher injects Meridian.Interchange.Outbox.IOutboxStore
```

`di/no-captive-dependencies` reads the capture from the registrations, where `IOutboxStore` is registered scoped. `di/hosted-services-scope-their-work` reads the same edit from the hierarchy, because `IOutboxStore` is its named target and the dispatcher derives from `BackgroundService`. The two overlap on this dependency, and neither replaces the other. The registration rule also catches a captured scoped `IOutboxProcessor`, or a transient partner client, that the hierarchy rule never names. The hierarchy rule also catches `IOptionsSnapshot<InterchangeOptions>`, which is framework-registered and never spelled in a source-level registration, so the registration rule cannot see it. Swap the parameter to that snapshot type and only the hierarchy rule fires, as [Introduce a violation](#introduce-a-violation) shows. Revert the parameter and `check` is exit 0 again.

The registrations this rule reads are the ones the source spells: `AddSingleton`, `AddScoped`, `AddTransient`, their `TryAdd` twins, `AddHostedService`, `AddDbContext`, `AddDbContextPool`, and `AddHttpClient<TClient>`. A keyed-service or raw `ServiceDescriptor` registration, and one made by assembly scanning, a factory body, or a framework default, is outside that boundary. The rendered glossary names the boundary, so an agent reading the context sees the edge of what the rule knows.

## The scoped catch

`exceptions/no-general-catch` is green over a worker that catches narrowly. `OutboxProcessor` retries a failed send inside `catch (HttpRequestException)` and lets everything else surface. The rule bans swallowing base `Exception` across `Meridian.Interchange.*`, and swallowing means catching base `Exception` with no `when` filter and no rethrow. The rule excepts the types that derive from `BackgroundService`, here the dispatcher alone.

That one exception is the essential part. `OutboxDispatcher.ExecuteAsync` wraps each poll in `catch (Exception)` and logs, because an unhandled exception out of a `BackgroundService` stops the host on .NET 6 and later. The poll loop is the worker's top-level handler, the one site the guidance sanctions for a catch-all. CA1031 (Do not catch general exception types) would flag the dispatcher's site; the spec sanctions it by scope, and the ban applies everywhere else.

Add the blanket catch one layer down, in `OutboxProcessor`, where a failed delivery should surface. Wrap the per-message loop body in a catch-all:

```csharp
catch (Exception)
{
    // Keep draining the batch even if one message blows up.
}
```

The build warns `SR2001` at the new catch and exits 0, and `check` exits 1:

```text
FAIL exceptions/no-general-catch — Types in `Meridian.Interchange.*`, except types derived from `BackgroundService`, must not swallow `Exception`.
  because: Catching base Exception outside a top-level handler swallows the faults you meant to see; scope the catch-all to that handler and let other code catch only the specific types it can handle.
  citation: https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types
  fix: Catch the specific exception you can handle; the only sanctioned catch-all is the dispatcher's poll loop, where OutboxDispatcher logs and continues to the next poll.
  src/Meridian.Interchange/Processing/OutboxProcessor.cs:32 — Meridian.Interchange.Processing.OutboxProcessor catches System.Exception
```

The match is exact, so `MustNotSwallow(typeof(Exception))` flags a catch of base `Exception`, and a `catch (TimeoutException)` in the same method stays invisible to the rule. That narrow catch is the state the guidance wants. Add a `when` filter to the blanket catch, or end its block in `throw`, and the site goes green without narrowing the type. Either leaves the failure visible, so neither is a swallow. Revert the catch and `check` is exit 0 again.

## The flowed token

`async/accept-cancellation` is green over a worker that passes a token the whole way down. Every `Task`-returning method on an authored type in `Meridian.Interchange.*` accepts a `CancellationToken`. `OutboxDispatcher.ExecuteAsync` receives the host's `stoppingToken`, and `ScopedDispatchRunner` and `OutboxProcessor` pass it to the calls they make. The partner clients and the outbox store take it at the leaf, and the rule requires the parameter on every one of them.

```csharp
DotNetGuidance.AcceptCancellation(arch, arch.Types.InNamespace("Meridian.Interchange.*"), PackPosture.Enforce,
    "Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain.",
    arch.Types.Named("ScopedDispatchRunner", "OutboxProcessor"));
```

The pack applies the method projection, so the spec passes the types and the rule narrows to their `Task`-returning methods.

The rule reads the declared parameter list, and the match is exact. A `CancellationToken?` or a `params CancellationToken[]` parameter is a different declared type and does not satisfy it. Whether the body forwards the token to the calls it makes is a separate question of flow, which this presence rule does not answer. What it guarantees is that the token is on the surface for a caller to pass.

Drop the token from `ScopedDispatchRunner.RunPendingAsync`, the seam the dispatcher's `stoppingToken` flows through. The parameter is structural, so removing it takes three touches to compile. The signature loses the parameter, the interior `ProcessPendingAsync` call falls back to `CancellationToken.None`, and the `OutboxDispatcher` call site drops its argument.

```csharp
// ScopedDispatchRunner.cs
public async Task RunPendingAsync()
{
    using IServiceScope scope = scopeFactory.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
    await processor.ProcessPendingAsync(CancellationToken.None);
}

// OutboxDispatcher.cs, in the poll loop
await runner.RunPendingAsync();
```

The build warns `SR2001` at the method and exits 0, and `check` exits 1:

```text
FAIL async/accept-cancellation — Methods of authored types in `Meridian.Interchange.*` returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must accept a parameter of type `CancellationToken`.
  because: Accepting a CancellationToken lets a caller stop in-flight async work and flow that request on to the calls it makes, so a Task- or ValueTask-returning method without one cannot take part in cooperative cancellation.
  citation: https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Add a CancellationToken parameter and flow OutboxDispatcher's stoppingToken through the call chain.
  exemplar: ScopedDispatchRunner (src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:10), OutboxProcessor (src/Meridian.Interchange/Processing/OutboxProcessor.cs:14)
  src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:13 — Meridian.Interchange.Host.ScopedDispatchRunner.RunPendingAsync()
```

The violation is keyed to the method at its declaration site, `ScopedDispatchRunner.cs:13`, and it is the only red. `RunPendingAsync` still ends in `Async`, so `naming/async-suffix` stays green. Revert the three touches and `check` is exit 0 again.

## The persistence-ignorant outbox

`persistence/no-mapping-attributes` is green over a worker whose persisted type stays a plain record. `OutboxMessage` is a POCO (three properties, no base class, no mapping attribute). The outbox that contains it is an in-memory list a real deployment would back with a table. The rule encodes persistence ignorance. Microsoft defines it as "types that need to be persisted, but whose code is unaffected by the choice of persistence technology". The same page lists "persistence-specific required attributes" among the couplings that violate it. The rule bans the two type-level mapping attributes, `[Table]` and `[ComplexType]`, across `Meridian.Interchange.*`.

It is another pack rule, and one where the pack's own `Fix` covers the remedy, so the spec takes it as it comes:

```csharp
DotNetGuidance.NoMappingAttributes(arch, arch.Types.InNamespace("Meridian.Interchange.*"), PackPosture.Enforce);
```

The rule reads the type's own declared attributes and names only `[Table]` and `[ComplexType]`. The two persistence markers live in the `Schema` sub-namespace of `DataAnnotations` and tag the type itself as a mapped entity. `DataAnnotations` also contains the validation attributes a domain may want, so the rule does not ban it wholesale. A mapping attribute on a property (a `[Column]` or `[Key]`) is below the type surface this rule reads, so this rule does not see it. A rule over the type's properties can ban it. Keeping that mapping out of the domain type is the same discipline the `Fix` names, fluent configuration in the persistence layer.

Attribute the record as a mapped entity, the coupling the guideline warns about, and give it a table name:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

[Table("InterchangeOutbox")]
public sealed record OutboxMessage(string MessageId, string Channel, string Payload);
```

The build warns `SR2001` at the record and exits 0, and `check` exits 1:

```text
FAIL persistence/no-mapping-attributes — Types in `Meridian.Interchange.*` must not be attributed with `[Table]` or `[ComplexType]`.
  because: A persistence-specific attribute such as [Table] or [ComplexType] couples a persisted type to one data-access technology, so the same model can no longer be stored another way or moved to a new store; keep it ignorant of how it is persisted.
  citation: https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles
  fix: Keep the persisted type a POCO and map it from the persistence layer with fluent configuration (EF Core's IEntityTypeConfiguration, a Dapper column list) instead of attributes on the type.
  src/Meridian.Interchange/Outbox/OutboxMessage.cs:10 — Meridian.Interchange.Outbox.OutboxMessage
```

The violation is keyed to the record at its declaration site, `OutboxMessage.cs:10`, and it is the only red. The attribute mints an ordinary reference to `TableAttribute`, and no rule bans that. Revert the attribute and `check` is exit 0 again.

## The partner envelope

`contracts/no-entity-exposure` is green over a worker whose partner contract takes a DTO. `IPartnerClient.SendAsync` takes a `PartnerEnvelope`, a transport-facing record of a message's id and its serialized payload, so the public partner contract is the wire shape. `OutboxMessage`, the persisted record, surfaces only inside the `Outbox` module that declares it, and the partner clients consume only the envelope. The rule bans exposing `OutboxMessage` on a public signature across `Meridian.Interchange.*`, excepting the `Outbox` cone where the entity lives.

```csharp
arch.Rule("contracts/no-entity-exposure")
    .Enforce(arch.Types.InNamespace("Meridian.Interchange.*")
        .Except(arch.Namespace("Meridian.Interchange.Outbox.*"))
        .MustNotExpose(typeof(OutboxMessage)))
    .Because("Exposing a persisted entity on a public signature couples partner-facing code to the storage model, so a change to how a message is persisted reshapes the partner contract; hand partners a DTO made for the wire instead.")
    .Citation("https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads")
    .Fix("Map the message to a PartnerEnvelope at the OutboxProcessor boundary and expose that; keep OutboxMessage inside the Outbox module.");
```

The rule reads the public signature surface of a type: the return, parameter, property, field and event types of its effectively public members. It mints an exposure edge when the banned type appears there. It reads only what is effectively public, so a `public` member of an `internal` type adds no position to that surface. The three typed clients and the legacy adapter, all `internal`, therefore expose no entity, even though each takes the envelope on a `public` method. The `.Except` sanctions the entity's home in `Meridian.Interchange.Outbox`, where `IOutboxStore` returns `OutboxMessage` from `GetPendingAsync`, and the ban applies across the rest of the worker.

Exposure is a fact about public signatures, and a reference in a method body is a different fact. The two come apart in `OutboxProcessor`. It references `OutboxMessage` throughout its body, in the loop variable it drains and the parameter of its private delivery helper, and mints no exposure edge. Its single public method returns `Task` and takes a `CancellationToken`, and it maps each message to a `PartnerEnvelope` at the call to the partner.

Change `IPartnerClient.SendAsync` back to take `OutboxMessage`, the coupling the guideline warns about. It is a multi-touch edit: the interface, all four implementations and the `OutboxProcessor` call site must change together or the worker does not compile. The [flowed token](#the-flowed-token) shows the same structural shape for one method, and here it spans a whole contract.

The build warns `SR2001` at the interface and exits 0, and `check` exits 1:

```text
FAIL contracts/no-entity-exposure — Types in `Meridian.Interchange.*`, except types in `Meridian.Interchange.Outbox.*`, must not expose `OutboxMessage`.
  because: Exposing a persisted entity on a public signature couples partner-facing code to the storage model, so a change to how a message is persisted reshapes the partner contract; hand partners a DTO made for the wire instead.
  citation: https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads
  fix: Map the message to a PartnerEnvelope at the OutboxProcessor boundary and expose that; keep OutboxMessage inside the Outbox module.
  src/Meridian.Interchange/Partners/IPartnerClient.cs:15 — Meridian.Interchange.Partners.IPartnerClient exposes Meridian.Interchange.Outbox.OutboxMessage
```

The edit touches six files, and the violation is a single red. It is keyed to the interface at `IPartnerClient.cs:15`, the one public signature that now exposes the entity. The four partner clients stay invisible to the rule because they are `internal`, and `OutboxProcessor`'s references stay references. Revert the contract to `PartnerEnvelope` and `check` is exit 0 again.

## The citations

The quoted phrase in each row is drawn from the cited page, verified against the live doc.

| Rule | Microsoft guidance | Source |
|---|---|---|
| `di/construct-via-container` | "Avoid direct instantiation of dependent classes within services" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `http/reuse-httpclient` | "reuse HttpClient instances for as many HTTP requests as possible" | [HttpClient guidelines](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines) |
| `di/no-service-locator` | "Avoid using the service locator pattern" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `di/no-buildserviceprovider` | "Avoid calls to BuildServiceProvider when configuring services" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `async/no-sync-over-async` | "can result in deadlocks and blocked context threads" | [Asynchronous programming scenarios](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios) |
| `di/hosted-services-scope-their-work` | "the service is registered as a singleton" | [Scoped services in a BackgroundService](https://learn.microsoft.com/dotnet/core/extensions/scoped-service) |
| `di/no-captive-dependencies` | "A singleton can inadvertently capture scoped or transient dependencies" | [Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `naming/async-suffix` | "Asynchronous methods in TAP include the Async suffix" | [Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |
| `exceptions/no-general-catch` | "AVOID catching System.Exception or System.SystemException, except in top-level exception handlers" | [Using standard exception types](https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types) |
| `async/accept-cancellation` | "consider adding a CancellationToken parameter" | [Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |
| `persistence/no-mapping-attributes` | "Persistence-specific required attributes" | [Architectural principles](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles) |
| `contracts/no-entity-exposure` | "Use ViewModels specifically made for client apps, independent from domain model constraints" | [CQRS reads/queries](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/cqrs-microservice-reads) |

The DI guidelines page is cited by four rules. Its design, recommendations and anti-patterns sections cover construction, the service-locator anti-pattern, `BuildServiceProvider` and the captive-dependency anti-pattern. The TAP page is cited twice, once for the `Async` suffix and once for the `CancellationToken` parameter. Those are the two conventions it fixes for a method returning a `Task` or a `ValueTask`.

## Pairing with analyzers

Several rules have a neighbor in the analyzer ecosystem, and the table names the nearest one and what differs. The differences repeat. A rule here runs over the whole solution and names the cone it governs and the seam it excepts. Under `Migrate` it ratchets against a baseline, and its citation renders into the agent's context. The same check runs at the end of an agent's turn through the [agent hook](../../hooks/README.md#when-it-fires).

| Rule | Nearest analyzer | What differs |
|---|---|---|
| `http/reuse-httpclient` | no analyzer bans `new HttpClient()` | enforced everywhere outside the composition root |
| `di/no-buildserviceprovider` | ASP0000, in the ASP.NET Core `Startup` shape only | solution-wide, whatever the shape |
| `async/no-sync-over-async` | VSTHRD002, VSTHRD103, MA0045, AsyncFixer02, blanket-wide | ratchets the legacy corner; new blocking is red |
| `di/hosted-services-scope-their-work` | `ValidateOnBuild`/`ValidateScopes`, at runtime when the host builds | caught statically, before the app runs |
| `di/no-captive-dependencies` | two community analyzers, one compilation each | registration and constructor can be in different projects |
| `naming/async-suffix` | VSTHRD200, blanket-wide | scoped to the cone and to authored types |
| `exceptions/no-general-catch` | CA1031, blanket-wide, so widely turned off | excepts the top-level handler; filters and rethrows pass |
| `async/accept-cancellation` | CA1068 and MA0032/MA0040/MA0079, once a token exists | requires the parameter itself |
| `persistence/no-mapping-attributes` | ArchUnitNET and NetArchTest, in hand-written test code | declarative, whole-solution, cited in the agent's context |
| `contracts/no-entity-exposure` | CA1002, a `List<T>` on a public member only | any named type crossing the boundary |

The two community analyzers for `di/no-captive-dependencies` are Excubo.Analyzers.DependencyInjectionValidation and georgepwall1991/DependencyInjection.Lifetime.Analyzers. CA1068 (CancellationToken parameters must come last) governs the position of a token already present. Meziantou's MA0032, MA0040 and MA0079 flag a call site that passes no token to a method that can take one.

`naming/async-suffix` narrows to authored types, so a type a generator emits whole, or marks with `[GeneratedCode]`, is out of reach and needs no suppression. A method a generator adds to a partial class you wrote is still checked. No analyzer polices a domain entity reaching a public signature or a mapping attribute on a domain type. Persistence ignorance is a documented principle with no build-time enforcer of its own.

`di/no-service-locator` has no row, because what differs there is a caveat about the rule itself. A ban on an interface member catches a call through that interface. A call through a concrete container type that re-declares the member is a different member, so such a type has to be named too. Every resolve in this worker goes through `IServiceProvider` or the `ServiceProviderServiceExtensions` methods, so the ban is complete here.

## Introduce a violation

Each rule goes red on a small edit and back to green on revert, and six of the edits are above. The [silent win](#the-silent-win) and the [captive dependency](#the-captive-dependency) are the `new HttpClient()` and the captured store. The [scoped catch](#the-scoped-catch) and the [flowed token](#the-flowed-token) are the blanket `catch` and the dropped parameter. The [persistence-ignorant outbox](#the-persistence-ignorant-outbox) and the [partner envelope](#the-partner-envelope) are the mapping attribute and the entity on the contract. The two edits below add `naming/async-suffix` and a second route into `di/hosted-services-scope-their-work`. The four rules without a worked edit on this page are `di/construct-via-container`, `di/no-service-locator`, `di/no-buildserviceprovider` and `async/no-sync-over-async`.

Change `OutboxDispatcher`'s constructor parameter from `IOptions<InterchangeOptions>` to `IOptionsSnapshot<InterchangeOptions>`. It compiles and would even resolve. But `IOptionsSnapshot` is scoped, as [the captive dependency](#the-captive-dependency) explains, so the singleton dispatcher would capture it for the whole process. The build warns `SR2001` at the parameter, and the rule reads the capture from the type hierarchy alone:

```text
FAIL di/hosted-services-scope-their-work — Types derived from `BackgroundService` must not reference `IOptionsSnapshot<TOptions>` or `IOutboxStore`.
  because: A BackgroundService is a singleton; a captured scoped IOptionsSnapshot or scoped store outlives its scope; resolve per work item from an IServiceScopeFactory scope.
  citation: https://learn.microsoft.com/dotnet/core/extensions/scoped-service
  fix: Inject IServiceScopeFactory, create a scope per iteration, resolve scoped services inside it.
  exemplar: OutboxDispatcher (src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:14), ScopedDispatchRunner (src/Meridian.Interchange/Host/ScopedDispatchRunner.cs:10)
  src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs:14 — Meridian.Interchange.Dispatch.OutboxDispatcher references Microsoft.Extensions.Options.IOptionsSnapshot<TOptions>
```

Rename `IOutboxProcessor.ProcessPendingAsync` to `ProcessPending`, with its implementation and the one call site in `ScopedDispatchRunner`, so it compiles. Both the interface and the class now declare a `Task`-returning method without the suffix. The build warns `SR2001` twice, and the rule fires on both:

```text
FAIL naming/async-suffix — Methods of authored types in `Meridian.Interchange.*` returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` must be named `*Async`.
  because: Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited.
  citation: https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Rename the method to end in Async.
  src/Meridian.Interchange/Processing/IOutboxProcessor.cs:7 — Meridian.Interchange.Processing.IOutboxProcessor.ProcessPending()
  src/Meridian.Interchange/Processing/OutboxProcessor.cs:19 — Meridian.Interchange.Processing.OutboxProcessor.ProcessPending()
```

Revert either edit and `check` is back to exit 0.

## Run it yourself

SlideRule ships as a .NET global tool (solution-loading commands need a .NET 10 SDK):

```bash
dotnet tool install -g SlideRule.Cli
```

From a checkout of this repository, build the worker and check it:

```bash
dotnet build examples/Meridian.Interchange/Meridian.Interchange.slnx
sliderule check examples/Meridian.Interchange/Meridian.Interchange.slnx
```

`check` exits 0 here and prints `Checked 12 rules: 12 passed, 0 failed, 0 skipped (0 violations, 0 warnings)`. Without the global tool, run the CLI from source with `dotnet run --project src/SlideRule.Cli -- check examples/Meridian.Interchange/Meridian.Interchange.slnx`. `sliderule status` prints the burndown. `sliderule render` rewrites the `AGENTS.md` block and refreshes `sliderule.model.json`, the file the build analyzer reads. The drawings in [ARCHITECTURE.md](ARCHITECTURE.md) need `--diagram`, and that page prints the command. `sliderule explain <rule-id>` expands any rule, as does the `arch_explain` MCP tool. Introduce the `new HttpClient()` edit above and `check` exits 1 with the block shown.

## From here

The [`../Meridian/`](../Meridian/) example shows three postures on one migrating codebase, and [`../Meridian.Operations/`](../Meridian.Operations/) draws module boundaries and renders a card into every module directory. Meridian.Interchange keeps the same fluent vocabulary and points it at one question per rule: which Microsoft page requires this. The cited page's address is printed with the rule in the `check` report and in the agent's context.
