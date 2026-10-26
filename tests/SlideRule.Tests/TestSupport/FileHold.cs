using System.Runtime.ExceptionServices;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     An open handle on a file, standing in for another process that has it open until disposed. The factory
///     decides which opens and moves the runtime refuses meanwhile.
/// </summary>
internal sealed class FileHold : IDisposable
{
    private readonly FileStream _stream;
    private readonly List<Exception> _refusals = [];
    private int _armedThreadId;
    private int _releaseAt;

    private FileHold(string path, FileShare share)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, share);
    }

    /// <summary>
    ///     The <see cref="IOException" />s and <see cref="UnauthorizedAccessException" />s the armed thread threw,
    ///     from <see cref="ReleaseOnRefusal" /> until disposal, in order. It is empty for a hold that was never armed.
    /// </summary>
    internal IReadOnlyList<Exception> Refusals => _refusals;

    /// <summary>
    ///     A read without delete sharing, as an antivirus or indexer scan holds a fresh file. Windows refuses to
    ///     rename the file, or to replace it as a move's destination, until the hold ends. POSIX renames over an
    ///     open file, so elsewhere nothing is refused.
    /// </summary>
    internal static FileHold WithoutDeleteSharing(string path)
    {
        return new FileHold(path, FileShare.ReadWrite);
    }

    /// <summary>
    ///     No other open of the file through the runtime succeeds until the hold ends. Windows enforces the sharing
    ///     mode, and elsewhere the runtime takes an exclusive lock that its own opens test for. A stat still
    ///     succeeds, so the file reads as present rather than deleted.
    /// </summary>
    internal static FileHold Exclusive(string path)
    {
        return new FileHold(path, FileShare.None);
    }

    /// <summary>
    ///     Ends the hold from inside the <paramref name="nth" /> refusal the calling thread throws, before any catch
    ///     filter sees it. The attempt after it is then certain to find the file free, whatever the retry schedule,
    ///     so code under test succeeds only if it retries at least <paramref name="nth" /> times, and
    ///     <see cref="Refusals" /> shows the refusals that happened.
    /// </summary>
    internal void ReleaseOnRefusal(int nth)
    {
        _releaseAt = nth;
        _armedThreadId = Environment.CurrentManagedThreadId;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        _stream.Dispose();
    }

    // The event fires for every exception in the process, so the thread test keeps other tests' exceptions out.
    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (Environment.CurrentManagedThreadId != _armedThreadId) return;
        if (e.Exception is not (IOException or UnauthorizedAccessException)) return;

        _refusals.Add(e.Exception);
        if (_refusals.Count == _releaseAt) _stream.Dispose();
    }
}
