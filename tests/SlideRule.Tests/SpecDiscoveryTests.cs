using System.Reflection;
using Shouldly;
using Xunit;
using SlideRule.Architecture;
using SlideRule.Discovery;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests;

/// <summary>
///     In-assembly spec discovery (GRAMMAR §9): deterministic ordering by full name, a loud failure on
///     zero specs rather than a silent skip, and one refusal naming every spec class that cannot be created.
/// </summary>
public class SpecDiscoveryTests
{
    // Zulu takes an argument and Alpha's parameterless constructor is private, so neither can be created;
    // Creatable can, and would count it.
    private const string UncreatableSpecsSource =
        """
        using SlideRule;

        namespace NoCtorProbe
        {
            public sealed class Zulu : IArchitectureSpec
            {
                public Zulu(int unused)
                {
                }

                public void Define(Arch arch)
                {
                }
            }

            public sealed class Creatable : IArchitectureSpec
            {
                public static int Created;

                public Creatable()
                {
                    Created++;
                }

                public void Define(Arch arch)
                {
                }
            }

            public sealed class Alpha : IArchitectureSpec
            {
                private Alpha()
                {
                }

                public void Define(Arch arch)
                {
                }
            }
        }
        """;

    [Fact]
    public void FindSpecs_OrdersByFullNameOrdinal()
    {
        List<string?> names = SpecDiscovery.FindSpecs(typeof(ArchitectureSpec).Assembly)
            .Select(spec => spec.GetType()
                .FullName)
            .ToList();

        names.ShouldBe(names.OrderBy(name => name, StringComparer.Ordinal)
            .ToList());
    }

    [Fact]
    public void FindSpecs_DiscoversPublicCanonicalSpec()
    {
        SpecDiscovery.FindSpecs(typeof(ArchitectureSpec).Assembly)
            .ShouldContain(spec => spec is ArchitectureSpec);
    }

    [Fact]
    public void FindSpecs_PublicSpecNestedInPublicType_IsDiscovered()
    {
        // Type.IsPublic is false for ANY nested type, so a predicate built on it silently skips a spec
        // nested in a public class; Type.IsVisible is true when the whole containing chain is public. This
        // spec — public, nested through a public chain — is the pin under that choice.
        SpecDiscovery.FindSpecs(typeof(PublicOuter.NestedSpec).Assembly)
            .ShouldContain(spec => spec is PublicOuter.NestedSpec);
    }

    [Fact]
    public void FindSpecs_NoSpecsInAssembly_ThrowsLoudly()
    {
        // The Core assembly declares no IArchitectureSpec — discovery must be loud, not silent.
        Should.Throw<SpecDiscoveryException>(() => SpecDiscovery.FindSpecs(typeof(Arch).Assembly));
    }

    [Fact]
    public void FindSpecs_ClassesWithoutAParameterlessConstructor_CreatesNoneAndNamesEachInOrder()
    {
        // Arrange: emitted, never declared here, because the cases above scan this assembly. The classes are
        // declared out of order, and one of the three can be created and counts its creations.
        byte[] image = SpecAssemblyCompiler.EmitImage(UncreatableSpecsSource, "SlideRule.UncreatableSpecs");
        Assembly assembly = Assembly.Load(image);

        // Act
        var thrown = Should.Throw<SpecDiscoveryException>(() => SpecDiscovery.FindSpecs(assembly));

        // Assert
        thrown.Message.ShouldBe(
            "Spec classes 'NoCtorProbe.Alpha' and 'NoCtorProbe.Zulu' have no public parameterless constructor. "
            + "SlideRule creates one instance of each public IArchitectureSpec class it finds, so declare a "
            + "public constructor that takes no arguments on each, or make a class abstract if it is a base for "
            + "other specs.");
        assembly.GetType("NoCtorProbe.Creatable", throwOnError: true)!
            .GetField("Created")!
            .GetValue(null)
            .ShouldBe(0);
    }

    [Fact]
    public void FindSpecs_NeverReachesIntoReferencedAssemblies()
    {
        Assembly assembly = typeof(ArchitectureSpec).Assembly;

        // This project references the repo's own arch-spec project, which declares a public
        // IArchitectureSpec — and a rule pack, whose whole point is contributing rules. Discovery stays
        // inside the assembly it was handed either way: nothing a spec references can inject a rule the
        // spec did not ask for, which is what keeps rule packs plain libraries rather than a
        // conventions layer.
        SpecDiscovery.FindSpecs(assembly)
            .ShouldAllBe(spec => spec.GetType()
                .Assembly == assembly);
        typeof(SlideRuleArchitectureSpec).Assembly.ShouldNotBe(assembly);
    }

    public static class PublicOuter
    {
        public sealed class NestedSpec : IArchitectureSpec
        {
            public void Define(Arch arch)
            {
                arch.Rule("discovery/nested-visible")
                    .Enforce(arch.Types.MustHavePrefix("I"))
                    .Because("Public specs nested in public types are discoverable via Type.IsVisible.");
            }
        }
    }
}
