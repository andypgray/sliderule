using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every <c>actions/checkout</c> step across the workflows and the action metadata
///     opted out of credential persistence.
/// </summary>
/// <remarks>
///     <para>
///         <b>What goes wrong without it.</b> <c>actions/checkout</c> writes the job's
///         <c>GITHUB_TOKEN</c> into <c>.git/config</c> and leaves it there for the rest of the run, so
///         every later step — including whatever a third-party action executes — can read a credential
///         it was never handed. <c>persist-credentials: false</c> is the opt-out, and it has to be
///         repeated on every checkout because it is a per-step input rather than a workflow setting.
///     </para>
///     <para>
///         <b>Why uniformity needs a gate and not review.</b> One checkout without the input among many
///         that carry it is the shape this class exists for: nothing in the YAML complains, both
///         spellings run green, and the odd one out is invisible until someone reads every checkout side
///         by side. Scorecard reads the pattern across the repository, so the cost of the gap is not
///         confined to the workflow that has it.
///     </para>
///     <para>
///         <b>Moving the pin.</b> A workflow that needs to push — one that commits, tags, or
///         opens a pull request — needs the credential, and this test will red for it. That is the
///         intended path: widen it deliberately as part of that change, naming the step and why it is
///         exempt, rather than dropping the input silently.
///     </para>
/// </remarks>
public sealed class CheckoutCredentialPersistenceTests
{
    [Fact]
    public void CheckoutSteps_AcrossTheWorkflows_DoNotPersistCredentials()
    {
        // Arrange
        IReadOnlyList<CheckoutStep> steps = ReadCheckoutSteps();

        // Act
        List<string> persisting = steps
            .Where(static step => !step.OptsOut)
            .Select(static step => $"  {step.Workflow}:{step.Line}")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        persisting.ShouldReportNothing(
            "actions/checkout step(s) omit 'persist-credentials: false', leaving the job's GITHUB_TOKEN "
            + "in .git/config for every step that follows");
    }

    [Fact]
    public void CheckoutSteps_AreReadable()
    {
        // Act
        IReadOnlyList<CheckoutStep> steps = ReadCheckoutSteps();

        // Assert: the arm above passes over a workflow it cannot read, so the steps are held non-empty
        // rather than trusted. A rewrite into a form this scan cannot follow fails here.
        steps.ShouldNotBeEmpty($"No actions/checkout steps were found under {TrackedFiles.WorkflowDirectory}.");
    }

    // Every use of the action across the tracked workflows and the action metadata, tagged with the file and
    // line it sits on.
    private static IReadOnlyList<CheckoutStep> ReadCheckoutSteps()
    {
        return TrackedFiles.ActionSteps
            .SelectMany(ActionUses.In)
            .Where(static use => use.Reference.StartsWith("actions/checkout@", StringComparison.Ordinal))
            .Select(static use => new CheckoutStep(use.File, use.Line, OptsOutOfPersistedCredentials(use.Body)))
            .ToArray();
    }

    private static bool OptsOutOfPersistedCredentials(string[] body)
    {
        return body.Any(static line =>
            line.Contains("persist-credentials:", StringComparison.Ordinal)
            && line.Contains("false", StringComparison.Ordinal));
    }

    private sealed record CheckoutStep(string Workflow, int Line, bool OptsOut);
}
