using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate's whole-statements extent, over spec source written here so that each change it must
///     catch can be made on purpose. The gate rebuilds a fence from the source statements it quotes and reds when
///     the fence differs from what it rebuilt, printing the rebuilt body to paste, so each fact pins that body.
/// </summary>
public sealed class QuoteSyncWholeStatementsTests
{
    private static readonly string[] Spec =
    [
        "public sealed class ArchitectureSpec : IArchitectureSpec",
        "{",
        "    public void Define(Arch arch)",
        "    {",
        "        arch.Rule(\"layering/domain-independent\")",
        "            .Enforce(domain.MustNotReference(web))",
        "            .Because(\"Domain is the model; it stays free of the web tier.\")",
        "            .Fix(\"Define the abstraction in Domain.\");",
        "",
        "        DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);",
        "    }",
        "}"
    ];

    // The rule as a page quotes it, dedented.
    private static readonly string[] Rule =
    [
        "arch.Rule(\"layering/domain-independent\")",
        "    .Enforce(domain.MustNotReference(web))",
        "    .Because(\"Domain is the model; it stays free of the web tier.\")",
        "    .Fix(\"Define the abstraction in Domain.\");"
    ];

    [Fact]
    public void AStatementQuotedWhole_RebuildsAsTheFence()
    {
        // Act
        (IReadOnlyList<string>? rebuilt, string? unfound) = QuoteSyncTests.StatementsAsWritten(Rule, Spec);

        // Assert
        rebuilt.ShouldBe(Rule);
        unfound.ShouldBeNull();
    }

    [Fact]
    public void ALineTheSourceAddsToAStatement_IsInTheRebuiltBody()
    {
        // Arrange: the spec gains a citation inside the rule, and the fence does not show it.
        string[] spec = [.. Spec[..7], "            .Citation(\"https://example.com/guidance\")", .. Spec[7..]];

        // Act
        (IReadOnlyList<string>? rebuilt, _) = QuoteSyncTests.StatementsAsWritten(Rule, spec);

        // Assert
        rebuilt.ShouldBe([.. Rule[..3], "    .Citation(\"https://example.com/guidance\")", Rule[3]]);
    }

    [Fact]
    public void ALineTheFenceLeavesOut_IsInTheRebuiltBody()
    {
        // Arrange: the fence drops the rule's last line and its semicolon with it, so every line it still shows
        // matches the source.
        string[] fence = Rule[..3];

        // Act
        (IReadOnlyList<string>? rebuilt, _) = QuoteSyncTests.StatementsAsWritten(fence, Spec);

        // Assert
        rebuilt.ShouldBe(Rule);
    }

    [Fact]
    public void EachStatementOfAFence_RebuildsFromItsOwnSourceStatement()
    {
        // Arrange: both statements, at an indentation of the fence's own, with the blank line between them.
        string[] fence =
        [
            .. Rule.Select(static line => "  " + line),
            "",
            "  DotNetGuidance.NoBuildServiceProvider(arch, arch.Types, PackPosture.Enforce);"
        ];

        // Act
        (IReadOnlyList<string>? rebuilt, _) = QuoteSyncTests.StatementsAsWritten(fence, Spec);

        // Assert
        rebuilt.ShouldBe(fence);
    }

    [Fact]
    public void ALineOutsideEveryStatement_IsLeftOutOfTheRebuiltBody()
    {
        // Arrange
        string[] fence = ["// The rule:", .. Rule];

        // Act
        (IReadOnlyList<string>? rebuilt, _) = QuoteSyncTests.StatementsAsWritten(fence, Spec);

        // Assert
        rebuilt.ShouldBe(Rule);
    }

    [Fact]
    public void AnOpeningLineThatBeginsNoSourceStatement_IsNamed()
    {
        // Arrange
        string[] fence = ["arch.Rule(\"layering/web-independent\")", "    .Enforce(web.MustNotReference(domain));"];

        // Act
        (IReadOnlyList<string>? rebuilt, string? unfound) = QuoteSyncTests.StatementsAsWritten(fence, Spec);

        // Assert
        rebuilt.ShouldBeNull();
        unfound.ShouldBe("arch.Rule(\"layering/web-independent\")");
    }
}
