namespace SlideRule.Roslyn;

/// <summary>
///     An error whose message is meant to be shown to the person who ran the tool, complete on its own, such
///     as an unreadable solution filter or a baseline file that has been tampered with. Throw it when the
///     fault is something the reader can fix; anything else is a bug and should surface as itself.
/// </summary>
internal sealed class UserErrorException : InvalidOperationException
{
    public UserErrorException(string message) : base(message)
    {
    }

    public UserErrorException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
