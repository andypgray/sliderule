namespace SlideRule.Discovery;

/// <summary>
///     Thrown by <see cref="SpecDiscovery.FindSpecs" /> when an assembly declares no spec class, or one with no
///     public parameterless constructor; finding none is an error, never an empty result.
/// </summary>
internal sealed class SpecDiscoveryException : Exception
{
    internal SpecDiscoveryException(string message)
        : base(message)
    {
    }
}
