using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.TestSupport;
using SlideRule.Xunit;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the two packages a consumer's own project compiles against: their exported surface
///     must be the surface their committed pin records, and a change to it has to move the pin in the
///     same commit.
/// </summary>
/// <remarks>
///     <para>
///         The csprojs already run ApiCompat against the bytes on nuget.org, but only at pack time, in one CI
///         job and in the release gate. This reds in the suite, in seconds, and names the line.
///     </para>
///     <para>
///         The pin never rewrites itself. On drift it writes the regenerated text beside the test assembly
///         and names both paths, and a person copies it across. No environment variable turns that into an
///         overwrite, because the whole value of the file is that every line in it was read by someone: a
///         NuGet version can never be replaced once pushed, only unlisted, so an accidentally-public member
///         becomes permanent at the next release.
///     </para>
///     <para>
///         Between the pins and the exemptions every package the solution declares is accounted for, but
///         how many there are is not asserted here: the self-spec rule <c>packaging/only-the-named-ship</c>
///         holds that, and a second copy of the fact could disagree with it.
///     </para>
/// </remarks>
public sealed class PublicSurfaceSyncTests
{
    private const string PropsPath = "Directory.Build.props";

    // Where the baseline version and strict mode are declared for every package that validates. A
    // project's own declaration still wins over it, exactly as MSBuild evaluates the two, which is why the
    // checks below read the csproj first and why one arm holds every csproj to declaring neither.
    private const string ScopedProps = "src/Directory.Build.props";

    private const string BaselineProperty = "PackageValidationBaselineVersion";

    private const string StrictModeProperty = "EnableStrictModeForBaselineValidation";

    private const string AnalyzerVersionProperty = "SlideRuleAnalyzersVersion";

    private const string CoreCsproj = "src/SlideRule/SlideRule.csproj";

    private const string AnalyzersCsproj = "src/SlideRule.Analyzers/SlideRule.Analyzers.csproj";

    /// <summary>
    ///     The two assemblies a consumer writes code against: the contract package every spec project
    ///     references, and the adapter a test project derives from.
    /// </summary>
    private static readonly (string Csproj, Assembly Assembly)[] PinnedPackages =
    [
        (CoreCsproj, typeof(Arch).Assembly),
        (DocPaths.AdapterProject, typeof(ArchRuleTests<>).Assembly)
    ];

    /// <summary>
    ///     The assemblies that reach a consumer without packing: the spec-literal analyzer and the
    ///     completion provider travel inside the contract package, and the extraction and workspace
    ///     assemblies inside the tool and the xUnit adapter (extraction inside the analyzer package too),
    ///     rather than as packages of their own. Named here rather than derived, because nothing their project
    ///     files declare tells them apart from a project that never leaves the repository — none is packable,
    ///     and what carries them is another project's packaging.
    /// </summary>
    private static readonly string[] CarriedInAPackage =
    [
        "SlideRule.SpecAnalyzers",
        "SlideRule.SpecCompletion",
        "SlideRule.Extraction",
        "SlideRule.Roslyn"
    ];

    /// <summary>
    ///     Every assembly a release puts in front of a consumer, by assembly name rather than package id,
    ///     because the CLI's two differ: it packs as <c>SlideRule.Cli</c> and its assembly is
    ///     <c>sliderule-cli</c>, which is the name the internals-visible entries use. What packs is read
    ///     from what the solution declares, so a new package is held to the
    ///     binding identity from the day it packs; what a package carries without packing itself has to be
    ///     named, and is.
    /// </summary>
    private static readonly string[] ShippedAssemblyNames =
    [
        .. SolutionInventory.PackableProjects.Select(static project => project.AssemblyName),
        .. CarriedInAPackage
    ];

    /// <summary>
    ///     The two packages deliberately left unpinned, each with the text in its own csproj that makes the
    ///     exemption true. Reading the evidence back is what stops an exemption outliving its reason: the day
    ///     the CLI stops being a tool, or the analyzer stops declaring itself a development dependency, the
    ///     sentence that says otherwise will have gone and this arm fails rather than silently covering a
    ///     package nothing watches.
    /// </summary>
    private static readonly Exemption[] Exemptions =
    [
        new(DocPaths.CliProject,
            "PackAsTool",
            "true",
            "a global tool: what it ships is the `sliderule` command, not an assembly anyone compiles "
            + "against"),
        new(AnalyzersCsproj,
            "DevelopmentDependency",
            "true",
            "an analyzer: every type in it is internal and the package ships no lib/ folder, so there is "
            + "no surface anyone can compile against")
    ];

    private static readonly Regex NumericVersion = new(@"^\d+\.\d+\.\d+");

    [Fact]
    public void EveryPinnedPackage_ShipsExactlyTheSurfaceItsPinRecords()
    {
        // Arrange
        List<string> drift = new();

        // Act: render each shipped surface fresh and compare it to the committed pin.
        foreach ((string _, Assembly assembly) in PinnedPackages)
        {
            string pinPath = PublicSurface.PinPath(assembly);
            string pinned = RepoRoot.ReadText(pinPath);
            string rendered = PublicSurface.Render(assembly);
            IReadOnlyList<string> moved = PublicSurface.Drift(pinned, rendered);
            if (moved.Count == 0) continue;

            drift.AddRange(RegeneratedFiles.DriftReport(pinPath, moved, rendered));
        }

        // Assert
        drift.ShouldReportNothing(
            "The shipped public surface has moved away from its pin. Every line below is something a "
            + "consumer sees, in a package version that can never be replaced once pushed — read them, "
            + "make internal anything that was never meant to be public, and only then copy the "
            + "regenerated file over the pin");
    }

    [Fact]
    public void TheReflectionSweep_SeesTheWholeSurface()
    {
        // Arrange: the failure this arm exists for is a pin regenerated from a sweep that saw nothing.
        // An empty pin compared against an empty render passes forever and says nothing. The anchors are
        // read through typeof so a rename follows them here and is left for the arm above to report.
        (Assembly Assembly, Type Anchor)[] anchors =
        [
            (typeof(Arch).Assembly, typeof(Arch)),
            (typeof(ArchRuleTests<>).Assembly, typeof(ArchRuleTests<>))
        ];
        List<string> blind = new();

        // Act
        foreach ((Assembly assembly, Type anchor) in anchors)
        {
            string rendered = PublicSurface.Render(assembly);
            var anchorLine = $"{anchor.FullName}\n";
            if (!rendered.Contains(anchorLine, StringComparison.Ordinal))
                blind.Add($"{assembly.GetName().Name}: the sweep did not see {anchor.FullName}, so "
                          + "whatever it did see is not this assembly's exported surface.");

            IReadOnlyList<Type> enums = PublicSurface.ExportedEnums(assembly);
            IEnumerable<Type> empty = enums.Where(static enumType => PublicSurface.Constants(enumType)
                .Count == 0);
            blind.AddRange(empty.Select(enumType =>
                $"{assembly.GetName().Name}: exported enum {enumType.FullName} contributed no constants, "
                + "so the renumbering this pin exists to catch would go unseen."));
        }

        // Assert
        blind.ShouldReportNothing("The reflection sweep behind the pins is not seeing what it claims to");
    }

    [Fact]
    public void EveryPackableProject_IsEitherPinnedOrExemptWithALiveReason()
    {
        // Arrange
        List<string> findings = new();

        // Act: a pinned package must have the pack-time check switched on and a committed pin beside it,
        // and an exempt one must still carry the text its exemption rests on.
        foreach ((string csproj, Assembly assembly) in PinnedPackages)
        {
            findings.AddRange(MissingValidation(csproj));

            string pinPath = PublicSurface.PinPath(assembly);
            if (!File.Exists(RepoRoot.Absolute(pinPath)))
                findings.Add($"{csproj} is registered as pinned but {pinPath} does not exist.");
        }

        foreach (Exemption exemption in Exemptions)
        {
            IReadOnlyDictionary<string, string> properties = SolutionInventory.Properties(exemption.Csproj);
            properties.TryGetValue(exemption.Element, out string? declared);

            if (declared is null || !declared.Contains(exemption.Evidence, StringComparison.Ordinal))
                findings.Add($"{exemption.Csproj} is exempt from the surface pin because it is "
                             + $"{exemption.Reason}, but its <{exemption.Element}> no longer says "
                             + $"'{exemption.Evidence}'. Either the exemption is dead and the package "
                             + "needs a pin, or the reason needs rewriting.");
        }

        // Assert
        findings.ShouldReportNothing(
            "These shipping packages are neither pinned nor exempt for a reason their csproj still states");
    }

    /// <summary>
    ///     The coverage half of the arm above, in the one direction a registry can never check itself:
    ///     every project the solution declares packable has to appear in one of the two lists. Without
    ///     it a new package ships with its exported surface watched by nothing and every arm here still
    ///     green, because all of them only ever read what somebody remembered to register.
    /// </summary>
    [Fact]
    public void EveryPackageTheSolutionDeclares_IsRegisteredAsPinnedOrExempt()
    {
        // Arrange
        HashSet<string> registered = PinnedPackages
            .Select(static pinned => pinned.Csproj)
            .Concat(Exemptions.Select(static exemption => exemption.Csproj))
            .ToHashSet(StringComparer.Ordinal);

        // Act
        List<string> unregistered = SolutionInventory.PackableProjects
            .Where(project => !registered.Contains(project.CsprojPath))
            .Select(static project => $"{project.CsprojPath} packs {project.PackageId}, but is "
                                      + "neither pinned nor registered as an exemption.")
            .ToList();

        // Assert
        unregistered.ShouldReportNothing(
            "These projects publish a package whose exported surface nothing here watches — pin it, or "
            + "register an exemption whose reason the csproj still states");
    }

    [Fact]
    public void EveryValidatedProject_NamesABaselineNoNewerThanTheVersion()
    {
        // Arrange: the baseline names the last PUBLISHED version, so it lags <Version> by design and is
        // deliberately not a fifth site in the four-way version equality ManifestVersionTests holds. What
        // it can never be is ahead: a baseline nobody has pushed cannot be downloaded, and the pack-time
        // check then fails at release time with a restore error rather than an API one.
        Version version = NumericPart(PropertyOf(PropsPath, "Version"));
        List<string> findings = new();

        // Act
        foreach ((string csproj, Assembly _) in PinnedPackages)
        {
            Declaration declared = Effective(csproj, BaselineProperty)
                                   ?? throw new InvalidOperationException(
                                       $"Neither {csproj} nor {ScopedProps} declares <{BaselineProperty}>.");

            Version baseline = NumericPart(declared.Value);
            if (baseline <= version) continue;

            findings.Add($"{csproj} validates against baseline {declared.Value}, declared in {declared.File}, "
                         + $"which is ahead of the {version} this repository builds. The baseline may only "
                         + "name a version already on nuget.org.");
        }

        // Assert
        findings.ShouldReportNothing("These packages name a package-validation baseline that cannot exist yet");
    }

    [Fact]
    public void TheValidationBaseline_IsDeclaredOnlyInTheScopedProps()
    {
        // Arrange: a release moves the baseline in one place. A project that declared either property
        // itself would override that place for its own package alone, and the next release would move
        // one of the two and leave the other validating against the version before.
        string[] shared = [BaselineProperty, StrictModeProperty];
        List<string> overrides = new();

        // Act
        foreach (SolutionProject project in SolutionInventory.SrcProjects)
        {
            IReadOnlyDictionary<string, string> own = SolutionInventory.Properties(project.CsprojPath);
            overrides.AddRange(shared
                .Where(own.ContainsKey)
                .Select(property => $"{project.CsprojPath} declares <{property}> itself, which overrides "
                                    + $"{ScopedProps} for this project alone."));
        }

        // Assert
        overrides.ShouldReportNothing(
            $"The package-validation baseline and mode belong to {ScopedProps} alone, so a release moves them "
            + "for every validated package at once");
    }

    [Fact]
    public void TheBuildAnalyzerVersion_IsTheValidationBaseline()
    {
        // Arrange: the src projects run the last released build analyzer over themselves, and the baseline
        // names the last released version too. Both move in the commit after a release indexes, so a
        // difference means one of them was left behind, and an analyzer that lags reads the committed model
        // with a reader two releases old.
        string baseline = PropertyOf(ScopedProps, BaselineProperty);

        // Act
        string analyzer = PropertyOf(ScopedProps, AnalyzerVersionProperty);

        // Assert
        analyzer.ShouldBe(baseline,
            customMessage: $"{ScopedProps} runs build analyzer {analyzer} over src but validates against "
                           + $"{baseline}. Both name the last published release; move them together.");
    }

    [Fact]
    public void ShippedAssemblies_CarryThePinnedBindingIdentity()
    {
        // Arrange: AssemblyVersion is what a compiled spec's reference to the contract carries, and the
        // default binder refuses a host older than that reference. Letting it track the package version
        // meant every release minted a new identity and broke specs built against a newer contract than
        // the tool running them, which is why it is pinned — and why nothing but this asserts the value.
        List<string> findings = new();

        // Act
        foreach (string name in ShippedAssemblyNames)
        {
            Assembly assembly = Assembly.Load(new AssemblyName(name));
            Version? identity = assembly.GetName()
                .Version;
            if (identity == PublicSurface.PinnedBindingIdentity) continue;

            findings.Add($"{name} has AssemblyVersion {identity}, not "
                         + $"{PublicSurface.PinnedBindingIdentity}. Every spec assembly already compiled "
                         + "against this contract names the old identity and will stop binding.");
        }

        // Assert
        findings.ShouldReportNothing("These shipped assemblies no longer carry the pinned binding identity");
    }

    [Fact]
    public void EveryPinFile_IsCommittedAsLfWithOneTrailingNewline()
    {
        // Arrange: the pin is compared as text against a render that joins on "\n", so a checkout that
        // rewrote the file to CRLF would red every line of it at once and read like a surface change.
        // .gitattributes forces LF, and this is what notices the day it stops.
        List<string> findings = new();

        // Act
        foreach ((string _, Assembly assembly) in PinnedPackages)
        {
            string pinPath = PublicSurface.PinPath(assembly);
            string text = RepoRoot.ReadText(pinPath);

            if (text.Contains('\r')) findings.Add($"{pinPath} carries a carriage return; the pins are LF-only.");

            if (!text.EndsWith("\n", StringComparison.Ordinal))
                findings.Add($"{pinPath} does not end on a newline.");
            else if (text.EndsWith("\n\n", StringComparison.Ordinal))
                findings.Add($"{pinPath} ends on a blank line; the render writes exactly one newline.");
        }

        // Assert
        findings.ShouldReportNothing("These surface pins are not committed in the shape the render writes");
    }

    private static IReadOnlyList<string> MissingValidation(string csproj)
    {
        (string Property, string Expected)[] required =
        [
            ("EnablePackageValidation", "true"),
            (StrictModeProperty, "true")
        ];

        List<string> missing = required
            .Where(entry => Effective(csproj, entry.Property) is not { } declared
                            || !string.Equals(declared.Value, entry.Expected, StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{csproj} does not get <{entry.Property}>{entry.Expected} from itself or from "
                             + $"{ScopedProps}, so nothing compares the package it builds against the one "
                             + "already published.")
            .ToList();

        if (Effective(csproj, BaselineProperty) is null)
            missing.Add($"{csproj} switches package validation on but neither it nor {ScopedProps} names a "
                        + "baseline version, which silently compares it against nothing.");

        return missing;
    }

    // What a property evaluates to for one project, as far as the two files that may declare it can say:
    // the project's own declaration wins, because MSBuild imports the scoped props before the project body.
    private static Declaration? Effective(string csproj, string property)
    {
        IReadOnlyDictionary<string, string> own = SolutionInventory.Properties(csproj);
        if (own.TryGetValue(property, out string? declared)) return new Declaration(declared, csproj);

        IReadOnlyDictionary<string, string> scoped = SolutionInventory.Properties(ScopedProps);

        return scoped.TryGetValue(property, out string? inherited) ? new Declaration(inherited, ScopedProps) : null;
    }

    private static string PropertyOf(string path, string property)
    {
        IReadOnlyDictionary<string, string> properties = SolutionInventory.Properties(path);
        properties.TryGetValue(property, out string? value);

        return value
               ?? throw new InvalidOperationException($"{path} declares no <{property}>.");
    }

    // Versions here may carry a prerelease suffix that System.Version cannot parse, and the comparison
    // only ever needs the numeric core: a baseline names a released version, so the suffix is noise.
    private static Version NumericPart(string declared)
    {
        Match match = NumericVersion.Match(declared);
        if (!match.Success) throw new InvalidOperationException($"'{declared}' has no major.minor.patch part.");

        return Version.Parse(match.Value);
    }

    /// <summary>
    ///     One package deliberately left unpinned: the <paramref name="Csproj" /> it is declared in, the
    ///     <paramref name="Element" /> whose text carries the <paramref name="Evidence" /> that makes the
    ///     exemption true, and the <paramref name="Reason" /> a reader of a failure needs.
    /// </summary>
    private sealed record Exemption(string Csproj, string Element, string Evidence, string Reason);

    /// <summary>
    ///     A property's <paramref name="Value" /> and the repository-relative <paramref name="File" /> that
    ///     declared it.
    /// </summary>
    private sealed record Declaration(string Value, string File);
}
