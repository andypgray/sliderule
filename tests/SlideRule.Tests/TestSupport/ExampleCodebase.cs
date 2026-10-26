using System.Collections.Concurrent;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Each example solution extracted once per run, the way the verbs extract it under the spec its convention
///     resolves to, and shared by every caller.
/// </summary>
/// <remarks>
///     The extraction starts at most once per example and every caller awaits that one task, so a caller reads
///     what it is handed and changes none of it. It takes no test's cancellation token, because the task it
///     produces outlives the test that first awaits it.
/// </remarks>
internal static class ExampleCodebase
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<SpecfulCodebase>>> Extractions =
        new(StringComparer.Ordinal);

    /// <summary><paramref name="example" /> extracted under the spec its convention resolves to.</summary>
    internal static Task<SpecfulCodebase> Of(ExampleSolution example)
    {
        Lazy<Task<SpecfulCodebase>> extraction = Extractions.GetOrAdd(
            example.SolutionPath,
            static (_, solution) => new Lazy<Task<SpecfulCodebase>>(() => ExtractAsync(solution)),
            example);

        return extraction.Value;
    }

    private static Task<SpecfulCodebase> ExtractAsync(ExampleSolution example)
    {
        return SpecfulCodebase.ExtractAsync(
            example.SolutionPath, spec: null, example.SolutionDirectory, CancellationToken.None);
    }
}
