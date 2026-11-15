using System.Globalization;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.Ci;

/// <summary>
///     The runtime-11 CI leg's guard: the leg names the runtime major its test host must run on, and this fails when
///     the roll-forward that puts it there stopped applying, so the leg cannot pass on .NET 10 unnoticed.
/// </summary>
public sealed class TestHostRuntimeTests
{
    /// <summary>The runtime major the test host must run on. Unset (the default) skips the guard.</summary>
    private const string RuntimeMajorVariable = "SLIDERULE_TESTS_RUNTIME_MAJOR";

    [Fact]
    public void TestHost_RunsOnTheRuntimeTheLegNames()
    {
        string? named = Environment.GetEnvironmentVariable(RuntimeMajorVariable);
        if (string.IsNullOrWhiteSpace(named))
            Assert.Skip($"Only a CI leg that names a runtime sets {RuntimeMajorVariable}.");

        int expected = int.Parse(named, NumberStyles.None, CultureInfo.InvariantCulture);

        Environment.Version.Major.ShouldBe(expected, $"the test host runs on .NET {Environment.Version}");
    }
}
