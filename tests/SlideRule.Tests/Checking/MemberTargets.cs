// Reflectable member-target types for MustNotUse specs (typeof + nameof). MemberUseVerbTests.Scene
// re-declares these — with their members — plus subject types that use them; the two copies must stay
// in lockstep, the same discipline as CheckerTargets / Sources.Hierarchy and the FullDisplay
// correspondence test.
// The sub-namespace keeps the targets apart from the tests that select them.
// ReSharper disable once CheckNamespace

namespace SlideRule.Tests.Checking.MemberTargets;

public sealed class Clock
{
    public int Ticks { get; set; }

    public void Advance()
    {
    }

    public void Advance(int by)
    {
    }
}

public interface IGauge
{
    void Read();
}

public sealed class Gauge : IGauge
{
    public void Read()
    {
    }
}

// The compiler emits each method of an extension block as a static method of the declaring class, which is
// what a member anchor reflects on. MemberUseVerbTests and the parity corpus each re-declare this class.
public static class Sequences
{
    extension(IEnumerable<int> values)
    {
        public int Total()
        {
            return 0;
        }
    }
}
