using System.Text.Json;
using System.Text.Json.Serialization;
using SlideRule.Tests.DocHygiene;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The checkout of the eShopOnWeb fork the showcase page quotes, at the commit its pin names. The fork is an
///     external repository that this suite never clones or builds. A run that wants the showcase facts names a checkout
///     through <see cref="RootVariable" />, and CI's showcase job fetches the pinned commit and builds its spec before
///     running the classes that read the checkout.
/// </summary>
internal static class ShowcaseCheckout
{
    /// <summary>
    ///     The variable naming the checkout's root directory. Unset (the default) skips every showcase fact.
    /// </summary>
    internal const string RootVariable = "SLIDERULE_SHOWCASE_ROOT";

    /// <summary>The pin, relative to this repository's root.</summary>
    internal const string PinPath = "examples/eShopOnWeb/showcase.json";

    /// <summary>The name the replay and its fences know the showcase by.</summary>
    internal const string Name = "eShopOnWeb";

    private static readonly Lazy<ShowcasePin> ParsedPin = new(static () => ShowcasePin.Parse(RepoRoot.ReadText(PinPath)));

    /// <summary>The committed pin.</summary>
    internal static ShowcasePin Pin => ParsedPin.Value;

    /// <summary>
    ///     The checkout <see cref="RootVariable" /> names, as an example solution, without asking whether it is the
    ///     pinned one. A test reaches it through <see cref="ShouldBeAtThePin" />, which does ask.
    /// </summary>
    /// <exception cref="InvalidOperationException">The variable is unset.</exception>
    internal static ExampleSolution Solution
    {
        get
        {
            string root = SetRoot;

            return new ExampleSolution(Name, root, UnderRoot(Pin.Solution), Path.GetDirectoryName(SpecProject));
        }
    }

    /// <summary>The path of the pinned spec project inside the checkout <see cref="RootVariable" /> names.</summary>
    /// <exception cref="InvalidOperationException">The variable is unset.</exception>
    internal static string SpecProject => UnderRoot(Pin.SpecProject);

    private static string? Root =>
        Environment.GetEnvironmentVariable(RootVariable) is { } root && !string.IsNullOrWhiteSpace(root)
            ? Path.GetFullPath(root)
            : null;

    private static string SetRoot => Root ?? throw new InvalidOperationException($"{RootVariable} is not set.");

    // A path the pin spells relative to the checkout, with forward slashes, under the checkout's root.
    private static string UnderRoot(string relative)
    {
        return Path.Combine([SetRoot, .. relative.Split('/')]);
    }

    /// <summary>
    ///     Skips the running test unless <see cref="RootVariable" /> names a checkout, and otherwise asserts that the
    ///     checkout is the pinned commit with a clean tracked tree and a built spec. A checkout that falls short on
    ///     any of those fails the test instead of skipping it. Setting the variable asks for these facts, so a skip
    ///     there would let a fetch that went wrong pass them unchecked.
    /// </summary>
    /// <remarks>Asserting, hence the name.</remarks>
    internal static ExampleSolution ShouldBeAtThePin()
    {
        Assert.SkipWhen(
            Root is null,
            $"The showcase facts read a checkout of {Pin.Fork.Repository} at {Pin.Fork.Commit}, which this suite does "
            + $"not fetch. Set {RootVariable} to the root of one, with its spec built, and run this again.");

        ExampleSolution checkout = Solution;
        string root = checkout.SolutionDirectory;
        List<string> problems = [];

        string head = GitCommand.Output(root, "rev-parse", "HEAD")
            .Trim();
        if (head != Pin.Fork.Commit) problems.Add($"HEAD is {head}, and the pin names {Pin.Fork.Commit}.");

        string changed = GitCommand.Output(root, "status", "--porcelain", "--untracked-files=no")
            .Trim();
        if (changed.Length > 0) problems.Add($"the tracked tree has changes:\n{changed}");

        string specProject = checkout.SpecProjectDirectory!;
        var specAssembly = $"{Path.GetFileNameWithoutExtension(Pin.SpecProject)}.dll";
        string output = Path.Combine(specProject, "bin");
        bool built = Directory.Exists(output) && Directory.EnumerateFiles(output, specAssembly, SearchOption.AllDirectories)
            .Any();
        if (!built) problems.Add($"no {specAssembly} is under {output}; build the spec project first.");

        problems.ShouldReportNothing($"{RootVariable} names {root}, which is not the checkout {PinPath} pins");
        return checkout;
    }

    /// <summary>A reader of the checkout's files by forward-slash path relative to its root.</summary>
    internal static Func<string, IReadOnlyList<string>?> LineReader()
    {
        return RepoRoot.LineReaderUnder(Solution.SolutionDirectory);
    }
}

/// <summary>
///     The showcase pin: the <paramref name="Fork" /> commit the page quotes and the tag that keeps it reachable,
///     the <paramref name="Upstream" /> commit the fork branched from, the <paramref name="Solution" /> and the
///     <paramref name="SpecProject" /> as forward-slash paths relative to the checkout's root, and what a load of
///     that commit reports that the page has <paramref name="Declared" />.
/// </summary>
internal sealed record ShowcasePin(
    ShowcaseRepository Fork,
    ShowcaseRepository Upstream,
    string Solution,
    string SpecProject,
    ShowcaseDeclarations Declared)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    /// <summary>
    ///     Parses the pin <paramref name="json" /> holds. A missing member the record requires, a null it does not
    ///     allow or a member it does not declare throws a <see cref="JsonException" />.
    /// </summary>
    internal static ShowcasePin Parse(string json)
    {
        return JsonSerializer.Deserialize<ShowcasePin>(json, Options)
               ?? throw new JsonException("The showcase pin is null.");
    }
}

/// <summary>
///     One repository the pin names: its <paramref name="Repository" /> URL, the <paramref name="Commit" />, and the
///     <paramref name="Tag" /> at that commit where one is pushed.
/// </summary>
internal sealed record ShowcaseRepository(string Repository, string Commit, string? Tag = null);

/// <summary>
///     What a load of the pinned commit reports beside its model, each relative to the checkout's root with forward
///     slashes: the <paramref name="UnsupportedProjects" /> no extractor reads, the <paramref name="LoadDiagnostics" />
///     other than NuGet audit advisories, and the <paramref name="MergeNotes" /> the extraction raises.
/// </summary>
internal sealed record ShowcaseDeclarations(
    IReadOnlyList<string> UnsupportedProjects,
    IReadOnlyList<string> LoadDiagnostics,
    IReadOnlyList<string> MergeNotes);
