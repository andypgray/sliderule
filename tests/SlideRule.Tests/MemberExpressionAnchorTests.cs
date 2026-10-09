using System.Reflection;
using Shouldly;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The expression-based member anchors (GRAMMAR §4.5): <c>arch.Member&lt;T&gt;(x =&gt; x.M)</c> and
///     <c>arch.Member(() =&gt; Type.M)</c> desugar at mint to the same <see cref="Member" /> leaf as the
///     <c>typeof</c> + <c>nameof</c> form. Each test pins the anchor one expression shape resolves to,
///     through IVT reads of <c>DeclaringType</c>/<c>Name</c>/<c>IsMethod</c>.
/// </summary>
public class MemberExpressionAnchorTests
{
    [Fact]
    public void Member_InstanceMethodExpression_AnchorsDeclaringTypeAndName()
    {
        Member member = new Arch().Member<Task>(t => t.Wait());

        member.DeclaringType.ShouldBe(typeof(Task));
        member.Name.ShouldBe("Wait");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_InstancePropertyExpression_IsNotMethod()
    {
        Member member = new Arch().Member<AnchorWidget>(w => w.Count);

        member.DeclaringType.ShouldBe(typeof(AnchorWidget));
        member.Name.ShouldBe("Count");
        member.IsMethod.ShouldBeFalse();
    }

    [Fact]
    public void Member_FieldExpression_IsNotMethod()
    {
        Member member = new Arch().Member<AnchorWidget>(w => w.Size);

        member.DeclaringType.ShouldBe(typeof(AnchorWidget));
        member.Name.ShouldBe("Size");
        member.IsMethod.ShouldBeFalse();
    }

    [Fact]
    public void Member_StaticPropertyExpression_AnchorsViaParameterlessForm()
    {
        Member member = new Arch().Member(() => DateTime.Now);

        member.DeclaringType.ShouldBe(typeof(DateTime));
        member.Name.ShouldBe("Now");
        member.IsMethod.ShouldBeFalse();
    }

    [Fact]
    public void Member_StaticMethodExpression_AnchorsViaParameterlessForm()
    {
        // The Action form (void) and the Func<object?> form (value) both anchor the static method by name.
        Member action = new Arch().Member(() => GC.Collect());
        action.DeclaringType.ShouldBe(typeof(GC));
        action.Name.ShouldBe("Collect");
        action.IsMethod.ShouldBeTrue();

        Member func = new Arch().Member(() => Guid.NewGuid());
        func.DeclaringType.ShouldBe(typeof(Guid));
        func.Name.ShouldBe("NewGuid");
        func.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_ConstructedGenericReceiver_NormalizesToDefinition()
    {
        // Task<int>.Result → the definition Task<>, exactly like the typeof(Task<>) anchor (GRAMMAR §4.5).
        Member member = new Arch().Member<Task<int>>(t => t.Result);

        member.DeclaringType.ShouldBe(typeof(Task<>));
        member.Name.ShouldBe("Result");
        member.IsMethod.ShouldBeFalse();
    }

    [Fact]
    public void Member_InheritedMember_AnchorsDeclaringBaseType()
    {
        // Ping is declared on AnchorBase; the anchor is the DeclaringType, never the ReflectedType (AnchorDerived).
        Member member = new Arch().Member<AnchorDerived>(d => d.Ping());

        member.DeclaringType.ShouldBe(typeof(AnchorBase));
        member.Name.ShouldBe("Ping");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_ExtensionMethodCall_AnchorsDeclaringStaticClass()
    {
        // A reduced extension call `w.Doubled()` anchors the declaring static class — the §4.5 ReducedFrom parity.
        Member member = new Arch().Member<AnchorWidget>(w => w.Doubled());

        member.DeclaringType.ShouldBe(typeof(AnchorWidgetExtensions));
        member.Name.ShouldBe("Doubled");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_ValueTypeReceiver_UnwrapsBoxingConvert()
    {
        // A struct property boxed to object? by the Func<T,object?> overload: the body Convert is unwrapped
        // before classification (distinct from a receiver-side Convert).
        Member member = new Arch().Member<TimeSpan>(t => t.TotalDays);

        member.DeclaringType.ShouldBe(typeof(TimeSpan));
        member.Name.ShouldBe("TotalDays");
        member.IsMethod.ShouldBeFalse();
    }

    [Fact]
    public void Member_GenericMethodCall_AnchorsByNameCoveringAllInstantiations()
    {
        Member member = new Arch().Member<AnchorWidget>(w => w.Echo(0));

        member.DeclaringType.ShouldBe(typeof(AnchorWidget));
        member.Name.ShouldBe("Echo");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_ConvertWrappedInterfaceReceiver_AnchorsInterfaceMember()
    {
        // A receiver-side interface cast anchors the interface member — the §4.5 dispatch boundary (the
        // Convert on the receiver is peeled to find the parameter, but the tree's resolved method is
        // IAnchorReadable.Read). The (IAnchorReadable) cast is required: it moves the statically-resolved
        // method the expression tree records from the concrete AnchorReadable.Read to the interface, so it
        // must survive redundant-cast cleanup (redundant for runtime dispatch, not for this).
        // ReSharper disable once RedundantCast
        Member member = new Arch().Member<AnchorReadable>(r => ((IAnchorReadable)r).Read());

        member.DeclaringType.ShouldBe(typeof(IAnchorReadable));
        member.Name.ShouldBe("Read");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_OverloadCoveringCall_AnchorsNameNotSignature()
    {
        // The expression picks one overload (Over(int)), but the anchor is (declaring type, name) — one
        // ban covers every overload (GRAMMAR §4.5). There is no signature in the leaf.
        Member member = new Arch().Member<AnchorWidget>(w => w.Over(1));

        member.DeclaringType.ShouldBe(typeof(AnchorWidget));
        member.Name.ShouldBe("Over");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_ExpressionMinted_ReifiesIdenticallyToTypeofMinted()
    {
        var arch = new Arch();
        Member expression = arch.Member<Task>(t => t.Wait());
        Member typeofForm = arch.Member(typeof(Task), nameof(Task.Wait));

        expression.DeclaringType.ShouldBe(typeofForm.DeclaringType);
        expression.Name.ShouldBe(typeofForm.Name);
        expression.IsMethod.ShouldBe(typeofForm.IsMethod);
    }

    [Fact]
    public void Member_InstanceCreateDelegateInvocation_AnchorsMethodInfoCreateDelegate()
    {
        // A genuine invocation of MethodInfo.CreateDelegate — its argument is a typeof, not a MethodInfo
        // constant — is not a method-group lowering, so it anchors CreateDelegate itself.
        Member member = new Arch().Member<MethodInfo>(m => m.CreateDelegate(typeof(Action)));

        member.DeclaringType.ShouldBe(typeof(MethodInfo));
        member.Name.ShouldBe("CreateDelegate");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_StaticCreateDelegateInvocation_AnchorsDelegateCreateDelegate()
    {
        // A genuine invocation of Delegate.CreateDelegate passing a captured MethodInfo local (a closure
        // field, not a compiler-emitted constant) anchors CreateDelegate, not a method-group lowering.
        MethodInfo mi = typeof(AnchorBase).GetMethod(nameof(AnchorBase.Ping))!;
        Member member = new Arch().Member(() => Delegate.CreateDelegate(typeof(Action), mi));

        member.DeclaringType.ShouldBe(typeof(Delegate));
        member.Name.ShouldBe("CreateDelegate");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_AsCastReceiver_AnchorsInterfaceMember()
    {
        // An as-cast receiver is identity-preserving like the interface Convert: peeled to find the
        // parameter, but the tree's resolved method is IAnchorReadable.Read. The (r as IAnchorReadable)
        // cast is required — it moves the statically-resolved method to the interface — so it must
        // survive redundant-cast cleanup; the `!` guards the IAnchorReadable? receiver (CS8602).
        // ReSharper disable once RedundantCast
        // ReSharper disable once RedundantSuppressNullableWarningExpression
        Member member = new Arch().Member<AnchorReadable>(r => (r as IAnchorReadable)!.Read());

        member.DeclaringType.ShouldBe(typeof(IAnchorReadable));
        member.Name.ShouldBe("Read");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_StaticFormExtensionCall_AnchorsDeclaringStaticClass()
    {
        // An extension called in the static form `() => Ext.M(arg)` anchors the declaring static class like
        // any other static call — the reduced-form receiver redirect is gated on the instance form only. The
        // explicit static spelling is deliberate (it is the mirror of the reduced form below), so it must
        // survive reduce-to-extension-call cleanup.
        // ReSharper disable once InvokeAsExtensionMember
        Member member = new Arch().Member(() => AnchorWidgetExtensions.Doubled(new AnchorWidget()));

        member.DeclaringType.ShouldBe(typeof(AnchorWidgetExtensions));
        member.Name.ShouldBe("Doubled");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_StaticFormReducedExtensionCall_AnchorsDeclaringStaticClass()
    {
        // A reduced extension call in the static form `() => captured.M()` still lowers to `Ext.M(captured)`
        // and anchors the declaring static class — no redirect, since the redirect is instance-form-gated.
        var w = new AnchorWidget();
        Member member = new Arch().Member(() => w.Doubled());

        member.DeclaringType.ShouldBe(typeof(AnchorWidgetExtensions));
        member.Name.ShouldBe("Doubled");
        member.IsMethod.ShouldBeTrue();
    }

    [Fact]
    public void Member_PoisonedAnchor_FailsClosedOnSentinelRead()
    {
        // An unresolvable anchor (a non-member body) mints a poisoned Member: PoisonError carries the
        // diagnostic the validator reports, and DeclaringType/Name/IsMethod hold no readable sentinel.
        // Reading any of them fails closed, so a poisoned anchor can never be mistaken for a resolved one.
        Member member = new Arch().Member<AnchorWidget>(w => w.Count + 1);

        member.PoisonError.ShouldNotBeNull();
        Should.Throw<InvalidOperationException>(() => member.DeclaringType);
        Should.Throw<InvalidOperationException>(() => member.Name);
        Should.Throw<InvalidOperationException>(() => member.IsMethod);
    }
}
