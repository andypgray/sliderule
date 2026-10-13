using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The non-parallel collection for the test classes that read or write state the whole test process
///     shares. Membership is <c>[Collection]</c>(<c>"Serial"</c>).
/// </summary>
/// <remarks>
///     <para>
///         The runner starts a <c>DisableParallelization</c> collection only once every parallel collection
///         has finished, and runs its tests one at a time, so a member never overlaps any other test in the
///         assembly. That is the whole guarantee, and it is what a member needs: a class belongs here when
///         it sets a process environment variable (<see cref="ScopedEnvironmentVariable" />), reads or resets
///         <see cref="SlideRule.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" />'s in-flight counter or
///         <see cref="SlideRule.Cli.Mcp.Infrastructure.ServerShutdown" />'s static state, needs
///         <see cref="WarmWorkspacePool" /> to keep one session across two calls or asserts on the sessions it
///         holds, or times a deadline that a busy machine would miss. A test that needs the workspace loads or
///         the MSBuild-gate branch of one call reads it off <c>CliResult</c>, which counts that call alone, so
///         that is no reason to be a member.
///     </para>
///     <para>
///         Spawning a child process or loading an MSBuild workspace is not a reason to be here: those
///         classes run in parallel, each on its own fixture copy. Every member's whole run adds to the
///         suite's wall time, because nothing overlaps it, so a class joins only for one of the reasons above.
///     </para>
/// </remarks>
[CollectionDefinition("Serial", DisableParallelization = true)]
public sealed class SerialCollection;
