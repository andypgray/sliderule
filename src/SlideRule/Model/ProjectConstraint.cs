namespace SlideRule.Model;

/// <summary>
///     A reified project-artifact constraint (GRAMMAR §4.10): a <see cref="ProjectSelection" /> subject
///     plus a packaging modal verb phrase. Its inherited <see cref="Constraint.Subject" /> is null, a project
///     selection having no type selection to stand in for it.
/// </summary>
internal abstract class ProjectConstraint : Constraint
{
    private protected ProjectConstraint(ProjectSelection subject)
        : base(null)
    {
        ProjectSubject = subject;
    }

    internal ProjectSelection ProjectSubject { get; }
}
