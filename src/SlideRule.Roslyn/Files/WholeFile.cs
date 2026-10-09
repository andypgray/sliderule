using System.Text;
using SlideRule.Rendering;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Roslyn.Files;

/// <summary>
///     Writes a generated file whose whole content this tool owns — compare first, then write only what
///     changed.
/// </summary>
/// <remarks>
///     <para>
///         The contract, which every caller depends on and none may restate: the composed text is LF, and an
///         existing file is compared after its CRLF is normalized to LF, so a checkout that rewrote the line
///         endings reports <see cref="WriteOutcome.Unchanged" /> and is left exactly as it is — bytes and
///         timestamp both. A file that must change is written UTF-8 with no BOM, atomically, and its
///         directory is created when it is missing.
///     </para>
///     <para>
///         The comparison is what makes these files safe to regenerate on every run: a build that rewrote an
///         unchanged file would touch its timestamp, and downstream anything keyed on that — an incremental
///         build, a watcher, a diff in a review — would read work where there was none.
///     </para>
///     <para>
///         Distinct from the managed-block splice, which owns a region of somebody else's file and must
///         preserve the hand-written text around it. This owns the whole file, which is what lets it compare
///         one string against one string.
///     </para>
/// </remarks>
internal static class WholeFile
{
    /// <summary>
    ///     Writes <paramref name="composed" /> to <paramref name="absolutePath" /> when it differs from
    ///     what is there, and reports which of the two happened.
    /// </summary>
    /// <param name="absolutePath">The file to own, whose directory is created when absent.</param>
    /// <param name="composed">The whole file's text, LF-newlined.</param>
    internal static WriteOutcome Write(string absolutePath, string composed)
    {
        if (IsCurrent(absolutePath, composed)) return WriteOutcome.Unchanged;

        // These are committed, version-controlled files, so the write is atomic: a crash mid-write must not
        // truncate one. UTF8Encoding(false) emits no preamble, which is what keeps the file BOM-less.
        byte[] bytes = new UTF8Encoding(false).GetBytes(composed);
        AtomicFile.WriteAllBytes(absolutePath, bytes);
        return WriteOutcome.Wrote;
    }

    /// <summary>
    ///     Whether the file already holds <paramref name="composed" />, line endings aside: the comparison
    ///     <see cref="Write" /> makes before it writes, for a caller that must not write.
    /// </summary>
    internal static bool IsCurrent(string absolutePath, string composed)
    {
        return File.Exists(absolutePath) && NormalizeNewlines(File.ReadAllText(absolutePath)) == composed;
    }

    internal static string NormalizeNewlines(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}
