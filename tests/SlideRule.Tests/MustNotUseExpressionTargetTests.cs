using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The static-form <c>MustNotUse</c> verb sugar (GRAMMAR §3.3/§4.5):
///     <c>subject.MustNotUse(() =&gt; Type.M)</c> desugars target-by-target through
///     <c>arch.Member(() =&gt; …)</c> to the identical <see cref="Member" /> leaf — so it reifies
///     identically to both the explicit <c>arch.Member</c> expression spelling and the
///     <c>typeof</c> + <c>nameof</c> spelling. Each pair is compared by its model-file text and its
///     sentence (the <c>GenericSugarTests</c> idiom). Type/lambda shapes are compile-time, so a
///     <c>[Fact]</c> per family suffices.
/// </summary>
public class MustNotUseExpressionTargetTests
{
    [Fact]
    public void MustNotUse_VerbStaticProperty_ReifiesIdenticallyToArchMemberAndTypeof()
    {
        Func<Arch, Constraint> verb = arch => arch.Types.MustNotUse(() => DateTime.Now);

        Checker.ShouldReifyAlike(verb, arch => arch.Types.MustNotUse(arch.Member(() => DateTime.Now)));
        Checker.ShouldReifyAlike(
            verb,
            arch => arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))));
    }

    [Fact]
    public void MustNotUse_VerbStaticVoidMethod_ReifiesIdenticallyToArchMemberAndTypeof()
    {
        // The Action form: a void static method lambda binds the Expression<Action> overload.
        Func<Arch, Constraint> verb = arch => arch.Types.MustNotUse(() => GC.Collect());

        Checker.ShouldReifyAlike(verb, arch => arch.Types.MustNotUse(arch.Member(() => GC.Collect())));
        Checker.ShouldReifyAlike(verb, arch => arch.Types.MustNotUse(arch.Member(typeof(GC), nameof(GC.Collect))));
    }

    [Fact]
    public void MustNotUse_VerbValueReturningMethod_BindsFuncForm()
    {
        // () => Guid.NewGuid() converts to BOTH lambda overloads (a value-returning call is a valid statement
        // expression too, so it reaches Expression<Action> as well as Expression<Func<object?>>). C# "better
        // conversion from expression" picks the Func form — the same betterness the four lambda Arch.Member overloads
        // already rely on — so this compiles unambiguously; the compile itself is the disambiguation proof.
        // The comparison confirms the resolved method leaf matches the typeof spelling.
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotUse(() => Guid.NewGuid()),
            arch => arch.Types.MustNotUse(arch.Member(typeof(Guid), nameof(Guid.NewGuid))));
    }

    [Fact]
    public void MustNotUse_VerbMultipleStaticTargets_ReifiesIdenticallyToArchMemberList()
    {
        // The adoption shape: the whole all-static list passed bare to the verb equals the two-arch.Member spelling.
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow),
            arch => arch.Types.MustNotUse(arch.Member(() => DateTime.Now), arch.Member(() => DateTime.UtcNow)));
    }
}
