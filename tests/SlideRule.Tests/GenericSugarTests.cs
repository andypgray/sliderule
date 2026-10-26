using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Generic-type sugar (GRAMMAR §5.2/§5.3): <c>arch.Type&lt;T&gt;()</c>, the
///     <c>Implementing</c>/<c>DerivedFrom</c>/<c>AttributedWith</c> adjective twins, and the
///     <c>MustImplement</c>/<c>MustDeriveFrom</c>/<c>MustBeAttributedWith</c> constraint twins each reify
///     identically to their <c>typeof</c> counterpart: the same model-file text and the same sentence. A
///     <c>[Fact]</c> per family: type arguments are compile-time, so they cannot be theory data. (Open
///     generics stay <c>typeof</c> — inexpressible as a type argument — and are out of scope by design.)
/// </summary>
public class GenericSugarTests
{
    [Fact]
    public void TypeSugar_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Type<SugarType>().MustBeSealed(),
            arch => arch.Type(typeof(SugarType)).MustBeSealed());
    }

    [Fact]
    public void Implementing_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.Implementing<ISugarPort>().MustBeSealed(),
            arch => arch.Types.Implementing(typeof(ISugarPort)).MustBeSealed());
    }

    [Fact]
    public void DerivedFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.DerivedFrom<SugarBase>().MustBeSealed(),
            arch => arch.Types.DerivedFrom(typeof(SugarBase)).MustBeSealed());
    }

    [Fact]
    public void AttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.AttributedWith<SugarAttribute>().MustBeSealed(),
            arch => arch.Types.AttributedWith(typeof(SugarAttribute)).MustBeSealed());
    }

    [Fact]
    public void MustImplement_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustImplement<ISugarPort>(),
            arch => arch.Types.MustImplement(typeof(ISugarPort)));
    }

    [Fact]
    public void MustDeriveFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustDeriveFrom<SugarBase>(),
            arch => arch.Types.MustDeriveFrom(typeof(SugarBase)));
    }

    [Fact]
    public void MustBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustBeAttributedWith<SugarAttribute>(),
            arch => arch.Types.MustBeAttributedWith(typeof(SugarAttribute)));
    }

    [Fact]
    public void MustNotImplement_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotImplement<ISugarPort>(),
            arch => arch.Types.MustNotImplement(typeof(ISugarPort)));
    }

    [Fact]
    public void MustNotDeriveFrom_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotDeriveFrom<SugarBase>(),
            arch => arch.Types.MustNotDeriveFrom(typeof(SugarBase)));
    }

    [Fact]
    public void MustNotBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotBeAttributedWith<SugarAttribute>(),
            arch => arch.Types.MustNotBeAttributedWith(typeof(SugarAttribute)));
    }

    // ---- The member attribute axis (GRAMMAR §5.7): one receiver-typed overload per member selection, each
    //      keeping its own kind-only verbs reachable after the sugar ----

    [Fact]
    public void MemberAttributedWith_GenericOnMethodSelection_KeepsReturningReachable()
    {
        // The MethodSelection overload returns a MethodSelection, so `.Returning` chains off the sugar —
        // this would not compile against the MemberSelection overload alone.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Methods.AttributedWith<SugarAttribute>()
                .Returning(typeof(Task))
                .MustBePublic(),
            arch => arch.Types.Methods.AttributedWith(typeof(SugarAttribute))
                .Returning(typeof(Task))
                .MustBePublic());
    }

    [Fact]
    public void MemberAttributedWith_GenericOnPropertySelection_KeepsMustBeGetOnlyReachable()
    {
        // The PropertySelection overload returns a PropertySelection, so the properties-only verb chains off
        // the sugar — this would not compile against the MemberSelection overload alone.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Properties.AttributedWith<SugarAttribute>()
                .MustBeGetOnly(),
            arch => arch.Types.Properties.AttributedWith(typeof(SugarAttribute))
                .MustBeGetOnly());
    }

    [Fact]
    public void MemberAttributedWith_GenericOnFieldSelection_KeepsMustBeReadonlyReachable()
    {
        // The FieldSelection twin of the row above — the fields-only verb after the sugar.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Fields.AttributedWith<SugarAttribute>()
                .MustBeReadonly(),
            arch => arch.Types.Fields.AttributedWith(typeof(SugarAttribute))
                .MustBeReadonly());
    }

    [Fact]
    public void MemberAttributedWith_GenericOnMembers_ServesTheKindOnlyReceiver()
    {
        // .Members is typed MemberSelection — its KindMemberSelection is internal, so no per-kind overload
        // can name it and the MemberSelection overload is the only one that binds, so a receiver typed
        // MemberSelection is what reaches that overload.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Members.AttributedWith<SugarAttribute>()
                .MustBePublic(),
            arch => arch.Types.Members.AttributedWith(typeof(SugarAttribute))
                .MustBePublic());
    }

    [Fact]
    public void MemberAttributedWith_GenericOnEvents_ServesTheKindOnlyReceiver()
    {
        // .Events is the other MemberSelection-typed projection — same overload, a different kind filter
        // riding underneath it.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Events.AttributedWith<SugarAttribute>()
                .MustBePublic(),
            arch => arch.Types.Events.AttributedWith(typeof(SugarAttribute))
                .MustBePublic());
    }

    [Fact]
    public void MemberMustBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.Methods.MustBeAttributedWith<SugarAttribute>(),
            arch => arch.Types.Methods.MustBeAttributedWith(typeof(SugarAttribute)));
    }

    [Fact]
    public void MemberMustNotBeAttributedWith_Generic_ReifiesIdenticallyToTypeof()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.Methods.MustNotBeAttributedWith<SugarAttribute>(),
            arch => arch.Types.Methods.MustNotBeAttributedWith(typeof(SugarAttribute)));
    }

    [Fact]
    public void AnyOf_MultiTypeSugar_ReifiesIdenticallyToWrappedSelections()
    {
        // arch.AnyOf(typeof(A), typeof(B)) ≡ arch.AnyOf(arch.Type(a), arch.Type(b)) — the multi-type noun
        // that arch.Types(params Type[]) cannot be (CS0102 against the arch.Types property).
        Checker.ShouldReifyAlike(
            arch => arch.AnyOf(typeof(SugarType), typeof(SugarBase)).MustBeSealed(),
            arch => arch.AnyOf(arch.Type<SugarType>(), arch.Type<SugarBase>()).MustBeSealed());
    }

    [Fact]
    public void Except_TypeSugar_ReifiesIdenticallyToWrappedSelections()
    {
        // .Except(typeof(A)) ≡ .Except(arch.Type(a)); several types are the union arch.AnyOf would mint, so
        // the multi-type form is the AnyOf spelling written for the caller.
        Checker.ShouldReifyAlike(
            arch => arch.Types.Except(typeof(SugarType)).MustBeSealed(),
            arch => arch.Types.Except(arch.Type(typeof(SugarType))).MustBeSealed());
        Checker.ShouldReifyAlike(
            arch => arch.Types.Except(typeof(SugarType), typeof(SugarBase)).MustBeSealed(),
            arch => arch.Types.Except(arch.AnyOf(typeof(SugarType), typeof(SugarBase))).MustBeSealed());
    }

    // Local reification markers — the sugar-equality tests only need a non-generic interface, base class,
    // and attribute; the checker never runs here.
    private interface ISugarPort;

    private abstract class SugarBase;

    private sealed class SugarAttribute : Attribute;

    private sealed class SugarType;
}
