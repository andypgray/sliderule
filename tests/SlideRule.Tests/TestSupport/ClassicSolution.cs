namespace SlideRule.Tests.TestSupport;

/// <summary>The text of a classic <c>.sln</c> file declaring C# projects, one directory per project.</summary>
internal static class ClassicSolution
{
    /// <summary>
    ///     The <c>Project</c> line and its <c>EndProject</c> for the C# project <paramref name="name" />, at
    ///     <c>&lt;name&gt;\&lt;name&gt;.csproj</c>, each ending in a line feed.
    /// </summary>
    internal static string Entry(string name, string projectGuid = "{11111111-1111-1111-1111-111111111111}")
    {
        return $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{name}\", \"{name}\\{name}.csproj\", \"{projectGuid}\"\n"
               + "EndProject\n";
    }

    /// <summary>
    ///     A solution declaring one <see cref="Entry" /> per name. It declares no configurations, so no project
    ///     reads as one the build skips, and the rest of the file is the minimum a real solution carries.
    /// </summary>
    internal static string Text(params string[] projectNames)
    {
        IEnumerable<string> entries = projectNames.Select(name => Entry(name));
        return "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version 17\n" + string.Concat(entries);
    }
}
