using SlideRule.Fluent;
using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>Trailer stage for a <c>Migrate</c> rule.</summary>
internal sealed class MigrateRuleBuilder(RuleRegistration registration) : IMigrateRule
{
    public IMigrateRule Because(string because)
    {
        registration.Becauses.Add(Guard.NotNull(because, nameof(because)));
        return this;
    }

    public IMigrateRule Fix(string fix)
    {
        registration.Fixes.Add(Guard.NotNull(fix, nameof(fix)));
        return this;
    }

    public IMigrateRule Citation(string uri)
    {
        registration.Citations.Add(Guard.NotNull(uri, nameof(uri)));
        return this;
    }

    public IMigrateRule Exemplar(Selection exemplar)
    {
        registration.Exemplars.Add(Guard.NotNull(exemplar, nameof(exemplar)));
        return this;
    }

    public IMigrateRule Baseline(string path)
    {
        registration.Baselines.Add(Guard.NotNull(path, nameof(path)));
        return this;
    }

    public IMigrateRule WhileYoureThere(MigrationPolicy policy)
    {
        registration.Policies.Add(Guard.Defined(policy, nameof(policy)));
        return this;
    }
}
