using SlideRule.Fluent;
using SlideRule.Internal;

namespace SlideRule.Model;

/// <summary>Posture stage for a rule anchor: sets the posture on the registered node (GRAMMAR §3.2).</summary>
internal sealed class RuleBuilder(RuleRegistration registration) : IRuleBuilder
{
    public IEnforceRule Enforce(Constraint constraint)
    {
        registration.Constraint = Guard.NotNull(constraint, nameof(constraint));
        registration.RecordPosture(Posture.Enforce);
        return new EnforceRuleBuilder(registration);
    }

    public IMigrateRule Migrate(string from, Constraint to)
    {
        string guardedFrom = Guard.NotNull(from, nameof(from));
        Constraint guardedTo = Guard.NotNull(to, nameof(to));
        registration.MigrateFrom = guardedFrom;
        registration.Constraint = guardedTo;
        registration.RecordPosture(Posture.Migrate);
        return new MigrateRuleBuilder(registration);
    }
}
