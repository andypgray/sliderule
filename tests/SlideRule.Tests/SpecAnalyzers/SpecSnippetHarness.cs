using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SlideRule.SpecAnalyzers;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     Compiles a spec written as source text and runs the spec-literal analyzer over it the way a
///     compiler runs it, through <see cref="CompilationWithAnalyzers" />.
/// </summary>
/// <remarks>
///     <para>
///         The snippet is held to compiling cleanly first, which is why the entry point is named for an
///         assertion. An unbound call produces no invocation operation at all, so a typo in a snippet
///         would come back as the analyzer saying nothing — and a fact whose whole claim is silence would
///         pass on the strength of the typo. Checking the compilation before its warnings are read is what
///         makes every silent case mean what it says.
///     </para>
///     <para>
///         The reference set is the running test host's own assemblies, which is what puts the real
///         contract in reach rather than a stub: the analyzer reads a marker out of metadata and asks the
///         contract for the verdict, so a stand-in contract would be measuring the stand-in.
///         <see cref="WithoutTheContract" /> is the same set with the contract taken out, for the fact
///         that a project which never references it has nothing registered over it.
///     </para>
/// </remarks>
internal static class SpecSnippet
{
    /// <summary>The path a single-file snippet is parsed under.</summary>
    internal const string FilePath = "Spec.cs";

    /// <summary>
    ///     The language version every source here is parsed at, named rather than left to the compiler's
    ///     own default.
    /// </summary>
    /// <remarks>
    ///     A default is whatever version the compiler the tests happen to resolve calls current, which makes
    ///     the version a silent dependency of the package graph. Measured over the corpora compiled here it
    ///     has to be 14: the rule pack names an unbound generic in a <c>nameof</c>, which is a 14 feature,
    ///     and the anchor helper types the fixture corpus binds are structs with primary constructors, which
    ///     need 12. A source that failed to compile would produce no invocation operation and read as an
    ///     analyzer that agreed with a silent validator, so the version is part of what makes silence mean
    ///     something.
    /// </remarks>
    internal static CSharpParseOptions Language { get; } = new(LanguageVersion.CSharp14);

    /// <summary>The options every spec compilation here is built with: a library, with nullable annotations on.</summary>
    /// <remarks>
    ///     Nullable annotations on, which is how every spec project in this repository is built. The analysis
    ///     reports warnings and never errors, so it changes nothing the zero-errors assertion reads; what it
    ///     buys is that a real source file compiles here in the context it was written in.
    /// </remarks>
    internal static CSharpCompilationOptions Options { get; } =
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

    /// <summary>
    ///     The global usings the SDK writes for a class library with implicit usings on, as a source tree of
    ///     its own.
    /// </summary>
    /// <remarks>
    ///     A spec file written inside a project rather than as a snippet has no <c>using</c> of its own to
    ///     carry: it names <c>Arch</c>, <c>Selection</c> and the rest by namespace nesting from its own
    ///     file-scoped namespace, and reaches the framework types it anchors through these seven. This is
    ///     the whole of what a real spec file needs beside its own text, which is why a corpus of them can
    ///     be compiled from the repository as it is written.
    /// </remarks>
    private static (string Path, string Source) ImplicitUsings { get; } =
        ("ImplicitUsings.cs",
            """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """);

    /// <summary>The test assembly's own name, which a corpus read out of it is compiled under.</summary>
    internal static string Suite { get; } = typeof(SpecSnippet).Assembly.GetName().Name!;

    /// <summary>
    ///     The references a spec project compiles against: the platform, plus exactly one copy of the
    ///     contract.
    /// </summary>
    internal static ImmutableArray<MetadataReference> WithTheContract { get; } = TrustedPlatform.References;

    /// <summary>The same set less the contract, which is a project with no spec in it.</summary>
    internal static ImmutableArray<MetadataReference> WithoutTheContract { get; } =
        TrustedPlatform.Without(typeof(Arch).Assembly.Location);

    /// <summary>
    ///     <see cref="WithTheContract" /> less the assemblies named, each by file name.
    /// </summary>
    /// <remarks>
    ///     What a compilation built from an assembly's own sources needs: with that assembly still
    ///     referenced its types arrive from metadata as well as from source, and a source file left out by
    ///     mistake would bind against the compiled copy instead of failing. Taking it out makes the
    ///     zero-errors assertion the guard that every source the compilation needs is actually in it.
    /// </remarks>
    /// <param name="assemblyFileNames">The file names to leave out, such as <c>Some.Assembly.dll</c>.</param>
    internal static ImmutableArray<MetadataReference> Less(params string[] assemblyFileNames)
    {
        return TrustedPlatform.Less(assemblyFileNames);
    }

    /// <summary>
    ///     Compiles <paramref name="source" /> as one file and returns what the analyzer reported over it,
    ///     ordered by position.
    /// </summary>
    /// <param name="source">The spec as it is written.</param>
    /// <param name="beforeDecide">The fault seam, invoked at the top of every call into the contract.</param>
    /// <param name="cancellationToken">The token the compilation and the analysis run under.</param>
    internal static Task<IReadOnlyList<Diagnostic>> ShouldCompileAndDiagnoseAsync(
        string source, Action? beforeDecide = null, CancellationToken cancellationToken = default)
    {
        return ShouldCompileAndDiagnoseAsync([(FilePath, source)], beforeDecide: beforeDecide,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     The same over several files and a chosen reference set, which is the shape a larger corpus is
    ///     driven through.
    /// </summary>
    /// <param name="files">The sources, each with the path it is parsed under.</param>
    /// <param name="references">The references to compile against; the contract's set by default.</param>
    /// <param name="beforeDecide">The fault seam, invoked at the top of every call into the contract.</param>
    /// <param name="cancellationToken">The token the compilation and the analysis run under.</param>
    /// <param name="assemblyName">
    ///     The identity the compilation takes. It decides friend access, so a corpus read out of an assembly
    ///     that the contract names a friend has to be compiled under that assembly's own name or the
    ///     internals it reaches will not bind.
    /// </param>
    /// <param name="versions">
    ///     The version seam, which hands the version check the two informational versions it compares in place
    ///     of the two assemblies' own.
    /// </param>
    internal static async Task<IReadOnlyList<Diagnostic>> ShouldCompileAndDiagnoseAsync(
        IReadOnlyList<(string Path, string Source)> files,
        IReadOnlyList<MetadataReference>? references = null,
        Action? beforeDecide = null,
        CancellationToken cancellationToken = default,
        string assemblyName = "Spec",
        Func<(string? Analyzer, string? Core)>? versions = null)
    {
        SyntaxTree[] trees = files
            .Select(file => CSharpSyntaxTree.ParseText(
                file.Source, Language, file.Path, cancellationToken: cancellationToken))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName, trees, references ?? WithTheContract, Options);

        var analyzer = new SpecLiteralAnalyzer(beforeDecide, versions);
        CompilationWithAnalyzers withAnalyzers =
            compilation.WithAnalyzers([analyzer]);

        // One bind serves both halves, and the compiler's is held to zero errors before the analyzer's is read.
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync(cancellationToken);
        diagnostics.Where(IsCompilers)
            .ShouldHaveNothingToCompileAbout();

        return diagnostics
            .Where(diagnostic => !IsCompilers(diagnostic))
            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Compiles an assembly's own <paramref name="sources" /> under its own name and less its own
    ///     metadata, with the implicit usings beside them, and returns what the analyzer reported.
    /// </summary>
    /// <param name="sources">The assembly's sources, each with the path it is parsed under.</param>
    /// <param name="assemblyName">The assembly the sources belong to, whose identity the compilation takes.</param>
    /// <param name="beforeDecide">The fault seam, invoked at the top of every call into the contract.</param>
    /// <param name="cancellationToken">The token the compilation and the analysis run under.</param>
    internal static Task<IReadOnlyList<Diagnostic>> ShouldCompileOwnSourcesAndDiagnoseAsync(
        IReadOnlyList<(string Path, string Source)> sources,
        string assemblyName,
        Action? beforeDecide = null,
        CancellationToken cancellationToken = default)
    {
        List<(string Path, string Source)> files = [.. sources, ImplicitUsings];
        ImmutableArray<MetadataReference> references = Less($"{assemblyName}.dll");

        return ShouldCompileAndDiagnoseAsync(
            files, references, beforeDecide: beforeDecide, cancellationToken: cancellationToken,
            assemblyName: assemblyName);
    }

    /// <summary>
    ///     The statements as a spec, one per line: the smallest class that can carry them, so what a caller
    ///     shows is the whole of what draws a warning.
    /// </summary>
    /// <param name="statements">The statements of <c>Define</c>.</param>
    internal static string Around(params string[] statements)
    {
        IEnumerable<string> body = statements.Select(statement => "        " + statement);
        string[] lines =
        [
            "using SlideRule;",
            "",
            "public sealed class Spec",
            "{",
            "    public void Define(Arch arch)",
            "    {",
            .. body,
            "    }",
            "}"
        ];

        return string.Join(Environment.NewLine, lines);
    }

    // The compiler's own diagnostics, told from the analyzer's and the driver's by the prefix every C#
    // compiler ID carries.
    private static bool IsCompilers(Diagnostic diagnostic)
    {
        return diagnostic.Id.StartsWith("CS", StringComparison.Ordinal);
    }
}
