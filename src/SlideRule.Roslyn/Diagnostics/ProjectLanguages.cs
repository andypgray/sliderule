using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The one answer to which projects this product reads: every arm that skips a project the model was never
///     going to contain asks here, so widening the set is one edit.
/// </summary>
internal static class ProjectLanguages
{
    internal static bool IsCSharp(Project project)
    {
        return string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);
    }
}
