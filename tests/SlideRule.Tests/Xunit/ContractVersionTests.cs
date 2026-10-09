using Shouldly;
using Xunit;
using SlideRule.Xunit;

namespace SlideRule.Tests.Xunit;

/// <summary>
///     The adapter's guard against a contract of another version. The adapter runs the contract's engine through an
///     internals grant that binds at run time, so a test project resolving the two packages at different versions
///     would otherwise fail on whatever moved between them. The guard compares the two releases, ignoring the commit a
///     build appends, and names both versions and the fix when they differ.
/// </summary>
public sealed class ContractVersionTests
{
    [Fact]
    public void Mismatch_OneReleaseBuiltFromTwoCommits_Agrees()
    {
        ContractVersion.Mismatch("1.2.0+aaaa", "1.2.0+bbbb")
            .ShouldBeNull();
    }

    [Fact]
    public void Mismatch_TwoReleases_NamesBothVersionsAndTheFix()
    {
        string? refusal = ContractVersion.Mismatch("1.2.0+aaaa", "1.1.0");

        refusal.ShouldBe(
            "The SlideRule xUnit adapter checked nothing: it is version 1.2.0, and the test project loaded version "
            + "1.1.0 of SlideRule. That happens when a project references SlideRule and "
            + "SlideRule.Xunit at two different versions, so keep the two packages on one version.");
    }

    [Fact]
    public void Mismatch_AContractCarryingNoVersion_IsRefusedAsUnknown()
    {
        ContractVersion.Mismatch("1.2.0", null)
            .ShouldNotBeNull()
            .ShouldContain("loaded version (unknown) of SlideRule");
    }

    [Fact]
    public void Mismatch_TheTwoAssembliesThisRepositoryBuilds_Agree()
    {
        // Both are built from the one version the root props declares, so the guard is silent on every run here.
        ContractVersion.Mismatch()
            .ShouldBeNull();
    }
}
