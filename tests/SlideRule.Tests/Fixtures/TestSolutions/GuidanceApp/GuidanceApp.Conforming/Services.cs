using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuidanceApp.Conforming;

// Touches the anchors the package's rules name only where a rule allows it, such as at a seam or in a filtered
// or rethrowing catch, and holds one method of each convention the async rules leave alone.

public static class Program
{
    public static IServiceProvider Compose(IServiceCollection services)
    {
        services.AddSingleton<Clock>();
        services.AddScoped<OrderService>();
        var client = new HttpClient();                         // the composition root may construct
        client.Dispose();
        return null!;
    }

    public static OrderService Resolve(IServiceProvider sp) => sp.GetRequiredService<OrderService>();

    public static void Main()
    {
        try { Compose(new ServiceCollection()); } catch (Exception) { }   // the top-level handler
    }
}

public sealed class OrderingStartup
{
    public static Clock Resolve(IServiceProvider sp) => sp.GetRequiredService<Clock>();   // a *Startup seam
}

public sealed class Clock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

public sealed class OrderService(Clock clock)
{
    public async ValueTask<int> CountAsync(CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return clock.Now.Second;
    }

    public void Guarded(Action run)
    {
        try { run(); } catch (Exception ex) when (ex is IOException) { }
        try { run(); } catch (Exception) { throw; }
    }
}

public sealed class Order
{
    [Column("id")] public int Id { get; set; }
}

public sealed class OrdersController : ControllerBase
{
    public Task<int> Index(CancellationToken ct) => Task.FromResult(1);   // an action: the routing convention names it
}

public sealed class OrderServiceTests
{
    [Fact]
    public async Task CountsTheOrders() => await Task.Yield(); // a test: the framework names it
}
