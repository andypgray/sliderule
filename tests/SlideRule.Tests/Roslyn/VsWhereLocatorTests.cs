using Shouldly;
using SlideRule.Roslyn.MsBuild;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Covers <see cref="VsWhereLocator" />'s reading of a <c>vswhere</c> run, over captured output rather
///     than a live probe: the instances it lists, and the reason it gives when it lists none.
/// </summary>
public sealed class VsWhereLocatorTests
{
    [Fact]
    public void Interpret_RealVswhereOutput_ExtractsKeyFields()
    {
        // Sampled from `vswhere -all -prerelease -format json`. Trimmed to two entries covering both
        // the stable VS 2022 and a higher-version preview that the BuildHost would otherwise pick.
        const string json = """
                            [
                              {
                                "instanceId": "2d7df52c",
                                "installationPath": "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community",
                                "installationVersion": "17.14.36915.13",
                                "displayName": "Visual Studio Community 2022",
                                "isPrerelease": false
                              },
                              {
                                "instanceId": "e6562aad",
                                "installationPath": "C:\\Program Files\\Microsoft Visual Studio\\18\\Community",
                                "installationVersion": "18.4.11605.240",
                                "displayName": "Visual Studio Community 18"
                              }
                            ]
                            """;

        (IReadOnlyList<VsInstance> instances, string? whyNone) = VsWhereLocator.Interpret(0, json, string.Empty);

        whyNone.ShouldBeNull();
        instances.Count.ShouldBe(2);
        instances[0]
            .InstallationPath.ShouldBe(@"C:\Program Files\Microsoft Visual Studio\2022\Community");
        instances[0]
            .Version.ShouldBe(new Version(17, 14, 36915, 13));
        instances[0]
            .Name.ShouldBe("Visual Studio Community 2022");
        instances[1]
            .Version.ShouldBe(new Version(18, 4, 11605, 240));
    }

    [Theory]
    [InlineData("[]", "vswhere listed no instances")]
    [InlineData("", "vswhere printed nothing")]
    [InlineData("  \r\n", "vswhere printed nothing")]
    [InlineData("{}", "vswhere's output is not a JSON array")]
    [InlineData("Error 0x57: invalid argument\r\nUsage: vswhere", "vswhere's output is not JSON: Error 0x57: invalid argument")]
    [InlineData("""[{ "installationPath": "C:\\VS\\Bad", "installationVersion": "not-a-version" }]""",
        "vswhere listed 1 instance, none with a readable installationPath and installationVersion")]
    [InlineData("""[{ "installationVersion": "17.0.0" }, { "installationPath": "C:\\VS\\NoVersion" }]""",
        "vswhere listed 2 instances, none with a readable installationPath and installationVersion")]
    public void Interpret_NoUsableInstance_SaysWhy(string standardOutput, string expected)
    {
        // Read as plain objects: the theory asks only that nothing was listed, so it names no instance type.
        (IReadOnlyCollection<object> instances, string? whyNone) = VsWhereLocator.Interpret(0, standardOutput, string.Empty);

        instances.ShouldBeEmpty();
        whyNone.ShouldBe(expected);
    }

    [Theory]
    [InlineData(87, "", "vswhere exited with code 87")]
    [InlineData(1, "\r\n  Error 0x80070005: access denied  \r\nmore detail", "vswhere exited with code 1: Error 0x80070005: access denied")]
    public void Interpret_NonZeroExit_FailsWhateverItPrinted(int exitCode, string standardError, string expected)
    {
        // A failed run is not trusted for a partial listing, even one that parses.
        const string listing = """[{ "installationPath": "C:\\VS\\Good", "installationVersion": "17.14.0" }]""";

        // Read as plain objects: the theory asks only that nothing was listed, so it names no instance type.
        (IReadOnlyCollection<object> instances, string? whyNone) = VsWhereLocator.Interpret(exitCode, listing, standardError);

        instances.ShouldBeEmpty();
        whyNone.ShouldBe(expected);
    }

    [Fact]
    public void Interpret_EntryMissingPath_IsSkipped()
    {
        const string json = """
                            [
                              { "installationVersion": "17.0.0" },
                              {
                                "installationPath": "C:\\VS\\Good",
                                "installationVersion": "17.14.0"
                              }
                            ]
                            """;

        (IReadOnlyList<VsInstance> instances, _) = VsWhereLocator.Interpret(0, json, string.Empty);

        instances.ShouldHaveSingleItem()
            .InstallationPath.ShouldBe(@"C:\VS\Good");
    }

    [Fact]
    public void Interpret_MissingDisplayName_DefaultsToVisualStudio()
    {
        const string json = """
                            [
                              {
                                "installationPath": "C:\\VS\\NoName",
                                "installationVersion": "17.0.0"
                              }
                            ]
                            """;

        (IReadOnlyList<VsInstance> instances, _) = VsWhereLocator.Interpret(0, json, string.Empty);

        instances[0]
            .Name.ShouldBe("Visual Studio");
    }
}
