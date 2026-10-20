using SlideRule.Fluent;
using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>Trailer stage for an <c>Enforce</c> rule.</summary>
internal sealed class EnforceRuleBuilder(RuleRegistration registration) : IEnforceRule
{
    public IEnforceRule Because(string because)
    {
        registration.Becauses.Add(Guard.NotNull(because, nameof(because)));
        return this;
    }

    public IEnforceRule Fix(string fix)
    {
        registration.Fixes.Add(Guard.NotNull(fix, nameof(fix)));
        return this;
    }

    public IEnforceRule Citation(string uri)
    {
        registration.Citations.Add(Guard.NotNull(uri, nameof(uri)));
        return this;
    }

    public IEnforceRule Exemplar(Selection exemplar)
    {
        registration.Exemplars.Add(Guard.NotNull(exemplar, nameof(exemplar)));
        return this;
    }
}
