using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.ModelFile;

namespace SlideRule.Analyzers;

/// <summary>
///     One model file's text read once: what the read produced and, for a text that read, the rules one
///     project's compilation can decide on its own; or the fault that stopped the read.
/// </summary>
/// <remarks>
///     <para>
///         Immutable, and built by <see cref="Load" /> alone, because the analyzer keeps one of these per
///         model file path for as long as the file is unchanged and hands the same instance to every
///         compilation that names it. Anything mutable here would be shared across those compilations'
///         analyses, which run concurrently.
///     </para>
///     <para>
///         <see cref="Load" /> is the path-keyed read's function and throws nothing but a cancellation, which
///         travels out through the compilation start and is kept nowhere. The reader answers any content
///         with a refusal rather than an exception, and every node class a read model can carry has a row in
///         the one-project decidability table, which an inventory gate holds true; what is left is a copy of
///         SlideRule that will not bind, and that is held as <see cref="Fault" />. The read itself is
///         the boundary <c>From</c>, called only inside Load's guard: an exception let out of it would surface
///         as the compiler's own <c>AD0001</c> rather than as the analyzer's fault warning.
///     </para>
///     <para>
///         <see cref="Result" /> and <see cref="InRules" /> name Core, so each is a boundary of its own, read
///         only by another boundary and never on a faulted model.
///     </para>
/// </remarks>
internal sealed class LoadedModel : IReadHandle
{
    private readonly ModelReadResult? _result;
    private readonly IReadOnlyList<ArchRule>? _inRules;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private LoadedModel(ModelReadResult result, IReadOnlyList<ArchRule> inRules)
    {
        _result = result;
        _inRules = inRules;
    }

    private LoadedModel(Exception fault)
    {
        Fault = fault;
    }

    /// <inheritdoc />
    public Exception? Fault { get; }

    /// <summary>The document the text carried, or why it is not a model file.</summary>
    internal ModelReadResult Result
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => _result!;
    }

    /// <summary>
    ///     The rules whose verdict follows from one project's compilation, in model order; empty for a
    ///     refused text.
    /// </summary>
    internal IReadOnlyList<ArchRule> InRules
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => _inRules!;
    }

    /// <summary>
    ///     Reads <paramref name="text" /> as a model file, holding any fault but a cancellation as the
    ///     model's own.
    /// </summary>
    /// <param name="text">The model file's text.</param>
    /// <param name="beforeRead">A test seam run first inside the guard; null in production.</param>
    internal static LoadedModel Load(SourceText text, Action? beforeRead)
    {
        try
        {
            beforeRead?.Invoke();

            return From(text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new LoadedModel(exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LoadedModel From(SourceText text)
    {
        ModelReadResult result = ModelDocumentReader.Read(text.ToString());
        if (result.Document is not { } document) return new LoadedModel(result, Array.Empty<ArchRule>());

        ArchitectureModel model = document.ToModel();
        var inRules = new List<ArchRule>(model.Rules.Count);
        foreach (ArchRule rule in model.Rules)
            if (OneProjectDecidability.Classify(rule).Decidable)
                inRules.Add(rule);

        return new LoadedModel(result, inRules);
    }
}
