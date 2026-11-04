using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Container-registration facts (GRAMMAR §4.7), over the MSBuild-free fast path compiled against the real
///     DI/Hosting abstractions (and, for EF Core / Microsoft.Extensions.Http — absent from the suite — source
///     stubs in the <c>Microsoft.Extensions.DependencyInjection</c> namespace, faithful because recognition is
///     namespace + name + first-parameter).
/// </summary>
public sealed class CodebaseExtractorRegistrationTests
{
    // ── AddDbContext / AddDbContextPool (source stubs; EF Core absent repo-wide) ───────────────────────────

    private const string DbContextStub = """
                                         namespace Microsoft.Extensions.DependencyInjection
                                         {
                                             public static class EntityFrameworkServiceCollectionExtensions
                                             {
                                                 public static IServiceCollection AddDbContext<TContext>(
                                                     this IServiceCollection services,
                                                     System.Action<object>? optionsAction = null,
                                                     ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
                                                     ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
                                                     where TContext : class => services;

                                                 public static IServiceCollection AddDbContextPool<TContext>(
                                                     this IServiceCollection services,
                                                     System.Action<object> optionsAction,
                                                     int poolSize = 1024)
                                                     where TContext : class => services;
                                             }
                                         }
                                         """;

    // ── AddHttpClient (source stubs; Microsoft.Extensions.Http absent from the suite) ──────────────────────

    private const string HttpClientStub = """
                                          namespace Microsoft.Extensions.DependencyInjection
                                          {
                                              public interface IHttpClientBuilder {}
                                              public static class HttpClientFactoryServiceCollectionExtensions
                                              {
                                                  public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                                                      where TClient : class => null!;
                                                  public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services)
                                                      where TClient : class where TImplementation : class, TClient => null!;
                                                  public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                                              }
                                          }
                                          """;
    // ── Add{Singleton,Scoped,Transient} — generic forms ───────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddFamilyTwoTypeArgs_RecordsServiceAndImplementationPerLifetime()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public interface IBar {}
                                                                          public class Bar : IBar {}
                                                                          public interface IBaz {}
                                                                          public class Baz : IBaz {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                                  services.AddScoped<IBar, Bar>();
                                                                                  services.AddTransient<IBaz, Baz>();
                                                                              }
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IFoo", "N.Foo"));
        Registrations(model)
            .ShouldContain((Lifetime.Scoped, "N.IBar", "N.Bar"));
        Registrations(model)
            .ShouldContain((Lifetime.Transient, "N.IBaz", "N.Baz"));
    }

    [Fact]
    public void Recognize_AddSingletonOneTypeArgReceiverOnly_RecordsSelfRegistration()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton<Foo>();
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.Foo", "N.Foo"));
    }

    [Fact]
    public void Recognize_AddSingletonOneTypeArgWithFactory_RecordsNoImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton<Foo>(sp => new Foo());
                                                                          }
                                                                          """));

        // A factory registration names no implementation type — (Foo, null), not (Foo, Foo).
        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.Foo", null));
        Registrations(model)
            .ShouldNotContain((Lifetime.Singleton, "N.Foo", "N.Foo"));
    }

    // ── Add{...} — typeof forms ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddTypeofTwoArgs_RecordsServiceAndImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IFoo), typeof(Foo));
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IFoo", "N.Foo"));
    }

    [Fact]
    public void Recognize_AddTypeofOneArg_RecordsSelfRegistration()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Bar {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddScoped(typeof(Bar));
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Scoped, "N.Bar", "N.Bar"));
    }

    [Fact]
    public void Recognize_AddTypeofWithFactory_RecordsNoImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IFoo), sp => new Foo());
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IFoo", null));
    }

    [Fact]
    public void Recognize_OpenGenericTypeofRegistration_RecordsDefinitionLevelFqns()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IRepo<T> {}
                                                                          public class Repo<T> : IRepo<T> {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IRepo<T>", "N.Repo<T>"));
    }

    // ── Type arguments the compiler could not bind exactly ────────────────────────────────────────────────

    // One composition root for every row below. Outer.Hidden is inaccessible, Dup is ambiguous between the two
    // imported namespaces, and Missing and Elsewhere.Missing are declared nowhere, Elsewhere being a namespace that
    // exists. None of these calls compiles, which is the point: extraction reads code that does not.
    private static CodebaseModel ExtractRegistration(string call)
    {
        return CompilationFactory.ExtractWithDi(
            ("DbStub.cs", DbContextStub),
            ("HttpStub.cs", HttpClientStub),
            ("Elsewhere.cs", """
                             namespace A { public class Dup {} }
                             namespace B { public class Dup {} }
                             namespace Elsewhere { public class Present {} }
                             """),
            ("Reg.cs", $$"""
                         using Microsoft.Extensions.DependencyInjection;
                         using Microsoft.Extensions.DependencyInjection.Extensions;
                         using A;
                         using B;
                         namespace N;
                         public interface IFoo {}
                         public class Foo : IFoo {}
                         public class Outer { private class Hidden {} }
                         public static class Reg
                         {
                             public static void Configure(IServiceCollection services) => {{call}};
                         }
                         """));
    }

    private static (Lifetime Lifetime, string Service, string? Implementation)[] Registrations(CodebaseModel model)
    {
        return model.ServiceRegistrations
            .Select(registration => (registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName))
            .ToArray();
    }

    [Theory]
    [InlineData("services.AddSingleton<Outer.Hidden>()", Lifetime.Singleton, "N.Outer.Hidden", "N.Outer.Hidden")]
    [InlineData("services.AddSingleton<Dup>()", Lifetime.Singleton, "A.Dup", "A.Dup")]
    [InlineData("services.AddSingleton<Missing>()", Lifetime.Singleton, null, null)]
    [InlineData("services.AddSingleton<Elsewhere.Missing>()", Lifetime.Singleton, null, null)]
    [InlineData("services.AddSingleton<Outer.Hidden>(sp => null!)", Lifetime.Singleton, "N.Outer.Hidden", null)]
    [InlineData("services.AddSingleton<Dup>(sp => null!)", Lifetime.Singleton, "A.Dup", null)]
    [InlineData("services.AddSingleton<Missing>(sp => null!)", Lifetime.Singleton, null, null)]
    [InlineData("services.AddScoped<IFoo, Outer.Hidden>()", Lifetime.Scoped, "N.IFoo", "N.Outer.Hidden")]
    [InlineData("services.AddScoped<IFoo, Dup>()", Lifetime.Scoped, "N.IFoo", "A.Dup")]
    [InlineData("services.AddScoped<IFoo, Missing>()", Lifetime.Scoped, "N.IFoo", null)]
    [InlineData("services.AddScoped<IFoo, Elsewhere.Missing>()", Lifetime.Scoped, "N.IFoo", null)]
    [InlineData("services.AddTransient<Outer.Hidden, Foo>()", Lifetime.Transient, "N.Outer.Hidden", "N.Foo")]
    [InlineData("services.AddTransient<Dup, Foo>()", Lifetime.Transient, "A.Dup", "N.Foo")]
    [InlineData("services.AddTransient<Missing, Foo>()", Lifetime.Transient, null, null)]
    [InlineData("services.TryAddSingleton<Outer.Hidden>()", Lifetime.Singleton, "N.Outer.Hidden", "N.Outer.Hidden")]
    [InlineData("services.TryAddSingleton<Dup>()", Lifetime.Singleton, "A.Dup", "A.Dup")]
    [InlineData("services.TryAddSingleton<Missing>()", Lifetime.Singleton, null, null)]
    [InlineData("services.TryAddTransient<IFoo, Outer.Hidden>()", Lifetime.Transient, "N.IFoo", "N.Outer.Hidden")]
    [InlineData("services.TryAddTransient<IFoo, Dup>()", Lifetime.Transient, "N.IFoo", "A.Dup")]
    [InlineData("services.TryAddTransient<IFoo, Missing>()", Lifetime.Transient, "N.IFoo", null)]
    [InlineData("services.AddHostedService<Outer.Hidden>()", Lifetime.Singleton, "Microsoft.Extensions.Hosting.IHostedService", "N.Outer.Hidden")]
    [InlineData("services.AddHostedService<Dup>()", Lifetime.Singleton, "Microsoft.Extensions.Hosting.IHostedService", "A.Dup")]
    [InlineData("services.AddHostedService<Missing>()", Lifetime.Singleton, null, null)]
    [InlineData("services.AddDbContext<Outer.Hidden>()", Lifetime.Scoped, "N.Outer.Hidden", "N.Outer.Hidden")]
    [InlineData("services.AddDbContext<Dup>()", Lifetime.Scoped, "A.Dup", "A.Dup")]
    [InlineData("services.AddDbContext<Missing>()", Lifetime.Scoped, null, null)]
    [InlineData("services.AddDbContextPool<Outer.Hidden>(_ => {})", Lifetime.Scoped, "N.Outer.Hidden", "N.Outer.Hidden")]
    [InlineData("services.AddDbContextPool<Dup>(_ => {})", Lifetime.Scoped, "A.Dup", "A.Dup")]
    [InlineData("services.AddDbContextPool<Missing>(_ => {})", Lifetime.Scoped, null, null)]
    [InlineData("services.AddHttpClient<Outer.Hidden>()", Lifetime.Transient, "N.Outer.Hidden", "N.Outer.Hidden")]
    [InlineData("services.AddHttpClient<Dup>()", Lifetime.Transient, "A.Dup", "A.Dup")]
    [InlineData("services.AddHttpClient<Missing>()", Lifetime.Transient, null, null)]
    [InlineData("services.AddHttpClient<IFoo, Outer.Hidden>()", Lifetime.Transient, "N.IFoo", "N.Outer.Hidden")]
    [InlineData("services.AddHttpClient<IFoo, Dup>()", Lifetime.Transient, "N.IFoo", "A.Dup")]
    [InlineData("services.AddHttpClient<IFoo, Missing>()", Lifetime.Transient, "N.IFoo", null)]
    public void Recognize_GenericFormOverATypeTheCompilerCouldNotBindExactly_RecordsTheCandidateOrNothing(
        string call, Lifetime lifetime, string? service, string? implementation)
    {
        // A type the compiler can only guess at (inaccessible, ambiguous) is recorded under its candidate, as the
        // typeof operand is. One it cannot find at all has no candidate: the call records nothing where that type
        // is the service, and no implementation where it is the implementation.
        (Lifetime, string, string?)[] expected = service is null ? [] : [(lifetime, service, implementation)];

        CodebaseModel model = ExtractRegistration(call);

        Registrations(model)
            .ShouldBe(expected);
    }

    public static TheoryData<string, string> GenericAndTypeofTwins
    {
        get
        {
            var twins = new TheoryData<string, string>();
            foreach (string written in new[] { "Outer.Hidden", "Dup", "Missing", "Elsewhere.Missing" })
            {
                twins.Add($"services.AddSingleton<{written}>()", $"services.AddSingleton(typeof({written}))");
                twins.Add($"services.AddScoped<IFoo, {written}>()", $"services.AddScoped(typeof(IFoo), typeof({written}))");
                twins.Add($"services.AddTransient<{written}, Foo>()", $"services.AddTransient(typeof({written}), typeof(Foo))");
                twins.Add($"services.AddSingleton<{written}>(sp => null!)", $"services.AddSingleton(typeof({written}), sp => null!)");
            }

            return twins;
        }
    }

    [Theory]
    [MemberData(nameof(GenericAndTypeofTwins))]
    public void Recognize_TypeTheCompilerCouldNotBindExactly_RecordsTheSameInTheGenericAndTypeofForms(
        string genericCall, string typeofCall)
    {
        // The two spellings of one registration must not disagree about a type the code names imperfectly: the
        // forms theory above pins what the generic side records, so this pins the typeof side to the same.
        CodebaseModel genericModel = ExtractRegistration(genericCall);
        CodebaseModel typeofModel = ExtractRegistration(typeofCall);
        (Lifetime Lifetime, string Service, string? Implementation)[] expected = Registrations(typeofModel);

        Registrations(genericModel)
            .ShouldBe(expected);
    }

    // ── TryAdd twins ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_TryAddTwins_RecordPerLifetimeLikeTheirAddSiblings()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Microsoft.Extensions.DependencyInjection.Extensions;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public interface IBar {}
                                                                          public class Bar : IBar {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.TryAddSingleton<IFoo, Foo>();
                                                                                  services.TryAddScoped<IBar, Bar>();
                                                                              }
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IFoo", "N.Foo"));
        Registrations(model)
            .ShouldContain((Lifetime.Scoped, "N.IBar", "N.Bar"));
    }

    // ── AddHostedService ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_AddHostedService_RecordsSynthesizedIHostedServiceAsServiceAndTAsImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Microsoft.Extensions.Hosting;
                                                                          using System.Threading;
                                                                          using System.Threading.Tasks;
                                                                          namespace N;
                                                                          public class Worker : IHostedService
                                                                          {
                                                                              public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
                                                                              public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
                                                                          }
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "Microsoft.Extensions.Hosting.IHostedService", "N.Worker"));
    }

    [Fact]
    public void Recognize_AddHostedServiceWhenIHostedServiceUnresolvable_RecordsImplementationOnly()
    {
        // Hosting.Abstractions absent, so IHostedService does not resolve; the recognizer falls back to the
        // implementation-only form (the worker in the service slot, no implementation). The call is a source
        // stub (the real extension lives in the removed assembly).
        CodebaseModel model = CompilationFactory.ExtractWithDiNoHosting(("Reg.cs", """
                                                                                   namespace Microsoft.Extensions.DependencyInjection
                                                                                   {
                                                                                       public static class HostedServiceStub
                                                                                       {
                                                                                           public static IServiceCollection AddHostedService<THostedService>(this IServiceCollection services)
                                                                                               where THostedService : class => services;
                                                                                       }
                                                                                   }
                                                                                   namespace N
                                                                                   {
                                                                                       using Microsoft.Extensions.DependencyInjection;
                                                                                       public class Worker {}
                                                                                       public static class Reg
                                                                                       {
                                                                                           public static void Configure(IServiceCollection services) => services.AddHostedService<Worker>();
                                                                                       }
                                                                                   }
                                                                                   """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.Worker", null));
    }

    [Fact]
    public void Recognize_AddDbContextDefault_RecordsScoped()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddDbContext<MyContext>();
                       }
                       """));

        Registrations(model)
            .ShouldContain((Lifetime.Scoped, "N.MyContext", "N.MyContext"));
    }

    [Fact]
    public void Recognize_AddDbContextLiteralLifetime_IsHonored()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) =>
                               services.AddDbContext<MyContext>(null, ServiceLifetime.Singleton);
                       }
                       """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.MyContext", "N.MyContext"));
        Registrations(model)
            .ShouldNotContain((Lifetime.Scoped, "N.MyContext", "N.MyContext"));
    }

    [Fact]
    public void Recognize_AddDbContextNonLiteralLifetime_RecordsNoFact()
    {
        // A non-literal lifetime argument (a variable) is not guessed — no fact is recorded at all.
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services, ServiceLifetime lifetime) =>
                               services.AddDbContext<MyContext>(null, lifetime);
                       }
                       """));

        Registrations(model)
            .ShouldNotContain(registration => registration.Service == "N.MyContext");
    }

    [Fact]
    public void Recognize_AddDbContextPool_RecordsScoped()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", DbContextStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class MyContext {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddDbContextPool<MyContext>(_ => {});
                       }
                       """));

        Registrations(model)
            .ShouldContain((Lifetime.Scoped, "N.MyContext", "N.MyContext"));
    }

    [Fact]
    public void Recognize_AddHttpClientOneTypeArg_RecordsTransientSelfClient()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public class Client {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient<Client>();
                       }
                       """));

        Registrations(model)
            .ShouldContain((Lifetime.Transient, "N.Client", "N.Client"));
    }

    [Fact]
    public void Recognize_AddHttpClientTwoTypeArgs_RecordsTransientClientAndImplementation()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public interface IClient {}
                       public class Client : IClient {}
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient<IClient, Client>();
                       }
                       """));

        Registrations(model)
            .ShouldContain((Lifetime.Transient, "N.IClient", "N.Client"));
    }

    [Fact]
    public void Recognize_AddHttpClientNamedOnly_RecordsNoFact()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Stub.cs", HttpClientStub),
            ("Reg.cs", """
                       using Microsoft.Extensions.DependencyInjection;
                       namespace N;
                       public static class Reg
                       {
                           public static void Configure(IServiceCollection services) => services.AddHttpClient("named");
                       }
                       """));

        Registrations(model)
            .ShouldBeEmpty();
    }

    // ── Gate negatives ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_UserNamespaceLookAlike_IsNotRecognized()
    {
        // AddSingleton in a user namespace (only it is in scope — MEDI's extension is not imported here) fails
        // the namespace gate.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          namespace MyApp
                                                                          {
                                                                              public static class DiExtensions
                                                                              {
                                                                                  public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddSingleton<T>(
                                                                                      this Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
                                                                              }
                                                                          }
                                                                          namespace Consumer
                                                                          {
                                                                              using MyApp;
                                                                              public class Foo {}
                                                                              public static class Root
                                                                              {
                                                                                  public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                                                                                      services.AddSingleton<Foo>();
                                                                              }
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Recognize_NonServiceCollectionFirstParameter_IsNotRecognized()
    {
        // A MEDI-namespace extension whose receiver is not IServiceCollection fails the first-parameter gate.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using System.Text;
                                                                          namespace Microsoft.Extensions.DependencyInjection
                                                                          {
                                                                              public static class FakeExtensions
                                                                              {
                                                                                  public static void AddSingleton<T>(this StringBuilder sb) {}
                                                                              }
                                                                          }
                                                                          namespace N
                                                                          {
                                                                              using Microsoft.Extensions.DependencyInjection;
                                                                              public class Foo {}
                                                                              public static class Root
                                                                              {
                                                                                  public static void Configure()
                                                                                  {
                                                                                      var sb = new StringBuilder();
                                                                                      sb.AddSingleton<Foo>();
                                                                                  }
                                                                              }
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBeEmpty();
    }

    // ── Wrapper body seen ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_InSolutionWrapperBody_SeesTheRealCallInside()
    {
        // The wrapper call (AddMyServices, a user-namespace extension) is not recognized, but the real
        // AddSingleton inside its body IS — the pass walks every tree, wrapper bodies included.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class MyWiring
                                                                          {
                                                                              public static IServiceCollection AddMyServices(this IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                                  return services;
                                                                              }
                                                                          }
                                                                          public static class Root
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddMyServices();
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBe([(Lifetime.Singleton, "N.IFoo", "N.Foo")]); // only the inner real call; AddMyServices is not recognized
    }

    // ── Honesty boundary ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_Configure_IsNotRecognized()
    {
        // Configure passes the namespace + first-parameter gate but its name is not in the recognized-call
        // table — the options interfaces are framework-registered and invisible (GRAMMAR §4.7). It is generic over
        // the collection too, so it is not a wrapper call either: the framework declares it, not the solution.
        CompilationInput input = CompilationFactory.CompileWithDi("TestProject", ("Reg.cs", """
                                                                                            using Microsoft.Extensions.DependencyInjection;
                                                                                            namespace N;
                                                                                            public class MyOptions {}
                                                                                            public static class Reg
                                                                                            {
                                                                                                public static void Configure(IServiceCollection services) => services.Configure<MyOptions>(o => {});
                                                                                            }
                                                                                            """));

        Registrations(Harness.Extract([input]))
            .ShouldBeEmpty();
        FragmentExtractor.Extract(input, Ct)
            .WrapperCalls.ShouldBeNull();
    }

    [Fact]
    public void Recognize_KeyedService_IsNotRecognized()
    {
        CompilationInput input = CompilationFactory.CompileWithDi("TestProject", ("Reg.cs", """
                                                                                            using Microsoft.Extensions.DependencyInjection;
                                                                                            namespace N;
                                                                                            public interface IFoo {}
                                                                                            public class Foo : IFoo {}
                                                                                            public static class Reg
                                                                                            {
                                                                                                public static void Configure(IServiceCollection services) => services.AddKeyedSingleton<IFoo, Foo>("key");
                                                                                            }
                                                                                            """));

        Registrations(Harness.Extract([input]))
            .ShouldBeEmpty();
        FragmentExtractor.Extract(input, Ct)
            .WrapperCalls.ShouldBeNull();
    }

    // ── ServiceDescriptor handed to the collection ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("services.Add(ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), ServiceLifetime.Scoped))", Lifetime.Scoped, "N.Foo")]
    [InlineData("services.Add(ServiceDescriptor.Describe(typeof(IFoo), sp => new Foo(), ServiceLifetime.Transient))", Lifetime.Transient, null)]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IFoo, Foo>())", Lifetime.Singleton, "N.Foo")]
    [InlineData("services.TryAdd(ServiceDescriptor.Transient(typeof(IFoo), typeof(Foo)))", Lifetime.Transient, "N.Foo")]
    [InlineData("services.Replace(ServiceDescriptor.Scoped<IFoo>(sp => new Foo()))", Lifetime.Scoped, null)]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IFoo), typeof(Foo), ServiceLifetime.Transient))", Lifetime.Transient, "N.Foo")]
    [InlineData("services.Add(new ServiceDescriptor(typeof(IFoo), new Foo()))", Lifetime.Singleton, null)]
    [InlineData("services.Insert(0, new(typeof(IFoo), typeof(Foo), ServiceLifetime.Scoped))", Lifetime.Scoped, "N.Foo")]
    [InlineData("new ServiceCollection().Add(ServiceDescriptor.Singleton<IFoo, Foo>())", Lifetime.Singleton, "N.Foo")]
    [InlineData("services.Add(ServiceDescriptor.Describe(serviceType: typeof(IFoo), typeof(Foo), ServiceLifetime.Scoped))", Lifetime.Scoped, "N.Foo")]
    [InlineData("var d = ServiceDescriptor.Singleton<IFoo, Foo>(); services.Add(d)", Lifetime.Singleton, "N.Foo")]
    [InlineData("var d = new ServiceDescriptor(typeof(IFoo), typeof(Foo), ServiceLifetime.Transient); if (!services.Contains(d)) services.Add(d)", Lifetime.Transient, "N.Foo")]
    public void Recognize_DescriptorHandedToTheCollection_RecordsItsRegistration(string call, Lifetime lifetime, string? implementation)
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", DescriptorSource(call)));

        Registrations(model)
            .ShouldBe([(lifetime, "N.IFoo", implementation)]);
    }

    [Theory]
    [InlineData("services.Remove(ServiceDescriptor.Singleton<IFoo, Foo>())")]
    [InlineData("services.Add(ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), Chosen))")]
    [InlineData("services.Add(ServiceDescriptor.KeyedSingleton<IFoo, Foo>(\"key\"))")]
    [InlineData("var descriptor = ServiceDescriptor.Singleton<IFoo, Foo>()")]
    [InlineData("var d = ServiceDescriptor.Singleton<IFoo, Foo>(); services.Remove(d)")]
    [InlineData("var d = ServiceDescriptor.Singleton<IFoo, Foo>(); d = ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), Chosen); services.Add(d)")]
    [InlineData("var d = ServiceDescriptor.Singleton<IFoo, Foo>(); (d, _) = (ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), Chosen), 0); services.Add(d)")]
    [InlineData("var d = ServiceDescriptor.Singleton<IFoo, Foo>(); ref ServiceDescriptor alias = ref d; alias = ServiceDescriptor.Describe(typeof(IFoo), typeof(Foo), Chosen); services.Add(d)")]
    public void Recognize_DescriptorNotAddedOrUnreadable_RecordsNothing(string call)
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", DescriptorSource(call)));

        Registrations(model)
            .ShouldBeEmpty();
    }

    private static string DescriptorSource(string call)
    {
        return $$"""
                 using Microsoft.Extensions.DependencyInjection;
                 using Microsoft.Extensions.DependencyInjection.Extensions;
                 namespace N;
                 public interface IFoo {}
                 public class Foo : IFoo {}
                 public static class Reg
                 {
                     public static ServiceLifetime Chosen { get; } = ServiceLifetime.Scoped;

                     public static void Configure(IServiceCollection services)
                     {
                         {{call}};
                     }
                 }
                 """;
    }

    // ── ServiceDescriptor subclasses, read through the base constructor ─────────────────────────────────────

    // Named without the ServiceDescriptor suffix, and declared in a file of its own, apart from the creation.
    private static string DescriptorSubclass(string constructor)
    {
        return $$"""
                 using System;
                 using Microsoft.Extensions.DependencyInjection;
                 namespace N;
                 public sealed class UniqueDescriptor : ServiceDescriptor
                 {
                     {{constructor}}

                     private static ServiceLifetime Promote(ServiceLifetime lifetime) => lifetime;
                 }
                 """;
    }

    [Theory]
    [InlineData(
        "public UniqueDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime) : base(serviceType, implementationType, lifetime) {}",
        "typeof(IFoo), typeof(Foo), ServiceLifetime.Transient")]
    [InlineData(
        "public UniqueDescriptor(ServiceLifetime lifetime, Type impl, Type svc) : base(svc, impl, lifetime) {}",
        "ServiceLifetime.Transient, typeof(Foo), typeof(IFoo)")]
    public void Recognize_DescriptorSubclassForwardingItsParameters_RecordsItsRegistration(string constructor, string arguments)
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Descriptor.cs", DescriptorSubclass(constructor)),
            ("Reg.cs", DescriptorSource($"services.Add(new UniqueDescriptor({arguments}))")));

        Registrations(model)
            .ShouldBe([(Lifetime.Transient, "N.IFoo", "N.Foo")]);
    }

    // A computed base argument, and a forwarded lifetime the creation leaves to the constructor's default: neither
    // lifetime is read, so neither records one, Singleton included.
    [Theory]
    [InlineData(
        "public UniqueDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime) : base(serviceType, implementationType, Promote(lifetime)) {}",
        "typeof(IFoo), typeof(Foo), ServiceLifetime.Transient")]
    [InlineData(
        "public UniqueDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime = ServiceLifetime.Scoped) : base(serviceType, implementationType, lifetime) {}",
        "typeof(IFoo), typeof(Foo)")]
    public void Recognize_DescriptorSubclassWhoseLifetimeIsUnread_RecordsNothing(string constructor, string arguments)
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Descriptor.cs", DescriptorSubclass(constructor)),
            ("Reg.cs", DescriptorSource($"services.Add(new UniqueDescriptor({arguments}))")));

        Registrations(model)
            .ShouldBeEmpty();
    }

    // ── Generic wrappers, expanded at each call ──────────────────────────────────────────────────────────

    private const string DiNamespace = "Microsoft.Extensions.DependencyInjection";

    // The shape of a wrapper that replaces any earlier registration: the parameterless overload forwards a literal
    // lifetime to the one that hands a descriptor to the collection.
    private const string UniqueWrappers = """
                                          using System;
                                          using Microsoft.Extensions.DependencyInjection;
                                          using Microsoft.Extensions.DependencyInjection.Extensions;
                                          namespace W;
                                          public static class UniqueExtensions
                                          {
                                              public static IServiceCollection AddUnique<TService, TImplementing>(this IServiceCollection services)
                                                  where TService : class where TImplementing : class, TService =>
                                                  AddUnique<TService, TImplementing>(services, ServiceLifetime.Singleton);

                                              public static IServiceCollection AddUnique<TService, TImplementing>(this IServiceCollection services, ServiceLifetime lifetime)
                                                  where TService : class where TImplementing : class, TService
                                              {
                                                  services.RemoveAll<TService>();
                                                  services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TImplementing), lifetime));
                                                  return services;
                                              }

                                              public static IServiceCollection AddUnique<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
                                                  where TService : class
                                              {
                                                  services.Add(ServiceDescriptor.Describe(typeof(TService), factory, ServiceLifetime.Singleton));
                                                  return services;
                                              }

                                              public static IServiceCollection AddWithDefault<TService>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Transient)
                                                  where TService : class
                                              {
                                                  services.Add(ServiceDescriptor.Describe(typeof(TService), typeof(TService), lifetime));
                                                  return services;
                                              }
                                          }
                                          """;

    private const string UniqueCallers = """
                                         using Microsoft.Extensions.DependencyInjection;
                                         using W;
                                         namespace N;
                                         public interface IFoo {}
                                         public class Foo : IFoo {}
                                         public interface IBar {}
                                         public class Bar : IBar {}
                                         public class Baz {}
                                         public static class Root
                                         {
                                             public static ServiceLifetime Chosen { get; } = ServiceLifetime.Transient;

                                             public static void Configure(IServiceCollection services)
                                             {
                                                 services.AddUnique<IFoo, Foo>();
                                                 services.AddUnique<IBar, Bar>(ServiceLifetime.Scoped);
                                                 services.AddUnique<Baz>(sp => new Baz());
                                                 services.AddWithDefault<Foo>();
                                                 services.AddUnique<IFoo, Foo>(Chosen);
                                             }
                                         }
                                         """;

    private static readonly (Lifetime Lifetime, string Service, string? Implementation)[] UniqueRegistrations =
    [
        (Lifetime.Singleton, "N.Baz", null),
        (Lifetime.Singleton, "N.IFoo", "N.Foo"),
        (Lifetime.Scoped, "N.IBar", "N.Bar"),
        (Lifetime.Transient, "N.Foo", "N.Foo")
    ];

    // The wrappers declared in wrapperNamespace, the namespace Microsoft's guidance gives Add{Group} methods among
    // them; the callers' import of W becomes a comment there, so every line keeps its number.
    private static (string Path, string Source)[] UniqueWiring(string wrapperNamespace)
    {
        string wrappers = UniqueWrappers.Replace("namespace W;", $"namespace {wrapperNamespace};", StringComparison.Ordinal);
        string callers = wrapperNamespace == "W"
            ? UniqueCallers
            : UniqueCallers.Replace("using W;", "// the wrappers share the collection's namespace", StringComparison.Ordinal);
        return [("Wrappers.cs", wrappers), ("Root.cs", callers)];
    }

    [Theory]
    [InlineData("W")]
    [InlineData(DiNamespace)]
    public void Recognize_GenericWrapperOverDescriptor_RecordsEachCallsRegistrationAtTheCall(string wrapperNamespace)
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(UniqueWiring(wrapperNamespace));

        // The call passing a lifetime that is not a literal records nothing; the wrapper bodies record nothing of
        // their own, since every registration there reads a type or lifetime parameter.
        Registrations(model)
            .ShouldBe(UniqueRegistrations);
        model.Registration(Lifetime.Singleton, "N.IFoo", "N.Foo")
            .Sites.Rendered()
            .ShouldBe(["Root.cs:15"]);
    }

    [Theory]
    [InlineData(false, "W")]
    [InlineData(true, "W")]
    [InlineData(false, DiNamespace)]
    [InlineData(true, DiNamespace)]
    public void Recognize_GenericWrapperInAnotherProject_IsExpandedAtTheMerge(bool referencedAsImage, string wrapperNamespace)
    {
        (string Path, string Source)[] wiring = UniqueWiring(wrapperNamespace);
        CompilationInput wrappers = CompilationFactory.CompileWithDi("Wrappers", wiring[0]);
        CompilationInput callers = CompilationFactory.CompileWithDiReferencing(
            "App", wrappers.Compilation, "Wrappers", referencedAsImage, wiring[1]);

        CodebaseModel model = Harness.Extract([wrappers, callers]);

        Registrations(model)
            .ShouldBe(UniqueRegistrations);
    }

    [Fact]
    public void Recognize_NonGenericLifetimeWrapperInAnotherProject_IsReachedByItsLifetimeArgument()
    {
        // With no type arguments, a call to a wrapper declared elsewhere is told apart by its lifetime argument:
        // a literal at the call, spelled through the type's name or an alias of it, or the calling wrapper's own
        // lifetime parameter passed on, whose default applies.
        CompilationInput wrappers = CompilationFactory.CompileWithDi("Wrappers", ("Clock.cs", """
                                                                                              using Microsoft.Extensions.DependencyInjection;
                                                                                              namespace W;
                                                                                              public class Clock {}
                                                                                              public static class ClockExtensions
                                                                                              {
                                                                                                  public static IServiceCollection AddClock(this IServiceCollection services, ServiceLifetime lifetime)
                                                                                                  {
                                                                                                      services.Add(ServiceDescriptor.Describe(typeof(Clock), typeof(Clock), lifetime));
                                                                                                      return services;
                                                                                                  }
                                                                                              }
                                                                                              """));
        CompilationInput callers = CompilationFactory.CompileWithDiReferencing(
            "App", wrappers.Compilation, "Wrappers", asImage: true, ("Root.cs", """
                                                                                using Microsoft.Extensions.DependencyInjection;
                                                                                using W;
                                                                                using Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime;
                                                                                namespace N;
                                                                                public static class Root
                                                                                {
                                                                                    public static IServiceCollection AddScopedClock(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped) =>
                                                                                        services.AddClock(lifetime);

                                                                                    public static void Configure(IServiceCollection services)
                                                                                    {
                                                                                        services.AddClock(ServiceLifetime.Transient);
                                                                                        services.AddScopedClock();
                                                                                        services.AddClock(Lifetime.Singleton);
                                                                                    }
                                                                                }
                                                                                """));

        CodebaseModel model = Harness.Extract([wrappers, callers]);

        Registrations(model)
            .ShouldBe([
                (Lifetime.Singleton, "W.Clock", "W.Clock"),
                (Lifetime.Scoped, "W.Clock", "W.Clock"),
                (Lifetime.Transient, "W.Clock", "W.Clock")
            ]);
    }

    [Fact]
    public void Recognize_NonGenericWrapperWhoseLifetimeParameterIsSpelledThroughAnAlias_IsExpandedAtItsCall()
    {
        // Nothing in the wrapper's declaration spells ServiceLifetime, so only its declared symbol says it takes one.
        // The tuple alias beside it names no type by a name at all, and must be read past.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          using Lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime;
                                                                          using Pair = (int, int);
                                                                          namespace N;
                                                                          public class Clock {}
                                                                          public static class ClockExtensions
                                                                          {
                                                                              public static IServiceCollection AddClock(this IServiceCollection services, Lifetime lifetime)
                                                                              {
                                                                                  services.Add(ServiceDescriptor.Describe(typeof(Clock), typeof(Clock), lifetime));
                                                                                  return services;
                                                                              }
                                                                          }
                                                                          public static class Root
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddClock(ServiceLifetime.Transient);
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBe([(Lifetime.Transient, "N.Clock", "N.Clock")]);
    }

    [Fact]
    public void Recognize_PartialWrappers_ReadTheBodyAndTheDefiningDeclarationsDefault()
    {
        // The body binds its type parameters to the implementing declaration, and a parameter's default is written on
        // the defining one, so a partial wrapper is read from both.
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public class Bar {}
                                                                          public static partial class Wiring
                                                                          {
                                                                              public static partial IServiceCollection AddThing<T>(this IServiceCollection services) where T : class;
                                                                              public static partial IServiceCollection AddWithDefault<T>(this IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Scoped) where T : class;
                                                                          }
                                                                          public static partial class Wiring
                                                                          {
                                                                              public static partial IServiceCollection AddThing<T>(this IServiceCollection services) where T : class
                                                                              {
                                                                                  services.AddSingleton<T>();
                                                                                  return services;
                                                                              }

                                                                              public static partial IServiceCollection AddWithDefault<T>(this IServiceCollection services, ServiceLifetime lifetime) where T : class
                                                                              {
                                                                                  services.Add(ServiceDescriptor.Describe(typeof(T), typeof(T), lifetime));
                                                                                  return services;
                                                                              }
                                                                          }
                                                                          public static class Root
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.AddThing<Foo>();
                                                                                  services.AddWithDefault<Bar>();
                                                                              }
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBe([(Lifetime.Singleton, "N.Foo", "N.Foo"), (Lifetime.Scoped, "N.Bar", "N.Bar")]);
    }

    [Fact]
    public void Recognize_WrappersInFileLocalTypesSharingAName_AreEachExpandedAtTheirCalls()
    {
        // Two file-local types share a full name, and the model keeps one type by it; each still declares its own
        // wrapper.
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("First.cs", FileLocalWiring("AddFirst", "Foo", "AddSingleton")),
            ("Second.cs", FileLocalWiring("AddSecond", "Bar", "AddScoped")));

        Registrations(model)
            .ShouldBe([(Lifetime.Singleton, "N.Foo", "N.Foo"), (Lifetime.Scoped, "N.Bar", "N.Bar")]);
    }

    private static string FileLocalWiring(string wrapper, string registered, string registration)
    {
        return $$"""
                 using Microsoft.Extensions.DependencyInjection;
                 namespace N;
                 public class {{registered}} {}
                 file static class Wiring
                 {
                     public static IServiceCollection {{wrapper}}<T>(this IServiceCollection services) where T : class
                     {
                         services.{{registration}}<T>();
                         return services;
                     }
                 }
                 public static class {{registered}}Root
                 {
                     public static void Configure(IServiceCollection services) => services.{{wrapper}}<{{registered}}>();
                 }
                 """;
    }

    // One project built for two frameworks, whose wrapper registers a different lifetime under each. A registration
    // made directly under the same #if is recorded for the frameworks that compile it, and so is each framework's
    // reading of the wrapper at that framework's own calls.
    private static string FrameworkWiring(string calls)
    {
        return $$"""
                 using Microsoft.Extensions.DependencyInjection;
                 namespace N;
                 public class Foo {}
                 public static class Wiring
                 {
                     public static IServiceCollection AddFoo<T>(this IServiceCollection services) where T : class
                     {
                 #if NET8_0
                         services.AddSingleton<T>();
                 #else
                         services.AddScoped<T>();
                 #endif
                         return services;
                     }
                 }
                 public static class Root
                 {
                     public static void Configure(IServiceCollection services)
                     {
                 {{calls}}
                     }
                 }
                 """;
    }

    [Theory]
    [InlineData("        services.AddFoo<Foo>();", true)]
    [InlineData("#if NET8_0\n        services.AddFoo<Foo>();\n#endif", false)]
    public void Recognize_WrapperOfAMultiTargetedProject_IsExpandedThroughTheTemplateOfEachCallsFramework(string calls, bool calledOnBoth)
    {
        string source = FrameworkWiring(calls);
        CodebaseModel model = Harness.Extract([FrameworkOfWiring("net8.0", source, "NET8_0"), FrameworkOfWiring("net48", source)]);

        (Lifetime Lifetime, string Service, string? Implementation)[] expected = calledOnBoth
            ? [(Lifetime.Singleton, "N.Foo", "N.Foo"), (Lifetime.Scoped, "N.Foo", "N.Foo")]
            : [(Lifetime.Singleton, "N.Foo", "N.Foo")];
        Registrations(model)
            .ShouldBe(expected);
    }

    [Fact]
    public void Recognize_SingleFrameworkCallerOfAMultiTargetedWrapper_IsExpandedThroughEveryFrameworksTemplate()
    {
        // The caller builds for one framework and the wrapper's project for two, and which of them the caller compiles
        // against is not recorded, so both readings count.
        string wiring = FrameworkWiring("");
        CompilationInput modern = FrameworkOfWiring("net8.0", wiring, "NET8_0");
        CompilationInput callers = CompilationFactory.CompileWithDiReferencing(
            "App", modern.Compilation, "Shared", asImage: false, ("App.cs", """
                                                                            using Microsoft.Extensions.DependencyInjection;
                                                                            using N;
                                                                            namespace App;
                                                                            public static class AppRoot
                                                                            {
                                                                                public static void Configure(IServiceCollection services) => services.AddFoo<Foo>();
                                                                            }
                                                                            """));

        CodebaseModel model = Harness.Extract([modern, FrameworkOfWiring("net48", wiring), callers]);

        Registrations(model)
            .ShouldBe([(Lifetime.Singleton, "N.Foo", "N.Foo"), (Lifetime.Scoped, "N.Foo", "N.Foo")]);
    }

    private static CompilationInput FrameworkOfWiring(string targetFramework, string source, params string[] preprocessorSymbols)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source, CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols), "Wiring.cs");
        var compilation = CSharpCompilation.Create(
            "Shared", [tree], TrustedPlatform.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new CompilationInput(compilation, "Shared", [], targetFramework);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_WrapperReachedThroughASiblingsCompiledAssembly_IsExpandedAtTheCall()
    {
        // App references Library's built assembly by its path rather than as a project, as a HintPath reference does,
        // so the wrapper reaches App as metadata, in the collection's own namespace. A project of this load produces
        // its assembly, which is what tells it from the framework's methods there.
        (string Path, string Source)[] wiring = UniqueWiring(DiNamespace);
        MetadataReference libraryAssembly = CompilationFactory.ImageOf(CompilationFactory.CompileWithDi("Library", wiring[0]).Compilation);
        using var workspace = new AdhocWorkspace();
        var libraryId = ProjectId.CreateNewId();
        var appId = ProjectId.CreateNewId();
        Solution solution = workspace.CurrentSolution
            .AddProject(libraryId, "Library", "Library", LanguageNames.CSharp)
            .AddProject(appId, "App", "App", LanguageNames.CSharp)
            .WithProjectCompilationOptions(libraryId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithProjectCompilationOptions(appId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(libraryId, TrustedPlatform.References)
            .AddMetadataReferences(appId, [.. TrustedPlatform.References, libraryAssembly])
            .AddDocument(DocumentId.CreateNewId(libraryId), wiring[0].Path, SourceText.From(wiring[0].Source), filePath: wiring[0].Path)
            .AddDocument(DocumentId.CreateNewId(appId), wiring[1].Path, SourceText.From(wiring[1].Source), filePath: wiring[1].Path);

        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(solution, ct: Ct);

        Registrations(model)
            .ShouldBe(UniqueRegistrations);
    }

    // ── Builder wrappers, which reach the collection through a property ───────────────────────────────────

    // One builder exposes Services on an interface, the other inherits it from a base class. Each generic extension
    // forwards to an internal collection wrapper that holds a descriptor subclass, declared in a file of its own,
    // in a local before handing it over.
    private const string HandlerDescriptor = """
                                             using System;
                                             using Microsoft.Extensions.DependencyInjection;
                                             namespace W;
                                             public sealed class UniqueDescriptor : ServiceDescriptor
                                             {
                                                 public UniqueDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
                                                     : base(serviceType, implementationType, lifetime) {}
                                             }
                                             """;

    private const string HandlerBuilders = """
                                           using Microsoft.Extensions.DependencyInjection;
                                           namespace W;
                                           public interface IHandler<T> {}
                                           public interface IMyBuilder { IServiceCollection Services { get; } }
                                           public abstract class BuilderBase { public IServiceCollection Services { get; } = new ServiceCollection(); }
                                           public sealed class AppBuilder : BuilderBase {}
                                           public static class BuilderExtensions
                                           {
                                               public static IMyBuilder AddHandler<TMessage, THandler>(this IMyBuilder builder) where THandler : IHandler<TMessage>
                                               {
                                                   builder.Services.AddHandler<TMessage, THandler>();
                                                   return builder;
                                               }

                                               public static AppBuilder AddHandler<TMessage, THandler>(this AppBuilder builder) where THandler : IHandler<TMessage>
                                               {
                                                   builder.Services.AddHandler<TMessage, THandler>();
                                                   return builder;
                                               }

                                               internal static IServiceCollection AddHandler<TMessage, THandler>(this IServiceCollection services) where THandler : IHandler<TMessage>
                                               {
                                                   var descriptor = new UniqueDescriptor(typeof(IHandler<TMessage>), typeof(THandler), ServiceLifetime.Transient);
                                                   if (!services.Contains(descriptor)) services.Add(descriptor);
                                                   return services;
                                               }
                                           }
                                           """;

    private const string HandlerCallers = """
                                          using W;
                                          namespace N;
                                          public class Ping {}
                                          public class PingHandler : IHandler<Ping> {}
                                          public class Pong {}
                                          public class PongHandler : IHandler<Pong> {}
                                          public static class Root
                                          {
                                              public static void Configure(IMyBuilder builder, AppBuilder app)
                                              {
                                                  builder.AddHandler<Ping, PingHandler>();
                                                  app.AddHandler<Pong, PongHandler>();
                                              }
                                          }
                                          """;

    private static readonly (Lifetime Lifetime, string Service, string? Implementation)[] HandlerRegistrations =
    [
        (Lifetime.Transient, "W.IHandler<T>", "N.PingHandler"),
        (Lifetime.Transient, "W.IHandler<T>", "N.PongHandler")
    ];

    [Fact]
    public void Recognize_BuilderWrapperOverAHeldSubclassDescriptor_RecordsEachHandlerAtTheBuilderCall()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(
            ("Descriptor.cs", HandlerDescriptor), ("Builders.cs", HandlerBuilders), ("Root.cs", HandlerCallers));

        // The service is the handler interface at definition level, named by its own type parameter.
        Registrations(model)
            .ShouldBe(HandlerRegistrations);
        model.Registration(Lifetime.Transient, "W.IHandler<T>", "N.PingHandler")
            .Sites.Rendered()
            .ShouldBe(["Root.cs:11"]);
        model.Registration(Lifetime.Transient, "W.IHandler<T>", "N.PongHandler")
            .Sites.Rendered()
            .ShouldBe(["Root.cs:12"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recognize_BuilderWrapperInAnotherProject_IsExpandedAtTheMerge(bool referencedAsImage)
    {
        CompilationInput wrappers = CompilationFactory.CompileWithDi(
            "Wrappers", ("Descriptor.cs", HandlerDescriptor), ("Builders.cs", HandlerBuilders));
        CompilationInput callers = CompilationFactory.CompileWithDiReferencing(
            "App", wrappers.Compilation, "Wrappers", referencedAsImage, ("Root.cs", HandlerCallers));

        CodebaseModel model = Harness.Extract([wrappers, callers]);

        Registrations(model)
            .ShouldBe(HandlerRegistrations);
    }

    [Fact]
    public void Recognize_WrapperCompiledIntoAPackage_RecordsNothing()
    {
        // The wrapper's assembly is referenced but is not a project of the solution, so no template reaches the
        // merge, and a call to a wrapper with no syntax tree to walk expands to nothing.
        CompilationInput wrappers = CompilationFactory.CompileWithDi("Wrappers", ("Wrappers.cs", UniqueWrappers));
        CSharpCompilation callers = CompilationFactory.CreateCompilation(
            "App", [.. TrustedPlatform.References, CompilationFactory.ImageOf(wrappers.Compilation)], ("Root.cs", UniqueCallers));

        CodebaseModel model = Harness.Extract([new CompilationInput(callers, "App", [])]);

        Registrations(model)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Recognize_WrappersForwardingInACycle_RecordEachRegistrationOnce()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public class Foo {}
                                                                          public static class Cycle
                                                                          {
                                                                              public static IServiceCollection AddPing<T>(this IServiceCollection services) where T : class
                                                                              {
                                                                                  services.AddSingleton<T>();
                                                                                  return services.AddPong<T>();
                                                                              }

                                                                              public static IServiceCollection AddPong<T>(this IServiceCollection services) where T : class =>
                                                                                  services.AddPing<T>();
                                                                          }
                                                                          public static class Root
                                                                          {
                                                                              public static void Configure(IServiceCollection services) => services.AddPing<Foo>();
                                                                          }
                                                                          """));

        Registrations(model)
            .ShouldBe([(Lifetime.Singleton, "N.Foo", "N.Foo")]);
    }

    // ── Whole-compilation reach (top-level Program) ───────────────────────────────────────────────────────

    [Fact]
    public void Recognize_RegistrationInTopLevelProgram_IsSeen()
    {
        // A top-level-statements Program is not a declared type, so this only holds because the registration
        // pass walks every syntax tree, not each declared type.
        CodebaseModel model = CompilationFactory.ExtractConsoleAppWithDi(("Program.cs", """
                                                                                        using Microsoft.Extensions.DependencyInjection;
                                                                                        var services = new ServiceCollection();
                                                                                        services.AddSingleton<N.IFoo, N.Foo>();

                                                                                        namespace N
                                                                                        {
                                                                                            public interface IFoo {}
                                                                                            public class Foo : IFoo {}
                                                                                        }
                                                                                        """));

        Registrations(model)
            .ShouldContain((Lifetime.Singleton, "N.IFoo", "N.Foo"));
    }

    // ── Sites ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recognize_RegistrationSite_IsTheCallLine()
    {
        CodebaseModel model = CompilationFactory.ExtractWithDi(("Reg.cs", """
                                                                          using Microsoft.Extensions.DependencyInjection;
                                                                          namespace N;
                                                                          public interface IFoo {}
                                                                          public class Foo : IFoo {}
                                                                          public static class Reg
                                                                          {
                                                                              public static void Configure(IServiceCollection services)
                                                                              {
                                                                                  services.AddSingleton<IFoo, Foo>();
                                                                              }
                                                                          }
                                                                          """));

        model.Registration(Lifetime.Singleton, "N.IFoo", "N.Foo")
            .Sites.Lines()
            .ShouldBe([9]);
    }
}
