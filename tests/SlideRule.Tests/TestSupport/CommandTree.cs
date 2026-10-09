using System.CommandLine;

namespace SlideRule.Tests.TestSupport;

/// <summary>The commands of a command tree, each with the words that name it.</summary>
internal static class CommandTree
{
    /// <summary>
    ///     Every command below <paramref name="root" />, a group's subcommands right after it, each with its path: the
    ///     names from the first level down, separated by spaces, so a subcommand reads <c>baseline init</c>.
    /// </summary>
    internal static IEnumerable<(string Path, Command Command)> Below(Command root)
    {
        return Below(root, "");
    }

    private static IEnumerable<(string Path, Command Command)> Below(Command command, string prefix)
    {
        foreach (Command subcommand in command.Subcommands)
        {
            string path = prefix.Length == 0 ? subcommand.Name : $"{prefix} {subcommand.Name}";
            yield return (path, subcommand);

            foreach ((string Path, Command Command) nested in Below(subcommand, path)) yield return nested;
        }
    }
}
