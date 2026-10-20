using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction;

/// <summary>
///     One compilation to extract from, with what extraction needs to know beside it: extraction itself reads
///     no MSBuild, no workspace and no project file on disk.
/// </summary>
/// <remarks>
///     <see cref="ProjectName" /> names the project that produced the compilation, never its assembly name,
///     which a project selection would miss. <see cref="ProjectReferences" /> names the projects that project
///     declares a reference to; an input built by hand may pass an empty list, and its project then references no
///     other project in the model. <see cref="TargetFramework" /> is <see langword="null" /> except where one project
///     file yielded several compilations. <see cref="SolutionMember" /> is <see langword="false" /> for a project
///     a <c>ProjectReference</c> pulled into the workspace, and <see langword="null" /> where nothing was read to
///     answer with: an input built by hand, a solution file that would not parse, a project whose path the load
///     never reported. <see cref="GeneratedTrees" /> holds the trees the workspace reported as source-generated,
///     by reference rather than document path, because a build that sets <c>EmitCompilerGeneratedFiles</c> moves
///     every generated document off its pseudo-path. <see cref="UndeclaredProjectReferences" /> names the
///     referenced projects that <see cref="ProjectReferences" /> does not account for, directly or through what a
///     declared project references in turn; <see langword="null" /> or empty reports none.
/// </remarks>
internal sealed record CompilationInput(
    Compilation Compilation,
    string ProjectName,
    IReadOnlyList<string> ProjectReferences,
    string? TargetFramework = null,
    bool? SolutionMember = null,
    IReadOnlyCollection<SyntaxTree>? GeneratedTrees = null,
    IReadOnlyList<string>? UndeclaredProjectReferences = null);
