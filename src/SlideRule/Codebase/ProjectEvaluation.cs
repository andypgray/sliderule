namespace SlideRule.Codebase;

/// <summary>
///     One evaluation of a project: the framework it was evaluated for, the project file it read, and the
///     properties a declaration in the solution set in it. A multi-targeted project carries one per framework,
///     because a condition on <c>$(TargetFramework)</c> can set a property for one framework and not another.
/// </summary>
/// <remarks>
///     The evaluation is recorded whole, not only through the properties it found, because a property nothing
///     set has no record of its own: an evaluation with no entry for a name is the fact that the name is unset
///     there, and only a list of the evaluations that ran can say so. The project file is where that missing
///     declaration would be written.
/// </remarks>
internal sealed class ProjectEvaluation
{
    internal ProjectEvaluation(string? targetFramework, SourceLocation projectFile, IReadOnlyList<EvaluatedProperty> properties)
    {
        TargetFramework = targetFramework;
        ProjectFile = projectFile;
        Properties = properties;
    }

    /// <summary>
    ///     Gets the framework this evaluation was for, as a short moniker, or <see langword="null" /> where the
    ///     evaluation ran with no framework told apart.
    /// </summary>
    internal string? TargetFramework { get; }

    /// <summary>Gets the project file's first line, where a property nothing set would be declared.</summary>
    internal SourceLocation ProjectFile { get; }

    /// <summary>Gets the properties a declaration in the solution set in this evaluation, ordinal by name.</summary>
    internal IReadOnlyList<EvaluatedProperty> Properties { get; }

    /// <summary>
    ///     The property of this name, matched ignoring case as MSBuild matches property names, or
    ///     <see langword="null" /> where nothing in the solution set it in this evaluation.
    /// </summary>
    internal EvaluatedProperty? Find(string name)
    {
        foreach (EvaluatedProperty property in Properties)
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property;

        return null;
    }
}
