namespace SlideRule.Codebase;

/// <summary>
///     One attribute in the extracted model, recorded by name: an entry of <see cref="MemberNode.Attributes" />
///     (GRAMMAR §4.6) or of <see cref="TypeNode.InheritedAttributes" />.
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
