namespace SlideRule.Tests.TestSupport;

/// <summary>Project and solution file text for scratch beds a test writes under the run's temp root.</summary>
internal static class ScratchProjects
{
    /// <summary>An SDK-style net10.0 class library with <paramref name="items" /> inside it.</summary>
    internal static string ClassLibrary(string items = "")
    {
        return "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
               + "  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\n"
               + items + "\n</Project>\n";
    }

    /// <summary>An .slnx declaring each of <paramref name="projectPaths" /> in order.</summary>
    internal static string Solution(params string[] projectPaths)
    {
        IEnumerable<string> entries = projectPaths.Select(project => $"  <Project Path=\"{project}\" />");
        return "<Solution>\n" + string.Join("\n", entries) + "\n</Solution>\n";
    }
}
