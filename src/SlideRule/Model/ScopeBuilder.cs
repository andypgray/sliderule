using SlideRule.Fluent;
using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>Posture stage for a scope anchor (GRAMMAR §3.2).</summary>
internal sealed class ScopeBuilder(ScopeRegistration registration) : IScopeBuilder
{
    public IQuarantinedScope Quarantine(Selection selection)
    {
        Record(selection, Posture.Quarantine);
        return new QuarantinedScopeBuilder(registration);
    }

    public ICautionedScope Caution(Selection selection)
    {
        Record(selection, Posture.Caution);
        return new CautionedScopeBuilder(registration);
    }

    private void Record(Selection selection, Posture posture)
    {
        registration.PostureCount++;
        registration.Posture = posture;
        registration.Scoped = Guard.NotNull(selection, nameof(selection));
    }
}
