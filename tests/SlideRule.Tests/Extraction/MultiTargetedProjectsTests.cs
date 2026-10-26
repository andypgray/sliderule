using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     <see cref="MultiTargetedProjects.Of" /> over the MSBuild-free fast path: which projects a merged model
///     says arrived as several compilations, their frameworks, and the one whose facts the types they share
///     ended up carrying.
/// </summary>
/// <remarks>
///     It is pinned here rather than at a composer because here is where the logic lives. The slot it fills
///     is documented as the machine-readable half of the merge notes — both from one merge, on one read — so a
///     composer takes it from here rather than projecting its own, which can drift from the notes.
/// </remarks>
public sealed class MultiTargetedProjectsTests
{
    [Fact]
    public void Of_AProjectThatCompiledTwice_NamesItsFrameworksAndTheWinner()
    {
        // Arrange — one project name, two compilations, both declaring the same type: the collapse the slot
        // exists to state, and the one case the merge also raises a note for.
        CompilationInput modern = CompilationFactory.SharedWidgetFramework("net10.0");
        CompilationInput legacy = CompilationFactory.SharedWidgetFramework("netstandard2.0");
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        // Act
        IReadOnlyList<MultiTargetedProject> projects = MultiTargetedProjects.Of(model);

        // Assert
        MultiTargetedProject shared = projects.ShouldHaveSingleItem();
        shared.Project.ShouldBe("Shared");
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBe("net10.0");
    }

    [Fact]
    public void Of_AProjectWhoseFrameworksShareNoType_NamesThemAndNoWinner()
    {
        // Arrange — two frameworks of one project declaring disjoint types, the shape a #if-guarded class
        // makes. Nothing collapsed, so nothing was displaced and naming a winner would be false about both
        // of them. This is the entry that is wider than the notes: the merge raises none here, and the
        // project still compiled more than once.
        CompilationInput legacy = CompilationFactory.Compile("Shared", ("Legacy.cs", """
                                                                                     namespace Shared { public class LegacyOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "netstandard2.0"
        };
        CompilationInput modern = CompilationFactory.Compile("Shared", ("Modern.cs", """
                                                                                     namespace Shared { public class ModernOnly {} }
                                                                                     """)) with
        {
            TargetFramework = "net10.0"
        };
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern]);

        // Act
        IReadOnlyList<MultiTargetedProject> projects = MultiTargetedProjects.Of(model);

        // Assert
        MultiTargetedProject shared = projects.ShouldHaveSingleItem();
        shared.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        shared.FactsFollow.ShouldBeNull();
    }

    [Fact]
    public void Of_ASingleTargetedProject_SaysNothingAtAll()
    {
        // The case every project of most solutions is in, and the one the omit-when-empty rendering rests
        // on: a quiet solution's documents leave the slot out.
        CodebaseModel model = CompilationFactory.Extract("Plain", ("Plain.cs", """
                                                                               namespace Plain { public class Thing {} }
                                                                               """));

        MultiTargetedProjects.Of(model)
            .ShouldBeEmpty();
    }
}
