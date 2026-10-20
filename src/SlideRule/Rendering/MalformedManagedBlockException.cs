namespace SlideRule.Rendering;

/// <summary>
///     Thrown by <c>ManagedBlock</c> when a file's markers are in a state that cannot be spliced. Nothing is
///     repaired and no text comes back; put the markers right in the file and render again.
/// </summary>
internal sealed class MalformedManagedBlockException : Exception
{
    public MalformedManagedBlockException(string message) : base(message)
    {
    }
}
