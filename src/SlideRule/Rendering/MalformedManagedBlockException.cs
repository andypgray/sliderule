namespace SlideRule.Rendering;

/// <summary>
///     Thrown by <c>ManagedBlock.Splice</c> and <c>ManagedBlock.ExtractBody</c> when a file's markers are
///     in a state that cannot be spliced: a begin marker with no end, an end before a begin, or a second
///     begin or end. Nothing is repaired and no text comes back; put the markers right in the file and
///     render again.
/// </summary>
internal sealed class MalformedManagedBlockException : Exception
{
    /// <summary>Creates the exception with a message describing the marker state that was refused.</summary>
    /// <param name="message">The description of the refused marker state.</param>
    public MalformedManagedBlockException(string message) : base(message)
    {
    }
}
