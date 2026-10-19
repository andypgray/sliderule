using Shouldly;
using SlideRule.Extraction;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Cancellation on the extraction path: a run that has been abandoned stops rather than finishing its
///     compilation, and taking a token changes nothing about the fragment a run that was not abandoned
///     produces.
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
    public void Extract_WithAnUncancellableTokenAndWithout_ProduceTheSameFragment()
    {
        CompilationInput input = CompilationFactory.Compile("TestProject", FragmentQuery.EveryPassSource);

        // The tokenless overload is the subject here, so this call takes no token.
#pragma warning disable xUnit1051
        string tokenless = FragmentExtractor.Extract(input)
            .Serialized();
#pragma warning restore xUnit1051
        string withNone = FragmentExtractor.Extract(input, CancellationToken.None)
            .Serialized();

        // Non-vacuity: both sides have to carry the bed, or an extraction that produced nothing at all
        // would satisfy the comparison below.
        tokenless.ShouldContain("N.Widget");
        withNone.ShouldBe(tokenless);
    }
}
