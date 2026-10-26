using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Hosting;
using SlideRule.Internal;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Shorthands over the one production entry point each of the extractor and the checker has, for rows that
///     build a codebase from compilations or check a model with no host around it. Each but
///     <see cref="CheckUncancellable" /> passes the running test's token, so a hung extraction or check fails its
///     test rather than stalling the run.
/// </summary>
internal static class Harness
{
    /// <summary>
    ///     Extracts the model from compilations built without MSBuild. Where two of <paramref name="inputs" />
    ///     declare the same type, the first supplies its facts.
    /// </summary>
    public static CodebaseModel Extract(IReadOnlyList<CompilationInput> inputs)
    {
        return CodebaseModelBuilder.Build(inputs, null, Ct);
    }

    /// <summary>Checks every rule with no baselines and no diff, so every violation fails and every tripwire skips.</summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase)
    {
        return Check(model.Rules, codebase, BaselineIndex.Empty, null);
    }

    /// <summary>Checks every rule against <paramref name="baselines" /> with no diff, so every tripwire skips.</summary>
    public static CheckReport Check(ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines)
    {
        return Check(model.Rules, codebase, baselines, null);
    }

    /// <summary>Checks exactly <paramref name="rules" />, over a whole universe and a whole model.</summary>
    public static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        return Check(rules, codebase, baselines, diff, null, null);
    }

    /// <summary>
    ///     Checks exactly <paramref name="rules" /> with no cancellation token, for a check whose result a cache
    ///     shares with the tests after the one that started it.
    /// </summary>
    public static CheckReport CheckUncancellable(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff)
    {
        return ArchChecker.Check(rules, codebase, baselines, diff, null, null, null, CancellationToken.None);
    }

    /// <summary>Checks exactly <paramref name="rules" /> with every argument the checker takes but the token.</summary>
    public static CheckReport Check(
        IReadOnlyList<ArchRule> rules, CodebaseModel codebase, BaselineIndex baselines, DiffContext? diff,
        NarrowedUniverse? narrowing, IncompleteModel? incompleteModel, SolutionRoot? root = null)
    {
        return ArchChecker.Check(rules, codebase, baselines, diff, narrowing, incompleteModel, root, Ct);
    }
}
