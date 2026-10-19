using System.Globalization;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The current thread's UI culture, set for the life of a <c>using</c> and restored after it, for a test
///     whose subject is output that a translated resource could change.
/// </summary>
internal sealed class ScopedUICulture : IDisposable
{
    private readonly CultureInfo _previous;

    /// <summary>Captures the current UI culture and sets it to <paramref name="culture" />.</summary>
    internal ScopedUICulture(CultureInfo culture)
    {
        _previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Puts the captured UI culture back.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _previous;
    }
}
