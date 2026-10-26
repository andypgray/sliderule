using Microsoft.CodeAnalysis;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     A source generator that adds each of <c>sources</c> as one post-initialization output under its hint
///     name.
/// </summary>
internal sealed class PostInitializationGenerator(params (string Hint, string Source)[] sources) : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(output =>
        {
            foreach ((string hint, string source) in sources)
                output.AddSource(hint, source);
        });
    }
}
