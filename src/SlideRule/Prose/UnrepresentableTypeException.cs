namespace SlideRule.Prose;

/// <summary>
///     Thrown by <see cref="TypeName.FullDisplay" /> when a reflection <see cref="Type" /> has no
///     extraction-format analog.
/// </summary>
internal sealed class UnrepresentableTypeException(Type type)
    : Exception($"Type '{type}' has no source-level display form the checker can match against.")
{
    internal Type Type { get; } = type;
}
