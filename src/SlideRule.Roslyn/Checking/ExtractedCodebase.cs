using SlideRule.Codebase;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Checking;

/// <summary>
///     What one run's extraction produced: the codebase to check, and what the load knows about how complete
///     it is.
/// </summary>
/// <remarks>
///     The two travel together because the second is only known once the workspace is open, and the xUnit
///     adapter opens it <em>inside</em> the extract delegate — so load facts handed to
///     <see cref="ArchCheckSequence" /> eagerly would be read before the load that measures them, and loading
///     earlier to have them would put extraction ahead of the baselines both of that type's xmldocs commit to.
/// </remarks>
/// <param name="Codebase">The extracted model the rules are evaluated against.</param>
/// <param name="Diagnostics">
///     What the load reported: the declared projects this run did not check
///     (<see cref="WorkspaceDiagnostics.UncheckedProjects" />, empty on any run no filter narrowed), and the
///     projects that failed to load or whose packages did not resolve
///     (<see cref="WorkspaceDiagnostics.IsIncomplete" />, false on a whole model).
/// </param>
internal sealed record ExtractedCodebase(CodebaseModel Codebase, WorkspaceDiagnostics Diagnostics);
