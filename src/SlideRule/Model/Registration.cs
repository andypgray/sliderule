using SlideRule.Validation;

namespace SlideRule.Model;

/// <summary>
///     A rule or scope anchor registered on an <see cref="Arch" /> the moment <c>Rule</c>/<c>Scope</c>
///     is called (register-on-anchor, GRAMMAR §3.2). Trailers append to lists so a repeated trailer
///     (<c>Because</c> twice, two <c>Baseline</c>s) is detectable by validation (§8 item 6).
/// </summary>
internal abstract class Registration(string id)
{
    internal string Id { get; } = id;

    /// <summary>
    ///     The spec-source position of the <c>Rule</c>/<c>Scope</c> call that registered this anchor,
    ///     captured via caller info (GRAMMAR §8), or null when none was captured.
    /// </summary>
    internal SpecSourceLocation? Location { get; set; }

    /// <summary>Null when no posture verb was called, which leaves the anchor dangling (§8 item 2).</summary>
    internal Posture? Posture { get; private set; }

    /// <summary>
    ///     How many times a posture verb was called. The stage machine (§3.2) forbids a fluent double-call, but a
    ///     stored builder reference is mutable, so a posture verb called twice on it silently overwrites (on a
    ///     scope, the scoped selection too); a count &gt; 1 is the repeated-posture error (§8 item 17).
    /// </summary>
    internal int PostureCount { get; private set; }

    internal List<string> Becauses { get; } = [];

    internal List<string> Baselines { get; } = [];

    /// <summary>
    ///     Records a posture verb's call. Each verb assigns its operand first, so a recorded posture always has
    ///     its operand.
    /// </summary>
    internal void RecordPosture(Posture posture)
    {
        PostureCount++;
        Posture = posture;
    }
}
