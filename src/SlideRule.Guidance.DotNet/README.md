# SlideRule.Guidance.DotNet

`SlideRule.Guidance.DotNet` is a Roslyn analyzer that reports code departing from Microsoft's .NET
guidance as compiler warnings. Each rule has its own diagnostic ID and links to the Microsoft page it
rests on. You need no spec, no model file and no tool: reference the package, and the next build
reports.

It is built with [SlideRule](https://github.com/andypgray/sliderule). The rules are SlideRule rules,
and the analyzer runs the same extractor and checker as `sliderule check` over the project being
compiled.

## Usage

Reference the package from the projects you want checked. One line in a `Directory.Build.props`
reaches all of them:

```xml
<ItemGroup>
  <PackageReference Include="SlideRule.Guidance.DotNet" Version="x.y.z" PrivateAssets="all" />
</ItemGroup>
```

The package is a development dependency, so the packages you publish do not depend on it. It needs
a compiler on Roslyn 4.0.1 or later (the .NET 6 SDK, Visual Studio 2022). An older compiler builds
as before, without the analyzer.

A broken rule prints one warning at the offending code:

```
WeatherService.cs(6,33): warning SR3001: http/reuse-httpclient: Contoso.WeatherService constructs System.Net.Http.HttpClient. Types, except types named `Program` or types named `*Startup`, must not construct `HttpClient`. Because: A new HttpClient per call exhausts sockets under load; IHttpClientFactory pools handlers. Fix: Take a typed or named client from IHttpClientFactory rather than constructing one. (https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines)
```

## Rules

| ID | Rule | Default | Guidance |
|---|---|---|---|
| `SR3001` | `http/reuse-httpclient` | Warning | [HttpClient guidelines](https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines) |
| `SR3002` | `di/no-service-locator` | Warning | [DI guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `SR3003` | `di/no-buildserviceprovider` | Warning | [DI guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines) |
| `SR3004` | `async/no-sync-over-async` | Warning | [Async scenarios](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios) |
| `SR3005` | `naming/async-suffix` | Warning | [Task-based async pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |
| `SR3006` | `exceptions/no-general-catch` | Warning | [Standard exception types](https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types) |
| `SR3007` | `async/accept-cancellation` | Off | [Task-based async pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap) |
| `SR3008` | `persistence/no-mapping-attributes` | Warning | [Architectural principles](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles) |

`SR3001` reports a `new HttpClient(...)`. `SR3002` reports a call to `IServiceProvider.GetService`,
or to the `GetService` and `GetRequiredService` extensions on it. `SR3003` reports a call to
`BuildServiceProvider`. `SR3004` reports `Task.Wait`, `.Result` on `Task<T>` or `ValueTask<T>`, and
`GetResult` on a task awaiter. `SR3005` reports a `Task`- or `ValueTask`-returning method whose name
does not end in `Async`. `SR3006` reports a `catch (Exception)` that neither filters with `when` nor
rethrows. `SR3007` reports a `Task`- or `ValueTask`-returning method with no `CancellationToken`
parameter. `SR3008` reports a type attributed with `[Table]` or `[ComplexType]`.

`SR3007` ships off because a codebase that does not flow tokens today gets one warning per async
method, and an interface method's warning repeats at every implementation.

## What each rule leaves alone

- A composition root may construct clients and resolve services. That is any type named `Program`,
  or with a name ending in `Startup`. `SR3006` also leaves alone the catch-all in a type derived from
  `BackgroundService`, which is a top-level handler.
- `SR3005` leaves alone the methods another convention names. Those are test methods attributed with
  xUnit's `[Fact]` or `[Theory]`; NUnit's `[Test]`, `[TestCase]`, `[TestCaseSource]`, `[SetUp]`,
  `[TearDown]`, `[OneTimeSetUp]` or `[OneTimeTearDown]`; or MSTest's `[TestMethod]`,
  `[DataTestMethod]`, `[TestInitialize]` or `[TestCleanup]`. They also include the public methods of a
  type derived from an MVC or Web API controller base, or from a SignalR `Hub`.
- `SR3007` leaves alone the same test methods.
- Both async rules skip generated code.

The attributes match as written on the method. An attribute of your own derived from `[Fact]` is not
one of them.

## Configuring a rule

Each rule is an ordinary analyzer rule, so `.editorconfig` sets its severity:

```ini
[*.cs]
# Turn on the cancellation rule.
dotnet_diagnostic.SR3007.severity = warning
# Turn off the suffix rule.
dotnet_diagnostic.SR3005.severity = none
```

`#pragma warning disable SR3004` and `[SuppressMessage]` work for a single site, as for any analyzer.
In a build that treats warnings as errors, add the IDs you want to keep as warnings to
`WarningsNotAsErrors`.

## Next to a SlideRule spec

A project with a SlideRule spec keeps its own rules. When the analyzer reads that project's
`sliderule.model.json`, any rule the model declares by the same ID is left to the model. The
project's posture, baseline and exemptions for that rule then apply, and the rule is not reported
twice. The analyzer reads the model when the `SlideRule.Analyzers` package passes it to the build.

To change a rule's scope or ratchet it against a baseline, write the rule in your spec under the same
ID.

## What is left to `sliderule check`

`di/no-captive-dependencies` is a rule of the same guidance that this package does not include. It
compares a singleton's constructor parameters with the lifetimes services are registered under, and
registrations are collected across the whole solution. One project's build cannot see them, so the
rule needs `sliderule check` and a spec that declares it.

## Diagnostics

`SR3093` reports a fault in the analyzer itself. Some or all of the project's findings are then
missing, and the warning names the exception. It is a warning of its own so that a build treating
warnings as errors can exclude it by ID. Report it at
<https://github.com/andypgray/sliderule/issues>.
