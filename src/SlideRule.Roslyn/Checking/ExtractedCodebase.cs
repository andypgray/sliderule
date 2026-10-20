using SlideRule.Codebase;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Checking;

/// <summary>
///     What one run's extraction produced: the codebase to check, and what the load knows about how complete
///     it is.
/// </summary>
/// <remarks>
///     The load facts travel with the codebase because a caller may open the workspace inside the extract
///     delegate, after <see cref="ArchCheckSequence" /> has loaded the baselines, and they exist only once it is
///     open.
/// </remarks>
internal sealed record ExtractedCodebase(CodebaseModel Codebase, WorkspaceDiagnostics Diagnostics);
