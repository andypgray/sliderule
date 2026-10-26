using Microsoft.CodeAnalysis;
using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     One project as the exclusion walk sees it: the <see cref="Name" /> the checked universe filters on,
///     the <c>.csproj</c> <see cref="FilePath" /> declared membership is tested against, and the names of the
///     projects it references. Reduced to a tuple so the walk is unit-testable over synthetic graphs, with no
///     workspace and no disk.
/// </summary>
/// <remarks>
///     <see cref="CanonicalFilePath" /> is <see cref="FilePath" /> as the load's <see cref="CanonicalProjectFiles" />
///     holds it, which only the overload of
///     <see cref="SpecExclusion.Compute(Solution, CanonicalProjectFiles, IReadOnlySet{string}, string)" />
///     taking a solution fills; a synthetic graph leaves it null and has <see cref="FilePath" /> resolved instead.
/// </remarks>
internal sealed record SpecExclusionProject(
    string Name,
    string? FilePath,
    IReadOnlyList<string> ProjectReferenceNames,
    string? CanonicalFilePath = null);

/// <summary>
///     Which projects a solution-member spec drops from the checked universe:
///     <c>{spec project} ∪ (transitive ProjectReference closure of the spec project ∖ declared members)</c>.
///     A spec project's own <c>ProjectReference</c>s drag the contract library — and any rule-pack library it
///     composes from — into the workspace as passengers, and a passenger is not solution material just
///     because MSBuild loaded it. What the solution <em>declares</em> is.
/// </summary>
/// <remarks>
///     <para>
///         The subtraction must not collapse to "the closure". A spec routinely references the very code it
///         governs, so excluding a raw closure would drop the codebase under law and silently report green over
///         nothing. Declared membership is the only sound discriminator: it says which projects the solution
///         claims, independently of who happens to reference whom.
///     </para>
///     <para>
///         Never fail open into an empty universe. When membership cannot be read (an unreadable or malformed
///         solution file, a malformed filter, or a format
///         <see cref="SolutionProjectFileParser.OwnsFormat">the parser does not own</see>) the answer degrades to
///         <c>{spec project}</c> alone, because an unparsed solution reads as zero declared members and the
///         alternative would subtract the whole closure.
///     </para>
///     <para>
///         A solution filter reads through to the solution it filters, and against that solution's
///         <see cref="SolutionMembership.Declared">whole</see> membership rather than the filter's selection:
///         membership answers "is this project solution material?", which a filter does not change. Selecting
///         instead would exclude a member a reference dragged in anyway.
///     </para>
/// </remarks>
internal static class SpecExclusion
{
    /// <summary>
    ///     The exclusion set for <paramref name="specProjectName" /> over a loaded <paramref name="solution" />,
    ///     whose project files <paramref name="projectFiles" /> holds, against an already-read
    ///     <paramref name="declaredMembers" /> set (null when membership is unreadable, which falls back to
    ///     <c>{spec project}</c>).
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(
        Solution solution, CanonicalProjectFiles projectFiles, IReadOnlySet<string>? declaredMembers,
        string specProjectName)
    {
        List<SpecExclusionProject> projects = solution.Projects
            .Select(project => new SpecExclusionProject(
                project.Name,
                project.FilePath,
                solution.ReferencedProjectNames(project).ToList(),
                projectFiles.PathOf(project)))
            .ToList();

        return Compute(projects, declaredMembers, specProjectName);
    }

    /// <summary>
    ///     The pure core, testable over a synthetic project graph. Ordinal-sorted so the set serializes to stable
    ///     bytes in the extraction cache.
    /// </summary>
    internal static IReadOnlyCollection<string> Compute(
        IReadOnlyList<SpecExclusionProject> projects,
        IReadOnlySet<string>? declaredMembers,
        string specProjectName)
    {
        if (declaredMembers is null) return [specProjectName];

        var referencesByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var declaredByName = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (SpecExclusionProject project in projects)
        {
            if (!referencesByName.TryGetValue(project.Name, out List<string>? references))
                referencesByName[project.Name] = references = [];
            references.AddRange(project.ProjectReferenceNames);

            // A multi-targeted project arrives once per framework, under the one name the load boundary
            // normalized its Projects to; declared by any entry is declared, and its reference edges union.
            declaredByName[project.Name] = declaredByName.GetValueOrDefault(project.Name)
                                           || IsMember(
                                               declaredMembers, project.FilePath,
                                               project.CanonicalFilePath ?? ResolveOrNull(project.FilePath));
        }

        var excluded = new SortedSet<string>(StringComparer.Ordinal) { specProjectName };
        var visited = new HashSet<string>(StringComparer.Ordinal) { specProjectName };
        var pending = new Queue<string>();
        pending.Enqueue(specProjectName);

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!referencesByName.TryGetValue(name, out List<string>? references)) continue;

            foreach (string reference in references)
            {
                if (!visited.Add(reference)) continue;
                pending.Enqueue(reference);

                // Absent from the graph ⇒ nothing to exclude: it is not in the universe to begin with.
                if (!declaredByName.GetValueOrDefault(reference, true)) excluded.Add(reference);
            }
        }

        return excluded;
    }

    /// <summary>
    ///     The solution's declared <c>.csproj</c> members, canonicalized for comparison, or
    ///     <see langword="null" /> when membership cannot be read — an unowned format or any read/parse
    ///     failure, a malformed solution filter included. Null is the fallback signal, never an empty set.
    /// </summary>
    internal static IReadOnlySet<string>? TryReadDeclaredMembers(string solutionPath)
    {
        if (!SolutionProjectFileParser.OwnsFormat(solutionPath)) return null;

        try
        {
            return CanonicalMemberSet(SolutionProjectFileParser.ReadDeclaredMembership(solutionPath).Declared);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Canonicalizes <paramref name="csprojPaths" /> into a per-OS comparable set (symlinks resolved,
    ///     <see cref="PathComparison" /> casing), the same standard both sides of every membership test use.
    /// </summary>
    internal static IReadOnlySet<string> CanonicalMemberSet(IEnumerable<string> csprojPaths)
    {
        var canonicalizer = new ProjectFileCanonicalizer();
        IEnumerable<string> canonical = csprojPaths.Select(canonicalizer.Resolve).OfType<string>();
        return new HashSet<string>(canonical, PathComparison.Comparer);
    }

    /// <summary>
    ///     Whether <paramref name="project" /> is one of <paramref name="declaredMembers" />, compared by the
    ///     canonical path <paramref name="projectFiles" /> holds for it. Unreadable membership and an unknown
    ///     project path both answer <see langword="true" />: neither disproves membership, and a false negative
    ///     would silently shrink the checked universe.
    /// </summary>
    internal static bool IsDeclaredMember(
        IReadOnlySet<string>? declaredMembers, CanonicalProjectFiles projectFiles, Project project)
    {
        return IsMember(
            declaredMembers, project.FilePath, projectFiles.PathOf(project) ?? ResolveOrNull(project.FilePath));
    }

    /// <summary>
    ///     The same question asked as a fact to report rather than a set to subtract:
    ///     <see langword="true" /> declared, <see langword="false" /> a passenger, <see langword="null" />
    ///     when nothing was read (unreadable membership, or a project whose path the load never reported).
    /// </summary>
    /// <remarks>
    ///     The unread cases answer null, not <see cref="IsDeclaredMember" />'s true: a report of "declared" that no
    ///     solution file was ever read for would be an asserted fact with nothing behind it.
    /// </remarks>
    internal static bool? SolutionMembershipOf(
        IReadOnlySet<string>? declaredMembers, CanonicalProjectFiles projectFiles, Project project)
    {
        if (declaredMembers is null || string.IsNullOrEmpty(project.FilePath)) return null;

        return IsDeclaredMember(declaredMembers, projectFiles, project);
    }

    // The declared set is canonical, so a project is compared in its canonical spelling, never in the one the
    // load reported; the canonical path is null exactly when the project file path is empty.
    private static bool IsMember(
        IReadOnlySet<string>? declaredMembers, string? projectFilePath, string? canonicalProjectFilePath)
    {
        if (declaredMembers is null || string.IsNullOrEmpty(projectFilePath)) return true;

        return declaredMembers.Contains(canonicalProjectFilePath!);
    }

    private static string? ResolveOrNull(string? projectFilePath)
    {
        return string.IsNullOrEmpty(projectFilePath) ? null : PathCanonicalizer.Resolve(projectFilePath);
    }
}
