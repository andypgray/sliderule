using SlideRule.Validation;

namespace SlideRule.Model;

/// <summary>
///     A rule or scope anchor registered on an <see cref="Arch" /> the moment <c>Rule</c>/<c>Scope</c>
///     is called (register-on-anchor, GRAMMAR §3.2).
/// </summary>
internal abstract class Registration(string id)
{
    internal string Id { get; } = id;

    /// <summary>
    ///     The spec-source position of the <c>Rule</c>/<c>Scope</c> call that registered this anchor,
    ///     captured via caller info (GRAMMAR §8), or null when none was captured.
    /// </summary>
    internal SpecSourceLocation? Location { get; set; }
}
