using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.Analyzers;

/// <summary>What one read of an additional file produced, as far as keeping it is concerned.</summary>
internal interface IReadHandle
{
    /// <summary>The fault that stopped the read, or null for a text that was read.</summary>
    Exception? Fault { get; }
}

/// <summary>
///     One kind of additional-file read, kept per path and handed back for as long as the text at that path
///     is unchanged, so a host that gives every compilation a fresh text of one file reads it once.
/// </summary>
/// <remarks>
///     <para>
///         An entry answers only a text whose checksum algorithm, length, encoding and checksum all match the
///         text it was read from. Anything else is read afresh, and the read replaces the entry. An entry
///         holds the read and the checksum, never the text.
///     </para>
///     <para>
///         A faulted read is handed back and never kept, so a fault that stopped one compilation cannot
///         answer the next one at the same path. Keys compare ordinally: a compiler spells a path one way, and
///         a second spelling costs a second entry and nothing else, because the checksum and not the key
///         decides whether an entry answers.
///     </para>
///     <para>
///         Nothing here names a type of SlideRule or SlideRule.Extraction, so a static field
///         of this type binds neither when the analyzer loads.
///     </para>
/// </remarks>
/// <typeparam name="T">The read's result.</typeparam>
internal sealed class PathKeyedReads<T>
    where T : class, IReadHandle
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<SourceText, T> _read;

    /// <summary>
    ///     Reads through <paramref name="read" />, which must hold every fault but a cancellation in the value
    ///     it returns.
    /// </summary>
    internal PathKeyedReads(Func<SourceText, T> read)
    {
        _read = read;
    }

    /// <summary>
    ///     The read of <paramref name="text" />, the text at <paramref name="path" />: the kept one where the
    ///     text is unchanged, otherwise a fresh one.
    /// </summary>
    internal T Read(string path, SourceText text)
    {
        if (_entries.TryGetValue(path, out Entry? kept) && kept.Answers(text)) return kept.Value;

        T read = _read(text);
        if (read.Fault is null) _entries[path] = new Entry(text, read);

        return read;
    }

    private sealed class Entry
    {
        private readonly SourceHashAlgorithm _algorithm;
        private readonly int _length;
        private readonly string? _encoding;
        private readonly ImmutableArray<byte> _checksum;

        internal Entry(SourceText text, T value)
        {
            _algorithm = text.ChecksumAlgorithm;
            _length = text.Length;
            _encoding = text.Encoding?.WebName;
            _checksum = text.GetChecksum();
            Value = value;
        }

        internal T Value { get; }

        // The encoding is compared because a file-read text's checksum is of the bytes it was decoded from,
        // and one file decoded two ways is two texts under one checksum.
        internal bool Answers(SourceText text)
        {
            return text.ChecksumAlgorithm == _algorithm
                   && text.Length == _length
                   && string.Equals(text.Encoding?.WebName, _encoding, StringComparison.Ordinal)
                   && text.GetChecksum().SequenceEqual(_checksum);
        }
    }
}
