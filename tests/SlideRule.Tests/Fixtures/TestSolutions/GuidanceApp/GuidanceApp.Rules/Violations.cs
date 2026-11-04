using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace GuidanceApp.Rules;

// At least one positive and one near-miss per rule; the comment on each line says which.

public sealed class Program
{
    public static HttpClient SeamClient() => new HttpClient();                         // near-miss: reuse-httpclient (seam)

    public static void SeamCatch(Action run)
    {
        try { run(); } catch (Exception) { }                                          // near-miss: no-general-catch (seam)
    }
}

public sealed class Startup
{
    public static object? SeamResolve(IServiceProvider sp) => sp.GetService(typeof(Cache));   // near-miss: service-locator (seam)
    public static Cache SeamResolveRequired(IServiceProvider sp) => sp.GetRequiredService<Cache>(); // near-miss: service-locator (seam)

    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<Cache>();
        services.AddSingleton<SingletonClock>();
        services.AddScoped<ScopedRepository>();
    }
}

public sealed class WeatherService
{
    public HttpClient MakeClient() => new HttpClient();                                // POSITIVE: reuse-httpclient
}

public sealed class Locator
{
    public object? ByInterface(IServiceProvider sp) => sp.GetService(typeof(Cache));  // POSITIVE: service-locator (interface)
    public Cache? ByExtension(IServiceProvider sp) => sp.GetService<Cache>();          // POSITIVE: service-locator (extension)
    public Cache ByRequired(IServiceProvider sp) => sp.GetRequiredService<Cache>();    // POSITIVE: service-locator (required)
}

public sealed class ContainerFactory
{
    public IServiceProvider Build(IServiceCollection services) => services.BuildServiceProvider(); // POSITIVE: buildserviceprovider
    public IServiceCollection Register(IServiceCollection services) => services.AddSingleton<SingletonClock>(); // near-miss
}

public sealed class Blocking
{
    public int ByResult(Task<int> t) => t.Result;                                      // POSITIVE: sync-over-async (Result)
    public void ByWait(Task t) => t.Wait();                                            // POSITIVE: sync-over-async (Wait)
    public int ByAwaiter(Task<int> t) => t.GetAwaiter().GetResult();                   // POSITIVE: sync-over-async (GetResult)
    public int ByValueTask(ValueTask<int> t) => t.Result;                              // POSITIVE: sync-over-async (ValueTask.Result)
    public async Task<int> AwaitedAsync(Task<int> t, CancellationToken ct) => await t; // near-miss: sync-over-async
}

public sealed class Naming
{
    public Task Fetch(CancellationToken ct) => Task.CompletedTask;                     // POSITIVE: async-suffix
    public Task FetchAsync(CancellationToken ct) => Task.CompletedTask;                // near-miss: async-suffix, accept-cancellation
    public ValueTask<int> CountAsync(CancellationToken ct) => new(1);                  // near-miss: both async rules (ValueTask<T>)
}

public sealed class Cancellation
{
    public Task LoadAsync() => Task.CompletedTask;                                     // POSITIVE: accept-cancellation
    public Task<int> Load() => Task.FromResult(1);                                     // POSITIVE: async-suffix AND accept-cancellation
}

public sealed class Catching
{
    public void Swallows(Action run)
    {
        try { run(); } catch (Exception) { }                                          // POSITIVE: no-general-catch
    }

    public void Filters(Action run)
    {
        try { run(); } catch (Exception ex) when (ex is IOException) { }              // near-miss: when filter
    }

    public void Rethrows(Action run)
    {
        try { run(); } catch (Exception) { throw; }                                   // near-miss: rethrow
    }

    public void Specific(Action run)
    {
        try { run(); } catch (InvalidOperationException) { }                          // near-miss: specific type
    }
}

[Table("orders")]
public sealed class OrderEntity                                                        // POSITIVE: no-mapping-attributes
{
    public int Id { get; set; }
}

public sealed class CustomerEntity                                                     // near-miss: [Column] is not banned
{
    [Column("id")] public int Id { get; set; }
}

public sealed class ScopedRepository;

public sealed class SingletonClock;

public sealed class Cache(ScopedRepository repository, SingletonClock clock)          // not in the package: captive-dependencies
{
    public ScopedRepository Repository { get; } = repository;
    public SingletonClock Clock { get; } = clock;
}

// The async rules report a signature where it is declared, never again where a member inherits it.

public interface IExporter
{
    Task Export();                                                                     // POSITIVE: async-suffix AND accept-cancellation
}

public sealed class Exporter : IExporter
{
    public Task Export() => Task.CompletedTask;                                        // near-miss: both async rules (implements IExporter)
}

public abstract class Job
{
    public abstract Task Run();                                                        // POSITIVE: async-suffix AND accept-cancellation
}

public sealed class NightlyJob : Job
{
    public override Task Run() => Task.CompletedTask;                                  // near-miss: both async rules (overrides Job)
}

public sealed class Disposer : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;                                        // near-miss: accept-cancellation (implements a framework interface)
}
