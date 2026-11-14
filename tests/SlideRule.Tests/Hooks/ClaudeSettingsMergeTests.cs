using System.CommandLine;
using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The merge behind <c>sliderule install-hook</c>, over strings: what it adds, what it keeps, the arguments it
///     writes into a command, and what it refuses.
/// </summary>
public sealed class ClaudeSettingsMergeTests
{
    private const string FilePath = ".claude/settings.json";

    // One deny rule and one Stop group: enough to show each half of the merge without the whole snippet's text.
    private const string Template = """
                                    {
                                      "permissions": {
                                        "deny": [
                                          "Bash(sliderule baseline*)"
                                        ]
                                      },
                                      "hooks": {
                                        "Stop": [
                                          {
                                            "hooks": [
                                              {
                                                "type": "command",
                                                "command": "sliderule hook",
                                                "timeout": 600
                                              }
                                            ]
                                          }
                                        ]
                                      }
                                    }
                                    """;

    private static readonly Command Tree = CommandTree.Root();

    private static string Snippet => EmbeddedResourceText.Load(InstallHookRunner.SnippetResource);

    [Fact]
    public void Merge_NoFile_WritesTheSnippetAsItStands()
    {
        // Act
        ClaudeSettingsMerge.Result result = Merge(null, Snippet);

        // Assert
        result.Text.ShouldBe(RepoRoot.ReadText("hooks/settings.snippet.json"));
        result.Changed.ShouldBeTrue();
        result.Lines.ShouldBe([
            "added UserPromptSubmit: sliderule hook",
            "added Stop: sliderule hook",
            "added SubagentStop: sliderule hook",
            "added permissions.deny: Bash(sliderule baseline*)",
            "added permissions.deny: Bash(*dnx SlideRule.Cli* baseline*)",
            "added permissions.deny: PowerShell(sliderule baseline*)",
            "added permissions.deny: PowerShell(*dnx SlideRule.Cli* baseline*)"
        ]);
    }

    [Fact]
    public void Merge_UnrelatedSettings_SurviveInTheirOrderBesideTheAddedOnes()
    {
        // Arrange
        const string existing = """
                                {
                                  "model": "opus",
                                  "hooks": {
                                    "PostToolUse": [
                                      {
                                        "matcher": "Edit",
                                        "hooks": [
                                          {
                                            "type": "command",
                                            "command": "lint & format"
                                          }
                                        ]
                                      }
                                    ]
                                  },
                                  "permissions": {
                                    "allow": [
                                      "Bash(ls)"
                                    ]
                                  },
                                  "env": {
                                    "A": "1"
                                  }
                                }
                                """;

        // Act
        ClaudeSettingsMerge.Result result = Merge(existing, Template);

        // Assert
        result.Text.ShouldBe("""
                             {
                               "model": "opus",
                               "hooks": {
                                 "PostToolUse": [
                                   {
                                     "matcher": "Edit",
                                     "hooks": [
                                       {
                                         "type": "command",
                                         "command": "lint & format"
                                       }
                                     ]
                                   }
                                 ],
                                 "Stop": [
                                   {
                                     "hooks": [
                                       {
                                         "type": "command",
                                         "command": "sliderule hook",
                                         "timeout": 600
                                       }
                                     ]
                                   }
                                 ]
                               },
                               "permissions": {
                                 "allow": [
                                   "Bash(ls)"
                                 ],
                                 "deny": [
                                   "Bash(sliderule baseline*)"
                                 ]
                               },
                               "env": {
                                 "A": "1"
                               }
                             }

                             """);
        result.Lines.ShouldBe(["added Stop: sliderule hook", "added permissions.deny: Bash(sliderule baseline*)"]);
    }

    [Fact]
    public void Merge_ASecondRun_ChangesNothing()
    {
        // Arrange
        string first = Merge(null, Snippet)
            .Text;

        // Act
        ClaudeSettingsMerge.Result second = Merge(first, Snippet);

        // Assert
        second.Changed.ShouldBeFalse(string.Join("\n", second.Lines));
        second.Text.ShouldBe(first);
        second.Lines.ShouldBe([
            "kept UserPromptSubmit: sliderule hook",
            "kept Stop: sliderule hook",
            "kept SubagentStop: sliderule hook"
        ]);
    }

    [Fact]
    public void Merge_ARunnerEntryTheUserTuned_IsKeptAndNothingIsAddedBesideIt()
    {
        // The entry carries a solution argument and its own timeout, so neither the command string nor the group
        // equals the template's, and it still answers for the template's group.
        // Arrange
        string existing = Settings("Stop", null, "sliderule hook src/App.sln --max-rounds 5", denied: true);

        // Act
        ClaudeSettingsMerge.Result result = Merge(existing, Template);

        // Assert
        result.Changed.ShouldBeFalse(string.Join("\n", result.Lines));
        result.Text.ShouldBe(existing);
        result.Lines.ShouldBe(["kept Stop: sliderule hook src/App.sln --max-rounds 5"]);
    }

    [Theory]
    // The installer itself does not run the hook.
    [InlineData("sliderule install-hook")]
    [InlineData("sliderule check")]
    // A command line the verb refuses reports a config error at every turn rather than checking anything.
    [InlineData("sliderule hook --bogus")]
    // Help parses to the verb and prints its page in place of a check.
    [InlineData("sliderule hook --help")]
    [InlineData("other-tool hook")]
    public void Merge_AnEntryThatDoesNotRunTheHook_LeavesTheRunnerToBeAdded(string command)
    {
        // Arrange
        string existing = Settings("Stop", null, command, denied: true);

        // Act
        ClaudeSettingsMerge.Result result = Merge(existing, Template);

        // Assert
        result.Lines.ShouldBe(["added Stop: sliderule hook"]);
        StopCommands(result.Text)
            .ShouldBe([command, "sliderule hook"]);
    }

    [Fact]
    public void Merge_ARunnerOnAnotherMatcher_DoesNotStandInForTheTemplatesGroup()
    {
        // Arrange
        string template = Settings("PreToolUse", "Write", "sliderule hook", denied: false);
        string existing = Settings("PreToolUse", "Edit", "sliderule hook", denied: false);

        // Act
        ClaudeSettingsMerge.Result result = Merge(existing, template);

        // Assert
        result.Lines.ShouldBe(["added PreToolUse (Write): sliderule hook"]);
    }

    [Fact]
    public void Merge_ARunnerOnTheSameMatcher_IsKept()
    {
        // Arrange
        string template = Settings("PreToolUse", "Write", "sliderule hook", denied: false);

        // Act
        ClaudeSettingsMerge.Result result = Merge(template, template);

        // Assert
        result.Lines.ShouldBe(["kept PreToolUse (Write): sliderule hook"]);
    }

    [Fact]
    public void Merge_APartialDenyList_GainsExactlyTheMissingRules()
    {
        // Arrange
        const string existing = """
                                {
                                  "permissions": {
                                    "deny": [
                                      "PowerShell(sliderule baseline*)",
                                      "Bash(rm -rf*)",
                                      "Bash(sliderule baseline*)"
                                    ]
                                  }
                                }
                                """;

        // Act
        ClaudeSettingsMerge.Result result = Merge(existing, Snippet);

        // Assert
        using JsonDocument document = JsonDocument.Parse(result.Text);
        document.RootElement.GetProperty("permissions")
            .Strings("deny")
            .ShouldBe([
                "PowerShell(sliderule baseline*)",
                "Bash(rm -rf*)",
                "Bash(sliderule baseline*)",
                "Bash(*dnx SlideRule.Cli* baseline*)",
                "PowerShell(*dnx SlideRule.Cli* baseline*)"
            ]);
    }

    [Theory]
    [InlineData("[]", "$", "an object")]
    [InlineData("null", "$", "an object")]
    [InlineData("""{"hooks": []}""", "$.hooks", "an object")]
    [InlineData("""{"hooks": {"Stop": {}}}""", "$.hooks.Stop", "an array")]
    [InlineData("""{"permissions": null}""", "$.permissions", "an object")]
    [InlineData("""{"permissions": {"deny": "Bash(x)"}}""", "$.permissions.deny", "an array")]
    [InlineData("""{"permissions": {"deny": ["Bash(x)", 1]}}""", "$.permissions.deny", "an array of strings")]
    public void Merge_AShapeItMustWriteInto_IsRefusedByItsPath(string existing, string path, string expected)
    {
        // Act
        var refusal = Should.Throw<UserErrorException>(() => Merge(existing, Template));

        // Assert
        refusal.Message.ShouldBe(
            $"{FilePath}: {path} is not {expected}, so the hook settings cannot be merged into it. Fix it, then run "
            + "this again.");
    }

    [Fact]
    public void Merge_MalformedJson_IsRefusedNamingTheFile()
    {
        // Act
        var refusal = Should.Throw<UserErrorException>(() => Merge("""{"hooks": """, Template));

        // Assert
        refusal.Message.ShouldStartWith($"{FilePath} is not valid JSON: ");
        refusal.Message.ShouldEndWith(" Fix it, then run this again.");
    }

    [Theory]
    [InlineData("src/App.sln", null, "sliderule hook src/App.sln")]
    [InlineData(null, "arch/Spec/Spec.csproj", "sliderule hook --spec arch/Spec/Spec.csproj")]
    [InlineData("src/App.sln", "arch/Spec.dll", "sliderule hook src/App.sln --spec arch/Spec.dll")]
    // The subcommand's name, which the parser would read as the subcommand.
    [InlineData("install", null, "sliderule hook ./install")]
    [InlineData("src/App.sln", "install", "sliderule hook src/App.sln --spec ./install")]
    // A leading dash, which the parser would read as an option.
    [InlineData("-name", null, "sliderule hook ./-name")]
    [InlineData("My App/App.sln", null, "sliderule hook \"My App/App.sln\"")]
    [InlineData("src/App(1).sln", null, "sliderule hook \"src/App(1).sln\"")]
    public void Merge_TheSolutionAndSpec_AreAppendedInAFormThatRunsTheHook(
        string? solution, string? spec, string expected)
    {
        // Act
        ClaudeSettingsMerge.Result result = ClaudeSettingsMerge.Merge(null, Template, solution, spec, Tree, FilePath);

        // Assert
        StopCommands(result.Text)
            .ShouldBe([expected]);
        ClaudeSettingsMerge.RunsHookRunner(Tree, expected)
            .ShouldBeTrue($"'{expected}' does not parse to the hook verb");
    }

    [Theory]
    [InlineData("a$b/App.sln")]
    [InlineData("a\"b/App.sln")]
    [InlineData("a`b/App.sln")]
    // A line break ends the command a shell runs.
    [InlineData("a\nb/App.sln")]
    [InlineData("a\rb/App.sln")]
    public void Merge_APathAShellReadsInsideQuotes_IsRefusedNamingIt(string solution)
    {
        // Act
        var refusal = Should.Throw<UserErrorException>(() =>
            ClaudeSettingsMerge.Merge(null, Template, solution, null, Tree, FilePath));

        // Assert
        refusal.Message.ShouldStartWith($"Cannot write '{solution}' into a hook command: ");
    }

    [Theory]
    [InlineData("sliderule hook", true)]
    [InlineData("sliderule hook ./install --max-rounds 5", true)]
    [InlineData("sliderule hook install", true)]
    [InlineData("sliderule install-hook", false)]
    [InlineData("sliderule install-hook App.sln", false)]
    [InlineData("sliderule hook -h", false)]
    [InlineData("sliderule check", false)]
    [InlineData("dotnet sliderule hook", false)]
    public void RunsHookRunner_ReadsTheCommandLineThroughTheCommandTree(string command, bool expected)
    {
        // Act
        bool runs = ClaudeSettingsMerge.RunsHookRunner(Tree, command);

        // Assert
        runs.ShouldBe(expected, command);
    }

    private static ClaudeSettingsMerge.Result Merge(string? existing, string template)
    {
        return ClaudeSettingsMerge.Merge(existing, template, null, null, Tree, FilePath);
    }

    // A settings file holding one group on eventName, with matcher when given, running command, and the template's
    // deny rule when denied is set, in the form the merge writes.
    private static string Settings(string eventName, string? matcher, string command, bool denied)
    {
        string[] deny = denied
            ? ["  \"permissions\": {", "    \"deny\": [", "      \"Bash(sliderule baseline*)\"", "    ]", "  },"]
            : [];
        string[] matcherLine = matcher is null ? [] : [$"        \"matcher\": \"{matcher}\","];
        string[] lines =
        [
            "{",
            .. deny,
            "  \"hooks\": {",
            $"    \"{eventName}\": [",
            "      {",
            .. matcherLine,
            "        \"hooks\": [",
            "          {",
            "            \"type\": \"command\",",
            $"            \"command\": \"{command}\",",
            "            \"timeout\": 900",
            "          }",
            "        ]",
            "      }",
            "    ]",
            "  }",
            "}",
            ""
        ];

        return string.Join("\n", lines);
    }

    private static IReadOnlyList<string> StopCommands(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("hooks")
            .GetProperty("Stop")
            .EnumerateArray()
            .SelectMany(static group => group.GetProperty("hooks")
                .EnumerateArray())
            .Select(static hook => hook.GetProperty("command")
                .GetString() ?? "")
            .ToList();
    }
}
