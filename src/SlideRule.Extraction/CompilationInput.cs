using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction;

/// <summary>
///     One compilation to extract from: the <see cref="Compilation" /> itself, the project name its types are recorded
///     under, the names of the projects it declares a reference to, and the target framework it was compiled for.
///     Extraction takes a list of these and needs nothing else: no MSBuild, no workspace, no project file on disk.
///     Loading a solution produces them instead, filling the reference names in from each project's own
///     <c>ProjectReference</c> items. An input built by hand can pass an empty reference list, and its project
///     then references no other project in the model.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TargetFramework" /> is <see langword="null" /> wherever it is unknown or beside the
///         point: an input built by hand, and every project that targets a single framework. It is filled
///         in only where one project file yielded several compilations, which is exactly where the project
///         name alone no longer says which compilation a fact came from.
///     </para>
///     <para>
///         <see cref="SolutionMember" /> says whether the solution file declares this project. <see langword="false" />
///         marks one that a <c>ProjectReference</c> dragged into the workspace, and <see langword="null" /> means
///         nothing was read to answer with: every input built by hand, any run whose solution file would not parse, and
///         a project whose file path the load never reported.
///     </para>
///     <para>
///         <see cref="GeneratedTrees" /> names which of the compilation's syntax trees came from a source
///         generator. A type counts as generated when a <c>GeneratedCodeAttribute</c> sits on it or on a
///         type containing it, or when every file declaring it is generator output. A file is generator
///         output when the workspace reported it as source-generated, which is what this property records,
///         or when its leading comment carries an <c>&lt;auto-generated&gt;</c> banner.
///         <see langword="null" /> leaves the banner as the only signal. The file signals are read for the
///         compilation's own types only: a type from a referenced assembly is judged by the attribute alone.
///     </para>
///     <para>
///         <see cref="UndeclaredProjectReferences" /> names the projects the compilation references that
///         <see cref="ProjectReferences" /> does not account for, either directly or through what a declared
///         project references in turn. A load fills it in where it read the declarations from the project file;
///         <see langword="null" /> or empty says there is nothing to report, which is every input built by hand.
///     </para>
/// </remarks>
// GeneratedTrees holds trees by reference rather than document paths because a build that sets
// EmitCompilerGeneratedFiles moves every generated document off its pseudo-path.
internal sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    bool? SolutionMember = null,
    IReadOnlyCollection<SyntaxTree>? GeneratedTrees = null,
    IReadOnlyList<string>? UndeclaredProjectReferences = null);
