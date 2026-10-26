using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Each <see cref="TestSourceHygiene" /> check over a seeded snippet that must report and a clean one that
///     must not, then each over the suite's own tracked sources.
/// </summary>
public sealed class TestSourceHygieneTests
{
    private const string SnippetPath = "Snippet.cs";

    private const string Fixtures = TestSourceHygiene.Root + "Fixtures/";

    private const string Stubs = TestSourceHygiene.Root + "Stubs/";

    [Theory]
    [InlineData("Read", "internal static string Read(string text) { text.ShouldNotBeEmpty(); return text; }")]
    [InlineData("Capture", "internal static Exception Capture(Action act) { return Should.Throw<Exception>(act); }")]
    [InlineData("Capture", "internal static Exception Capture(Action act) => Shouldly.Should.Throw<Exception>(act);")]
    [InlineData("Check", "internal static void Check(int[] items) { Array.ForEach(items, item => item.ShouldBe(1)); }")]
    [InlineData("Check", "internal static void Check(int item) { Inner(); void Inner() => item.ShouldBe(1); }")]
    [InlineData("Check", "internal static void Check(int item) { item.ShouldHaveTheSuitesOwnShape(); }")]
    [InlineData("Read", "internal static string Read(string? text) { text?.ShouldNotBeEmpty(); return text!; }")]
    [InlineData("AssertRead", "internal static string AssertRead(string text) { text.ShouldBe(\"a\"); return text; }")]
    [InlineData("Check", "internal static string Check { get { 1.ShouldBe(1); return \"\"; } }")]
    [InlineData("Check", "private static readonly Action<int> Check = item => item.ShouldBe(1);")]
    [InlineData("Read", "internal static void Read(int item) { if (item < 0) throw new ShouldAssertException(\"no\"); }")]
    [InlineData("Read", "internal static int Read(int[] items) => items is [var item] ? item : "
                        + "throw new Shouldly.ShouldAssertException(\"no\");")]
    public void AssertingHelpers_MemberThatAssertsUnderAnotherName_IsReported(string name, string member)
    {
        // Arrange
        string source = InClass(member);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.AssertingHelpers.Run(SnippetPath, source);

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldBe($"Snippet.cs:3 {name} — asserts but is not named Should*");
    }

    [Theory]
    [InlineData("internal static string ShouldRead(string text) { text.ShouldNotBeEmpty(); return text; }")]
    [InlineData("internal static void AssertOutcome(int item) { item.ShouldBe(1); }")]
    [InlineData("internal static async Task AssertOutcomeAsync(Task<int> task) { (await task).ShouldBe(1); }")]
    [InlineData("[Fact] public void Holds() { 1.ShouldBe(1); }")]
    [InlineData("[Fact(Skip = \"later\")] public void Holds() { 1.ShouldBe(1); }")]
    [InlineData("[Xunit.Theory] [InlineData(1)] public void Holds(int item) { item.ShouldBe(1); }")]
    [InlineData("[Property(MaxTest = 10)] public void Holds(int item) { item.ShouldBe(item); }")]
    [InlineData("internal static void Gate(bool ready) { Assert.SkipUnless(ready, \"needs a ready host\"); }")]
    [InlineData("internal static int Count(int[] items) { return items.Length; }")]
    [InlineData("internal static string Quote() { return \"value.ShouldBe(1);\"; }")]
    public void AssertingHelpers_ShouldNamedHelperTestOrNonAsserter_IsNotReported(string member)
    {
        // Arrange
        string source = InClass(member);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.AssertingHelpers.Run(SnippetPath, source);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void AssertingHelpers_MemberOnTheAllowList_IsNotReportedInItsOwnFile()
    {
        // Arrange
        string source = InClass("private static int Pinned() { 1.ShouldBe(1); return 1; }");

        // Act
        IReadOnlyList<string> listed = TestSourceHygiene.AssertingHelpers.Run(
            TestSourceHygiene.Root + "Checking/CheckerHarness.cs", source);
        IReadOnlyList<string> elsewhere = TestSourceHygiene.AssertingHelpers.Run(SnippetPath, source);

        // Assert
        listed.ShouldBeEmpty();
        elsewhere.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("if (!OperatingSystem.IsWindows()) return;")]
    [InlineData("if (!OperatingSystem.IsWindows()) { return; }")]
    [InlineData("if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;")]
    [InlineData("if (ready && OperatingSystem.IsMacOS()) return;")]
    [InlineData("if (OperatingSystem.IsWindows()) { } else return;")]
    public void PlatformEarlyReturns_ReturnUnderAPlatformCheck_IsReported(string statement)
    {
        // Arrange
        string source = InClass($$"""
                                  [Fact]
                                  public void Holds()
                                  {
                                      bool ready = true;
                                      {{statement}}
                                      1.ShouldBe(1);
                                  }
                                  """);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.PlatformEarlyReturns.Run(SnippetPath, source);

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldStartWith("Snippet.cs:7 Holds — returns early on an operating-system check");
    }

    [Theory]
    [InlineData("Assert.SkipUnless(OperatingSystem.IsWindows(), \"Windows path shapes.\");")]
    [InlineData("Assert.SkipWhen(OperatingSystem.IsWindows(), \"POSIX path shapes.\");")]
    [InlineData("if (!ready) return;")]
    [InlineData("Func<int> pick = () => { if (OperatingSystem.IsWindows()) return 1; return 2; };")]
    [InlineData("int Pick() { if (OperatingSystem.IsWindows()) return 1; return 2; }")]
    public void PlatformEarlyReturns_SkipOrReturnOutsideATestsOwnFlow_IsNotReported(string statement)
    {
        // Arrange
        string source = InClass($$"""
                                  [Fact]
                                  public void Holds()
                                  {
                                      bool ready = true;
                                      {{statement}}
                                      1.ShouldBe(1);
                                  }
                                  """);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.PlatformEarlyReturns.Run(SnippetPath, source);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void PlatformEarlyReturns_HelperOutsideATest_IsNotReported()
    {
        // Arrange
        string source = InClass("internal static void Prepare() { if (!OperatingSystem.IsWindows()) return; }");

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.PlatformEarlyReturns.Run(SnippetPath, source);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("await RunAsync(CancellationToken.None);", "CancellationToken.None")]
    [InlineData("await RunAsync(System.Threading.CancellationToken.None);", "System.Threading.CancellationToken.None")]
    [InlineData("await RunAsync(default(CancellationToken));", "default(CancellationToken)")]
    [InlineData("await Task.Run(() => RunAsync(CancellationToken.None));", "CancellationToken.None")]
    public void UncancellableTokens_NeverCancellingTokenInATest_IsReported(string statement, string token)
    {
        // Arrange
        string source = InClass($$"""
                                  [Fact]
                                  public async Task Holds()
                                  {
                                      {{statement}}
                                  }
                                  """);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.UncancellableTokens.Run(SnippetPath, source);

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldBe($"Snippet.cs:6 Holds — passes {token}, which never cancels with the run; pass Ct");
    }

    [Theory]
    [InlineData("[Fact] public async Task Holds() { await RunAsync(Ct); }")]
    [InlineData("[Fact] public void Holds() { var text = \"CancellationToken.None\"; }")]
    [InlineData("internal static Task WarmAsync() { return RunAsync(CancellationToken.None); }")]
    public void UncancellableTokens_RunningTokenOrSharedHelper_IsNotReported(string member)
    {
        // Arrange
        string source = InClass(member);

        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.UncancellableTokens.Run(SnippetPath, source);

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Sources_ReachTheSuiteAndLeaveOutFixturesAndStubs()
    {
        // Act
        IReadOnlyList<string> sources = TestSourceHygiene.Sources;

        // Assert: held non-empty rather than trusted, so a green sweep below means scanned and clean.
        sources.ShouldContain(TestSourceHygiene.Root + "DocHygiene/TestSourceHygieneTests.cs");
        sources.ShouldNotContain(static path => path.StartsWith(Fixtures, StringComparison.Ordinal));
        sources.ShouldNotContain(static path => path.StartsWith(Stubs, StringComparison.Ordinal));
    }

    [Fact]
    public void TestSources_AssertingMembersAreNamedShould()
    {
        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.AssertingHelpers.RunOverTree();

        // Assert
        findings.ShouldReportNothing(
            "These members assert but are not named Should*, so a call site reads as an accessor that cannot fail");
    }

    [Fact]
    public void TestSources_NoTestReturnsEarlyOnAPlatformCheck()
    {
        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.PlatformEarlyReturns.RunOverTree();

        // Assert
        findings.ShouldReportNothing("These tests report a pass on an operating system where they ran nothing");
    }

    [Fact]
    public void TestSources_NoTestPassesAnUncancellableToken()
    {
        // Act
        IReadOnlyList<string> findings = TestSourceHygiene.UncancellableTokens.RunOverTree();

        // Assert
        findings.ShouldReportNothing("These tests pass a token that never cancels instead of the running test's Ct");
    }

    [Fact]
    public void AllowLists_EveryEntryStillForgivesAHit()
    {
        // Act
        List<string> dead =
        [
            .. TestSourceHygiene.AssertingHelpers.DeadAllowances(),
            .. TestSourceHygiene.PlatformEarlyReturns.DeadAllowances(),
            .. TestSourceHygiene.UncancellableTokens.DeadAllowances()
        ];

        // Assert: an entry whose member no longer offends forgives nothing and hides the next offence there.
        dead.ShouldReportNothing("These allow-list entries match no finding and should be removed");
    }

    private static string InClass(string member)
    {
        return $$"""
                 internal static class Probe
                 {
                 {{member}}
                 }
                 """;
    }
}
