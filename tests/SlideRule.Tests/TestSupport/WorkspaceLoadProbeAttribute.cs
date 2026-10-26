using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit.v3;

[assembly: WorkspaceLoadProbe]

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Attributes every <c>MSBuildWorkspace</c> load to the test that caused it, so the suite's dominant
///     cost can be measured rather than estimated. Assembly-scoped, and inert unless
///     <see cref="ProbeFileVariable" /> names an output file — an off run pays one environment read per
///     test and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         Set <c>SLIDERULE_LOAD_PROBE</c> to a path and run the suite; the file is written at pipeline
///         shutdown as TSV (<c>class</c>, <c>method</c>, <c>loads</c>, <c>ms</c>), one row per test that
///         opened at least one workspace, plus a trailing total. Each row counts the loads on its own test's
///         async flow, so it is exact in a parallel run too; the total is the process-wide count.
///     </para>
/// </remarks>
internal sealed class WorkspaceLoadProbeAttribute : BeforeAfterTestAttribute
{
    /// <summary>Names the TSV the probe writes at shutdown. Unset (the default) disables the probe.</summary>
    private const string ProbeFileVariable = "SLIDERULE_LOAD_PROBE";

    private static readonly ConcurrentQueue<string> Rows = new();

    private static readonly ConcurrentDictionary<IXunitTest, Reading> Started = new();

    private static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProbeFileVariable));

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!Enabled) return;

        // Installed here, on the flow the test body then runs on, so the box counts this test's loads alone.
        StrongBox<long> loads = WorkspaceLoader.CountLoadsOnThisFlow();
        Started[test] = new Reading(loads, Environment.TickCount64);
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!Started.TryRemove(test, out Reading before)) return;

        long loads = Interlocked.Read(ref before.Loads.Value);
        if (loads == 0) return;

        long elapsed = Environment.TickCount64 - before.Ticks;
        string testClass = methodUnderTest.DeclaringType?.Name ?? "(unknown)";
        Rows.Enqueue($"{testClass}\t{methodUnderTest.Name}\t{loads}\t{elapsed}");
    }

    /// <summary>Writes the collected rows in class and method order, then the total and pool-reuse lines.</summary>
    /// <remarks>A write failure is swallowed, since a diagnostic must never fail a run.</remarks>
    internal static void Flush()
    {
        string? path = Environment.GetEnvironmentVariable(ProbeFileVariable);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            string[] lines =
            [
                "class\tmethod\tloads\tms",
                .. Rows.OrderBy(r => r, StringComparer.Ordinal),
                $"TOTAL\t\t{WorkspaceLoader.LoadCount}\t",
                // The other half of the story: how many acquisitions a warm pooled session answered instead.
                $"POOL-REUSED\t\t{WarmWorkspacePool.ReuseCount}\t"
            ];
            File.WriteAllLines(path, lines);
        }
        catch
        {
            // best-effort: the probe is a diagnostic, never a reason to fail a run.
        }
    }

    private readonly record struct Reading(StrongBox<long> Loads, long Ticks);
}
