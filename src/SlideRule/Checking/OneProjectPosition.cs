namespace SlideRule.Checking;

/// <summary>
///     Where a selection stands, on the three axes a one-project verdict turns on: which universe it
///     ranges over (<see cref="Side" />), what it does to a verdict (<see cref="Polarity" />), and what
///     its membership is asked of (<see cref="Reading" />).
/// </summary>
/// <remarks>
///     <para>
///         The axes are independent and each row of <see cref="OneProjectDecidability" /> keys on the ones
///         its own argument rests on. <see cref="Side" /> asks whether a wrong verdict here would be sited
///         in code this reader reports, which is what the generated-code filter turns on;
///         <see cref="OneProjectPolarity" /> and <see cref="OneProjectReading" /> each say what their own
///         axis asks.
///     </para>
///     <para>
///         A triple rather than a third value on <see cref="SelectionPosition" />: that enum's documented
///         meaning is the candidate universe (GRAMMAR §4.1) and the evaluator reads it as such, so a
///         decidability concept added to it would leak into the universe semantics of code that has no
///         business knowing about one-project reading.
///     </para>
/// </remarks>
/// <param name="side">Which universe the selection ranges over.</param>
/// <param name="polarity">What a type inside the selection does to the verdict.</param>
/// <param name="reading">What the selection's membership is asked of.</param>
internal readonly struct OneProjectPosition(
    SelectionPosition side,
    OneProjectPolarity polarity,
    OneProjectReading reading)
{
    /// <summary>Which universe the selection ranges over.</summary>
    internal SelectionPosition Side { get; } = side;

    /// <summary>What a type inside the selection does to the verdict.</summary>
    internal OneProjectPolarity Polarity { get; } = polarity;

    /// <summary>What the selection's membership is asked of.</summary>
    internal OneProjectReading Reading { get; } = reading;

    /// <summary>
    ///     Where a rule's own verb stands, which is where every walk starts. No constraint row reads any
    ///     of the three axes, so the value only has to be stated once and honestly: a verb is the thing
    ///     that bans, it is read before any selection moves the position, and it has no membership of its
    ///     own to be asked of anything.
    /// </summary>
    internal static OneProjectPosition Verb { get; } =
        new(SelectionPosition.Subject, OneProjectPolarity.Banning, OneProjectReading.Membership);

    /// <summary>
    ///     The position no row answers "decidable" for — a target that exempts, asked of the type. What a
    ///     walk reaches a node at where the node's own standing is not observable: every row it can meet is
    ///     position-blind, so the answer must not come to depend on a reading nobody has checked.
    /// </summary>
    internal static OneProjectPosition Conservative { get; } =
        new(SelectionPosition.Target, OneProjectPolarity.Exempting, OneProjectReading.Membership);

    /// <summary>
    ///     Where the payload of an <c>Except</c> stands, given where the selection it subtracts from does:
    ///     a target, because it says which types are <em>not</em> the selection and a type this project
    ///     cannot see can still be one of them; the opposite polarity, because subtracting from a ban
    ///     spares and subtracting from an allow-list condemns; and always asked of the type, because a
    ///     subtraction takes a node out of the set before any edge is walked, so it removes every copy of
    ///     a name whichever project compiles it.
    /// </summary>
    internal OneProjectPosition AsExceptPayload()
    {
        OneProjectPolarity flipped = Polarity == OneProjectPolarity.Banning
            ? OneProjectPolarity.Exempting
            : OneProjectPolarity.Banning;

        return new OneProjectPosition(SelectionPosition.Target, flipped, OneProjectReading.Membership);
    }
}
