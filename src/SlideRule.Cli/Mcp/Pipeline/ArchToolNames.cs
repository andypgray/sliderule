namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The wire names of the <c>arch_*</c> tools, in one place so the tool declarations and the pipeline
///     stages that key on a tool name cannot drift apart.
/// </summary>
/// <remarks>
///     The pipeline must name a tool without referencing the tool type, which reaches Roslyn workspace types
///     through its runners and must not be JITted on the startup path, before MSBuildLocator has registered.
/// </remarks>
internal static class ArchToolNames
{
    /// <summary>The whole-spec check tool.</summary>
    public const string Check = "arch_check";

    /// <summary>The ratchet burndown tool.</summary>
    public const string Status = "arch_status";

    /// <summary>The single-rule explanation tool.</summary>
    public const string Explain = "arch_explain";

    /// <summary>The path → scope-card tool.</summary>
    public const string Context = "arch_context";

    /// <summary>The pre-spec codebase survey tool.</summary>
    public const string Graph = "arch_graph";
}
