using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Baselines;

namespace SlideRule.Analyzers;

/// <summary>
///     One baseline file the build handed over: what reading it produced, or the fault that stopped the read.
/// </summary>
/// <remarks>
///     <para>
///         The text is not kept here, because one read answers every compilation whose text at the path is
///         unchanged. A refusal is reported at a position in the text, in a different action from the read, so
///         the caller holds the compilation's own text beside the read: an action that fetched the text a second
///         time could be reporting a position into different bytes.
///     </para>
///     <para>
///         What the baseline read keeps per path, so it is immutable and built by <see cref="Load" /> alone,
///         under the contract <see cref="LoadedModel" /> records for the model read.
///     </para>
/// </remarks>
internal sealed class HandedBaseline : IReadHandle
{
    private readonly BaselineReadResult? _result;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private HandedBaseline(BaselineReadResult result)
    {
        _result = result;
    }

    private HandedBaseline(Exception fault)
    {
        Fault = fault;
    }

    /// <inheritdoc />
    public Exception? Fault { get; }

    /// <summary>The document the text carried, or why it is not a baseline file.</summary>
    internal BaselineReadResult Result
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => _result!;
    }

    /// <summary>
    ///     Reads <paramref name="text" /> as a baseline file, holding any fault but a cancellation as the
    ///     read's own; <paramref name="beforeRead" /> is a test seam run first inside the guard, null in
    ///     production.
    /// </summary>
    internal static HandedBaseline Load(SourceText text, Action? beforeRead)
    {
        try
        {
            beforeRead?.Invoke();

            return From(text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HandedBaseline(exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static HandedBaseline From(SourceText text)
    {
        return new HandedBaseline(BaselineDocumentReader.Read(text.ToString()));
    }
}
