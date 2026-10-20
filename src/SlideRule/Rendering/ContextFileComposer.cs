using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     Composes every <c>AGENTS.md</c> body one render writes: the root block in the solution directory,
///     a local-rules card for each layer that has rules of its own, and a card for each scope. Cards that
///     land in the same directory are merged into one body, layer cards first and scope cards after, so no
///     directory is written twice. Nothing here touches the file system or prints anything: the files come
///     back as text, and a card that could not be placed comes back as a warning.
/// </summary>
// The single composition path: `render` splices what this returns and the card-drift gate compares
// against it, so the gate cannot drift from the command it gates. A gate that rebuilt the body its
// own way could only prove that two pieces of code agree with each other.
internal static class ContextFileComposer
{
    public const string FileName = "AGENTS.md";

    /// <summary>
    ///     Composes the context files for <paramref name="model" />: the root block, written to
    ///     <paramref name="solutionDirectory" />, always, and the scoped cards as well when
    ///     <paramref name="codebase" /> is supplied. <paramref name="specName" /> goes into each file's
    ///     provenance line.
    /// </summary>
    public static ContextComposition Compose(
        ArchitectureModel model, CodebaseModel? codebase, string solutionDirectory, string specName)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNullOrWhiteSpace(solutionDirectory, nameof(solutionDirectory));
        Guard.NotNullOrWhiteSpace(specName, nameof(specName));

        var units = new List<ContentUnit>
        {
            new(solutionDirectory, AgentContextRenderer.RootBlock(model, specName), true)
        };
        var warnings = new List<string>();

        if (codebase is not null) AddScopedUnits(model, codebase, solutionDirectory, units, warnings);

        return new ContextComposition(Group(units, specName), warnings);
    }

    /// <summary>
    ///     Reports whether this model places anything besides the root block: a scope of either posture, or
    ///     a layer with at least one rule of its own. Answered from the model alone, so a caller asks it before
    ///     paying for the codebase extraction <see cref="Compose" /> and <see cref="Placements" /> place
    ///     against, the expensive half of a render.
    /// </summary>
    // The scope test reads the rule's scope payload rather than its posture, so a posture added to
    // Posture later cannot silently lose its card here.
    public static bool HasAnythingToPlace(ArchitectureModel model)
    {
        Guard.NotNull(model, nameof(model));

        return model.Rules.Any(rule => rule.Scope is not null)
               || LayerContextResolver.HasAnchoredLayers(model);
    }

    /// <summary>
    ///     Renders every scoped card this model places against <paramref name="codebase" /> and pairs each
    ///     with the directory it belongs in. <paramref name="solutionDirectory" /> is the directory every card
    ///     is placed inside: pass the one the root block is written into.
    /// </summary>
    /// <returns>
    ///     The layer cards in declaration order, then the scope cards in model order. A card that could not
    ///     be placed keeps its position in the list, carrying a null directory and the reason it was
    ///     skipped, rather than being dropped.
    /// </returns>
    // The composition decision itself, so Compose and any other consumer of scoped context (the
    // `context` verb's path lookup, say) agree on the card kinds, their order, and what an unplaceable
    // card means, instead of each walking the two resolvers and deciding again.
    public static IReadOnlyList<ContextCard> Placements(
        ArchitectureModel model, CodebaseModel codebase, string solutionDirectory)
    {
        Guard.NotNull(model, nameof(model));
        Guard.NotNull(codebase, nameof(codebase));
        Guard.NotNullOrWhiteSpace(solutionDirectory, nameof(solutionDirectory));

        // One evaluator and one boundary for both emission keys: the evaluator materializes the
        // solution-declared type list and its noun indexes in its constructor, and the boundary splits the
        // solution directory once and memoizes each declaration site's verdict, so resolving the two keys
        // separately would build both twice.
        var evaluator = new SelectionEvaluator(codebase, new SolutionRoot(solutionDirectory));
        var boundary = new DirectoryPlacement.Boundary(solutionDirectory);
        var cards = new List<ContextCard>();

        foreach (LayerPlacement placement in LayerContextResolver.Resolve(model, evaluator, boundary))
            cards.Add(new ContextCard(
                placement.Destination,
                AgentContextRenderer.LayerCard(placement.LayerName, placement.Purpose, placement.Rules)));

        // Which card a scope gets is the posture's decision and only the posture's: the resolver already
        // picked the one card-bearing rule per scope, so this dispatch never has to ask which child it holds.
        foreach (ScopePlacement placement in ScopedContextResolver.Resolve(model, evaluator, boundary))
            cards.Add(new ContextCard(
                placement.Destination,
                placement.Rule.Posture == Posture.Caution
                    ? AgentContextRenderer.CautionCard(placement.Rule)
                    : AgentContextRenderer.ScopeCard(placement.Rule)));

        return cards;
    }

    private static void AddScopedUnits(
        ArchitectureModel model, CodebaseModel codebase, string solutionDirectory, List<ContentUnit> units,
        List<string> warnings)
    {
        foreach (ContextCard card in Placements(model, codebase, solutionDirectory))
        {
            if (card.Destination.DirectoryPath is null)
            {
                warnings.Add(card.Destination.SkipReason!);
                continue;
            }

            units.Add(new ContentUnit(card.Destination.DirectoryPath, card.Body, false));
        }
    }

    // Groups in first-seen order, so the root file comes first. A group that includes the root uses the root
    // block's own provenance line; a scoped-only file gets one prepended.
    private static IReadOnlyList<ContextFile> Group(IReadOnlyList<ContentUnit> units, string specName)
    {
        var groups = new List<FileGroup>();
        foreach (ContentUnit unit in units)
        {
            string key = Path.GetFullPath(unit.Directory);
            // Per-OS so two case-spellings of one directory merge into a single AGENTS.md on
            // case-insensitive file systems (Windows/macOS) — Ordinal would splice the file twice, the
            // second clobbering the first — while staying distinct on Linux.
            FileGroup? group = groups.FirstOrDefault(candidate => string.Equals(candidate.Key, key, PathComparison.Comparison));
            if (group is null)
            {
                group = new FileGroup(key, unit.Directory);
                groups.Add(group);
            }

            group.Bodies.Add(unit.Body);
            group.HasRoot |= unit.IsRoot;
        }

        return groups.Select(group => new ContextFile(
            Path.Combine(group.Directory, FileName),
            group.HasRoot
                ? string.Join("\n\n", group.Bodies)
                : AgentContextRenderer.ProvenanceLine(specName) + "\n\n" + string.Join("\n\n", group.Bodies))).ToList();
    }

    private sealed class ContentUnit(string directory, string body, bool isRoot)
    {
        public string Directory { get; } = directory;
        public string Body { get; } = body;
        public bool IsRoot { get; } = isRoot;
    }

    private sealed class FileGroup(string key, string directory)
    {
        public string Key { get; } = key;
        public string Directory { get; } = directory;
        public List<string> Bodies { get; } = [];
        public bool HasRoot { get; set; }
    }
}
