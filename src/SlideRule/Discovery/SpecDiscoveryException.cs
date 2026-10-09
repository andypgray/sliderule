namespace SlideRule.Discovery;

/// <summary>
///     Thrown by <see cref="SpecDiscovery.FindSpecs" /> when an assembly declares no
///     <see cref="IArchitectureSpec" /> class, or declares one without a public parameterless constructor.
///     Finding none is an error, never an empty result. The message names the assembly in the first case,
///     and every class without the constructor in the second.
/// </summary>
internal sealed class SpecDiscoveryException : Exception
{
    internal SpecDiscoveryException(string message)
        : base(message)
    {
    }
}
