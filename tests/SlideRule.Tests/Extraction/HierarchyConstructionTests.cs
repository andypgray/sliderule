using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The construction-preserving hierarchy facts over the fast path:
///     <see cref="TypeNode.AllInterfaces" /> (transitive, substituted closure),
///     <see cref="TypeNode.BaseTypeChain" /> (nearest-first, object terminus), and
///     <see cref="TypeNode.AttributeConstructions" /> (declared only) and <see cref="TypeNode.InheritedAttributes" />
///     (what the base chain passes down). The checker's <c>typeof(...)</c> matching depends on each of these
///     keeping a construction's definition FullName apart from its constructed one.
/// </summary>
public sealed class HierarchyConstructionTests
{
    private const string T = "SlideRule.Tests.Checking.Targets.";

    private static readonly CodebaseModel InheritedModel = CompilationFactory.Extract(Sources.InheritedAttributes);

    // Mark has no usage, PinnedChild has none of its own (its base's Inherited = false is not read), and Stamp's
    // usage leaves Inherited unset, so all three pass down; Pinned does not, and Stamp, written twice, is listed
    // once. The leaf reads two levels up, and the base itself inherits nothing from System.Object.
    [Fact]
    public void InheritedAttributes_TwoLevelsBelowTheBase_ListEachInheritableAttributeOnce()
    {
        InheritedModel.Type($"{T}CarryLeaf")
            .InheritedAttributes.ConstructedNames()
            .ShouldBe([$"{T}BrandAttribute<System.Int32>", $"{T}MarkAttribute", $"{T}PinnedChildAttribute", $"{T}StampAttribute"]);

        InheritedModel.Type($"{T}CarryBase")
            .InheritedAttributes.ShouldBeEmpty();
    }

    [Fact]
    public void InheritedAttributes_AnAttributeTwoBasesDeclare_IsListedOnce()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class MarkAttribute : Attribute {}
                                                         [Mark] public class Root {}
                                                         [Mark] public class Middle : Root {}
                                                         public class Leaf : Middle {}
                                                         """);

        model.Type("N.Leaf")
            .InheritedAttributes.ConstructedNames()
            .ShouldBe(["N.MarkAttribute"]);
    }

    [Fact]
    public void InheritedAttributes_GenericAttributeOnABase_KeepsTheDefinitionAndTheConstruction()
    {
        AttributeNode brand = InheritedModel.Type($"{T}CarryDerived")
            .InheritedAttributes.ShouldHaveSingle(a => a.DefinitionFullName.StartsWith($"{T}Brand", StringComparison.Ordinal),
                a => a.FullName);
        brand.DefinitionFullName.ShouldBe($"{T}BrandAttribute<T>");
        brand.FullName.ShouldBe($"{T}BrandAttribute<System.Int32>");
    }

    // A framework base passes its own attributes down: List<T> declares [DebuggerDisplay] with the default usage
    // and [TypeForwardedFrom], whose usage sets Inherited = false.
    [Fact]
    public void InheritedAttributes_MetadataBase_ReadsTheBasesOwnUsage()
    {
        IReadOnlyList<string> inherited = InheritedModel.Type($"{T}Bag")
            .InheritedAttributes.ConstructedNames();

        inherited.ShouldContain("System.Diagnostics.DebuggerDisplayAttribute");
        inherited.ShouldNotContain("System.Runtime.CompilerServices.TypeForwardedFromAttribute");
    }

    [Fact]
    public void InheritedAttributes_AttributedInterface_PassesNothingToItsImplementer()
    {
        InheritedModel.Type($"{T}ViaInterface")
            .InheritedAttributes.ShouldBeEmpty();
        InheritedModel.Type($"{T}IMarked")
            .InheritedAttributes.ShouldBeEmpty();
    }

    // The fact is recorded by name and mints nothing, so an attribute class only a base wears, here the
    // System.AttributeUsageAttribute every attribute class inherits from System.Attribute, is no external of the
    // fragment, which the cache persists and the one-project view sorts into side fragments.
    [Fact]
    public void InheritedAttributes_MintNoExternal()
    {
        CompilationInput input = CompilationFactory.Compile("TestProject", """
                                                                           using System;
                                                                           namespace N;
                                                                           public sealed class MarkAttribute : Attribute {}
                                                                           """);

        CodebaseFragment fragment = FragmentExtractor.Extract(input, Ct);

        FragmentMerger.Merge([fragment])
            .Type("N.MarkAttribute")
            .InheritedAttributes.ConstructedNames()
            .ShouldBe(["System.AttributeUsageAttribute"]);
        fragment.Externals.Select(e => e.Facts.FullName)
            .ShouldNotContain("System.AttributeUsageAttribute");
    }

    [Fact]
    public void AllInterfaces_SubstitutedTransitivityThroughGenericBase_ContainsConstructedInterface()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class HandlerBase<T> : IHandler<T> {}
                                                         public class Order {}
                                                         public class MyHandler : HandlerBase<Order> {}
                                                         """);

        TypeConstruction handler = model.Type("N.MyHandler")
            .AllInterfaces
            .ShouldHaveSingle(c => c.Definition.FullName == "N.IHandler<T>", c => c.FullName);
        handler.FullName.ShouldBe("N.IHandler<N.Order>");
    }

    [Fact]
    public void AllInterfaces_InterfaceExtendsInterface_IncludesTransitiveClosureOrdinalOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IA {}
                                                         public interface IB : IA {}
                                                         public class C : IB {}
                                                         """);

        model.Type("N.C")
            .AllInterfaces.Select(c => c.FullName)
            .ShouldBe(["N.IA", "N.IB"]);
    }

    [Fact]
    public void AllInterfaces_ConstructedWithPrimitiveArgument_RendersSystemInt32NotKeyword()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class IntHandler : IHandler<int> {}
                                                         """);

        TypeConstruction handler = model.Type("N.IntHandler")
            .AllInterfaces.ShouldHaveSingleItem();
        handler.Definition.FullName.ShouldBe("N.IHandler<T>");
        handler.FullName.ShouldBe("N.IHandler<System.Int32>");
    }

    [Fact]
    public void AllInterfaces_MultipleInterfaces_OrderedOrdinalByConstructedFullName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IZebra {}
                                                         public interface IApple {}
                                                         public class C : IZebra, IApple {}
                                                         """);

        model.Type("N.C")
            .AllInterfaces.Select(c => c.FullName)
            .ShouldBe(["N.IApple", "N.IZebra"]);
    }

    // The chain is nearest first and ends at object for every kind that has one; an interface has none.
    [Theory]
    [InlineData("public class A {} public class B : A {} public class D : B {}", "N.D", "N.B", "N.A", "System.Object")]
    [InlineData("public struct S {}", "N.S", "System.ValueType", "System.Object")]
    [InlineData("public enum E { A, B }", "N.E", "System.Enum", "System.ValueType", "System.Object")]
    [InlineData("public delegate void D();", "N.D", "System.MulticastDelegate", "System.Delegate", "System.Object")]
    [InlineData("public interface I {}", "N.I")]
    public void BaseTypeChain_EachKind_IsNearestFirstToObject(
        string declarations, string fullName, params string[] expected)
    {
        CodebaseModel model = CompilationFactory.Extract($"namespace N;\n{declarations}");

        model.Type(fullName)
            .BaseTypeChain.Select(c => c.FullName)
            .ShouldBe(expected);
    }

    [Fact]
    public void BaseTypeChain_SubstitutedGenericBase_KeepsConstructedNameAndOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class HandlerBase<T> {}
                                                         public class Order {}
                                                         public class MyHandler : HandlerBase<Order> {}
                                                         """);

        TypeConstruction nearest = model.Type("N.MyHandler")
            .BaseTypeChain[0];
        nearest.Definition.FullName.ShouldBe("N.HandlerBase<T>");
        nearest.FullName.ShouldBe("N.HandlerBase<N.Order>");
    }

    [Fact]
    public void AttributeConstructions_DeclaredAttributes_OrderedOrdinalByConstructedFullName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class ZedAttribute : Attribute {}
                                                         public sealed class AbleAttribute : Attribute {}
                                                         [Zed]
                                                         [Able]
                                                         public class C {}
                                                         """);

        model.Type("N.C")
            .AttributeConstructions.Select(c => c.FullName)
            .ShouldBe(["N.AbleAttribute", "N.ZedAttribute"]);
    }

    [Fact]
    public void AttributeConstructions_GenericAttribute_RendersConstructedName()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public sealed class TagAttribute<T> : Attribute {}
                                                         [Tag<int>]
                                                         public class C {}
                                                         """);

        TypeConstruction tag = model.Type("N.C")
            .AttributeConstructions.ShouldHaveSingleItem();
        tag.Definition.FullName.ShouldBe("N.TagAttribute<T>");
        tag.FullName.ShouldBe("N.TagAttribute<System.Int32>");
    }

    [Fact]
    public void HierarchyConstructions_ExternalNode_AreShallowlyEmpty()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Plain {}
                                                         """);

        TypeNode obj = model.Type("System.Object");
        obj.IsExternal.ShouldBeTrue();
        obj.AllInterfaces.ShouldBeEmpty();
        obj.BaseTypeChain.ShouldBeEmpty();
        obj.AttributeConstructions.ShouldBeEmpty();
        obj.InheritedAttributes.ShouldBeEmpty();
    }
}
