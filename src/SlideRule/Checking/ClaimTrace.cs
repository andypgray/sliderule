namespace SlideRule.Checking;

/// <summary>How a selection stands toward one type, as <see cref="ClaimTracer" /> found it.</summary>
internal enum ClaimVerdict
{
    /// <summary>The selection names the type: its head admitted it and every adjective kept it.</summary>
    Claimed,

    /// <summary>The selection's head admitted the type and one of its own <c>Except</c>s removed it.</summary>
    Excepted,

    /// <summary>The selection's head admitted the type and another of its own adjectives dropped it.</summary>
    Narrowed,

    /// <summary>The selection's head never admitted the type.</summary>
    Unreached,

    /// <summary>Evaluating the selection failed, as a check of a rule over it would report.</summary>
    Failed
}

/// <summary>
///     One fact a claim trace rests on — the namespace a glob matched, the project that declares the type,
///     the adjective that kept or dropped it — with the facts beneath it.
/// </summary>
internal sealed class ClaimStep
{
    internal ClaimStep(string text, IReadOnlyList<ClaimStep>? steps = null)
    {
        Text = text;
        Steps = steps ?? Array.Empty<ClaimStep>();
    }

    internal string Text { get; }

    // A layer's definition, a union operand that carries adjectives of its own, a family cell and an Except's
    // payload each trace as a group under the step that names them.
    internal IReadOnlyList<ClaimStep> Steps { get; }
}

/// <summary>A selection's verdict on one type and the steps that decided it.</summary>
internal sealed class ClaimTrace
{
    internal static readonly ClaimTrace Unreached = new(ClaimVerdict.Unreached, Array.Empty<ClaimStep>());

    internal ClaimTrace(ClaimVerdict verdict, IReadOnlyList<ClaimStep> steps)
    {
        Verdict = verdict;
        Steps = steps;
    }

    internal ClaimVerdict Verdict { get; }

    // In evaluation order, ending at the step that dropped an Excepted or Narrowed type; a Failed trace holds the
    // one step that says why. Empty for Unreached, which names nothing to explain.
    internal IReadOnlyList<ClaimStep> Steps { get; }
}
