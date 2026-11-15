using System.IO.Pipes;
using System.Runtime.Versioning;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     Where a warm server listens for one <see cref="HandoffIdentity" />, and the two halves of reaching it: owning
///     and serving the endpoint, and probing for and connecting to it. Only the current user can do either.
/// </summary>
/// <remarks>
///     <para>
///         On Windows the endpoint is a named pipe, and its owner holds a <c>Local\</c> mutex named after it for the
///         server's lifetime. Windows lets several processes create instances of one pipe name, so the mutex is
///         what keeps two servers from splitting the clients between them. The pipe grants the process token's owner,
///         which is the user except under an elevated token, whose owner is the Administrators group.
///     </para>
///     <para>
///         Elsewhere it is a Unix domain socket in a directory of this user's under <c>/tmp</c>, and its owner holds
///         an exclusive lock on a file beside it. The directory must be mode 0700, not a symbolic link, and
///         writable by this process: .NET reads no file's owner, and the three together leave only this user, or
///         root, as its owner. Only the lock's holder removes a socket a dead server left.
///     </para>
///     <para>
///         An owner proves neither that the server is ready nor that it is alive; it only spares a stop with no
///         server the cost of a connection attempt.
///     </para>
/// </remarks>
internal static class HandoffEndpoint
{
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    // As many instances as connections: a fixed count would let that many idle connections stop the endpoint
    // accepting, and the server drops a connection that sends no request in time.
    private const int MaxInstances = NamedPipeServerStream.MaxAllowedServerInstances;

    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    ///     Whether a server owns <paramref name="endpointName" />: its mutex exists, or its socket file does.
    /// </summary>
    public static bool OwnerPresent(string endpointName)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Mutex.TryOpenExisting(OwnershipName(endpointName), out Mutex? owner)) return false;

            owner.Dispose();
            return true;
        }

        return PrivateDirectory(create: false) is { } directory && File.Exists(SocketPath(directory, endpointName));
    }

    /// <summary>
    ///     Takes ownership of <paramref name="endpointName" />, or returns null when another process owns it or
    ///     this user's endpoint directory is refused. Disposing the result gives ownership up.
    /// </summary>
    /// <exception cref="IOException">The endpoint directory could not be made.</exception>
    /// <exception cref="UnauthorizedAccessException">The endpoint directory could not be made or written.</exception>
    public static EndpointOwnership? TryOwn(string endpointName)
    {
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(false, OwnershipName(endpointName), out bool createdNew);
            if (createdNew) return new EndpointOwnership(endpointName, mutex);

            mutex.Dispose();
            return null;
        }

        if (PrivateDirectory(create: true) is not { } directory) return null;

        FileStream held;
        try
        {
            held = new FileStream(
                Path.Combine(directory, endpointName + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }

        string socket = SocketPath(directory, endpointName);
        File.Delete(socket);
        return new EndpointOwnership(socket, held);
    }

    /// <summary>A new instance of the owned endpoint, ready to wait for a connection.</summary>
    public static NamedPipeServerStream CreateServerStream(EndpointOwnership ownership)
    {
        return new NamedPipeServerStream(
            ownership.PipeName, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, Options);
    }

    /// <summary>
    ///     Connects to <paramref name="endpointName" />'s server within <paramref name="timeout" />.
    /// </summary>
    /// <exception cref="TimeoutException">No server accepted in time.</exception>
    /// <exception cref="IOException">The endpoint could not be reached.</exception>
    /// <exception cref="UnauthorizedAccessException">The endpoint belongs to another user.</exception>
    public static async Task<NamedPipeClientStream> ConnectAsync(
        string endpointName, TimeSpan timeout, CancellationToken ct)
    {
        string pipeName = OperatingSystem.IsWindows()
            ? endpointName
            : SocketPath(PrivateDirectory(create: false) ?? throw new IOException("No private endpoint directory."), endpointName);

        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, Options);
        try
        {
            await client.ConnectAsync(timeout, ct);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    /// <summary>The <c>Local\</c> mutex that marks <paramref name="endpointName" /> as served.</summary>
    internal static string OwnershipName(string endpointName)
    {
        return @"Local\" + endpointName + ".owner";
    }

    /// <summary>
    ///     This user's endpoint directory off Windows, made with mode 0700 when <paramref name="create" /> asks for
    ///     it, or null when it is missing, a link, or has any other mode.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    internal static string? PrivateDirectory(bool create)
    {
        string directory = Path.Combine("/tmp", "sliderule-" + SafeUserName());
        if (!Directory.Exists(directory))
        {
            if (!create) return null;

            Directory.CreateDirectory(directory, PrivateDirectoryMode);
        }

        var info = new DirectoryInfo(directory);
        if (info.LinkTarget is not null || info.UnixFileMode != PrivateDirectoryMode) return null;

        return directory;
    }

    // A socket path is bounded at 104 bytes on macOS; the directory and the endpoint name stay well inside it.
    private static string SocketPath(string directory, string endpointName)
    {
        return Path.Combine(directory, endpointName + ".sock");
    }

    private static string SafeUserName()
    {
        return string.Concat(Environment.UserName.Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
    }
}

/// <summary>
///     A held endpoint: the name its server instances are created under, and the mutex or lock that marks it owned
///     until disposed.
/// </summary>
internal sealed class EndpointOwnership(string pipeName, IDisposable hold) : IDisposable
{
    /// <summary>The pipe name, or the socket path off Windows, its server instances take.</summary>
    public string PipeName => pipeName;

    /// <inheritdoc />
    public void Dispose()
    {
        hold.Dispose();
    }
}
