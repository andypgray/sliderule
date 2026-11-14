using System.Buffers.Binary;
using System.Text;
using Shouldly;
using SlideRule.Cli.Hooks.Handoff;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The hand-off's frames: a request and a reply survive the trip whole, and a frame that is too large, cut short
///     or not the expected message is refused as malformed, apart from a connection that closed before any frame.
/// </summary>
public sealed class HandoffProtocolTests
{
    [Fact]
    public async Task Request_RoundTripsEveryFieldTheServerReads()
    {
        // Arrange
        CheckRequest check = CheckRequests.For("C:/repo/App.sln", "C:/repo/arch/Spec.csproj", "C:/repo") with
        {
            ChangedFiles = ["C:/repo/src/A.cs", "C:/repo/src/B.cs"]
        };
        var identity = new HandoffIdentity("c:/repo/app.sln", "c:/repo/arch/spec.csproj", 1, "build");
        var sent = new HandoffRequest(identity, new Dictionary<string, string> { ["env:CI"] = "d1" }, "n1", check, 150);
        using var stream = new MemoryStream();

        // Act
        bool written = await HandoffProtocol.TryWriteFrameAsync(
            stream, sent, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, Ct);
        stream.Position = 0;
        HandoffRequest read = await HandoffProtocol.ReadFrameAsync(
            stream, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, Ct);

        // Assert: the record's own equality compares the file list by reference, so it is compared apart.
        written.ShouldBeTrue();
        (read with { Check = read.Check with { ChangedFiles = null }, Configuration = sent.Configuration })
            .ShouldBe(sent with { Check = check with { ChangedFiles = null } });
        read.Configuration.ShouldBe(sent.Configuration);
        read.Check.ChangedFiles.ShouldBe(check.ChangedFiles);
    }

    [Fact]
    public async Task Write_OverTheLimit_WritesNothingAndSaysSo()
    {
        // Arrange
        HandoffReply reply = new("n1", null, 0, new string('x', 64), "");
        using var stream = new MemoryStream();

        // Act
        bool written = await HandoffProtocol.TryWriteFrameAsync(stream, reply, HandoffJson.Default.HandoffReply, 32, Ct);

        // Assert
        written.ShouldBeFalse();
        stream.Length.ShouldBe(0);
    }

    [Theory]
    [InlineData("a length over the limit")]
    [InlineData("a negative length")]
    [InlineData("a body cut short")]
    [InlineData("a length cut short")]
    [InlineData("a body that is not the message")]
    public async Task Read_AMalformedFrame_IsRefusedAsMalformed(string shape)
    {
        // Arrange
        using var stream = new MemoryStream(FrameOf(shape));

        // Act
        Exception refusal = await Should.ThrowAsync<InvalidDataException>(() => HandoffProtocol.ReadFrameAsync(
            stream, HandoffJson.Default.HandoffReply, 16, Ct));

        // Assert
        refusal.Message.ShouldStartWith("A hand-off frame", customMessage: shape);
    }

    [Fact]
    public async Task Read_AConnectionClosedBeforeAnyFrame_IsAnEndOfStream()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act and assert: an end of stream, which the hook reads as a disconnect rather than a malformed reply.
        await Should.ThrowAsync<EndOfStreamException>(() => HandoffProtocol.ReadFrameAsync(
            stream, HandoffJson.Default.HandoffReply, 16, Ct));
    }

    private static byte[] FrameOf(string shape)
    {
        return shape switch
        {
            "a length over the limit" => Length(17),
            "a negative length" => Length(-1),
            "a body cut short" => [.. Length(10), .. "{}"u8],
            "a length cut short" => [1, 0],
            _ => [.. Length(5), .. Encoding.UTF8.GetBytes("[1,2]")]
        };
    }

    private static byte[] Length(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
