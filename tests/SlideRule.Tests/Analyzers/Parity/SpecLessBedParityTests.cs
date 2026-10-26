using Shouldly;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The parity pin over the beds that carry no spec project, each crossed with the hazard it was built
///     for: one source file compiled into two projects, one project compiled for two frameworks, a
///     generator that writes types into the project being built, and a type name a project and one of its
///     references both declare.
/// </summary>
/// <remarks>
///     <para>
///         The linked bed carries a layer <c>arch.Project</c> defines in each of the four positions a
///         layer can stand in, because those are the axes the project noun's row turns on: a layer that
///         bans is decided in the build, as is one that spares at one edge end, and only one that spares
///         by plain membership is left to the check. Three of the rows therefore run through
///         <see cref="ParityAssertions.ShouldHoldOnBothLegsAsync" />, and the fourth — the exclusion —
///         through <c>ForcedKeys</c>, which is how a row the classifier holds out is put in front of the
///         pin without flipping it.
///     </para>
///     <para>
///         Inline specs rather than spec projects, because the point of these rows is to put the hazards on
///         real MSBuild facts — a linked <c>Compile Include</c>, a <c>TargetFrameworks</c> list, a Razor
///         generator, a shadowed package type — rather than on compilations a test built to have them.
///     </para>
///     <para>
///         One spec per fact. A bed's hazards are separate claims and a spec carrying two of them would
///         have the first failure hide the second.
///     </para>
///     <para>
///         Every row that runs through <see cref="ParityAssertions.ShouldHoldOnBothLegsAsync" /> holds
///         both drivers: what each project reported asked whole, and then the union over its documents
///         asked one at a time, each key in the document that holds its site. The multi-framework bed
///         carries one row more — that the tier a project declares moves which action does the work and
///         nothing else — because it is the one bed whose csproj yields two compilations.
///     </para>
/// </remarks>
public sealed class SpecLessBedParityTests
{
    private const string LinkedSolution = "LinkedApp/LinkedApp.slnx";

    private const string MultiTfmSolution = "MultiTfm/MultiTfm.sln";

    private const string RazorSolution = "RazorApp/RazorApp.slnx";

    private const string ShadowedSolution = "ShadowedApp/ShadowedApp.slnx";

    private const string ResidencyRuleId = "linked/shared-lives-in-core";

    private const string ExclusionRuleId = "linked/tool-reaches-only-core-owned-shared-types";

    /// <summary>
    ///     The linked type against the residency verb. The whole-solution check answers that
    ///     <c>LinkedApp.Shared.Widget</c> resides in <c>LinkedApp.Core</c>, because residency is true of
    ///     every project that declares a type and two of them do. A build of <c>LinkedApp.Tool</c> sees one
    ///     declarer and would answer no, so the classifier holds the verb out and the build stays silent.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_CrossingTheLinkedTypeWithResidency_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await ResidencyAsync("LinkedApp/residency");

        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     What the residency verb would have reported had the classifier been asked to let it through,
    ///     which is why its row is out.
    /// </summary>
    /// <remarks>
    ///     The row is not flipped. The build stays silent about the rule, which is asserted first, and the
    ///     comparison runs through the one-project pipeline directly.
    /// </remarks>
    [Fact]
    public async Task TheOneProjectPipeline_CrossingTheLinkedTypeWithResidency_DisagreesWithTheWholeSolutionCheck()
    {
        // Arrange
        const string ruleId = ResidencyRuleId;
        ParitySubject subject = await ResidencyAsync("LinkedApp/residency forced");

        // Act
        IReadOnlyList<string> forced = subject.ForcedKeys(ruleId, Ct)
            .Describe();
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        solution.ShouldBeEmpty("the whole-solution check finds the shared type declared by Core");
        forced.ShouldBe(
            ["linked/shared-lives-in-core [Shape] T:LinkedApp.Shared.Widget at " + WidgetSource + ":5"],
            "what a forced-in residency verb reports from LinkedApp.Tool's view, which is why the row is out");
    }

    /// <summary>
    ///     The same linked type against a ban whose exclusion is headed by a project noun, which reads the
    ///     declarer list the same way residency does — so the project noun is out in target position and the
    ///     build stays silent here too.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_CrossingTheLinkedTypeWithAProjectHeadedExclusion_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await ExclusionAsync("LinkedApp/exclusion");

        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     What the same ban would have reported with its exclusion's project noun let through: the
    ///     exemption the whole-solution check grants is one a single compilation cannot see, so the build
    ///     would call a blessed reference a violation.
    /// </summary>
    /// <remarks>
    ///     The row is not flipped. The build stays silent about the rule, which is asserted first, and the
    ///     comparison runs through the one-project pipeline directly.
    /// </remarks>
    [Fact]
    public async Task TheOneProjectPipeline_CrossingTheLinkedTypeWithAProjectHeadedExclusion_DisagreesWithTheWholeSolutionCheck()
    {
        // Arrange
        const string ruleId = ExclusionRuleId;
        ParitySubject subject = await ExclusionAsync("LinkedApp/exclusion forced");

        // Act
        IReadOnlyList<string> forced = subject.ForcedKeys(ruleId, Ct)
            .Describe();
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        solution.ShouldBeEmpty("the whole-solution check excludes the widget, which Core also declares");
        forced.ShouldBe(
            [
                "linked/tool-reaches-only-core-owned-shared-types [Reference] T:LinkedApp.Tool.Command -> "
                + "T:LinkedApp.Shared.Widget at " + CommandSource + ":7"
            ],
            "what a forced-in project-headed exclusion reports from one project's view, which is why the "
            + "row is out");
    }

    /// <summary>
    ///     The coverage verb over the same linked type, with its row forced in for this fact alone: the
    ///     classifier holds <c>MustBelongTo</c> out until a one-project verdict has been compared with the
    ///     whole solution's, and this is that comparison.
    /// </summary>
    /// <remarks>
    ///     The row is not flipped. The build stays silent about the rule, which is asserted first, and the
    ///     comparison runs through the one-project pipeline directly.
    /// </remarks>
    [Fact]
    public async Task TheOneProjectPipeline_CrossingTheLinkedTypeWithCoverage_DisagreesWithTheWholeSolutionCheck()
    {
        // Arrange
        const string ruleId = "linked/shared-belongs-to-core";
        ParitySubject subject = await LinkedAsync(
            "LinkedApp/coverage",
            arch => arch.Rule(ruleId)
                .Enforce(arch.Namespace("LinkedApp.Shared.*").MustBelongTo(arch.Project("LinkedApp.Core")))
                .Because("Every shared type is one of Core's, whoever else compiles it."));

        // Act
        IReadOnlyList<string> forced = subject.ForcedKeys(ruleId, Ct)
            .Describe();
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        await subject.ShouldHoldOnBothLegsAsync(Ct);
        solution.ShouldBeEmpty("the whole-solution check finds the shared type in Core");
        forced.ShouldBe(
            ["linked/shared-belongs-to-core [Shape] T:LinkedApp.Shared.Widget at " + WidgetSource + ":5"],
            "what a forced-in coverage verb reports from one project's view, which is why the row is out");
    }

    /// <summary>
    ///     A layer <c>arch.Project</c> defines, as the subject of an outbound ban. The subject is the
    ///     tool's compiled types, which the linked <c>Compile Include</c> makes two rather than
    ///     one: <c>LinkedApp.Shared.Widget</c> is declared by the tool as much as by Core, and a build of
    ///     the tool sees exactly that. A ban only ever misses types, so both views answer the same thing.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAProjectDefinedLayerBanningFromTheSubject_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await LinkedAsync(
            "LinkedApp/layer as a banning subject",
            arch => arch.Rule("linked/tool-compiled-types-hold-nothing-shared")
                .Enforce(arch.Layer("Tool", arch.Project("LinkedApp.Tool"))
                    .MustNotReference(arch.Namespace("LinkedApp.Shared.*")))
                .Because("Whatever the tool compiles is the tool's own, and a shared type reached from "
                         + "inside it is a copy nobody else can see."));

        // The difference guard: legs that agree because nothing was ever selected prove nothing, so the
        // whole-solution check is held to the crossing the linked file makes before the two are compared.
        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "linked/tool-compiled-types-hold-nothing-shared [Reference] T:LinkedApp.Tool.Command -> "
                    + "T:LinkedApp.Shared.Widget at " + CommandSource + ":7"
                ],
                "the tool's own reference to the shared type it compiled in for itself");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The same layer as a ban's operand, which is the position a missed declarer can only cost a
    ///     violation in. The operand is the tool's own compiled surface, which the linked
    ///     <c>Compile Include</c> makes hold the shared widget as well as the tool's own command — so the
    ///     crossing exists only because two projects compile one file, and both views find it.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAProjectDefinedLayerAsABansOperand_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await LinkedAsync(
            "LinkedApp/layer as a ban operand",
            arch => arch.Rule("linked/tool-code-holds-no-compiled-in-copy")
                .Enforce(arch.Namespace("LinkedApp.Tool.*")
                    .MustNotReference(arch.Layer("Tool", arch.Project("LinkedApp.Tool"))
                        .InNamespace("LinkedApp.Shared.*")))
                .Because("A shared type this project compiled in for itself is a copy of somebody else's, "
                         + "and the tool's own code is to reach the original."));

        // The difference guard: legs that agree because nothing was ever selected prove nothing, so the
        // whole-solution check is held to the crossing the linked file makes before the two are compared.
        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "linked/tool-code-holds-no-compiled-in-copy [Reference] T:LinkedApp.Tool.Command -> "
                    + "T:LinkedApp.Shared.Widget at " + CommandSource + ":7"
                ],
                "the widget is in the operand because the tool declares it, which only the link makes true");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The same layer as an allow-list operand, which exempts rather than bans and is decided in the
    ///     build all the same: a project-headed allow entry is asked of one edge instance at a time, at
    ///     the project that instance's own compilation reached (GRAMMAR §4.1). The whole-solution check
    ///     refuses the tool's reference for exactly the reason one project's build does — the instance
    ///     reached the copy the tool compiled, which Core does not own — and neither side has anything the
    ///     other has not.
    /// </summary>
    /// <remarks>
    ///     What a linked file can show here is one half of the claim, the copy reached being the reader's
    ///     own; the other half, where an edge reaches a copy a third project declares and the law
    ///     names either declarer in turn, is in <c>SiblingCorpusParityTests</c>, no bed having a shape
    ///     that separates the two.
    /// </remarks>
    [Fact]
    public async Task TheBuildTier_OverAProjectDefinedLayerAsAnAllowListOperand_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await LinkedAsync(
            "LinkedApp/layer as an allow-list operand",
            arch => arch.Rule("linked/tool-reaches-only-core")
                .Enforce(arch.Namespace("LinkedApp.Tool.*")
                    .MustOnlyReference(arch.Layer("Core", arch.Project("LinkedApp.Core"))))
                .Because("Core owns everything the tool is allowed to reach."));

        // The difference guard: legs that agree because nothing was ever selected prove nothing, so the
        // whole-solution check is held to the crossing the linked file makes before the two are compared.
        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["linked/tool-reaches-only-core" + ReachedTheWidget],
                "the allow entry is asked at the project the instance reached, which is the tool's own copy");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The same layer inside an <c>Except</c> payload, the other exempting position and one where the
    ///     bare project noun's own row is held out. Wrapping the noun in a layer changes nothing:
    ///     what a layer names is read where the layer stands, and here it stands in an exemption.
    /// </summary>
    /// <remarks>
    ///     The row is not flipped. The build stays silent about the rule, which is asserted first, and the
    ///     comparison runs through the one-project pipeline directly, on both reference forms.
    /// </remarks>
    [Fact]
    public async Task TheOneProjectPipeline_OverAProjectDefinedLayerInAnExceptPayload_DisagreesWithTheWholeSolutionCheck()
    {
        // Arrange
        const string ruleId = "linked/tool-holds-only-core-owned-shared-types";
        ParitySubject subject = await LinkedAsync(
            "LinkedApp/layer in an Except payload",
            arch => arch.Rule(ruleId)
                .Enforce(arch.Namespace("LinkedApp.Tool.*")
                    .MustNotReference(arch.Namespace("LinkedApp.Shared.*")
                        .Except(arch.Layer("Core", arch.Project("LinkedApp.Core")))))
                .Because("A shared type Core does not own is one the tool compiled in for itself."));

        // Act
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        await subject.ShouldHoldOnBothLegsAsync(Ct);
        solution.ShouldBeEmpty("the whole-solution check excludes the widget, which Core also declares");
        ShouldForceTheSameKeysOnBothLegs(subject, ruleId, [ruleId + ReachedTheWidget]);
    }

    /// <summary>
    ///     The multi-framework project. One csproj, two compilations, and keys that carry no framework: a
    ///     type both frameworks declare is reported twice and the union of the compilations absorbs it.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAProjectCompiledForTwoFrameworks_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await MultiTfmAsync("MultiTfm");

        subject.Compilations.Count.ShouldBe(3, "two frameworks of the core project and the web project");
        subject.Expectation.Unmeasured.ShouldBeEmpty("rules the whole-solution check never measured");
        subject.Expectation.Keys.Describe()
            .ShouldBe([
                $"multi/core-types-are-sealed [Shape] T:MultiTfm.Core.ModernOnly at {BedSource("MultiTfm", "MultiTfm.Core", "ModernOnly.cs")}:7",
                $"multi/core-types-are-sealed [Shape] T:MultiTfm.Core.Widget at {BedSource("MultiTfm", "MultiTfm.Core", "Widget.Legacy.cs")}:6",
                $"multi/web-not-through-core [Reference] T:MultiTfm.Web.Endpoint -> T:MultiTfm.Core.Widget at {BedSource("MultiTfm", "MultiTfm.Web", "Endpoint.cs")}:7"
            ], "the banned reference, the type only one framework declares, and the type both do");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The same bed against the other half of the switch: a project that declares the build tier reports
    ///     what the same project on the live tier reports, over a csproj that yields two compilations.
    /// </summary>
    /// <remarks>
    ///     The bed the claim is worth a row on. Everywhere else one project is one compilation, so the two
    ///     tiers are compared over one set of trees; here the same source is compiled twice under different
    ///     symbols, and a tier that moved which compilation reported what would show nowhere else.
    /// </remarks>
    [Fact]
    public async Task TheBuildTier_OverAProjectCompiledForTwoFrameworks_ReportsWhatTheLiveTierReports()
    {
        ParitySubject subject = await MultiTfmAsync("MultiTfm tiers");

        subject.Expectation.Keys.ShouldNotBeEmpty("findings for the two tiers to agree about");

        await subject.ShouldAgreeWithTheBuildTierInBatchAsync(Ct);
    }

    /// <summary>
    ///     A layer holding both the project's own types and the ones its Razor generator writes, filtered by
    ///     <c>.Authored()</c>. The whole-solution check knows which are generated from the workspace; the
    ///     build has the attribute and the banner and nothing else.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverALayerOfAuthoredAndGeneratedTypes_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await BedAsync(
            "RazorApp", RazorSolution,
            arch => arch.Rule("razor/handwritten-web-types-are-sealed")
                .Enforce(arch
                    .AnyOf(arch.Namespace("RazorApp.Web"), arch.Namespace("AspNetCoreGeneratedDocument"))
                    .Authored()
                    .MustBeSealed())
                .Because("A handwritten web type nobody may subclass is the one the host resolved; the "
                         + "generated views are the generator's business."));

        // The case is only about the filter while there is something for it to filter, and only about the
        // rule while the rule reports: a bed whose generator stopped running would otherwise pass here.
        subject.Codebase.Types
            .Where(type => type.Namespace.StartsWith("AspNetCoreGeneratedDocument", StringComparison.Ordinal))
            .ShouldNotBeEmpty("generated view types for .Authored() to filter out");
        subject.Expectation.Keys.Describe()
            .ShouldBe([
                $"razor/handwritten-web-types-are-sealed [Shape] T:RazorApp.Web.HomeController at {WebSource("HomeController.cs")}:3",
                $"razor/handwritten-web-types-are-sealed [Shape] T:RazorApp.Web.Program at {WebSource("Program.cs")}:3"
            ], "what the whole-solution check reports once the generated views are filtered out");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     A ban whose target is a type name a project and a package it references both declare, so which
    ///     declaration an edge reached is the whole question.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverABanOnAShadowedTypeName_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = await BedAsync(
            "ShadowedApp", ShadowedSolution,
            arch => arch.Rule("shadowed/no-service-descriptor")
                .Enforce(arch.Namespace("ShadowedApp.*")
                    .MustNotReference(arch.Types.Named("Microsoft.Extensions.DependencyInjection.ServiceDescriptor")))
                .Because("The descriptor type is the container's own; holding one is holding the container."));

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     Asserts a rule the classifier holds out reports exactly <paramref name="keys" /> from one
    ///     project's view whichever form the siblings arrive in — which is what makes a row's verdict about
    ///     the rule rather than about how a build happened to be wired.
    /// </summary>
    /// <param name="subject">The bed and spec under test.</param>
    /// <param name="ruleId">The rule to force through the one-project pipeline.</param>
    /// <param name="keys">Every line the forced run gives, ordinally sorted.</param>
    private static void ShouldForceTheSameKeysOnBothLegs(
        ParitySubject subject, string ruleId, IReadOnlyList<string> keys)
    {
        foreach (ParityLeg leg in ParityAssertions.Legs)
            subject.ForcedKeys(ruleId, subject.On(leg), Ct)
                .Describe()
                .ShouldBe(
                    keys,
                    $"what a forced-in project-defined layer reports from one project's view with {ParityAssertions.Name(leg)}");
    }

    /// <summary>
    ///     The tail of the one line every exempting row's forced run gives: the tool's reference to the
    ///     copy of the shared type it compiled in for itself.
    /// </summary>
    private static string ReachedTheWidget =>
        " [Reference] T:LinkedApp.Tool.Command -> T:LinkedApp.Shared.Widget at " + CommandSource + ":7";

    private static string WidgetSource =>
        Path.Combine(BedDirectory, "LinkedApp", "LinkedApp.Core", "Shared", "Widget.cs");

    private static string CommandSource =>
        Path.Combine(BedDirectory, "LinkedApp", "LinkedApp.Tool", "Command.cs");

    private static string WebSource(string fileName)
    {
        return Path.Combine(BedDirectory, "RazorApp", "RazorApp.Web", fileName);
    }

    private static string BedSource(string bed, string project, string fileName)
    {
        return Path.Combine(BedDirectory, bed, project, fileName);
    }

    private static string BedDirectory =>
        FixtureBeds.TestSolutionsRoot;

    private static Task<ParitySubject> LinkedAsync(string name, Action<Arch> define)
    {
        return BedAsync(name, LinkedSolution, define);
    }

    /// <summary>The linked bed under the residency rule, shared by its row and the forced row that explains it.</summary>
    private static Task<ParitySubject> ResidencyAsync(string name)
    {
        return LinkedAsync(
            name,
            arch => arch.Rule(ResidencyRuleId)
                .Enforce(arch.Namespace("LinkedApp.Shared.*").MustResideInProject("LinkedApp.Core"))
                .Because("The shared widget is Core's to own, wherever else it is compiled."));
    }

    /// <summary>The linked bed under the project-headed exclusion, shared the same way.</summary>
    private static Task<ParitySubject> ExclusionAsync(string name)
    {
        return LinkedAsync(
            name,
            arch => arch.Rule(ExclusionRuleId)
                .Enforce(arch.Namespace("LinkedApp.Tool.*")
                    .MustNotReference(arch.Namespace("LinkedApp.Shared.*").Except(arch.Project("LinkedApp.Core"))))
                .Because("A shared type Core does not own is one the tool compiled in for itself."));
    }

    // The multi-framework bed's one spec, shared by the two rows that run it: a ban the web project breaks
    // and a shape rule the core project breaks under each of its frameworks.
    private static Task<ParitySubject> MultiTfmAsync(string name)
    {
        return BedAsync(
            name, MultiTfmSolution,
            arch =>
            {
                arch.Rule("multi/web-not-through-core")
                    .Enforce(arch.Namespace("MultiTfm.Web").MustNotReference(arch.Namespace("MultiTfm.Core")))
                    .Because("The endpoint layer goes through a contract, never through the core type.");

                arch.Rule("multi/core-types-are-sealed")
                    .Enforce(arch.Namespace("MultiTfm.Core").MustBeSealed())
                    .Because("A core type nobody may subclass is the one the caller resolved.");
            });
    }

    private static Task<ParitySubject> BedAsync(string name, string relativeSolutionPath, Action<Arch> define)
    {
        string solutionPath = Path.Combine(BedDirectory, relativeSolutionPath.Replace('/', Path.DirectorySeparatorChar));

        return ParityHarness.ForSolutionAsync(name, Checker.Model(define), solutionPath, Ct);
    }
}
