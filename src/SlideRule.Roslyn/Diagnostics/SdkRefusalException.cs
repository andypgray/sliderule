namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The refusal <see cref="SdkResolutionRefusal" /> words when the .NET host cannot resolve the SDK a
///     <c>global.json</c> pins, with the host's failure as the inner exception.
/// </summary>
internal sealed class SdkRefusalException : UserErrorException
{
    public SdkRefusalException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
