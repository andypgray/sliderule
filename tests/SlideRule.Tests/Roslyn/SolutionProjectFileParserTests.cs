using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Tests for <see cref="SolutionProjectFileParser" />, no fixture solution anywhere. They pin the
///     textual project-membership extraction: both solution formats, both slash spellings, which
///     <c>.csproj</c> entries a build of the default configuration skips, the partition that sends the built
///     <c>.csproj</c> entries to the load and every other project entry, Web Sites included, to the coverage
///     statement (with solution folders in neither), and which formats the parser owns at all.
/// </summary>
/// <remarks>
///     <para>
///         The <see cref="SolutionProjectFileParser.ParseFilter" /> rows pin the half a solution filter adds:
///         its two resolution bases — the referenced solution against the filter's own directory,
///         every project entry against that solution's — and the four refusals, one of which
///         (<see cref="SolutionProjectFileParser.ReadDeclaredMembership" /> reaching a filter that points at
///         another filter) is a shape Roslyn does not support at all.
///     </para>
///     <para>
///         The <see cref="SolutionProjectFileParser.AnchorDirectory" /> rows are the only ones that need real
///         files, because resolving a filter means reading it: they pin the single directory a whole run
///         resolves its conventions against — baselines, render targets, diff resolution, every relativized
///         path — and that it is never a throw, since the caller asks before the load that owns refusing a
///         bad filter.
///     </para>
/// </remarks>
public sealed class SolutionProjectFileParserTests
{
    // A path root that need not exist — ParseDeclaredProjects resolves textually via Path.GetFullPath.
    private static readonly string SolutionDirectory = Path.Combine(Path.GetTempPath(), "sln-parse-tests");

    private static readonly string Alpha = Path.GetFullPath(Path.Combine(SolutionDirectory, "Alpha", "Alpha.csproj"));

    // The filter and the solution it points at deliberately live in different directories, which is the only
    // arrangement that can tell the two resolution bases apart.
    private static readonly string FilterDirectory = Path.Combine(SolutionDirectory, "filters");

    private static readonly string ReferencedSolution =
        Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "App.sln"));

    /// <summary>
    ///     Project kinds a solution may declare that are not C#, one row each: other languages, and the database,
    ///     JavaScript, installer, packaging, deployment and Service Fabric projects that real solutions declare
    ///     beside C#. F# and Visual Basic are the ones that reach a loaded solution, which is why each also has a
    ///     bed of its own; the rest come free with the "ends in <c>proj</c>" partition and are pinned here, over
    ///     text.
    /// </summary>
    /// <remarks>
    ///     The <c>.shproj</c> is deliberately not among them: it is the one extension this partition catches
    ///     that the parser classifies apart from "not a C# project", so it has a named fact of its own
    ///     (<see cref="ParseDeclaredProjects_ASharedProject_IsClassifiedByShapeRatherThanAsAnotherLanguage" />)
    ///     rather than a row asserting the answer the others get.
    /// </remarks>
    public static TheoryData<string, string> UnsupportedProjectKindCases => new()
    {
        { "Fs", "fsproj" },
        { "Vb", "vbproj" },
        { "Database", "sqlproj" },
        { "Native", "vcxproj" },
        { "Python", "pyproj" },
        { "Client", "esproj" },
        { "Installer", "wixproj" },
        { "Package", "wapproj" },
        { "Setup", "vdproj" },
        { "Deploy", "wdproj" },
        { "Cluster", "sfproj" }
    };

    [Fact]
    public void ParseDeclaredProjects_ClassicSln_ReturnsCsprojsIgnoringFolders()
    {
        // Arrange — an Alpha project (backslash), a Beta project (forward slash), and a solution folder.
        const string text = """
                            Microsoft Visual Studio Solution File, Format Version 12.00
                            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Alpha", "Alpha\Alpha.csproj", "{11111111-1111-1111-1111-111111111111}"
                            EndProject
                            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Beta", "nested/Beta/Beta.csproj", "{22222222-2222-2222-2222-222222222222}"
                            EndProject
                            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{33333333-3333-3333-3333-333333333333}"
                            EndProject
                            """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".sln", SolutionDirectory);

        // Assert — both csprojs (either slash spelling resolves); the folder is not a project at all, so it
        // lands in neither half rather than being reported as something this product could not read. A
        // classic .sln puts a bare folder name in the field a project path occupies, so the extension is what
        // keeps folders out; getting it wrong would name "Solution Items" as an unreadable project on every
        // classic solution.
        declared.Csproj.ShouldBe([
            Alpha,
            Path.GetFullPath(Path.Combine(SolutionDirectory, "nested", "Beta", "Beta.csproj"))
        ]);
        declared.Unsupported.ShouldBeEmpty();
    }

    [Fact]
    public void ParseDeclaredProjects_Slnx_ReturnsCsprojsIgnoringNonProjectEntriesAndNesting()
    {
        // Arrange — a root project, a folder-nested project, and an entry that is not a project file.
        const string text = """
                            <Solution>
                              <Project Path="Alpha\Alpha.csproj" />
                              <Folder Name="/Shared/">
                                <Project Path="nested/Beta/Beta.csproj" />
                              </Folder>
                              <Project Path="docs/Readme.md" />
                            </Solution>
                            """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".slnx", SolutionDirectory);

        // Assert — both csprojs (nesting flattened, both slash spellings); the markdown is not a project.
        declared.Csproj.ShouldBe([
            Alpha,
            Path.GetFullPath(Path.Combine(SolutionDirectory, "nested", "Beta", "Beta.csproj"))
        ]);
        declared.Unsupported.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(UnsupportedProjectKindCases))]
    public void ParseDeclaredProjects_AProjectInAnotherLanguage_IsNamedRatherThanDropped(
        string name, string extension)
    {
        // Arrange — one readable project beside one this product cannot read, which is the whole shape of a
        // polyglot solution. Both slash spellings and both formats are covered by the rows above; what this
        // adds is that the extension test is "ends in proj" rather than a list of the kinds anyone thought of.
        var text = $"""
                    <Solution>
                      <Project Path="Alpha\Alpha.csproj" />
                      <Project Path="{name}/{name}.{extension}" />
                    </Solution>
                    """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".slnx", SolutionDirectory);

        // Assert — the csproj half is untouched, so a polyglot solution loads its C# projects alone, and the
        // other project is a fact the run can state, carrying the kind this parser classified it as rather
        // than leaving a later reader to re-derive one from the extension.
        declared.Csproj.ShouldBe([Alpha]);
        declared.Unsupported.ShouldBe([
            new UnsupportedProject(
                Path.GetFullPath(Path.Combine(SolutionDirectory, name, $"{name}.{extension}")),
                UnsupportedProjectKind.NotCsharp)
        ]);
    }

    [Fact]
    public void ParseDeclaredProjects_ASharedProject_IsClassifiedByShapeRatherThanAsAnotherLanguage()
    {
        // A shared project is the one entry the "ends in proj" partition catches that is not a language: it
        // is a container whose .projitems files compile into every project that imports it, so its code is
        // very often C# and reaches the model through the importer. Classified with the .fsproj, it would tell
        // every reader "not a C# project" about a project whose sources usually are, and which is not missing
        // from the model at all.
        const string text = """
                            <Solution>
                              <Project Path="Alpha\Alpha.csproj" />
                              <Project Path="Shared/Shared.shproj" />
                            </Solution>
                            """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".slnx", SolutionDirectory);

        // Assert
        declared.Unsupported.ShouldBe([
            new UnsupportedProject(
                Path.GetFullPath(Path.Combine(SolutionDirectory, "Shared", "Shared.shproj")),
                UnsupportedProjectKind.SharedProject)
        ]);
    }

    /// <summary>
    ///     Classic <c>.sln</c> configuration shapes, as the solution's configuration list, the lines it maps the
    ///     Alpha project with, and whether a build that names no configuration skips Alpha. The default is
    ///     MSBuild's: <c>Debug</c> if any configuration has it, otherwise the first; then <c>Mixed Platforms</c>,
    ///     else <c>Any CPU</c>, else the first platform.
    /// </summary>
    public static TheoryData<string, string[], string[], bool> SlnConfigurationCases => new()
    {
        { "mapped without Build.0", ["Debug|Any CPU"], ["Debug|Any CPU.ActiveCfg"], true },
        { "mapped and built", ["Debug|Any CPU"], ["Debug|Any CPU.ActiveCfg", "Debug|Any CPU.Build.0"], false },
        { "not mapped at all", ["Debug|Any CPU"], [], false },
        { "no configurations declared", [], [], false },
        { "Debug preferred over the first", ["Release|Any CPU", "Debug|Any CPU"], ["Debug|Any CPU.ActiveCfg"], true },
        {
            "the first when there is no Debug", ["Release|Any CPU", "Staging|Any CPU"],
            ["Release|Any CPU.ActiveCfg", "Staging|Any CPU.ActiveCfg", "Staging|Any CPU.Build.0"], true
        },
        {
            "Mixed Platforms preferred over Any CPU", ["Debug|Any CPU", "Debug|Mixed Platforms"],
            ["Debug|Any CPU.ActiveCfg", "Debug|Any CPU.Build.0", "Debug|Mixed Platforms.ActiveCfg"], true
        },
        {
            "skipped only outside the default", ["Debug|Any CPU", "Release|Any CPU"],
            ["Debug|Any CPU.ActiveCfg", "Debug|Any CPU.Build.0", "Release|Any CPU.ActiveCfg"], false
        }
    };

    /// <summary>
    ///     <c>.slnx</c> configuration shapes, as the solution's <c>Configurations</c> element, the rules on the
    ///     Alpha project, and whether a build that names no configuration skips Alpha.
    /// </summary>
    public static TheoryData<string, string, string, bool> SlnxConfigurationCases => new()
    {
        { "excluded in the default pair", "", """<Build Solution="Debug|Any CPU" Project="false" />""", true },
        { "excluded in every build type", "", """<Build Solution="*|Any CPU" Project="false" />""", true },
        { "excluded with no solution pattern", "", """<Build Project="false" />""", true },
        { "excluded only in another build type", "", """<Build Solution="Release|*" Project="false" />""", false },
        {
            "a later rule overrides an earlier one", "",
            """<Build Project="false" /><Build Solution="Debug|*" Project="true" />""", false
        },
        {
            "the first declared build type when there is no Debug",
            """<Configurations><BuildType Name="Release" /><BuildType Name="Staging" /></Configurations>""",
            """<Build Solution="Release|*" Project="false" />""", true
        }
    };

    [Theory]
    [MemberData(nameof(SlnConfigurationCases))]
    public void ParseDeclaredProjects_ClassicSlnConfiguration_DecidesWhetherTheBuildSkipsAProject(
        string shape, string[] configurations, string[] alphaLines, bool skipped)
    {
        // Arrange — Alpha, mapped as the row says, beside Beta, which every configuration builds.
        string solutionConfigurations = string.Concat(
            configurations.Select(configuration => $"\t\t{configuration} = {configuration}\n"));
        string projectConfigurations = string.Concat(
            alphaLines.Select(line => $"\t\t{{11111111-1111-1111-1111-111111111111}}.{line} = Debug|Any CPU\n"));
        var text = $$"""
                     Microsoft Visual Studio Solution File, Format Version 12.00
                     Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Alpha", "Alpha\Alpha.csproj", "{11111111-1111-1111-1111-111111111111}"
                     EndProject
                     Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Beta", "Beta\Beta.csproj", "{22222222-2222-2222-2222-222222222222}"
                     EndProject
                     Global
                     	GlobalSection(SolutionConfigurationPlatforms) = preSolution
                     {{solutionConfigurations}}	EndGlobalSection
                     	GlobalSection(ProjectConfigurationPlatforms) = postSolution
                     {{projectConfigurations}}	EndGlobalSection
                     EndGlobal
                     """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".sln", SolutionDirectory);

        // Assert — a skipped project leaves the load's membership for a list of its own, and only then.
        string beta = Path.GetFullPath(Path.Combine(SolutionDirectory, "Beta", "Beta.csproj"));
        declared.NotBuilt.ShouldBe(skipped ? [Alpha] : [], shape);
        declared.Csproj.ShouldBe(skipped ? [beta] : [Alpha, beta], shape);
    }

    [Theory]
    [MemberData(nameof(SlnxConfigurationCases))]
    public void ParseDeclaredProjects_SlnxBuildRules_DecideWhetherTheBuildSkipsAProject(
        string shape, string configurations, string alphaRules, bool skipped)
    {
        // Arrange
        var text = $"""
                    <Solution>
                      {configurations}
                      <Project Path="Alpha\Alpha.csproj">{alphaRules}</Project>
                      <Project Path="Beta\Beta.csproj" />
                    </Solution>
                    """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".slnx", SolutionDirectory);

        // Assert
        string beta = Path.GetFullPath(Path.Combine(SolutionDirectory, "Beta", "Beta.csproj"));
        declared.NotBuilt.ShouldBe(skipped ? [Alpha] : [], shape);
        declared.Csproj.ShouldBe(skipped ? [beta] : [Alpha, beta], shape);
    }

    [Fact]
    public void ParseDeclaredProjects_WebSiteEntries_AreNamedAsWebSitesByTheirSolutionName()
    {
        // A Web Site's path field holds a URL or a folder, so neither passes the "ends in proj" test; without
        // the project-type test both would be dropped, and only the workspace's complaint about a missing file
        // would say they exist. The second entry spells its type GUID in lower case, which the project-type
        // test must match too.
        const string text = """
                            Microsoft Visual Studio Solution File, Format Version 12.00
                            Project("{E24C65DC-7377-472B-9ABA-BC803B73C61A}") = "Portal", "http://localhost:3961", "{33333333-3333-3333-3333-333333333333}"
                            	ProjectSection(WebsiteProperties) = preProject
                            		Debug.AspNetCompiler.PhysicalPath = "Portal\"
                            	EndProjectSection
                            EndProject
                            Project("{e24c65dc-7377-472b-9aba-bc803b73c61a}") = "LegacySite", "LegacySite\", "{44444444-4444-4444-4444-444444444444}"
                            EndProject
                            """;

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".sln", SolutionDirectory);

        // Assert
        declared.Csproj.ShouldBeEmpty();
        declared.Unsupported.ShouldBe([
            new UnsupportedProject(
                Path.GetFullPath(Path.Combine(SolutionDirectory, "http:", "localhost:3961")),
                UnsupportedProjectKind.WebSite,
                "Portal"),
            new UnsupportedProject(
                Path.GetFullPath(Path.Combine(SolutionDirectory, "LegacySite") + Path.DirectorySeparatorChar),
                UnsupportedProjectKind.WebSite,
                "LegacySite")
        ]);
    }

    [Fact]
    public void ParseDeclaredProjects_NoProjects_ReturnsEmpty()
    {
        // Arrange — a header-only solution with no project lines.
        const string text = "Microsoft Visual Studio Solution File, Format Version 12.00\n";

        // Act
        DeclaredProjects declared = SolutionProjectFileParser.ParseDeclaredProjects(text, ".sln", SolutionDirectory);

        // Assert
        declared.Csproj.ShouldBeEmpty();
        declared.Unsupported.ShouldBeEmpty();
    }

    [Fact]
    public void OwnsFormat_TheThreeSolutionFormats_AreOwnedAndNothingElseIs()
    {
        // A membership subtraction depends on telling "the parser cannot read this" from "the solution
        // declares nothing". All three real formats are readable — a .slnf by resolving through to the
        // solution it points at — so only a file that is not a solution at all answers false.
        SolutionProjectFileParser.OwnsFormat("/repo/App.sln")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/App.SLNX")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/Filtered.slnf")
            .ShouldBeTrue();
        SolutionProjectFileParser.OwnsFormat("/repo/App")
            .ShouldBeFalse();
    }

    [Fact]
    public void ParseFilter_ProjectsAndSolution_ResolveAgainstTheirOwnBases()
    {
        // The shape Visual Studio writes: the solution path is relative to the filter, and every project
        // path is relative to that solution. Resolving both against one base is the mistake this pins shut —
        // it would look right whenever the filter happens to sit beside the solution, and name paths that
        // exist nowhere as soon as it does not.
        const string filterText = """
                                  {
                                    "solution": {
                                      "path": "../solutions/App.sln",
                                      "projects": [
                                        "Alpha\\Alpha.csproj",
                                        "nested/Beta/Beta.csproj"
                                      ]
                                    }
                                  }
                                  """;

        (string referencedSolution, IReadOnlyList<string> requested) = SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory);

        referencedSolution.ShouldBe(ReferencedSolution);
        requested.ShouldBe([
            Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "Alpha", "Alpha.csproj")),
            Path.GetFullPath(Path.Combine(SolutionDirectory, "solutions", "nested", "Beta", "Beta.csproj"))
        ]);
    }

    [Fact]
    public void ParseFilter_NoProjectsArray_SelectsNothingRatherThanFailing()
    {
        // An empty selection is not an empty answer: ReadDeclaredMembership reads it as "no filtering at
        // all", which is Roslyn's own rule. The parser's job is only to report that nothing was requested.
        const string filterText = """{ "solution": { "path": "../solutions/App.sln", "projects": [] } }""";

        (string referencedSolution, IReadOnlyList<string> requested) = SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory);

        referencedSolution.ShouldBe(ReferencedSolution);
        requested.ShouldBeEmpty();
    }

    [Fact]
    public void ParseFilter_NotJson_RefusesAsMalformed()
    {
        const string filterText = """{ "solution": { "path": "../solutions/App.sln", "projects": [ }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter is not well-formed JSON.");
    }

    [Fact]
    public void ParseFilter_NoSolutionObject_RefusesNamingWhatIsMissing()
    {
        const string filterText = """{ "projects": [] }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter declares no 'solution' object.");
    }

    [Fact]
    public void ParseFilter_SolutionObjectWithoutAPath_RefusesSeparately()
    {
        // Distinct from the row above: the object is there and the path is not, which is a different edit to
        // make and so a different sentence to read.
        const string filterText = """{ "solution": { "projects": [] } }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("The solution filter declares no solution path.");
    }

    [Fact]
    public void ParseFilter_FilterPointingAtAnotherFilter_Refuses()
    {
        // Roslyn does not support chained filters, so resolving one would produce a membership no load could
        // ever match — a refusal here is the honest answer rather than a downstream mystery.
        const string filterText = """{ "solution": { "path": "../solutions/Inner.slnf", "projects": [] } }""";

        var ex = Should.Throw<FormatException>(() => SolutionProjectFileParser.ParseFilter(filterText, FilterDirectory));

        ex.Message.ShouldBe("A solution filter may not reference another solution filter.");
    }

    [Fact]
    public void AnchorDirectory_Filter_IsTheReferencedSolutionsDirectoryRatherThanTheFiltersOwn()
    {
        // The whole point of an anchor: a filter is a lens on a solution, not a codebase of its own, so a
        // run through one must resolve arch/ and write its output where a run over the solution would. Kept
        // in its own directory — the only arrangement that can tell the two apart — a filter anchored at
        // itself resolves no baseline the team ever committed and lands every written file beside itself.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string solution = WriteReferencedSolution(temp);
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", "projects": [] } }""");

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(solution));
    }

    [Fact]
    public void AnchorDirectory_BothSolutionFormats_AreTheirOwnDirectoryAndCostNoRead()
    {
        // An unfiltered run is the overwhelming majority and must answer without touching disk at all —
        // neither of these paths exists, and neither is looked for.
        SolutionProjectFileParser.AnchorDirectory(Path.Combine(SolutionDirectory, "App.sln"))
            .ShouldBe(SolutionDirectory);
        SolutionProjectFileParser.AnchorDirectory(Path.Combine(SolutionDirectory, "App.slnx"))
            .ShouldBe(SolutionDirectory);
    }

    [Fact]
    public void AnchorDirectory_MalformedFilter_FallsBackToItsOwnDirectoryWithoutThrowing()
    {
        // The anchor is computed before the workspace load, and the load is what refuses a malformed filter
        // by name. Throwing here would replace that refusal with a worse-placed one, so the fallback is the
        // filter's own directory and the operator still gets the message the load writes.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", """);

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(filter));
    }

    [Fact]
    public void AnchorDirectory_FilterNamingAMissingSolution_FallsBackToItsOwnDirectory()
    {
        // The filter parses and the path resolves — there is simply no solution there. Anchoring at a
        // directory that does not exist would resolve conventions into nowhere and create it on the first
        // write, so a solution that is not on disk is not an anchor.
        using TempDirectory temp = TestTempRoot.Fresh("solution-anchor");
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/Absent.sln", "projects": [] } }""");

        SolutionProjectFileParser.AnchorDirectory(filter)
            .ShouldBe(Path.GetDirectoryName(filter));
    }

    [Fact]
    public void SolutionFileOf_Filter_IsTheSolutionItReferences()
    {
        // The file a workspace load opens under a filter, which is where its solution properties come from.
        using TempDirectory temp = TestTempRoot.Fresh("solution-file-of");
        string solution = WriteReferencedSolution(temp);
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", "projects": [] } }""");

        SolutionProjectFileParser.SolutionFileOf(filter)
            .ShouldBe(solution);
    }

    [Fact]
    public void SolutionFileOf_BothSolutionFormats_AreThemselvesAndCostNoRead()
    {
        SolutionProjectFileParser.SolutionFileOf(Path.Combine(SolutionDirectory, "App.sln"))
            .ShouldBe(Path.Combine(SolutionDirectory, "App.sln"));
        SolutionProjectFileParser.SolutionFileOf(Path.Combine(SolutionDirectory, "App.slnx"))
            .ShouldBe(Path.Combine(SolutionDirectory, "App.slnx"));
    }

    [Fact]
    public void SolutionFileOf_MalformedFilter_IsNullWithoutThrowing()
    {
        using TempDirectory temp = TestTempRoot.Fresh("solution-file-of");
        string filter = WriteFilter(temp, """{ "solution": { "path": "../solutions/App.sln", """);

        SolutionProjectFileParser.SolutionFileOf(filter)
            .ShouldBeNull();
    }

    // The filter and the solution it points at, on real disk in the two-directory arrangement the constants
    // above describe. Only the anchor rows need this: resolving a filter means reading it.
    private static string WriteFilter(TempDirectory temp, string filterText)
    {
        return temp.WriteFile(["filters", "Selected.slnf"], filterText);
    }

    private static string WriteReferencedSolution(TempDirectory temp)
    {
        return temp.WriteFile(["solutions", "App.sln"], "Microsoft Visual Studio Solution File, Format Version 12.00\n");
    }
}
