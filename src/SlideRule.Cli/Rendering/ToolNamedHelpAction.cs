using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The help action with the tool's command name on its Usage line, in place of the name of the assembly that
///     happens to be running.
/// </summary>
/// <remarks>
///     System.CommandLine names the root command after the entry assembly, and that is <c>sliderule-cli</c>, not the
///     <c>sliderule</c> a user types: the assembly cannot share the command's name, because it would then differ from
///     the contract's <c>SlideRule</c> only by case, and the runtime, Windows and macOS treat two such names as one
///     assembly. A tool shim does not change it either: measured with the apphost copied to <c>sliderule.exe</c>, the
///     root is still named after the assembly. The stock action renders the page, and only the Usage line is renamed,
///     so every other line is exactly what the library prints.
/// </remarks>
internal sealed class ToolNamedHelpAction(HelpAction inner, string toolName) : SynchronousCommandLineAction
{
    private const string UsageHeading = "Usage:";

    /// <summary>The wrap width of the stock action this one renders through.</summary>
    internal int MaxWidth
    {
        get => inner.MaxWidth;
        set => inner.MaxWidth = value;
    }

    /// <inheritdoc />
    public override bool ClearsParseErrors => true;

    /// <inheritdoc />
    public override int Invoke(ParseResult parseResult)
    {
        InvocationConfiguration configuration = parseResult.InvocationConfiguration;
        TextWriter output = configuration.Output;
        StringWriter page = new();
        configuration.Output = page;
        int exitCode;
        try
        {
            exitCode = inner.Invoke(parseResult);
        }
        finally
        {
            configuration.Output = output;
        }

        string rootName = parseResult.RootCommandResult.Command.Name;
        output.Write(WithToolName(page.ToString(), rootName, toolName));
        return exitCode;
    }

    // The Usage line is the one place the root's own name is printed: the line under the heading, indented, opening
    // with that name and a space. A root that is already the tool's name is left as it is.
    internal static string WithToolName(string page, string rootName, string toolName)
    {
        string[] lines = page.Split('\n');
        for (var index = 1; index < lines.Length; index++)
        {
            if (lines[index - 1].TrimEnd('\r') != UsageHeading) continue;

            string line = lines[index];
            string text = line.TrimStart();
            if (!text.StartsWith(rootName + " ", StringComparison.Ordinal)) continue;

            string indent = line[..^text.Length];
            lines[index] = indent + toolName + text[rootName.Length..];
        }

        return string.Join("\n", lines);
    }
}
