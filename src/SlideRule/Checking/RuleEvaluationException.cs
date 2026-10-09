namespace SlideRule.Checking;

/// <summary>
///     Signals that a rule cannot be evaluated for a spec-authoring reason the checker turns into a
///     <see cref="ViolationKind.RuleError" /> (Failed) instead of crashing the run: a closed-generic type
///     anchor where the position wants the open definition, or an unrepresentable one; an escape-hatch
///     predicate that threw; or a subject a family verb cannot judge (no family, the wrong kind of family,
///     a cell listed twice, overlapping cells). Carries the agent-facing <see cref="Exception.Message" />
///     verbatim into the violation detail.
/// </summary>
internal sealed class RuleEvaluationException(string message) : Exception(message);
