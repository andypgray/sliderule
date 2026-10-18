using SlideRule.Fluent;
using SlideRule.Model;

namespace SlideRule.Prose;

/// <summary>
///     Assembles the deterministic law sentence from a <see cref="Constraint" /> (GRAMMAR §6). The
///     nouns and adjectives own their local fragments; this orchestrates the cross-node concerns:
///     the collective-vs-types voice switch, sentence-final canonicalization of <c>Except</c>/
///     <c>Where</c> with <c>Except</c> last, closing the <c>Except</c> parenthetical with a comma at
///     whatever junction follows it, colliding-simple-name qualification in target lists (the shared
///     <see cref="ProseFormat.ResolvePathDisplays" /> primitive), and capitalization.
/// </summary>
internal static class SentenceRenderer
{
    /// <summary>The full law sentence: <c>{Subject} {verb phrase}.</c></summary>
    internal static string Sentence(Constraint constraint)
    {
        // One arm per subject stratum (GRAMMAR §4.6, §4.10, §6): a member constraint speaks over its member
        // subject ("Methods of types in `MyApp.Web.*` …"), a project constraint over its project subject
        // ("Packable projects …"), and every other constraint over its type subject. This dispatch is also
        // what makes the bare Subject read below safe — it is null exactly for the project arm above it.
        // The phrase and its openness come out of the same arm, so the two cannot disagree about which
        // stratum was read.
        (string subject, bool endsOpen) = constraint switch
        {
            MemberConstraint member => (MemberSubject(member.MemberSubject), EndsOpen(member.MemberSubject)),
            ProjectConstraint project => (ProjectSubject(project.ProjectSubject), EndsOpen(project.ProjectSubject)),
            _ => (Subject(constraint.Subject!), EndsOpen(constraint.Subject!))
        };

        // The verb is the junction after the subject, so the verb is what closes a subject-final Except.
        return subject + (endsOpen ? ", " : " ") + constraint.VerbPhrase + ".";
    }

    /// <summary>The capitalized subject phrase for a selection (GRAMMAR §6).</summary>
    internal static string Subject(Selection selection)
    {
        return ProseFormat.Capitalize(Phrase(selection));
    }

    /// <summary>The capitalized member-subject phrase for a member selection (GRAMMAR §4.6, §6).</summary>
    internal static string MemberSubject(MemberSelection selection)
    {
        return ProseFormat.Capitalize(MemberPhrase(selection));
    }

    /// <summary>
    ///     How a member selection reads in reference position (GRAMMAR §6): its subject phrase, uncapitalized,
    ///     as a message quotes it or an <c>Except</c> payload renders it.
    /// </summary>
    internal static string MemberReference(MemberSelection selection)
    {
        return MemberPhrase(selection);
    }

    /// <summary>
    ///     A member <c>Except</c>'s payload list (GRAMMAR §6): each payload in reference position, or-joined with
    ///     no Oxford comma, the final joiner closing a penultimate payload that ended open.
    /// </summary>
    internal static string MemberReferenceList(IReadOnlyList<MemberSelection> selections)
    {
        List<string> parts = selections.Select(MemberReference).ToList();
        bool closesBeforeOr = selections.Count >= 2 && EndsOpen(selections[selections.Count - 2]);
        return ProseFormat.JoinReferences(parts, closesBeforeOr);
    }

    /// <summary>The capitalized project-subject phrase for a project selection (GRAMMAR §4.10, §6).</summary>
    internal static string ProjectSubject(ProjectSelection selection)
    {
        return ProseFormat.Capitalize(ProjectPhrase(selection));
    }

    /// <summary>
    ///     How a project selection reads in reference position (GRAMMAR §4.10, §6) — the same phrase,
    ///     uncapitalized, which is what an <c>Except</c> payload renders as inside a sentence.
    /// </summary>
    /// <remarks>
    ///     There is no bare-noun special case here as there is on the type side: a project selection has one
    ///     head and no noun that reads differently as a reference, so subject and reference position differ
    ///     by capitalization alone.
    /// </remarks>
    internal static string ProjectReference(ProjectSelection selection)
    {
        return ProjectPhrase(selection);
    }

    /// <summary>
    ///     Whether a subject reads as its own noun phrase rather than through the "types" head (GRAMMAR §6):
    ///     a bare layer or a bare family, which speak in the collective voice, or a bare single type, which
    ///     reads as its name.
    /// </summary>
    /// <remarks>
    ///     <see cref="Phrase(Selection,string,IReadOnlyList{string})" /> decides the voice by this test, and the
    ///     family-voice readers below ask it too, so none of them can disagree. Each of these subjects is one
    ///     thing — one layer, one family read as a whole, one type — so its reflexive is the singular "itself".
    /// </remarks>
    private static bool ReadsAsItsNoun(Selection selection)
    {
        return selection.Adjectives.Count == 0
               && SelectionWalk.NounOf(selection) is LayerNoun or EachNoun or TypeNoun;
    }

    /// <summary>
    ///     The singular cell word — "layer" or "project" — a family subject speaking in the <em>types</em>
    ///     voice names its cells by, and null for every other subject: a plain selection, which has no
    ///     cells, or a family in the collective voice, which reads as one noun phrase and names no cell
    ///     (GRAMMAR §5.3, §6).
    /// </summary>
    internal static string? FamilyCellWord(Selection selection)
    {
        if (SelectionWalk.FamilyNoun(selection) is not { } family) return null;

        return ReadsAsItsNoun(selection) ? null : family.CellWord;
    }

    /// <summary>
    ///     How a subject names itself as a reference target — the "self" the two leaf verbs allow (GRAMMAR
    ///     §5.3): "their own layer" or "their own project" for a family in the types voice, "themselves" for
    ///     any other subject in the types voice, and "itself" where the subject reads as its own noun: one
    ///     layer, one family read as a whole, or one type named bare.
    /// </summary>
    /// <remarks>
    ///     The reflexive agrees in number with the head the sentence was assembled under (§6): a plain
    ///     types-voice subject is the plural "types", so a singular "itself" would misagree with it, and a
    ///     family in that voice names the cell instead, because its self is the cell rather than the family.
    /// </remarks>
    internal static string SelfReference(Selection selection)
    {
        if (FamilyCellWord(selection) is { } cell) return $"their own {cell}";

        return ReadsAsItsNoun(selection) ? "itself" : "themselves";
    }

    /// <summary>
    ///     How a subject names its <em>other</em> cells — the far end the two family verbs forbid (GRAMMAR
    ///     §5.3): "the other layers" or "the other projects" for a family in the types voice, and "the
    ///     others" for a bare family or any subject with no cells to name. The twin of
    ///     <see cref="SelfReference" />, and the same voice agreement.
    /// </summary>
    internal static string OtherCells(Selection selection)
    {
        return FamilyCellWord(selection) is { } cell ? $"the other {cell}s" : "the others";
    }

    /// <summary>How a selection reads in reference position (lowercase; joins union members).</summary>
    internal static string Reference(Selection selection)
    {
        if (selection is UnionSelection union) return UnionReference(union);

        // A bare noun (no adjectives) uses its own reference fragment: "the Web layer",
        // "types in `MyApp.*`", "`SqlConnection`". A refined selection falls back to the
        // uncapitalized types-voice phrase.
        return selection.Adjectives.Count == 0 ? selection.Noun.ReferenceFragment : Phrase(selection);
    }

    /// <summary>
    ///     Renders a dependency verb's target/source list, qualifying colliding simple names with
    ///     the minimal distinguishing trailing namespace segments (GRAMMAR §6), then joins with no
    ///     Oxford comma.
    /// </summary>
    internal static string TargetList(IReadOnlyList<Selection> targets)
    {
        return ProseFormat.JoinReferences(ReferenceFragments(targets), ClosesBeforeOr(targets));
    }

    /// <summary>
    ///     One reference fragment per selection, collision-widened as a set: the fragments a list joins,
    ///     before any joining. A boundary's sanctioned surface renders through this so the list a scope
    ///     card prints and the list its sentence names are the same computation (GRAMMAR §6, §7).
    /// </summary>
    internal static IReadOnlyList<string> ReferenceFragments(IReadOnlyList<Selection> selections)
    {
        var paths = new List<IReadOnlyList<string>>();
        foreach (Selection selection in selections)
            if (TryBareTypeAnchor(selection, out TypeAnchor anchor))
                paths.Add(anchor.PathSegments);

        // The displays come back positionally aligned with the paths that went in, and the second walk
        // meets the bare-type selections in that same order, so each one's display is the next in line.
        IReadOnlyList<string> displays = ProseFormat.ResolvePathDisplays(paths);
        var parts = new List<string>(selections.Count);
        var widened = 0;
        foreach (Selection selection in selections)
        {
            if (!TryBareTypeAnchor(selection, out _))
            {
                parts.Add(Reference(selection));
                continue;
            }

            parts.Add(ProseFormat.Backtick(displays[widened]));
            widened++;
        }

        return parts;
    }

    /// <summary>
    ///     The same target list followed by a verb-phrase tail, with the last target's open <c>Except</c>
    ///     parenthetical closed before it: "…, except `TimeoutException`, without a `when` filter"
    ///     (GRAMMAR §6). A tail that is a bracketed parenthetical closes the clause on its own and
    ///     concatenates instead.
    /// </summary>
    internal static string TargetList(IReadOnlyList<Selection> targets, string tail)
    {
        return TargetList(targets) + CloseBefore(EndsOpen(targets[targets.Count - 1]), tail);
    }

    /// <summary>
    ///     Renders a member-access verb's target list (GRAMMAR §4.5, §6): each member as the
    ///     backticked declaring-type dot member — <c>`DateTime.Now`</c>, <c>`Task.Wait()`</c> with
    ///     <c>()</c> appended iff a method — then joins with no Oxford comma. Colliding declaring-type
    ///     simple names widen by the same minimal-trailing-segments rule as the reference list (fed
    ///     through <see cref="ProseFormat.ResolvePathDisplays" />), including when the member names differ.
    /// </summary>
    internal static string MemberList(IReadOnlyList<Member> members)
    {
        List<IReadOnlyList<string>> paths = members.Select(member => member.DeclaringAnchor.PathSegments).ToList();
        IReadOnlyList<string> displays = ProseFormat.ResolvePathDisplays(paths);

        var parts = new List<string>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            Member member = members[i];
            string suffix = member.IsMethod ? "()" : string.Empty;
            parts.Add(ProseFormat.Backtick(displays[i] + "." + member.Name + suffix));
        }

        return ProseFormat.JoinReferences(parts);
    }

    /// <summary>
    ///     The layer definition fragment for the module map: <c>**Domain** — `MyApp.Domain.*`</c>, and with a
    ///     purpose <c>**Domain** — `MyApp.Domain.*`. {purpose}</c> — prefix-preserving, the purpose verbatim.
    /// </summary>
    /// <remarks>
    ///     A definition spells the row's first half instead of the globs: a bare noun as its locative without
    ///     the head — <c>project `MyApp.Core`</c>, <c>projects `A` or `B`</c> — which is how the glob row
    ///     already reads its own noun, and anything else (a refinement, a heterogeneous union) as its
    ///     reference phrase: <c>types in the Core layer in `MyApp.Core.Model.*`</c>.
    /// </remarks>
    internal static string LayerDefinition(LayerNoun noun, string? purpose)
    {
        var fragment = $"**{noun.Name}** — {DefinitionPhrase(noun)}";
        if (purpose is not null) fragment += $". {purpose}";
        return fragment;
    }

    // The row's first half: the glob list, or what the definition names.
    private static string DefinitionPhrase(LayerNoun noun)
    {
        if (noun.Definition is not { } definition) return string.Join(", ", noun.Globs.Select(ProseFormat.Backtick));

        return BareLocative(definition) ?? Reference(definition);
    }

    // A bare noun's locative with the "types" head dropped, or null for a definition that has no such
    // reading — one carrying adjectives, a union whose operands do not collapse, or a noun whose whole
    // phrase is its head (a type, a registration, bare Types). Null falls through to the reference phrase,
    // which those nouns read correctly on their own.
    private static string? BareLocative(Selection definition)
    {
        if (definition.Adjectives.Count > 0) return null;

        string? locative = definition is UnionSelection union ? CollapsedLocativeOf(union) : definition.Noun.Locative;
        const string head = " in ";
        return locative is not null && locative.StartsWith(head, StringComparison.Ordinal)
            ? locative.Substring(head.Length)
            : null;
    }

    // The hoisted locative of a union whose operands agree, or null when they do not.
    private static string? CollapsedLocativeOf(UnionSelection union)
    {
        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        return nouns is null ? null : nouns[0].CollapsedLocative(nouns);
    }

    private static string Phrase(Selection selection)
    {
        return Phrase(selection, null, null);
    }

    // The types-voice phrase. headOverride and headPrefixOverride carry a union's head and head prefixes
    // down into an operand of a union that does not collapse, so the filters they stand for reach the prose
    // instead of being silently dropped from a sentence the checker still applies them to (GRAMMAR §6). The
    // operand's own prefixes follow the union's.
    private static string Phrase(Selection selection, string? headOverride, IReadOnlyList<string>? headPrefixOverride)
    {
        if (selection is UnionSelection union) return UnionPhrase(union, headOverride, headPrefixOverride);

        SelectionNoun noun = selection.Noun;
        IReadOnlyList<SelectionAdjective> adjectives = selection.Adjectives;

        // The noun's own phrase: a bare layer or a bare family in the collective voice ("the Domain layer",
        // "each of the Host, Adapter and Pack layers"), or a bare single type as its name ("`SqlConnection`").
        // Any adjective switches to types voice — the switch is structural, hence deterministic (GRAMMAR
        // §6). Asked of ReadsAsItsNoun, which the family verb phrases also ask, so the voice they agree with
        // is the voice assembled here by construction. The union half of that test is already discharged by
        // the early return above.
        if (ReadsAsItsNoun(selection) && headOverride is null && headPrefixOverride is null)
            return noun.ReferenceFragment;

        // The head defaults to "types" (the type nouns) but is taken from the noun for a noun whose
        // fragment IS its head — the registration noun — so a qualified Registered subject keeps its
        // qualifier instead of collapsing to a false bare "types" (GRAMMAR §5.1, head truth).
        (string? head, IReadOnlyList<string>? headPrefixes, string inline, string subjectFinal) = Placements(
            adjectives.Select(adjective =>
                (adjective.Placement, adjective.Fragment, adjective.OpensParenthetical)),
            headOverride ?? noun.SubjectHead,
            headPrefixOverride);

        return Premodifiers(headPrefixes) + head + noun.Locative + inline + subjectFinal;
    }

    // A union in reference position (GRAMMAR §6). A bare union reads as its collapsed reference when its
    // operands agree — "types in projects `A` or `B`", "the Domain or Web layers", "`X` or `Y`" — and
    // or-joins its operands otherwise. A union carrying its own adjectives has no bare reading, so it
    // falls through to the phrase.
    private static string UnionReference(UnionSelection union)
    {
        if (union.Adjectives.Count > 0) return UnionPhrase(union, null, null);

        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        return nouns is null
            ? ProseFormat.JoinReferences(union.Parts.Select(Reference).ToList(), ClosesBeforeOr(union.Parts))
            : nouns[0].CollapsedReference(nouns);
    }

    // A union in subject (types-voice) position (GRAMMAR §6). A bare union is its reference, so the
    // single-operand identity holds in both positions. Otherwise the union's own adjectives assemble
    // against the collapsed head and locative exactly as they do for a single selection — head
    // substitution, head premodification, inline, sentence-final — or, when the union does not collapse,
    // against the or-joined operand phrases with the head and its prefixes distributed into each, the
    // prefixes ahead of the operand's own.
    private static string UnionPhrase(
        UnionSelection union, string? headOverride, IReadOnlyList<string>? headPrefixOverride)
    {
        IReadOnlyList<SelectionAdjective> adjectives = union.Adjectives;
        if (adjectives.Count == 0 && headOverride is null && headPrefixOverride is null) return UnionReference(union);

        (string? head, IReadOnlyList<string>? headPrefixes, string inline, string subjectFinal) = Placements(
            adjectives.Select(adjective =>
                (adjective.Placement, adjective.Fragment, adjective.OpensParenthetical)),
            headOverride,
            headPrefixOverride);

        IReadOnlyList<SelectionNoun>? nouns = CollapsibleNouns(union);
        if (nouns is not null)
            return Premodifiers(headPrefixes) + (head ?? nouns[0].SubjectHead)
                                              + nouns[0].CollapsedLocative(nouns) + inline + subjectFinal;

        // The union's own clauses follow the last operand's phrase, so they close an Except it left open.
        List<string> parts = union.Parts.Select(part => Phrase(part, head, headPrefixes)).ToList();
        Selection lastPart = union.Parts[union.Parts.Count - 1];
        string clauses = CloseBefore(EndsOpen(lastPart), inline + subjectFinal);
        return ProseFormat.JoinReferences(parts, ClosesBeforeOr(union.Parts)) + clauses;
    }

    // Where each adjective lands (GRAMMAR §5.2, §4.10), accumulated in authoring order: Head substitutes,
    // HeadPrefix appends after the seeded prefixes, Inline concatenates, and SubjectFinal collects into a
    // group the return renders in sentence-final order. A prefix already held is not appended again: every
    // type and project prefix names exactly one fact, so a repeat narrows nothing, and a pack rule that adds
    // `.Authored()` to a caller's subject must not read "authored authored types" when that subject already
    // carries it. The prefixes come back null only when the seed was null and none was added, because Phrase
    // and UnionPhrase read null as "no override" when choosing a bare reference. The caller supplies the
    // seeds — Phrase its noun's head and whatever a union passed down, UnionPhrase the union's nullable
    // overrides, ProjectPhrase the bare plural — so no two subject assemblies can disagree about a placement,
    // and a fifth AdjectivePlacement is one edit rather than one per stratum. It takes (placement, fragment,
    // opens-parenthetical) triples rather than an adjective list because the strata's adjective hierarchies
    // are deliberately disjoint and share no base: the triples are the whole of what placement needs from
    // any of them. MemberPhrase keeps its own variant, and that one is a real divergence rather than a copy —
    // its HeadPrefix arm keeps a repeat because attribute prefixes can collide — while its sentence-final
    // group orders through the same SentenceFinalOrder, so a member Except renders after a member Where.
    private static (string? Head, IReadOnlyList<string>? HeadPrefixes, string Inline, string SubjectFinal) Placements(
        IEnumerable<(AdjectivePlacement Placement, string Fragment, bool OpensParenthetical)> adjectives,
        string? head, IReadOnlyList<string>? headPrefixes)
    {
        List<string>? prefixes = headPrefixes?.ToList();
        var inline = string.Empty;
        var clauses = new List<(string Fragment, bool OpensParenthetical)>();
        foreach ((AdjectivePlacement placement, string fragment, bool opensParenthetical) in adjectives)
            switch (placement)
            {
                case AdjectivePlacement.Head:
                    head = fragment;
                    break;
                case AdjectivePlacement.HeadPrefix:
                    prefixes ??= new List<string>();
                    if (!prefixes.Contains(fragment)) prefixes.Add(fragment);
                    break;
                case AdjectivePlacement.Inline:
                    inline += fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    clauses.Add((fragment, opensParenthetical));
                    break;
            }

        IEnumerable<(string Fragment, bool OpensParenthetical)> ordered =
            SentenceFinalOrder(clauses, clause => clause.OpensParenthetical);
        string subjectFinal = string.Concat(ordered.Select(clause => clause.Fragment));

        return (head, prefixes, inline, subjectFinal);
    }

    // The text in front of the head. Each prefix fragment carries its own trailing space, so the prefixes
    // concatenate as they stand.
    private static string Premodifiers(IReadOnlyList<string>? headPrefixes)
    {
        return headPrefixes is null ? string.Empty : string.Concat(headPrefixes);
    }

    // The sentence-final group in rendered order (GRAMMAR §6): every Where in authoring order, then every
    // Except in authoring order, so the parenthetical is the last thing before the junction that closes it.
    // OrderBy is stable, which is what keeps authoring order inside each half. Placements assembles the group
    // by this and the EndsOpen twins read its last clause by this, so a phrase and its openness cannot
    // disagree. Generic over the clause type because the strata's adjective hierarchies share no base.
    private static IEnumerable<TClause> SentenceFinalOrder<TClause>(
        IEnumerable<TClause> clauses, Func<TClause, bool> opensParenthetical)
    {
        return clauses.OrderBy(opensParenthetical);
    }

    // The operand nouns of a union that collapses to one head and locative, or null when it does not
    // (GRAMMAR §6): a single operand (which renders as the bare operand — the identity), an operand
    // carrying adjectives, mixed noun kinds, or a noun kind that declares no collapse all fall back to
    // the or-join.
    private static IReadOnlyList<SelectionNoun>? CollapsibleNouns(UnionSelection union)
    {
        if (union.Parts.Count < 2) return null;

        var nouns = new List<SelectionNoun>(union.Parts.Count);
        foreach (Selection part in union.Parts)
        {
            if (part is UnionSelection || part.Adjectives.Count > 0) return null;
            if (nouns.Count > 0 && part.Noun.GetType() != nouns[0].GetType()) return null;

            nouns.Add(part.Noun);
        }

        return nouns[0].CollapsedLocative(nouns) is null ? null : nouns;
    }

    // Whether a phrase ends inside an Except parenthetical (GRAMMAR §6): the last clause of its
    // sentence-final group, read in rendered rather than authoring order, says it opens one — and because a
    // Where never follows an Except in that order, the group ends open exactly when it carries an Except —
    // or, with no clause of its own, it is an or-joined union whose last operand ends open. A collapsed
    // union, a bare noun and an inline-terminated phrase are closed. The composer reads this at every
    // junction where text follows, because the fragment cannot know what follows it.
    private static bool EndsOpen(Selection selection)
    {
        IEnumerable<SelectionAdjective> clauses = selection.Adjectives
            .Where(adjective => adjective.Placement == AdjectivePlacement.SubjectFinal);
        SelectionAdjective? lastClause = SentenceFinalOrder(clauses, adjective => adjective.OpensParenthetical)
            .LastOrDefault();
        if (lastClause is not null) return lastClause.OpensParenthetical;

        bool endsWithInlineClause = selection.Adjectives
            .Any(adjective => adjective.Placement == AdjectivePlacement.Inline);
        if (endsWithInlineClause) return false;

        if (selection is not UnionSelection union || CollapsibleNouns(union) is not null) return false;

        return EndsOpen(union.Parts[union.Parts.Count - 1]);
    }

    // The project stratum's twin, reading the same rendered order. Except and Where are the only
    // sentence-final project adjectives, and the project phrase has no union or bare-noun arm to fall
    // through to.
    private static bool EndsOpen(ProjectSelection selection)
    {
        IEnumerable<ProjectAdjective> clauses = selection.Adjectives
            .Where(adjective => adjective.Placement == AdjectivePlacement.SubjectFinal);
        ProjectAdjective? lastClause = SentenceFinalOrder(clauses, adjective => adjective.OpensParenthetical)
            .LastOrDefault();
        return lastClause is { OpensParenthetical: true };
    }

    // A member subject renders its own inline and sentence-final clauses AFTER the reference, so those
    // clauses close an Except the source left open, and its last sentence-final clause, read in rendered
    // order, says whether it opened one of its own. Only head prefixes leave the reference at the end.
    private static bool EndsOpen(MemberSelection selection)
    {
        IEnumerable<MemberAdjective> clauses = selection.Adjectives
            .Where(adjective => adjective.Placement == AdjectivePlacement.SubjectFinal);
        MemberAdjective? lastClause = SentenceFinalOrder(clauses, adjective => adjective.OpensParenthetical)
            .LastOrDefault();
        if (lastClause is not null) return lastClause.OpensParenthetical;

        return EndsOpen(selection.Source)
               && selection.Adjectives.All(adjective => adjective.Placement == AdjectivePlacement.HeadPrefix);
    }

    // Comma-closes a following fragment when the phrase before it ended open. Every following fragment opens
    // with a space, so this yields ", returning `Task`", ", whose name …", ", without a `when` filter". An
    // empty following is closed by whatever comes after it — the sentence-final period, the verb — and an
    // Except following an Except already carries the comma.
    private static string CloseBefore(bool endsOpen, string following)
    {
        if (!endsOpen || following.Length == 0 || following[0] == ',') return following;

        return "," + following;
    }

    // The or-joiner's closing decision for a list: only the PENULTIMATE item can end open and still be
    // followed by text, because every item before it is already followed by a comma (GRAMMAR §6).
    private static bool ClosesBeforeOr(IReadOnlyList<Selection> parts)
    {
        return parts.Count >= 2 && EndsOpen(parts[parts.Count - 2]);
    }

    // Member-subject assembly (GRAMMAR §4.6, §6): head-prefix member adjectives in authoring order +
    // "{kind-plural} of {selection-reference}" + the inline Returning clauses in authoring order + the
    // sentence-final member Where and Except clauses, every Where before every Except. Every member adjective
    // that names the member is a head prefix, because an inline one after the reference reads as naming the
    // types. The kind-plural is the projection head; the
    // reference is the underlying type selection in reference position. There is no head-SUBSTITUTION arm:
    // the projection fixes the head.
    private static string MemberPhrase(MemberSelection selection)
    {
        string head = ProseFormat.MemberKindPlural(selection.Kind);
        string reference = Reference(selection.Source);

        var headPrefix = string.Empty;
        var inline = string.Empty;
        var clauses = new List<MemberAdjective>();
        foreach (MemberAdjective adjective in selection.Adjectives)
            switch (adjective.Placement)
            {
                case AdjectivePlacement.HeadPrefix:
                    // Stacked member prefixes are an INTERSECTION, so a member narrowed by two attribute
                    // adjectives carries both and the sentence must say both. Unlike Placements, this keeps a
                    // repeat: an attribute prefix renders the attribute's short name, so two different
                    // attributes can read alike, and dropping one would describe a wider subject than the
                    // checker uses.
                    headPrefix += adjective.Fragment;
                    break;
                case AdjectivePlacement.SubjectFinal:
                    clauses.Add(adjective);
                    break;
                default:
                    inline += adjective.Fragment;
                    break;
            }

        string subjectFinal = string.Concat(
            SentenceFinalOrder(clauses, adjective => adjective.OpensParenthetical).Select(adjective => adjective.Fragment));
        return headPrefix + head + " of " + reference + CloseBefore(EndsOpen(selection.Source), inline + subjectFinal);
    }

    // Project-subject assembly (GRAMMAR §4.10, §6): the bare plural "projects" as the head, then the shared
    // placement accumulator. It goes through Placements rather than a loop of its own because the project
    // stratum's placements mean exactly what the type side's do — Named substitutes the head ("project `A`"),
    // Packable premodifies it ("packable "), Matching is an inline reduced relative clause, and Except/Where
    // canonicalize sentence-final. There is no locative: the head carries the whole noun.
    private static string ProjectPhrase(ProjectSelection selection)
    {
        (string? head, IReadOnlyList<string>? headPrefixes, string inline, string subjectFinal) = Placements(
            selection.Adjectives.Select(adjective =>
                (adjective.Placement, adjective.Fragment, adjective.OpensParenthetical)),
            "projects",
            null);

        return Premodifiers(headPrefixes) + head + inline + subjectFinal;
    }

    private static bool TryBareTypeAnchor(Selection selection, out TypeAnchor anchor)
    {
        if (selection.Adjectives.Count == 0 && SelectionWalk.NounOf(selection) is TypeNoun typeNoun)
        {
            anchor = typeNoun.Anchor;
            return true;
        }

        anchor = null!;
        return false;
    }
}
