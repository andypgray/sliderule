namespace SlideRule.Codebase;

/// <summary>
///     One attribute declared on a member in the extracted model (GRAMMAR §4.6): an entry of
///     <see cref="MemberNode.Attributes" />.
/// </summary>
internal sealed class AttributeNode : IAttributeInfo
{
    internal AttributeNode(string definitionFullName, string fullName)
    {
        DefinitionFullName = definitionFullName;
        FullName = fullName;
    }

    /// <inheritdoc />
    public string DefinitionFullName { get; }

    /// <inheritdoc />
    public string FullName { get; }
}
