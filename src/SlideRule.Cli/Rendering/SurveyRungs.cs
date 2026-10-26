namespace SlideRule.Cli.Rendering;

/// <summary>
///     The rung at which each section of the codebase survey leaves, read by the human survey and the
///     <c>graph --json</c> document alike, so the two cannot elide a section at different grains.
/// </summary>
internal static class SurveyRungs
{
    /// <summary>Each project's namespace inventory.</summary>
    internal const DocumentGrain Namespaces = DocumentGrain.Overview;

    /// <summary>Each project's declared packages.</summary>
    internal const DocumentGrain PackageReferences = DocumentGrain.Skeleton;

    /// <summary>The external-reference rows.</summary>
    internal const DocumentGrain ExternalEdges = DocumentGrain.Skeleton;

    /// <summary>The rows of the two coverage statements: multiply-declared and shadowed types.</summary>
    internal const DocumentGrain CoverageStatements = DocumentGrain.Skeleton;

    /// <summary>A project row's qualifiers, leaving what names and sizes the project.</summary>
    internal const DocumentGrain ProjectRow = DocumentGrain.Index;

    /// <summary>The observed project edges.</summary>
    internal const DocumentGrain ProjectEdges = DocumentGrain.Index;

    /// <summary>The load's own rows: the package audit and the workspace diagnostics.</summary>
    internal const DocumentGrain LoadReport = DocumentGrain.Index;
}
