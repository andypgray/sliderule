using SlideRule.Validation;

namespace SlideRule.Fluent;

/// <summary>
///     The calls available after <c>Migrate</c>: <see cref="Because" />, which is required, and the
///     optional <see cref="Baseline" />, <see cref="WhileYoureThere" />, <see cref="Fix" />,
///     <see cref="Citation" /> and <see cref="Exemplar" />, in any order and each at most once. Every prose
///     value is a single non-blank line; anything else is reported when the spec is loaded.
/// </summary>
public interface IMigrateRule
{
    /// <summary>
    ///     States why the migration exists, in one line of prose. Required: a rule without it is reported
    ///     when the spec is loaded. Rendered into the generated agent context and echoed with every
    ///     violation.
    /// </summary>
    IMigrateRule Because([SpecSlot(nameof(SpecSlot.Because))] string because);

    /// <summary>
    ///     States what to do instead, in one line of prose. Optional. Printed as a <c>fix:</c> line by
    ///     <c>explain</c> and beneath a failed rule by <c>check</c>, appended to the rule's build warning, and
    ///     published as the rule's help text in SARIF output.
    /// </summary>
    IMigrateRule Fix([SpecSlot(nameof(SpecSlot.Fix))] string fix);

    /// <summary>
    ///     Names the page the rule's reasoning rests on, as an absolute <c>http</c> or <c>https</c> URL.
    ///     Optional. Rendered as "See &lt;url&gt;." between the reason and the migration policy in the
    ///     generated agent context, printed as a <c>citation:</c> line by <c>explain</c> and beneath a
    ///     failed rule by <c>check</c>, and published as the rule's help URI in SARIF output. A value that
    ///     is not an absolute http(s) URL is reported when the spec is loaded.
    /// </summary>
    IMigrateRule Citation([SpecSlot(nameof(SpecSlot.Citation))] string uri);

    /// <summary>
    ///     Names the type to copy when fixing a violation, as a selection: <c>arch.Types.Named("OrdersRepository")</c>
    ///     or <c>arch.Type&lt;OrderService&gt;()</c>, or a union of them for several. Optional. <c>check</c>
    ///     resolves it against the solution and prints each type with the file and line that declare it beneath a
    ///     failed rule, after the fix; <c>explain</c> and the build warning print it as written. It never changes
    ///     the verdict. A part that matches no type the solution declares is reported as a warning on the rule,
    ///     so a type renamed after the spec named it shows up in the next check.
    /// </summary>
    IMigrateRule Exemplar(Selection exemplar);

    /// <summary>
    ///     Sets the file that records the rule's grandfathered violations, captured with the CLI's
    ///     <c>baseline</c> verb. Optional: when omitted the path is <c>arch/baselines/{rule-id}.json</c>,
    ///     each <c>/</c> in the rule ID becoming a directory. A relative path resolves against the
    ///     solution directory. Until a baseline is captured, every violation fails the check. A blank
    ///     path is reported when the spec is loaded.
    /// </summary>
    IMigrateRule Baseline([SpecSlot(nameof(SpecSlot.Baseline))] string path);

    /// <summary>
    ///     Sets what an editor already changing a file with grandfathered violations is asked to do about
    ///     them. Optional; defaults to <see cref="MigrationPolicy.MigrateIfSmall" />. Rendered into the
    ///     generated agent context as guidance, and nothing checks it. A value outside the three declared
    ///     policies is refused at the call.
    /// </summary>
    IMigrateRule WhileYoureThere(MigrationPolicy policy);
}
