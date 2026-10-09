using SlideRule.Validation;

namespace SlideRule.Fluent;

/// <summary>
///     The calls available after <c>Enforce</c>: <see cref="Because" />, which is required, and the
///     optional <see cref="Fix" />, <see cref="Citation" /> and <see cref="Exemplar" />, in any order and
///     each at most once. Every prose value is a single non-blank line; anything else is reported when the
///     spec is loaded.
/// </summary>
public interface IEnforceRule
{
    /// <summary>
    ///     States why the rule exists, in one line of prose. Required: a rule without it is reported when
    ///     the spec is loaded. Rendered into the generated agent context and echoed with every violation.
    /// </summary>
    IEnforceRule Because([SpecSlot(nameof(SpecSlot.Because))] string because);

    /// <summary>
    ///     States what to do instead, in one line of prose. Optional. Printed as a <c>fix:</c> line by
    ///     <c>explain</c> and beneath a failed rule by <c>check</c>, appended to the rule's build warning, and
    ///     published as the rule's help text in SARIF output.
    /// </summary>
    IEnforceRule Fix([SpecSlot(nameof(SpecSlot.Fix))] string fix);

    /// <summary>
    ///     Names the page the rule's reasoning rests on, as an absolute <c>http</c> or <c>https</c> URL.
    ///     Optional. Rendered as "See &lt;url&gt;." after the reason in the generated agent context,
    ///     printed as a <c>citation:</c> line by <c>explain</c> and beneath a failed rule by <c>check</c>,
    ///     and published as the rule's help URI in SARIF output. A value that is not an absolute http(s)
    ///     URL is reported when the spec is loaded.
    /// </summary>
    IEnforceRule Citation([SpecSlot(nameof(SpecSlot.Citation))] string uri);

    /// <summary>
    ///     Names the type to copy when fixing a violation, as a selection: <c>arch.Types.Named("OrdersRepository")</c>
    ///     or <c>arch.Type&lt;OrderService&gt;()</c>, or a union of them for several. Optional. <c>check</c>
    ///     resolves it against the solution and prints each type with the file and line that declare it beneath a
    ///     failed rule, after the fix; <c>explain</c> and the build warning print it as written. It never changes
    ///     the verdict. A part that matches no type the solution declares is reported as a warning on the rule,
    ///     so a type renamed after the spec named it shows up in the next check.
    /// </summary>
    IEnforceRule Exemplar(Selection exemplar);
}
