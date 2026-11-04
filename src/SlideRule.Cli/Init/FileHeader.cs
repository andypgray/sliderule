using Microsoft.CodeAnalysis;

namespace SlideRule.Cli.Init;

/// <summary>
///     The header a repository's <c>.editorconfig</c> asks every C# file to start with, as the IDE0073 fix writes
///     it, so a tree that enforces code style in its build accepts the spec class.
/// </summary>
internal static class FileHeader
{
    private const string Key = "file_header_template";

    /// <summary>
    ///     The header for a C# file at <paramref name="path" />, ending in the blank line that separates it from the
    ///     code, or null where no <c>.editorconfig</c> sets the template for that path.
    /// </summary>
    /// <remarks>
    ///     The configs are resolved by the compiler's own reader, so sections, globs and <c>root = true</c> mean what
    ///     they mean to the build. <c>\n</c> in the value is a line break and <c>{fileName}</c> the file's name, the
    ///     two substitutions IDE0073 makes.
    /// </remarks>
    internal static string? For(string path)
    {
        List<AnalyzerConfig> configs = [];
        for (DirectoryInfo? directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, ".editorconfig");
            if (File.Exists(candidate)) configs.Add(AnalyzerConfig.Parse(File.ReadAllText(candidate), candidate));
        }

        if (configs.Count == 0) return null;

        AnalyzerConfigOptionsResult options = AnalyzerConfigSet.Create(configs)
            .GetOptionsForSourcePath(path);
        if (!options.AnalyzerOptions.TryGetValue(Key, out string? template)
            || string.IsNullOrWhiteSpace(template)
            || string.Equals(template, "unset", StringComparison.OrdinalIgnoreCase))
            return null;

        IEnumerable<string> lines = template.Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("{fileName}", Path.GetFileName(path), StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Length == 0 ? "//" : "// " + line);
        return string.Join("\n", lines) + "\n\n";
    }
}
