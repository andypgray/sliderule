using Shouldly;
using SlideRule.Extraction;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Cancellation on the extraction path: a run that has been abandoned stops rather than finishing its
///     compilation, whether it extracts one input or every input of a solution.
/// </summary>
public sealed class FragmentExtractorCancellationTests
{
    [Fact]
    public void Extract_TokenAlreadyCancelled_ThrowsRatherThanReturningAFragment()
    {
        CompilationInput input = CompilationFactory.Compile("TestProject", FragmentQuery.EveryPassSource);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => FragmentExtractor.Extract(input, cancelled.Token));
    }

    [Fact]
    public void ExtractAll_TokenAlreadyCancelled_ThrowsRatherThanReturningFragments()
    {
        CompilationInput input = CompilationFactory.Compile("TestProject", FragmentQuery.EveryPassSource);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => FragmentExtractor.ExtractAll([input], null, cancelled.Token));
    }
}
