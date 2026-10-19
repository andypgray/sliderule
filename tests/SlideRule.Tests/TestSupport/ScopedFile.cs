namespace SlideRule.Tests.TestSupport;

/// <summary>
///     One file inside a shared fixture copy, put back as it was found when a <c>using</c> ends: a file that
///     existed gets its bytes and last-write time back through <see cref="FileSnapshot" />, and a file that did
///     not exist is deleted. For a test that edits, touches, deletes or plants a file the next test will read.
/// </summary>
/// <remarks>
///     Take the scope before the mutation. A helper that writes a new file returns <see cref="Planted" />
///     rather than leaving the caller to <see cref="Hold" /> it afterwards, which would see the file as
///     existing and restore it instead of deleting it.
/// </remarks>
internal sealed class ScopedFile : IDisposable
{
    private readonly (byte[] bytes, DateTime mtime)? _original;

    private ScopedFile(string path, (byte[] bytes, DateTime mtime)? original)
    {
        Path = path;
        _original = original;
    }

    /// <summary>The file this scope restores.</summary>
    internal string Path { get; }

    /// <summary>
    ///     Captures whether <paramref name="path" /> exists and, if it does, its bytes and last-write time.
    /// </summary>
    internal static ScopedFile Hold(string path)
    {
        (byte[] bytes, DateTime mtime)? original = File.Exists(path) ? FileSnapshot.Capture(path) : null;
        return new ScopedFile(path, original);
    }

    /// <summary>A file the caller has just written and that did not exist before: deleted on dispose.</summary>
    internal static ScopedFile Planted(string path)
    {
        return new ScopedFile(path, null);
    }

    /// <summary>Restores the captured bytes and last-write time, or deletes a file that was absent.</summary>
    public void Dispose()
    {
        if (_original is { } original)
            FileSnapshot.Restore(Path, original);
        else
            File.Delete(Path);
    }
}
