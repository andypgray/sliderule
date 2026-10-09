namespace SlideRule.Validation;

/// <summary>
///     What one string literal decided, held as the parts of a sentence rather than a finished one: the
///     code the catalog files it under, and the two halves a frame goes around.
/// </summary>
/// <remarks>
///     Two frames read one verdict. A reader who has only the built model needs the rule, scope or layer
///     the literal was written on named in the sentence, which is <see cref="On" />; a reader already
///     pointing at the literal needs the same sentence with that clause left out, which is
///     <see cref="AtTheLiteral" />. Keeping the parts is what stops the two from drifting into different
///     English, because neither frame owns the wording.
///     <para>
///         The verdict carries the code rather than the slot, because one slot can decide two ways: a
///         counterpart name template is <see cref="SpecValidationErrorCode.BlankPattern" /> when blank and
///         <see cref="SpecValidationErrorCode.CounterpartTemplateWithoutPlaceholder" /> when it carries no
///         placeholder.
///     </para>
/// </remarks>
internal sealed class LiteralVerdict
{
    private LiteralVerdict(SpecValidationErrorCode code, string head, string tail, bool namesSubject)
    {
        Code = code;
        Head = head;
        Tail = tail;
        NamesSubject = namesSubject;
    }

    /// <summary>
    ///     A verdict whose sentence names what the literal was written on: <c>{head} on {subject}{tail}</c>.
    /// </summary>
    internal static LiteralVerdict About(SpecValidationErrorCode code, string head, string tail)
    {
        return new LiteralVerdict(code, head, tail, true);
    }

    /// <summary>
    ///     A verdict whose sentence names no subject and so reads the same in both frames. A malformed ID
    ///     and a blank layer name are of this shape: the value the message would name is the one that is
    ///     wrong, so there is nothing to put in a subject clause.
    /// </summary>
    internal static LiteralVerdict Whole(SpecValidationErrorCode code, string sentence)
    {
        return new LiteralVerdict(code, sentence, string.Empty, false);
    }

    /// <summary>Which mistake this is.</summary>
    internal SpecValidationErrorCode Code { get; }

    /// <summary>The sentence up to the subject clause, or the whole sentence when it names no subject.</summary>
    internal string Head { get; }

    /// <summary>The sentence after the subject clause; empty when it names no subject.</summary>
    internal string Tail { get; }

    /// <summary>Whether the sentence has a place for the thing the literal was written on.</summary>
    internal bool NamesSubject { get; }

    /// <summary>
    ///     The sentence with no subject clause in it, for a reader that is already looking at the literal.
    /// </summary>
    internal string AtTheLiteral => Head + Tail;

    /// <summary>
    ///     The sentence with <paramref name="subject" /> named as what the literal was written on. A
    ///     subject-free verdict comes back unchanged, because it has nowhere to put one.
    /// </summary>
    /// <param name="subject">The quoted ID, or the outright name a layer or a member supplies.</param>
    internal string On(string subject)
    {
        return NamesSubject ? $"{Head} on {subject}{Tail}" : Head;
    }
}
