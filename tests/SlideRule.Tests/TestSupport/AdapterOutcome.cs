namespace SlideRule.Tests.TestSupport;

/// <summary>
///     What a call into one of the xUnit adapter's entry points ended in, captured as a value, a skip included.
/// </summary>
internal static class AdapterOutcome
{
    // Record.ExceptionAsync rethrows a SkipException rather than returning it, which xunit then applies to the
    // calling test, so a test asserting on a skip, or on its absence, would read as skipped and never assert.
    // Catching by hand keeps those assertions real.
    internal static async Task<Exception?> CaughtAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
