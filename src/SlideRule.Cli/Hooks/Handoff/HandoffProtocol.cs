using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SlideRule.Cli.Verbs;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     The one request a hook sends a warm server: who it expects to reach, the configuration it would check under,
///     a nonce the reply must echo, the check itself, and how long the server may wait to admit it, which is what
///     the discovery limit has left once the connection is made.
/// </summary>
internal sealed record HandoffRequest(
    HandoffIdentity Identity,
    IReadOnlyDictionary<string, string> Configuration,
    string Nonce,
    CheckRequest Check,
    int AdmissionWaitMilliseconds);

/// <summary>
///     The server's one reply: a decline naming its reason, or the completed check's exit code and both channels.
/// </summary>
/// <remarks>
///     A reply that is not declined and lacks its exit code or either channel is incomplete, and the hook reads it as
///     malformed.
/// </remarks>
internal sealed record HandoffReply(string Nonce, string? Declined, int? ExitCode, string? Output, string? Error)
{
    /// <summary>A decline of the request carrying <paramref name="nonce" />, for <paramref name="reason" />.</summary>
    public static HandoffReply Decline(string nonce, string reason)
    {
        return new HandoffReply(nonce, reason, null, null, null);
    }
}

/// <summary>
///     The frames the two sides exchange: a four-byte little-endian length, then that many bytes of UTF-8 JSON.
/// </summary>
internal static class HandoffProtocol
{
    /// <summary>The largest request either side accepts: a check request and its changed-file list.</summary>
    internal const int MaxRequestBytes = 1 << 20;

    /// <summary>
    ///     The largest reply either side accepts. The hook renders the whole report, unlike a tool call, so this
    ///     bounds the transport rather than fitting the text.
    /// </summary>
    internal const int MaxReplyBytes = 8 << 20;

    private const int LengthBytes = sizeof(int);

    /// <summary>
    ///     Writes <paramref name="message" /> as one frame, or nothing and false when it would exceed
    ///     <paramref name="limit" /> bytes.
    /// </summary>
    public static async Task<bool> TryWriteFrameAsync<T>(
        Stream stream, T message, JsonTypeInfo<T> typeInfo, int limit, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        if (body.Length > limit) return false;

        var length = new byte[LengthBytes];
        BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
        return true;
    }

    /// <summary>Reads one frame and the message in it.</summary>
    /// <exception cref="EndOfStreamException">The stream ended before the frame began.</exception>
    /// <exception cref="InvalidDataException">
    ///     The stream ended inside the frame, the length is negative or over <paramref name="limit" />, or the body
    ///     is not a message of this type.
    /// </exception>
    public static async Task<T> ReadFrameAsync<T>(
        Stream stream, JsonTypeInfo<T> typeInfo, int limit, CancellationToken ct)
    {
        var length = new byte[LengthBytes];
        int read = await stream.ReadAtLeastAsync(length, LengthBytes, throwOnEndOfStream: false, ct);
        if (read == 0) throw new EndOfStreamException("The hand-off connection closed before a frame.");
        if (read < LengthBytes) throw new InvalidDataException("A hand-off frame ended inside its length.");

        int size = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size < 0 || size > limit)
            throw new InvalidDataException($"A hand-off frame declared {size} bytes, outside 0 to {limit}.");

        var body = new byte[size];
        await ReadExactlyAsync(stream, body, ct);
        try
        {
            return JsonSerializer.Deserialize(body, typeInfo)
                   ?? throw new InvalidDataException("A hand-off frame held null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("A hand-off frame did not hold the expected message.", ex);
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, ct);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("A hand-off frame ended early.", ex);
        }
    }
}

/// <summary>The source-generated serializer for the hand-off frames.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(HandoffRequest))]
[JsonSerializable(typeof(HandoffReply))]
internal sealed partial class HandoffJson : JsonSerializerContext;
