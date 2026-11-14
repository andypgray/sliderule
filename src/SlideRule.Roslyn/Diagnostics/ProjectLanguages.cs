using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The one answer to which projects this product reads: every arm that skips a project the model was never
///     going to contain asks here, so widening the set is one edit.
/// </summary>
internal static class ProjectLanguages
{
    /// <summary>The file-name suffix the SDK's default compile glob matches in a C# project.</summary>
    internal const string CSharpSourceSuffix = ".cs";

    private const string VisualBasicSourceSuffix = ".vb";

    internal static bool IsCSharp(Project project)
    {
        return string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The file-name suffix the SDK's default compile glob matches in <paramref name="project" />'s language, or
    ///     null for a language whose sources are not globbed by suffix, such as F#, which lists every file in order.
    /// </summary>
    internal static string? SourceSuffixOf(Project project)
    {
        return project.Language switch
        {
            LanguageNames.CSharp => CSharpSourceSuffix,
            LanguageNames.VisualBasic => VisualBasicSourceSuffix,
            _ => null
        };
    }
}
