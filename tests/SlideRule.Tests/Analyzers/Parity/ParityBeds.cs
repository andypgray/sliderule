using System.Collections.Concurrent;
using SlideRule.Hosting;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     Each in-place bed's compilations, read off the warm pool once per class: nothing writes an in-place bed,
///     and a subject only reads the compilations it is handed.
/// </summary>
/// <remarks>
///     A bed's read takes no test's cancellation token, because the task it produces outlives the test that
///     first awaits it.
/// </remarks>
public sealed class ParityBeds
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ParityCompilation>>>> _beds =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     One solution under <paramref name="model" />, over the compilations this class has read off the bed
    ///     at <paramref name="solutionPath" />.
    /// </summary>
    internal async Task<ParitySubject> SubjectAsync(string name, ArchitectureModel model, string solutionPath)
    {
        Lazy<Task<IReadOnlyList<ParityCompilation>>> bed = _beds.GetOrAdd(
            solutionPath,
            path => new Lazy<Task<IReadOnlyList<ParityCompilation>>>(() =>
                ParityHarness.CompilationsOfAsync(path, CancellationToken.None)));
        IReadOnlyList<ParityCompilation> compilations = await bed.Value;

        return await ParityHarness.ForSolutionAsync(name, model, solutionPath, compilations);
    }
}
