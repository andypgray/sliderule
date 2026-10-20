namespace SlideRule.Codebase;

/// <summary>
///     One declared parameter of a method in the extracted model (GRAMMAR §4.6): an entry of
///     <see cref="MemberNode.Parameters" />.
/// </summary>
internal sealed class ParameterNode : IParameterInfo
{
    internal ParameterNode(string name, string typeFullName)
    {
        Name = name;
        TypeFullName = typeFullName;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string TypeFullName { get; }
}
