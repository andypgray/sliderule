using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The specs this repository actually writes, compiled from their own sources and held to drawing no
///     warning at all: the self-spec, the canonical sample, and the rule pack.
/// </summary>
/// <remarks>
///     <para>
///         The fixture corpus proves the analyzer says what spec-load validation says about specs that are
///         wrong. This proves the other half over specs that are right, which is the half an estate feels:
///         a warning here would arrive on every build of a spec nobody can fix, because there is nothing
///         wrong with it. Silence is the whole claim, so each compilation is held to zero errors first —
///         an unbound call produces no invocation operation, and a source that failed to compile would be
///         silent for the wrong reason.
///     </para>
///     <para>
///         Silence alone could also mean the analyzer never reached anything. Each fact therefore counts
///         the literals that reached the contract, through the seam the analyzer already carries for its
///         fault tests, and holds that count above zero. The count is one per literal written at a marked
///         parameter, plus one per method body that declared a rule, scope or layer name, so it is a floor
///         on the marked arguments seen rather than an exact inventory of them.
///     </para>
///     <para>
///         <b>The example specs are left out.</b> Each is written against the product assemblies of its own
///         example solution, and those are not in this project's output, so a compilation of one here would
///         have unbound names throughout. A compilation with errors proves nothing about an analyzer that
///         reads invocation operations, and asserting silence over it would be asserting silence about a
///         file the compiler never bound.
///     </para>
/// </remarks>
public sealed class SpecRealCorpusTests
{
    private const string SelfSpecAssembly = "SlideRule.Architecture";

    private const string RulePackAssembly = "SlideRule.Packs.DotNet";

    private const string SelfSpecDirectory = "arch/" + SelfSpecAssembly + "/";

    private const string RulePackDirectory = "src/" + RulePackAssembly + "/";

    private const string StubDirectory = "tests/SlideRule.Tests/Stubs/";

    private const string CanonicalSample = "tests/SlideRule.Tests/CanonicalSampleSpec.cs";

    private const string Vacuous =
        "The analyzer read no literal at a marked parameter in this compilation, so its silence says "
        + "nothing about the spec: a corpus it never walked and a corpus with nothing wrong in it are the "
        + "same answer.";

    [Fact]
    public async Task TheSelfSpec_DrawsNoWarning()
    {
        // Arrange: the spec project's own sources, less its own assembly, so what compiles is the source.
        List<(string Path, string Source)> sources = Under(SelfSpecDirectory);

        // Act
        (IReadOnlyList<Diagnostic> reported, int reached) = await AnalyzeAsync(sources, SelfSpecAssembly);

        // Assert
        reported.ShouldBeSilent();
        reached.ShouldBeGreaterThan(0, Vacuous);
    }

    [Fact]
    public async Task TheCanonicalSample_DrawsNoWarning()
    {
        // Arrange: the sample and the product stand-ins it names, which are the whole of what it binds.
        List<(string Path, string Source)> sources = [Read(CanonicalSample), .. Under(StubDirectory)];

        // Act
        (IReadOnlyList<Diagnostic> reported, int reached) = await AnalyzeAsync(sources, SpecSnippet.Suite);

        // Assert
        reported.ShouldBeSilent();
        reached.ShouldBeGreaterThan(0, Vacuous);
    }

    [Fact]
    public async Task TheRulePack_DrawsNoWarning()
    {
        // Arrange: a pack declares its rules in methods a consuming spec calls, which is the shape the
        // analyzer reads as a declaration whether or not any spec ever makes the call.
        List<(string Path, string Source)> sources = Under(RulePackDirectory);

        // Act
        (IReadOnlyList<Diagnostic> reported, int reached) = await AnalyzeAsync(sources, RulePackAssembly);

        // Assert
        reported.ShouldBeSilent();
        reached.ShouldBeGreaterThan(0, Vacuous);
    }

    [Fact]
    public void EveryCompilation_IsBuiltFromSourcesThatExist()
    {
        // Each corpus is named by directory so a file added to one joins it, which only holds while each
        // directory has files in it: an empty one would compile cleanly and report nothing.
        List<string> directories = [SelfSpecDirectory, RulePackDirectory, StubDirectory];
        List<string> empty = directories
            .Where(directory => Under(directory).Count == 0)
            .Select(directory => $"{directory} contributes no source to the corpus compiled from it.")
            .ToList();

        empty.ShouldReportNothing("Corpus director(ies) are empty, so the compilation built from them holds "
                                  + "no spec and its silence means nothing");
    }

    // The analyzer over one compilation, under the identity of the assembly the sources belong to and less
    // that assembly's own metadata, so the source is what binds and the friend access it really has is the
    // friend access it gets here. The literals that reached the contract are counted through the seam the
    // analyzer carries; the analysis runs concurrently, so the increment is interlocked.
    private static async Task<(IReadOnlyList<Diagnostic> Reported, int Reached)> AnalyzeAsync(
        IReadOnlyList<(string Path, string Source)> sources, string assemblyName)
    {
        var reached = 0;
        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileOwnSourcesAndDiagnoseAsync(
            sources, assemblyName, () => Interlocked.Increment(ref reached), Ct);

        return (reported, reached);
    }

    // Every tracked C# source under a directory, in path order. Tracked rather than enumerated off disk so
    // that build output contributes nothing and a file in a subdirectory is not missed.
    private static List<(string Path, string Source)> Under(string directory)
    {
        return TrackedFiles.CSharp
            .Where(path => path.StartsWith(directory, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(Read)
            .ToList();
    }

    private static (string Path, string Source) Read(string path)
    {
        return (path, RepoRoot.ReadText(path));
    }
}
