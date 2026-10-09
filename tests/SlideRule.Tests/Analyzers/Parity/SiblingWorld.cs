using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     A solution built out of source strings: some units are projects of it, some are packages it
///     references, and each project's compilation is what a build of that project would hand an analyzer.
/// </summary>
/// <remarks>
///     <para>
///         The difference between a project and a package is the whole point of the type. A project is
///         extracted as one of the solution's own, and its name appears in the model file beside the
///         assembly it compiles to; a package is referenced and never extracted, so its types are
///         external to every project. The two are spelled differently here because a fixture that
///         confused them would assert a project graph no build declares.
///     </para>
///     <para>
///         Project names and assembly names are deliberately different words throughout, because the
///         build tier has to answer "whose code is this" from the assembly and report it under the
///         project.
///     </para>
///     <para>
///         A project may hold several documents, and two of them may share a path. The path is what a
///         finding is reported at and what a document is asked for, so a world about which document
///         answers is a world whose files are named by hand.
///     </para>
/// </remarks>
internal sealed class SiblingWorld
{
    /// <summary>Where the build says the model file sits; every baseline path resolves against it.</summary>
    internal const string ModelDirectory = "/repo";

    private readonly List<Unit> _units = [];

    /// <summary>An assembly the solution references and does not build.</summary>
    /// <param name="assemblyName">The assembly's name, which is also how other units name it here.</param>
    /// <param name="source">Its one source file.</param>
    internal SiblingWorld Package(string assemblyName, string source)
    {
        return Add(null, assemblyName, [($"{assemblyName}.cs", source)], OutputKind.DynamicallyLinkedLibrary, []);
    }

    /// <summary>A project of the solution, compiled and extracted as one of its own.</summary>
    /// <param name="projectName">The project's name, as the model file records it.</param>
    /// <param name="assemblyName">The assembly it compiles to, which is also how other units name it here.</param>
    /// <param name="source">Its one source file, which is given the assembly's own name.</param>
    /// <param name="references">The assemblies it is compiled against.</param>
    internal SiblingWorld Project(
        string projectName, string assemblyName, string source, params string[] references)
    {
        return Project(projectName, assemblyName, [($"{assemblyName}.cs", source)], references);
    }

    /// <summary>The same project over several documents, each at the path the caller names it by.</summary>
    /// <remarks>
    ///     Two files may share a path, which a compilation built by hand can hold and a build cannot: that
    ///     is one of the shapes the per-document pin is about, so the paths are taken as written rather than
    ///     deduped.
    /// </remarks>
    /// <param name="projectName">The project's name, as the model file records it.</param>
    /// <param name="assemblyName">The assembly it compiles to, which is also how other units name it here.</param>
    /// <param name="files">Its documents, each as the path a site is reported at and the source at it.</param>
    /// <param name="references">The assemblies it is compiled against.</param>
    internal SiblingWorld Project(
        string projectName, string assemblyName, IReadOnlyList<(string Path, string Source)> files,
        params string[] references)
    {
        return Add(projectName, assemblyName, files, OutputKind.DynamicallyLinkedLibrary, references);
    }

    /// <summary>
    ///     The same project compiled as a console application — the output kind that makes Roslyn synthesize
    ///     the <c>Program</c> a file of top-level statements is the body of.
    /// </summary>
    /// <remarks>
    ///     A method of its own rather than an argument on the overload above: an optional parameter ahead of
    ///     a <c>params</c> array cannot be skipped by a caller that passes references positionally, so every
    ///     existing call would have had to name it.
    /// </remarks>
    /// <param name="projectName">The project's name, as the model file records it.</param>
    /// <param name="assemblyName">The assembly it compiles to, which is also how other units name it here.</param>
    /// <param name="files">Its documents, each as the path a site is reported at and the source at it.</param>
    /// <param name="references">The assemblies it is compiled against.</param>
    internal SiblingWorld ConsoleProject(
        string projectName, string assemblyName, IReadOnlyList<(string Path, string Source)> files,
        params string[] references)
    {
        return Add(projectName, assemblyName, files, OutputKind.ConsoleApplication, references);
    }

    /// <summary>
    ///     This world under the spec <paramref name="define" /> builds, prepared for the pin.
    /// </summary>
    /// <param name="name">What a failure calls this subject.</param>
    /// <param name="define">The spec.</param>
    /// <param name="baselines">
    ///     Each baseline file the spec names, as the path a rule carries and the file's text. Both sides
    ///     read the same text through the same reader, so a seal or a site count means the same thing to
    ///     the whole-solution check and to the build.
    /// </param>
    internal ParitySubject Subject(string name, Action<Arch> define, params (string Path, string Text)[] baselines)
    {
        List<Unit> projects = _units
            .Where(unit => unit.ProjectName is not null)
            .OrderBy(unit => unit.ProjectName, StringComparer.Ordinal)
            .ToList();
        projects.ShouldNotBeEmpty(name);

        List<CompilationInput> inputs = projects
            .Select(unit => new CompilationInput(unit.Compilation, unit.ProjectName!, ProjectNamesOf(unit)))
            .ToList();
        CodebaseModel codebase = CodebaseExtractor.ExtractFromCompilations(inputs);
        ArchitectureModel model = Checker.Model(define);

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var documents = new Dictionary<string, BaselineDocument>(StringComparer.Ordinal);
        foreach ((string path, string text) in baselines)
        {
            texts[path] = text;
            BaselineReadResult read = BaselineDocumentReader.Read(text);
            read.Refusal?.Detail.ShouldBeNull(path);
            documents[path] = read.Document.ShouldNotBeNull(path);
        }

        BaselineIndex index = BaselineIndexBuilder.ForRules(
            model.Rules, path => documents.GetValueOrDefault(path));
        List<ParityCompilation> compilations = projects
            .Select(unit => new ParityCompilation(unit.ProjectName!, unit.Compilation))
            .ToList();

        return ParityHarness.ForCompilations(
            name, model, codebase, index, ModelDirectory, texts, compilations);
    }

    private SiblingWorld Add(
        string? projectName, string assemblyName, IReadOnlyList<(string Path, string Source)> files,
        OutputKind kind, string[] references)
    {
        MetadataReference[] metadata =
        [
            CompilationFactory.CoreLibrary,
            .. references.Select(reference => Named(reference).Compilation.ToMetadataReference())
        ];
        CSharpCompilation compilation = CompilationFactory.CreateCompilation(
            assemblyName, metadata, kind, [.. files]);
        _units.Add(new Unit(assemblyName, projectName, references, compilation));

        return this;
    }

    // Only the project units count as project references: a package is referenced and never built here,
    // and naming one would put it in the model file's project graph.
    private IReadOnlyList<string> ProjectNamesOf(Unit unit)
    {
        return unit.References
            .Select(Named)
            .Where(referenced => referenced.ProjectName is not null)
            .Select(referenced => referenced.ProjectName!)
            .OrderBy(projectName => projectName, StringComparer.Ordinal)
            .ToList();
    }

    private Unit Named(string assemblyName)
    {
        return _units.Single(unit => string.Equals(unit.AssemblyName, assemblyName, StringComparison.Ordinal));
    }

    private sealed record Unit(
        string AssemblyName,
        string? ProjectName,
        string[] References,
        CSharpCompilation Compilation);
}
