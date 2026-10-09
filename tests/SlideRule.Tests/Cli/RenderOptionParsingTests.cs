using System.CommandLine;
using Shouldly;
using Xunit;
using SlideRule.Cli;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <c>render --model</c> at the command line: the flag parses, its absence is false, and its help text
///     is the pinned one.
/// </summary>
/// <remarks>
///     The parse alone, off the real command tree — what the flag then does is the subject of
///     <see cref="RenderCommandE2ETests" />, which drives the same tree end to end and is where the flag's
///     arrival in the request is actually observable. The help text is pinned here because it is the only
///     sentence most callers ever read about the file, and an option nobody can find is an option nobody
///     turns on.
/// </remarks>
public sealed class RenderOptionParsingTests
{
    private const string ModelHelp =
        "Write sliderule.model.json, the built model as data, beside the root AGENTS.md. Needed once: "
        + "any later render refreshes a model file that exists.";

    [Fact]
    public void Parse_RenderWithModel_ReadsTheFlagAsTrue()
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        Option<bool> model = ModelOption(root);

        ParseResult parsed = root.Parse(["render", "MyApp.sln", "--model"]);

        parsed.Errors.ShouldBeEmpty();
        parsed.GetValue(model)
            .ShouldBeTrue();
    }

    [Fact]
    public void Parse_RenderWithoutModel_ReadsTheFlagAsFalse()
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        Option<bool> model = ModelOption(root);

        ParseResult parsed = root.Parse(["render", "MyApp.sln"]);

        parsed.Errors.ShouldBeEmpty();
        parsed.GetValue(model)
            .ShouldBeFalse();
    }

    [Fact]
    public void RenderCommand_ModelOption_CarriesItsPinnedHelp()
    {
        ModelOption(CommandFactory.BuildRootCommand())
            .Description.ShouldBe(ModelHelp);
    }

    // The option off the same tree instance the parse runs against: a parse result answers about the
    // options the command it parsed actually holds, so a second tree's option would read as absent.
    private static Option<bool> ModelOption(RootCommand root)
    {
        Command render = root.Subcommands.First(command => command.Name == "render");
        return (Option<bool>)render.Options.First(option => option.Name == "--model");
    }
}
