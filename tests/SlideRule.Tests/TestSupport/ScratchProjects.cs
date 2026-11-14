using SlideRule.Roslyn.Diagnostics;

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

    /// <summary>The version <see cref="BrokenSdkPin" /> pins, which no machine has installed.</summary>
    internal const string UnresolvableSdkVersion = "99.0.100";

    /// <summary>
    ///     A <c>global.json</c> pinning <see cref="UnresolvableSdkVersion" /> with roll-forward off, so the .NET host
    ///     fails to resolve an SDK in any directory it covers; with <paramref name="errorMessage" />, hostfxr reports
    ///     that text alone in place of its own, which keeps the failure's detail the same on every machine.
    /// </summary>
    internal static string BrokenSdkPin(string? errorMessage = null)
    {
        string instruction = errorMessage is null ? "" : $", \"errorMessage\": \"{errorMessage}\"";
        return $"{{ \"sdk\": {{ \"version\": \"{UnresolvableSdkVersion}\", \"rollForward\": \"disable\"{instruction} }} }}\n";
    }

    /// <summary>The errorMessage the broken-pin beds hand hostfxr.</summary>
    internal const string PinInstruction = "Run ./bootstrap.sh to install the pinned SDK.";

    /// <summary>
    ///     Writes a <see cref="BrokenSdkPin" /> carrying <see cref="PinInstruction" /> at the root of
    ///     <paramref name="bed" />, a class library at <c>Lib/Lib.csproj</c> and an <c>App.slnx</c> declaring it, and
    ///     returns the pin's and the solution's paths.
    /// </summary>
    internal static (string Pin, string Solution) WriteBrokenPinBed(TempDirectory bed)
    {
        string pin = bed.WriteFile(["global.json"], BrokenSdkPin(PinInstruction));
        bed.WriteFile(["Lib", "Lib.csproj"], ClassLibrary());
        string solution = bed.WriteFile(["App.slnx"], Solution("Lib/Lib.csproj"));
        return (pin, solution);
    }

    /// <summary>
    ///     Writes and builds a C# project at <c>App/App.csproj</c> whose <c>Widget</c> names a type of the SDK-style
    ///     Visual Basic project it references, <c>Legacy/Legacy.vbproj</c>, and an <c>App.slnx</c> declaring both,
    ///     and returns the solution's path. The workspace loads the VB project without a failure.
    /// </summary>
    internal static string WriteVisualBasicBed(TempDirectory bed)
    {
        bed.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Legacy\Legacy.vbproj" />
              </ItemGroup>
            </Project>
            """);
        bed.WriteFile(["App", "Widget.cs"], "namespace App;\n\npublic sealed class Widget\n{\n    public Legacy.Ledger? Ledger { get; }\n}\n");
        bed.WriteFile(
            ["Legacy", "Legacy.vbproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        bed.WriteFile(["Legacy", "Ledger.vb"], "Namespace Legacy\n    Public Class Ledger\n    End Class\nEnd Namespace\n");
        string solution = bed.WriteFile(["App.slnx"], Solution("App/App.csproj", "Legacy/Legacy.vbproj"));
        DotnetCli.Run("build App.slnx", bed.Path);
        return solution;
    }

    /// <summary>
    ///     Gives the <see cref="WriteVisualBasicBed" /> bed's VB type a declaration error, so the workspace has no
    ///     assembly to hand the C# compilation.
    /// </summary>
    internal static void BreakVisualBasicBed(TempDirectory bed)
    {
        bed.WriteFile(
            ["Legacy", "Ledger.vb"],
            "Namespace Legacy\n    Public Class Ledger\n        Inherits Missing\n    End Class\nEnd Namespace\n");
    }

    /// <summary>
    ///     The warning a load of the <see cref="WriteVisualBasicBed" /> bed logs for its one cross-language reference
    ///     once <see cref="BreakVisualBasicBed" /> has run.
    /// </summary>
    internal static string VisualBasicBedWarning(TempDirectory bed)
    {
        string referrer = bed.PathOf("App", "App.csproj");
        string target = bed.PathOf("Legacy", "Legacy.vbproj");
        var finding = new CrossLanguageReferences.Finding(referrer, target);
        return CrossLanguageReferences.Message(finding);
    }

    /// <summary>An .slnx declaring each of <paramref name="projectPaths" /> in order.</summary>
    internal static string Solution(params string[] projectPaths)
    {
        IEnumerable<string> entries = projectPaths.Select(project => $"  <Project Path=\"{project}\" />");
        return "<Solution>\n" + string.Join("\n", entries) + "\n</Solution>\n";
    }
}
