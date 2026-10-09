namespace SlideRule.Checking;

/// <summary>
///     What a selection's membership is asked of where it stands: the type
///     (<see cref="Membership" />), or one copy of it (<see cref="EdgeEnd" />).
/// </summary>
/// <remarks>
///     <para>
///         The axis that decides whether a one-project reader's shorter declarer list can be observed at
///         all. <see cref="SelectionAdmission.Contains" /> is N-way — a project selection names a type
///         that any of its declarers compiles — so a declarer this compilation cannot see changes the
///         answer. <see cref="SelectionAdmission.CountedSites" /> asks at one project instead: the one
///         whose copy an instance reached, or whose compilation made the reference (GRAMMAR §4.1).
///     </para>
///     <para>
///         Every instance a one-project reader reports is one its own compilation made, and it attributes
///         both ends of that instance exactly — its own types to itself, a sibling's to the assembly they
///         were bound from. So the project the question is asked about is never one of the declarers it
///         cannot see, and the answer is the whole solution's rather than a subset of it.
///     </para>
/// </remarks>
internal enum OneProjectReading
{
    /// <summary>Whether the selection names the type at all, whichever project compiles it.</summary>
    Membership,

    /// <summary>Whether the selection names the type at the project one edge instance reached.</summary>
    EdgeEnd
}
