# SlideRule.Packs.DotNet

`SlideRule.Packs.DotNet` is a rule pack: nine canonical .NET rules as an ordinary class
library, so a spec that wants them does not write them again. You reference the project and call
the methods you want, with no plugin host, manifest or discovery step in between.

This project is not published to NuGet. It ships inside this repository as a working example, and
three of the repository's own specs take rules from it. It targets `net10.0`, so a spec project that
references it targets `net10.0` or later. To use it elsewhere, copy its source and package
references into a class library that references the `SlideRule` package. You can also write a pack
of your own the same way.

## Taking a rule

```csharp
using SlideRule;
using SlideRule.Packs.DotNet;

public sealed class MyArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Selection host = arch.Namespace("MyApp.Host.*");

        DotNetGuidance.ReuseHttpClient(arch, arch.Types, host, PackPosture.Enforce);

        DotNetGuidance.AsyncSuffix(arch, arch.Types.InNamespace("MyApp.*"),
            PackPosture.Migrate("Repository methods return Task without the Async suffix."));
    }
}
```

In the spec project, add a `ProjectReference` to this project alongside the one to `SlideRule`, and
set `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`. The checker loads a spec
through a resolver rooted at the spec's own DLL. Staging the pack's package dependencies next to that
DLL makes the load independent of what the checking machine has restored. Without the setting the
spec still builds, and a missing dependency is reported later, as an error when `check` loads the spec.

A `typeof()` anchor on a .NET shared framework type is the one case a build setting cannot fix. An
ASP.NET Core or WPF type that arrives through `<FrameworkReference>` is never staged into a class
library's output. Anchor such a type by its full name, as in
`.DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")`, which renders the same as the `typeof()`
form.

## The nine rules

| Method | Rule ID | What it requires |
|---|---|---|
| `ReuseHttpClient` | `http/reuse-httpclient` | only the composition root constructs an `HttpClient` |
| `NoServiceLocator` | `di/no-service-locator` | only the resolve seam resolves from an `IServiceProvider` |
| `NoBuildServiceProvider` | `di/no-buildserviceprovider` | no second container is built while configuring services |
| `NoSyncOverAsync` | `async/no-sync-over-async` | no blocking on a `Task` or a `ValueTask` |
| `NoCaptiveDependencies` | `di/no-captive-dependencies` | singletons inject no scoped or transient type |
| `AsyncSuffix` | `naming/async-suffix` | authored `Task`/`ValueTask`-returning methods end in `Async` |
| `NoGeneralCatch` | `exceptions/no-general-catch` | only the top-level handler swallows base `Exception` |
| `AcceptCancellation` | `async/accept-cancellation` | authored `Task`/`ValueTask`-returning methods accept a `CancellationToken` |
| `NoMappingAttributes` | `persistence/no-mapping-attributes` | persisted types have no `[Table]` or `[ComplexType]` |

A method's name is the rule-name half of its ID, in PascalCase and with the area dropped, so
`http/reuse-httpclient` is `ReuseHttpClient`. Every method
takes
`(Arch arch, Selection subject, …, PackPosture posture, string? fix = null, Selection? exemplar = null)`,
and three of them take one extra `Selection` naming a seam (`compositionRoot`, `resolveSeam`,
`topLevelHandler`).

## What the pack owns, and what you own

The pack owns `Because` and `Citation`. The reason to reuse an `HttpClient` is the same in every
codebase, and so is the page that explains it. Both ship with the rule, and you can override
neither. If a rule's reason is different for you, write the rule yourself.

You own the posture. Pass `PackPosture.Enforce` for law, or
`PackPosture.Migrate("<what the code does today>")` for a ratchet against a baseline. The same rule
is `Enforce` in a codebase that already keeps it and `Migrate` in one working it off, and only you
know which applies. The baseline path stays conventional (`arch/baselines/<rule-id>.json`), so the
pack never names one.

You own the selections, which choose the types the rule governs and the seam it exempts.

You may override `Fix`, since remediation names local types and the pack's generic hint cannot.
Pass your hint as the `fix` argument. Pack methods return `void`, so a `.Fix(...)` trailer does not
compile, and exactly one `Because`, one `Citation` and one `Fix` reach the rule.

You may also name the type to copy. Pass a selection as the `exemplar` argument, such as
`exemplar: arch.Types.Named("CarrierClient")`. `check` resolves it like any rule's exemplar,
prints where the type is declared beneath a failed rule, and warns when no type matches it.

## Sharing a definition without taking the rule

Sometimes the rule is yours and only the list is the pack's. This repository's own
`mcp/no-blocking-waits` bans the same members as `async/no-sync-over-async`. Its reason names what a
block costs inside a long-lived MCP server, and a pack method takes no reason, so the rule is written
locally.

`BlockingWaitAnchors` returns that member set for a local rule to ban. You keep the ID, the posture,
the selections, the reason and the fix. The pack keeps one definition of what blocking on a task
means, so a member added to it reaches both rules at once.

```csharp
Selection host = arch.Namespace("MyApp.Host.*");
(Member blockingWait, Member[] moreBlockingWaits) = DotNetGuidance.BlockingWaitAnchors(arch);

arch.Rule("host/no-blocking-waits")
    .Enforce(host
        .Except(arch.Types.Named("ShutdownDrain"))
        .MustNotUse(blockingWait, moreBlockingWaits))
    .Because("A blocking wait in the request path holds a thread for the whole call and drops the " +
             "CancellationToken, so a client cancel leaves the work running.")
    .Fix("Await the call and flow the CancellationToken.");
```

The set comes in two parts only because `MustNotUse` takes one member and then the rest. It is the
only export of this kind in the pack today.

## Opting out, and colliding

To opt out of a rule, leave its method uncalled. A rule arrives only where your spec calls for it, so
the pack needs no suppression mechanism or severity dial.

Taking a rule from the pack and writing it yourself gives two rules one ID. The spec then fails to
load, with the error at the first of the two declarations. The pack adds no prefix to its IDs, which
is what makes the collision an error. Rule IDs are the baseline key and the `explain` handle, so one
error beats the same law reported twice under two names.

## `ApplyAll`

`ApplyAll` declares all nine at one posture, each with the pack's own `Fix`. Its only callers are the
pack's own tests, which use it to cover the full surface in one call. A real spec names the rules it
means, each at its own posture and over its own subject.

To test a pack of your own without loading a solution, pass a spec that calls its methods to
`SpecValidation.Validate`, from the contract package. It returns every mistake `sliderule check`
would report before reading any code, and the test asserts that the list is empty.

## Anchor doctrine

Every member anchor in this pack uses the `nameof` form, `arch.Member(typeof(X), nameof(X.M))`. The
pack ships inside a codebase its own rules govern, and the expression form is real code that the
checker reads. Written as `arch.Member<IServiceProvider>(sp => sp.GetService(...))`, an anchor would
record a use edge from `DotNetGuidance`, which `di/no-service-locator` reports as a violation wherever
it governs the pack. A `nameof` operand records no edge, and both forms produce the same anchor, so
every rule checks the same members either way.

A test reads this project's source and fails on an expression anchor, so the doctrine survives a
reformat.
