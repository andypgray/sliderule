using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Checking;

/// <summary>
///     The exceptions in a rule's selections that remove nothing from the selection they are written on (GRAMMAR
///     §4.1): each <c>Except</c> payload, and each name, glob and part of a live one, read against the set it
///     subtracts from.
/// </summary>
/// <remarks>
///     <para>
///         The direction is the position's. An exception from a subject or a ban that removes nothing spares
///         nothing, so the rule is stricter than written; one from an allow-list or a membership forbids nothing,
///         so the rule is partly inert; one from an <c>among:</c> part errs both ways. An exception inside an
///         exception flips the direction, since it narrows what the outer one removes.
///     </para>
///     <para>
///         Only a pointer warns: an exception that names a specific type, namespace, project, layer, exact name or
///         folder. One that describes a kind (a shape, an accessibility, an attribute, a name pattern, a predicate)
///         and removes nothing states a policy true of no type today, so the sentence still holds. It warns only
///         where the selection it is written on holds a type the solution only references and the kind cannot see
///         one (<see cref="BlindToPackages" />).
///     </para>
///     <para>
///         A bare <c>typeof</c> the model does not hold is silent, as a bare <c>typeof</c> target is: there is no type
///         to remove, and the exception takes effect once the code references it. A narrowed run is silent, since an
///         exception's types may sit in a project the run never loaded.
///     </para>
/// </remarks>
internal sealed class DeadExceptions
{
    private readonly ModelCaveats _caveats;
    private readonly IReadOnlyList<ProjectNode> _projects;
    private readonly SelectionEvaluator _selections;

    internal DeadExceptions(SelectionEvaluator selections, IReadOnlyList<ProjectNode> projects, ModelCaveats caveats)
    {
        _selections = selections;
        _projects = projects;
        _caveats = Guard.NotNull(caveats, nameof(caveats));
    }

    /// <summary>
    ///     The warnings for every exception in <paramref name="constraint" />'s subject and operands that removes
    ///     nothing.
    /// </summary>
    internal IReadOnlyList<CheckWarning> Of(Constraint constraint, Selection? subject, MemberSelection? memberSubject)
    {
        if (_caveats.Narrowing is not null) return [];

        var walk = new Walk(this);
        if (constraint is ProjectConstraint project)
        {
            walk.InProjects(project.ProjectSubject, DeadOperandRole.BanException);
        }
        else
        {
            walk.InTypes(subject!, SelectionPosition.Subject, DeadOperandRole.BanException);
            // InMembers reads only a member selection's own Excepts, so without one its sources are never needed.
            if (memberSubject is not null
                && memberSubject.Adjectives.Any(adjective => adjective is MemberExceptAdjective))
            {
                HashSet<TypeNode> sources = _selections.Evaluate(memberSubject.Source, SelectionPosition.Subject);
                walk.InMembers(memberSubject, sources, DeadOperandRole.BanException);
            }

            foreach (OperandRole operand in OperandRoles.Of(constraint))
                walk.InTypes(operand.Operand, operand.Position, DirectionOf(operand.Role));
        }

        return walk.Found
            .Select(found => CheckWarning.DeadException(
                found.Role, found.Exception, found.Host,
                AuthoringHints.ForDeadException(found.Role, found.Advice, _caveats)))
            .ToList();
    }

    /// <summary>The warnings for every exception inside a rule's exemplar that removes nothing.</summary>
    internal IReadOnlyList<CheckWarning> OfExemplar(Selection exemplar)
    {
        if (_caveats.Narrowing is not null) return [];

        var walk = new Walk(this);
        walk.InTypes(exemplar, SelectionPosition.Subject, DeadOperandRole.BanException);
        // An exemplar's warning names no direction, so findings that differ only in it are one warning.
        return walk.Found
            .GroupBy(found => (found.Exception, found.Host))
            .Select(group => group.First())
            .Select(found => CheckWarning.DeadExemplarException(
                found.Exception, found.Host, AuthoringHints.ForExemplarException(found.Advice, _caveats)))
            .ToList();
    }

    // The direction an operand's exceptions err in: a ban's spare, an allow-list's or a membership's forbid.
    private static DeadOperandRole DirectionOf(DeadOperandRole operand)
    {
        return operand switch
        {
            DeadOperandRole.Target => DeadOperandRole.BanException,
            DeadOperandRole.AllowList or DeadOperandRole.Membership => DeadOperandRole.AllowException,
            DeadOperandRole.Among => DeadOperandRole.AmongException,
            _ => throw new ArgumentOutOfRangeException(nameof(operand), operand, "Not an operand's role.")
        };
    }

    /// <summary>
    ///     Whether <paramref name="piece" /> describes its types by something a package's type never carries here:
    ///     the <c>arch.Types</c> noun, which holds only solution-declared types, or a base, interface, attribute or
    ///     file adjective, none of which an external type has.
    /// </summary>
    internal static bool BlindToPackages(Selection piece)
    {
        return piece.Noun is TypesNoun
               || piece.Adjectives.Any(adjective => adjective is ImplementingAdjective or DerivedFromAdjective
                   or AttributedWithAdjective or UnderAdjective);
    }

    // A pointer that removes nothing names something gone or misplaced.
    private static bool IsPointer(Selection piece)
    {
        if (piece is UnionSelection union) return union.Parts.Any(IsPointer);

        return piece.Noun is TypeNoun or NamespaceNoun or ProjectNoun or LayerNoun
               || piece.Adjectives.Any(adjective => adjective is NamedAdjective or InNamespaceAdjective
                   or UnderAdjective);
    }

    private static bool IsPointer(MemberSelection piece)
    {
        return piece.Adjectives.Any(adjective => adjective is MemberNamedAdjective) || IsPointer(piece.Source);
    }

    private static bool IsPointer(ProjectSelection piece)
    {
        return piece.Adjectives.Any(adjective => adjective is ProjectNamedAdjective or ProjectMatchingAdjective);
    }

    private static DeadOperandRole Flipped(DeadOperandRole direction)
    {
        return direction switch
        {
            DeadOperandRole.BanException => DeadOperandRole.AllowException,
            DeadOperandRole.AllowException => DeadOperandRole.BanException,
            _ => direction
        };
    }

    private readonly struct Finding(DeadOperandRole role, string exception, string host, string advice)
    {
        internal DeadOperandRole Role { get; } = role;

        internal string Exception { get; } = exception;

        internal string Host { get; } = host;

        internal string Advice { get; } = advice;
    }

    // One walk per rule: the findings in the order the selections are written, each (direction, exception, host)
    // once, since two warnings saying the same thing would be two alerts with one fingerprint.
    private sealed class Walk(DeadExceptions owner)
    {
        private readonly HashSet<(DeadOperandRole, string, string)> _seen = [];

        internal List<Finding> Found { get; } = [];

        internal void InTypes(Selection selection, SelectionPosition position, DeadOperandRole direction)
        {
            SelectionEvaluator selections = owner._selections;
            if (selection.Noun is EachNoun { Projects: { } family })
                InProjects(family, direction);
            else if (selections.Composite(selection) is { } parts)
                foreach (Selection part in parts)
                    InTypes(part, position, direction);

            IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;
            foreach ((int index, HashSet<TypeNode> host) in selections.ExceptHosts(selection, position))
            {
                var except = (ExceptAdjective)adjectives[index];
                if (host.Count > 0)
                {
                    string? hostProse = null;
                    bool externals = host.Any(type => type.IsExternal);
                    foreach ((Selection piece, bool elsewhere) in TypePieces(except.Payload, host))
                        if (IsPointer(piece) || (externals && BlindToPackages(piece)))
                        {
                            hostProse ??= selection.WithRefinements(adjectives.Take(index).ToList()).ReferenceProse();
                            Add(direction, piece.ReferenceProse(), hostProse,
                                AuthoringHints.ForExceptionShape(piece, elsewhere, externals));
                        }
                }

                InTypes(except.Payload, SelectionPosition.Target, Flipped(direction));
            }
        }

        internal void InMembers(MemberSelection selection, IEnumerable<TypeNode> sources, DeadOperandRole direction)
        {
            SelectionEvaluator selections = owner._selections;
            IReadOnlyList<MemberAdjective> adjectives = selection.Adjectives;
            foreach ((int index, HashSet<MemberNode> host) in MemberSelectionEvaluator.ExceptHosts(selection, sources, selections))
            {
                var except = (MemberExceptAdjective)adjectives[index];
                if (host.Count > 0)
                {
                    string? hostProse = null;
                    foreach (MemberSelection payload in except.Payloads)
                    foreach (MemberSelection piece in MemberPieces(payload, host))
                        if (IsPointer(piece))
                        {
                            hostProse ??= selection.WithAdjectives(adjectives.Take(index).ToList()).ReferenceProse();
                            Add(direction, piece.ReferenceProse(), hostProse, AuthoringHints.ForMemberExceptionShape(piece));
                        }
                }

                foreach (MemberSelection payload in except.Payloads)
                {
                    InTypes(payload.Source, SelectionPosition.Target, Flipped(direction));
                    HashSet<TypeNode> payloadSources = selections.Evaluate(payload.Source, SelectionPosition.Target);
                    InMembers(payload, payloadSources, Flipped(direction));
                }
            }
        }

        internal void InProjects(ProjectSelection selection, DeadOperandRole direction)
        {
            IReadOnlyList<ProjectAdjective> adjectives = selection.Adjectives;
            foreach ((int index, IReadOnlyList<ProjectNode> host) in ProjectSelectionEvaluator.ExceptHosts(selection, owner._projects))
            {
                var except = (ProjectExceptAdjective)adjectives[index];
                if (host.Count > 0)
                {
                    string? hostProse = null;
                    foreach ((ProjectSelection piece, bool elsewhere) in ProjectPieces(except.Payload, host))
                        if (IsPointer(piece))
                        {
                            hostProse ??= selection.WithAdjectives(adjectives.Take(index).ToList()).ReferenceProse();
                            Add(direction, piece.ReferenceProse(), hostProse, AuthoringHints.ForProjectExceptionShape(elsewhere));
                        }
                }

                InProjects(except.Payload, Flipped(direction));
            }
        }

        // What of one payload removes nothing from the host: the whole payload where none of what it names is
        // there, whether it named something elsewhere or nothing at all (a bare typeof the model does not hold
        // stays silent); below a live one, each part of a union and the opened definition of a layer, then each
        // name or glob of the payload's own lists that removed nothing.
        private IEnumerable<(Selection Piece, bool Elsewhere)> TypePieces(Selection payload, HashSet<TypeNode> host)
        {
            SelectionEvaluator selections = owner._selections;
            HashSet<TypeNode> named = selections.Evaluate(payload, SelectionPosition.Target);
            // The constructor says the filtered names become a set here, where a spread would leave it to the
            // declared type.
            // ReSharper disable once UseCollectionExpression
            var removed = new HashSet<TypeNode>(named.Where(host.Contains));
            if (removed.Count == 0)
            {
                if (named.Count > 0 || SelectionEvaluator.IsPatternSelection(payload))
                    yield return (payload, named.Count > 0);
                yield break;
            }

            if (DeadNames.OpenedParts(payload, selections) is { } parts)
                foreach (Selection part in parts)
                foreach ((Selection Piece, bool Elsewhere) inner in TypePieces(part, host))
                    yield return inner;

            foreach (Selection deadName in DeadNames.In(payload, removed, selections))
                yield return (deadName, selections.Evaluate(deadName, SelectionPosition.Target).Count > 0);
        }

        private IEnumerable<MemberSelection> MemberPieces(MemberSelection payload, HashSet<MemberNode> host)
        {
            List<MemberNode> removed = MemberSelectionEvaluator.Removable(payload, owner._selections)
                .Where(host.Contains)
                .ToList();
            return removed.Count == 0 ? [payload] : MemberDeadNames.In(payload, removed);
        }

        private IEnumerable<(ProjectSelection Piece, bool Elsewhere)> ProjectPieces(
            ProjectSelection payload, IReadOnlyList<ProjectNode> host)
        {
            IReadOnlyList<ProjectNode> named = ProjectSelectionEvaluator.Resolve(payload, owner._projects);
            List<ProjectNode> removed = named.Where(host.Contains).ToList();
            if (removed.Count == 0)
            {
                yield return (payload, named.Count > 0);
                yield break;
            }

            // Every project in removed meets the whole payload, so a one-entry respelling keeps one of them exactly
            // when its single name or glob does: the evaluator's own matching decides which entries are dead.
            IReadOnlyList<ProjectAdjective> adjectives = payload.Adjectives;
            for (var j = 0; j < adjectives.Count; j++)
            {
                IEnumerable<ProjectAdjective> singles = adjectives[j] switch
                {
                    ProjectNamedAdjective { Names.Count: > 1 } list => list.Names
                        .Select(name => new ProjectNamedAdjective([name])),
                    ProjectMatchingAdjective { Globs.Count: > 1 } list => list.Globs
                        .Select(glob => new ProjectMatchingAdjective([glob])),
                    _ => []
                };
                foreach (ProjectAdjective single in singles)
                {
                    var spelled = new List<ProjectAdjective>(adjectives) { [j] = single };
                    ProjectSelection piece = payload.WithAdjectives(spelled);
                    if (ProjectSelectionEvaluator.Resolve(piece, removed).Count > 0) continue;

                    yield return (piece, ProjectSelectionEvaluator.Resolve(piece, owner._projects).Count > 0);
                }
            }
        }

        private void Add(DeadOperandRole direction, string exception, string host, string advice)
        {
            if (_seen.Add((direction, exception, host))) Found.Add(new Finding(direction, exception, host, advice));
        }
    }
}
