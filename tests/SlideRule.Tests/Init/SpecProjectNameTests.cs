using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Roslyn;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary><see cref="SpecProjectName" />: the spec project's name and the namespace it spells.</summary>
public sealed class SpecProjectNameTests
{
    [Theory]
    [InlineData("C:/repo/MyApp.sln", null, "MyApp.Architecture", "MyApp.Architecture")]
    [InlineData("C:/repo/Acme.Billing.slnx", null, "Acme.Billing.Architecture", "Acme.Billing.Architecture")]
    [InlineData("C:/repo/my-app.sln", null, "my-app.Architecture", "my_app.Architecture")]
    [InlineData("C:/repo/2026.Rewrite.sln", null, "2026.Rewrite.Architecture", "_2026.Rewrite.Architecture")]
    [InlineData("C:/repo/Acme.Event.sln", null, "Acme.Event.Architecture", "Acme.Event.Architecture")]
    [InlineData("C:/repo/Acme.class.sln", null, "Acme.class.Architecture", "Acme._class.Architecture")]
    [InlineData("C:/repo/MyApp.sln", " Specs.Rules ", "Specs.Rules", "Specs.Rules")]
    public void For_NamesTheProjectAndSpellsAValidNamespace(
        string solution, string? name, string project, string @namespace)
    {
        SpecProjectName spelled = SpecProjectName.For(solution, name);

        spelled.ShouldBe(new SpecProjectName(project, @namespace));
    }

    [Theory]
    [InlineData("C:/repo/Acme.Arch.sln", null)]
    [InlineData("C:/repo/Arch.sln", null)]
    [InlineData("C:/repo/MyApp.sln", "MyApp.Arch")]
    public void For_ANamespaceSegmentArch_RefusesNamingCs0118(string solution, string? name)
    {
        var refusal = Should.Throw<UserErrorException>(() => SpecProjectName.For(solution, name));

        refusal.Message.ShouldContain("which shadows the Arch type Define takes (CS0118)");
    }

    [Theory]
    [InlineData("My/App")]
    [InlineData("...")]
    public void For_ANameThatCannotBeAFolder_Refuses(string name)
    {
        var refusal = Should.Throw<UserErrorException>(() => SpecProjectName.For("C:/repo/App.sln", name));

        refusal.Message.ShouldBe(
            $"'{name}' cannot name the spec project's folder and file. Pass --name with a project name.");
    }
}
