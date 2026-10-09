using SlideRule.Cli.Verbs;

namespace SlideRule.Cli;

/// <summary>
///     The outer shell shared by every command action: run the action (which crosses the MSBuild gate)
///     and hand any failure to <see cref="CliErrorMapper" />, which renders it. A run the operator cancelled
///     prints one line instead. Either way <see cref="RunAsync" /> then answers <see cref="ExitCodes.Error" />.
///     Its own body touches no Roslyn type, so it stays outside the JIT quarantine.
/// </summary>
internal static class CommandEntryPoint
{
    public static async Task<int> RunAsync(Func<Task<int>> action, TextWriter error, CancellationToken ct)
    {
        (bool succeeded, int code) = await TryRunAsync(action, error, ct);
        return succeeded ? code : ExitCodes.Error;
    }

    /// <summary>
    ///     Runs <paramref name="action" /> under the same handler as <see cref="RunAsync" /> and hands back its
    ///     value, or <c>(false, default)</c> once the failure has been written to <paramref name="error" />.
    /// </summary>
    public static async Task<(bool Succeeded, T Value)> TryRunAsync<T>(
        Func<Task<T>> action, TextWriter error, CancellationToken ct)
    {
        try
        {
            return (true, await action());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ctrl+C or a termination signal, answered within System.CommandLine's grace period; a run that
            // outlives it exits 130 or 143 and never gets here. A cancellation of a token nobody cancelled
            // falls through to the mapper, as the bug it is.
            await error.WriteLineAsync("Cancelled.");
            return (false, default!);
        }
        catch (Exception ex)
        {
            CliErrorMapper.Write(ex, error);
            return (false, default!);
        }
    }
}
