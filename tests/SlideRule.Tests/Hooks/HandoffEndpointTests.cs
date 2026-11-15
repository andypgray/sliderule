using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Shouldly;
using SlideRule.Cli.Hooks.Handoff;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The hand-off endpoint: one owner per name, an owner a client can see without connecting, and an endpoint
///     only the current user can reach.
/// </summary>
public sealed class HandoffEndpointTests
{
    [Fact]
    public void Ownership_IsExclusive_AndVisibleUntilGivenUp()
    {
        // Arrange
        string name = "sliderule-test-" + Guid.NewGuid().ToString("N")[..12];

        // Act
        bool before = HandoffEndpoint.OwnerPresent(name);
        EndpointOwnership? first = HandoffEndpoint.TryOwn(name);
        bool held;
        EndpointOwnership? second;
        using (first)
        {
            using NamedPipeServerStream _ = HandoffEndpoint.CreateServerStream(first!);
            held = HandoffEndpoint.OwnerPresent(name);
            second = HandoffEndpoint.TryOwn(name);
        }

        bool after = HandoffEndpoint.OwnerPresent(name);

        // Assert
        (before, first is not null, held, second is null, after).ShouldBe((false, true, true, true, false));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ServerStream_GrantsOnlyTheCurrentUser()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A pipe's access rules are a Windows security descriptor.");

        // Arrange
        string name = "sliderule-test-" + Guid.NewGuid().ToString("N")[..12];
        using EndpointOwnership ownership = HandoffEndpoint.TryOwn(name)!;

        // Act
        using NamedPipeServerStream stream = HandoffEndpoint.CreateServerStream(ownership);
        List<string> granted = stream.GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<AccessRule>()
            .Where(static rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(static rule => rule.IdentityReference.Value)
            .Distinct()
            .ToList();

        // Assert: CurrentUserOnly grants the token's owner, which is the user's own SID except under an elevated
        // token, whose owner is the Administrators group.
        granted.ShouldBe([WindowsIdentity.GetCurrent().Owner!.Value]);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void PrivateDirectory_WithAnyModeButTheOwnersOnly_IsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The endpoint directory exists only off Windows.");

        // Arrange
        string directory = HandoffEndpoint.PrivateDirectory(create: true)!;
        UnixFileMode original = File.GetUnixFileMode(directory);

        // Act
        string? refused;
        try
        {
            File.SetUnixFileMode(directory, original | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            refused = HandoffEndpoint.PrivateDirectory(create: false);
        }
        finally
        {
            File.SetUnixFileMode(directory, original);
        }

        // Assert
        (original, refused).ShouldBe(
            (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, (string?)null));
    }
}
