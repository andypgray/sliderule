using Microsoft.CodeAnalysis.CSharp;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Init;

/// <summary>
///     The spec project's name, which is also its folder under <c>arch/</c> and its file name, and the namespace the
///     spec class is declared in.
/// </summary>
internal sealed record SpecProjectName(string Project, string Namespace)
{
    private const string Suffix = ".Architecture";

    /// <summary>
    ///     The name <paramref name="name" /> asks for, or the solution's own name plus <c>.Architecture</c>, and the
    ///     namespace it spells: each dot-separated segment made a C# identifier.
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     The name is not a file name, or a namespace segment is <c>Arch</c>, which shadows the <c>Arch</c> type
    ///     <c>Define</c> takes (CS0118).
    /// </exception>
    internal static SpecProjectName For(string solutionPath, string? name)
    {
        string project = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(solutionPath) + Suffix
            : name.Trim();

        if (project.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || project.Trim('.').Length == 0)
            throw new UserErrorException(
                $"'{project}' cannot name the spec project's folder and file. Pass --name with a project name.");

        List<string> segments = project.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(Identifier)
            .ToList();
        if (segments.Contains("Arch", StringComparer.Ordinal))
            throw new UserErrorException(
                $"The spec project '{project}' would declare its class in a namespace with an 'Arch' segment, "
                + "which shadows the Arch type Define takes (CS0118). Pass --name with a name that has no 'Arch' "
                + "segment.");

        return new SpecProjectName(project, string.Join(".", segments));
    }

    // A segment as a C# identifier: every character an identifier cannot hold becomes '_', and a segment that then
    // starts with a digit or spells a keyword gets a leading '_'.
    private static string Identifier(string segment)
    {
        char[] characters = segment.Select(character => SyntaxFacts.IsIdentifierPartCharacter(character) ? character : '_')
            .ToArray();
        var identifier = new string(characters);

        bool startsBadly = !SyntaxFacts.IsIdentifierStartCharacter(identifier[0]);
        bool keyword = SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None;
        return startsBadly || keyword ? "_" + identifier : identifier;
    }
}
