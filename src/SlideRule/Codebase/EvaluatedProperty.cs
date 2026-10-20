namespace SlideRule.Codebase;

/// <summary>One property a declaration in the solution set for a project, in one evaluation.</summary>
internal sealed class EvaluatedProperty : IProjectPropertyInfo
{
    internal EvaluatedProperty(string name, string value, string? targetFramework, SourceLocation site)
    {
        Name = name;
        Value = value;
        TargetFramework = targetFramework;
        Site = site;
    }

    /// <summary>Gets where the definition that won is declared.</summary>
    public SourceLocation Site { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Value { get; }

    /// <inheritdoc />
    public string? TargetFramework { get; }

    /// <inheritdoc />
    public string FilePath => Site.FilePath;

    /// <inheritdoc />
    public int Line => Site.Line;
}
