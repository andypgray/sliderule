using SlideRule.Cli.Pipeline;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     What <see cref="MsBuildGate" />'s replay-aware source selection reads off a <c>check</c>, <c>status</c> or
///     <c>graph</c> request: the inputs its decision turns on, and nothing else.
/// </summary>
internal interface IReplayableRequest
{
    /// <summary>The solution argument, settings applied: a file, a directory, or null for discovery.</summary>
    string? Solution { get; }

    /// <summary>The directory solution discovery walks up from.</summary>
    string WorkingDirectory { get; }

    /// <summary>The explicit <c>--binlog</c> to replay instead of a design-time build, or null to auto-select.</summary>
    string? Binlog { get; }

    /// <summary>Whether to bypass the persisted caches, the extraction fragments and the build capture alike.</summary>
    bool NoCache { get; }
}
