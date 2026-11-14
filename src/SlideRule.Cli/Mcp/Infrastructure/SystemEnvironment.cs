using System.Collections;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     The production <see cref="IEnvironment" /> backed by <see cref="System.Environment" />.
/// </summary>
internal sealed class SystemEnvironment : IEnvironment
{
    public string? GetVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    public IReadOnlyDictionary<string, string> Variables()
    {
        return Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(static entry => (string)entry.Key, static entry => (string?)entry.Value ?? "", StringComparer.Ordinal);
    }

    public void SetVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
    }
}
