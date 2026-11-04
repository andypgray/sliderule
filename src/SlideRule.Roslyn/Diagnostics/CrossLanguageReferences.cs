using Microsoft.CodeAnalysis;
using SlideRule.Internal;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Which C# projects reference a loaded project in another language whose assembly never reached the C#
///     compilation. The workspace builds such a reference from the other project's own compilation and leaves it
///     out, with no diagnostic, when that compilation cannot produce one (a Visual Basic project with declaration
///     errors, for one), so the C# project's types from it bind to nothing.
/// </summary>
/// <remarks>
///     The signal is the C# compilation's own list of referenced assemblies, the one thing that decides whether the
///     types bind. Only a C# project with such a reference is compiled here, and the compilation stays cached on the
///     solution for extraction to reuse.
/// </remarks>
internal static class CrossLanguageReferences
{
    /// <summary>
    ///     A C# project, by its absolute project-file path, and a project it references, by the same, whose
    ///     assembly its compilation did not receive.
    /// </summary>
    internal readonly record struct Finding(string ReferrerPath, string TargetPath);

    /// <summary>
    ///     Every such pair in <paramref name="solution" />, once per project-file pair however many frameworks either
    ///     loads for, ordinal-sorted by referrer and then target. The projects in <paramref name="alreadyFailed" />
    ///     are left out.
    /// </summary>
    internal static async Task<IReadOnlyList<Finding>> DetectAsync(
        Solution solution, IReadOnlyList<string> alreadyFailed, CancellationToken ct)
    {
        var failed = new HashSet<string>(alreadyFailed, PathComparison.Comparer);
        var findings = new HashSet<Finding>();
        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is not { } filePath || !ProjectLanguages.IsCSharp(project)) continue;

            string referrerPath = Path.GetFullPath(filePath);
            if (failed.Contains(referrerPath)) continue;

            List<Project> targets = project.ProjectReferences
                .Select(reference => solution.GetProject(reference.ProjectId))
                .OfType<Project>()
                .Where(target => !ProjectLanguages.IsCSharp(target) && target.FilePath is not null)
                .ToList();
            if (targets.Count == 0) continue;

            if (await project.GetCompilationAsync(ct).ConfigureAwait(false) is not { } compilation) continue;

            // Assembly simple names compare case-insensitively, as the runtime and MSBuild compare them.
            HashSet<string> received = compilation.ReferencedAssemblyNames
                .Select(identity => identity.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IEnumerable<Finding> dropped = targets
                .Where(target => !received.Contains(target.AssemblyName))
                .Select(target => new Finding(referrerPath, Path.GetFullPath(target.FilePath!)));
            findings.UnionWith(dropped);
        }

        return findings
            .OrderBy(finding => finding.ReferrerPath, StringComparer.Ordinal)
            .ThenBy(finding => finding.TargetPath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The load warning for <paramref name="finding" />.</summary>
    internal static string Message(Finding finding)
    {
        return $"'{finding.ReferrerPath}' references '{finding.TargetPath}', whose assembly did not reach its "
               + "compilation; types from that project are unbound, so edges to them are absent from the model.";
    }
}
