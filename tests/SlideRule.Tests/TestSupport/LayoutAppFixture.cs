using System.Runtime.CompilerServices;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The output-layout fixture solution, and the one thing a caller must do to a copy of it before it
///     will build. It is the only fixture solution that <em>declares</em> a spec project, which makes it the
///     bed for anything needing real spec resolution against a real workspace — a prebuilt DLL handed to
///     <c>--spec</c> resolves before the workspace is touched and exercises none of the walk.
/// </summary>
/// <remarks>
///     <para>
///         <b>The root props file is not optional.</b> The fixture spec reaches the contract library through
///         a <c>HintPath</c> spelled as the property below, because every consumer copies the tree to a temp
///         directory and a relative <c>ProjectReference</c> into <c>src/</c> cannot survive that copy.
///         Nothing restores or builds the fixture in place, so the property has to arrive in a
///         <c>Directory.Build.props</c> written beside the copied solution — which also stops MSBuild's
///         upward walk at the copy root, keeping whatever happens to live above the temp directory out of
///         the build.
///     </para>
///     <para>
///         Consumers differ in everything else, such as whether they take a dedicated copy or lease one and
///         which output layout they build. What they share is exactly this file's contents, so it has one
///         owner — otherwise a rename of the fixture's property would be corrected in one consumer and
///         silently wrong in another.
///     </para>
/// </remarks>
internal static class LayoutAppFixture
{
    /// <summary>The fixture's directory, '/'-separated and relative to the test output's Fixtures root.</summary>
    internal const string FixtureDirectory = "OutputLayoutSolutions/LayoutApp";

    /// <summary>The solution file's name inside that directory.</summary>
    internal const string SolutionFileName = "LayoutApp.slnx";

    /// <summary>The declared spec project — the one the convention has to find.</summary>
    internal const string SpecProject = "LayoutApp.Spec";

    /// <summary>The built spec assembly's file name.</summary>
    internal const string SpecAssembly = "LayoutApp.Spec.dll";

    /// <summary>
    ///     The fixture spec's one rule. Asserting its rendered verdict rather than a returned path is what
    ///     makes a fact say the spec DLL was found <em>and</em> loaded <em>and</em> run.
    /// </summary>
    internal const string RuleId = "layout/naming-interfaces";

    // The contract the fixture spec compiles against: this assembly's own copy, so the spec's
    // IArchitectureSpec is the identity the CLI's load context already carries rather than a second one.
    private static string ContractPath => Path.Combine(AppContext.BaseDirectory, "SlideRule.dll");

    /// <summary>
    ///     The root props file a copy needs: the contract path, plus whatever
    ///     <paramref name="extraProperties" /> the caller is varying (each already spelled as an MSBuild
    ///     property element). Written rather than committed because a committed file could not name this
    ///     assembly's output directory.
    /// </summary>
    internal static string PropsFile(IEnumerable<string> extraProperties)
    {
        var lines = new List<string>
        {
            "<Project>",
            "    <PropertyGroup>",
            $"        <SlideRuleContractPath>{ContractPath}</SlideRuleContractPath>"
        };
        IEnumerable<string> indented = extraProperties.Select(property => "        " + property);
        lines.AddRange(indented);
        lines.Add("    </PropertyGroup>");
        lines.Add("</Project>");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>Writes <see cref="PropsFile" /> at the root of <paramref name="workspace" />'s copy.</summary>
    internal static void WritePropsFile(TempFixtureWorkspace workspace, params string[] extraProperties)
    {
        File.WriteAllText(workspace.PathOf("Directory.Build.props"), PropsFile(extraProperties));
    }

    /// <summary>
    ///     A copy of the fixture, restored and built under the default layout. The copy is leased to the calling
    ///     test class and the lease's reset leaves bin/ alone, so whichever of its facts runs first pays for the
    ///     restore and the build and the rest reuse the output; the build runs again only when the spec assembly
    ///     is absent.
    /// </summary>
    /// <remarks>
    ///     The caller's file path is forwarded because it is the lease key: taken here instead, every class would
    ///     contend for one lease and all but the holder would build a private copy each time.
    /// </remarks>
    internal static TempFixtureWorkspace BuiltCopy([CallerFilePath] string callerFilePath = "")
    {
        var workspace = new TempFixtureWorkspace(
            FixtureDirectory, SolutionFileName, restore: false, callerFilePath: callerFilePath);
        WritePropsFile(workspace);

        string specAssembly = workspace.PathOf(SpecProject, "bin", "Debug", "net10.0", SpecAssembly);
        if (File.Exists(specAssembly)) return workspace;

        FixtureRestorer.Restore(workspace.SolutionPath);
        // --disable-build-servers (plus DotnetCli's node/server env) keeps the drained child from leaving a
        // persistent worker that would wedge the output pipe.
        DotnetCli.Run(
            $"build \"{workspace.SolutionPath}\" --disable-build-servers",
            Path.GetDirectoryName(workspace.SolutionPath)!);
        return workspace;
    }
}
