using Shouldly;
using Xunit;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The parity pin over the in-memory corpus: for each <see cref="SiblingCorpus" /> subject, what the
///     build tier says inside each project against what <c>check</c> says over the whole solution, and,
///     for the shapes about which document of a project answers, where each finding is placed.
/// </summary>
/// <remarks>
///     Compilations built from source strings rather than beds, because these are shapes a real solution
///     would have to be contrived to hold and the point is the verb rather than the project system.
///     Every fact runs both legs and, through the same assertion, both drivers: the whole compilation at
///     once and then document by document. A fact about which document answers states that placement for
///     itself first, because the union the assertion compares would be the same set however it were split
///     up.
/// </remarks>
public sealed class SiblingCorpusParityTests
{
    [Fact]
    public async Task TheBuildTier_OverTheInboundVerbsBothWaysRound_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.Inbound();

        // Three rules, and between them a violation written in each of the three projects that has one:
        // no single compilation sees all of it and the union over them does.
        subject.Expectation.Keys.Describe()
            .Count.ShouldBe(4, "the two contract crossings and the store held from the web layer twice over");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAQuarantineWithACapturedBaseline_ReportsOnlyTheCrossingItDoesNotHold()
    {
        ParitySubject subject = SiblingCorpus.CapturedContainment();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["legacy/data/containment [Reference] T:Web.Fresh -> T:Data.Ledger at Web.Site.cs:10"],
                "the blessed crossing is grandfathered and the new one is a broken law");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAProjectNounForARenamedAssembly_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.RenamedAssembly();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["projects/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.Site.cs:5"],
                "a project noun answers from the model file's table, not from the assembly name");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAPairThatHasGrownPastItsBaseline_ReportsEverySite()
    {
        ParitySubject subject = SiblingCorpus.GrownPair();

        subject.Expectation.Keys.Describe()
            .Count.ShouldBe(2, "a pair that has grown is red at every site, the blessed one included");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverASiblingWhoseBaseTypeItHasNoMetadataFor_SaysNothingAboutIt()
    {
        // The one difference this design accepts, on both legs and for the same reason: a compilation
        // resolves a referenced assembly's own references against its own reference set, so neither form
        // of reference carries the sibling's closure.
        ParitySubject subject = SiblingCorpus.ErrorHierarchy(vouched: false);
        var gap = new ParityGap(
            "the sibling's base type lives in Base.Lib, which the web project does not reference",
            key => key.Rule == "hierarchy/web-holds-no-root");

        subject.Expectation.Keys.Describe()
            .Count.ShouldBe(1, "the whole-solution check sees the sibling's whole hierarchy");

        await subject.ShouldHoldOnBothLegsAsync(Ct, gap);
    }

    [Fact]
    public async Task TheBuildTier_OverTheSameSiblingWithItsBaseAssemblyReferenced_ReportsIt()
    {
        // The control for the difference above: the same solution with one reference added, and the two
        // sets are equal. That is what shows the difference came from the missing reference.
        ParitySubject subject = SiblingCorpus.ErrorHierarchy(vouched: true);

        subject.Expectation.Keys.Describe()
            .Count.ShouldBe(1, "the whole-solution check sees the sibling's whole hierarchy");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAnAttributeASiblingsBuildConditionsAway_SaysNothing()
    {
        ParitySubject subject = SiblingCorpus.ConditionalAttribute();

        // The rule is out, so the build never reaches it: the expectation holds nothing and the two legs
        // report nothing.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The first of the two directions an attribute filter over another project's types is wrong in,
    ///     with the row forced in for this fact alone: a sibling that arrives as source shows the attribute
    ///     its author wrote, and the same sibling as a metadata image does not, because its own build
    ///     conditioned the attribute away.
    /// </summary>
    /// <remarks>
    ///     The row is not flipped. The build stays silent about the rule, which is asserted first, and the
    ///     comparison runs through the one-project pipeline directly.
    /// </remarks>
    [Fact]
    public void TheOneProjectPipeline_CrossingAnAttributeConditionedAway_ReportsLessFromAMetadataImage()
    {
        // Arrange
        ParitySubject subject = SiblingCorpus.ConditionalAttribute();
        const string ruleId = "markers/web-holds-nothing-marked";
        const string crossing =
            "markers/web-holds-nothing-marked [Reference] T:Web.Client -> T:Data.Store at Web.Site.cs:5";

        // Act
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();
        IReadOnlyList<string> asCompilations = subject.ForcedKeys(ruleId, subject.Compilations, Ct)
            .Describe();
        IReadOnlyList<string> asImages = subject.ForcedKeys(ruleId, subject.Imaged, Ct)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        solution.ShouldBe([crossing], "the whole-solution check reads the attribute off the source");
        asCompilations.ShouldBe([crossing], "a sibling that arrives as source shows what its author wrote");
        asImages.ShouldBeEmpty("the attribute is absent from the assembly the same project compiles to");
    }

    /// <summary>
    ///     The second direction, which is the one that keeps the row out: an attribute no author wrote,
    ///     which the compiler puts on the type as it emits it. A metadata image carries it and the source
    ///     does not, so the same filter selects a type the whole-solution check never selects and the
    ///     build reports a violation no check gives.
    /// </summary>
    /// <remarks>
    ///     Between this fact and the one above, the error runs both ways at once and no polarity argument
    ///     rescues it — which is why <c>AttributedWith</c> keys on the side it is read at and not on what
    ///     membership there does to the verdict. The row is not flipped: the build stays silent about the
    ///     rule, which is asserted first.
    /// </remarks>
    [Fact]
    public async Task TheOneProjectPipeline_CrossingAnAttributeTheCompilerSynthesizes_ReportsMoreFromAMetadataImage()
    {
        // Arrange
        ParitySubject subject = SiblingCorpus.SynthesizedAttribute();
        const string ruleId = "markers/web-holds-nothing-defaulted";
        const string authored =
            "markers/web-holds-nothing-defaulted [Reference] T:Web.Client -> T:Data.Marked at Web.Site.cs:5";
        const string synthesized =
            "markers/web-holds-nothing-defaulted [Reference] T:Web.Client -> T:Data.Catalogue at Web.Site.cs:6";

        // Act
        IReadOnlyList<string> solution = subject.SolutionKeys(ruleId)
            .Describe();
        IReadOnlyList<string> asCompilations = subject.ForcedKeys(ruleId, subject.Compilations, Ct)
            .Describe();
        IReadOnlyList<string> asImages = subject.ForcedKeys(ruleId, subject.Imaged, Ct)
            .Describe();

        // Assert — the row's own verdict first: the classifier holds the rule out, so the build never reaches it.
        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");
        await subject.ShouldHoldOnBothLegsAsync(Ct);
        solution.ShouldBe([authored], "only the hand-written attribute is on the source symbol");
        asCompilations.ShouldBe([authored], "a sibling that arrives as source shows what its author wrote");
        asImages.ShouldBe(
            [synthesized, authored],
            "what a forced-in attribute filter reports from a metadata image, which is why the row is out");
    }

    [Fact]
    public async Task TheBuildTier_OverARuleAboutRegistrations_SaysNothing()
    {
        ParitySubject subject = SiblingCorpus.RegisteredElsewhere();

        subject.Expectation.Checked.ShouldBeEmpty("rules the classifier lets a one-project build decide");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverTheRemainderSpec_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.Remainder();

        subject.Expectation.Unmeasured.ShouldBeEmpty(
            "remainder rule(s) whose subject matched nothing, which report no violation and so reach no verb");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     A project-headed allow-list operand naming the declarer whose copy the reference reached. The
    ///     reference is allowed, and the build allows it too — which is the direction the whole design
    ///     turns on, since a build that refused it would be reporting what no check over the whole
    ///     solution gives.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAnAllowListNamingTheCopyAReferenceReached_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.AllowListOverASharedName(namingTheReachedCopy: true);

        // The difference guard: the rule has to be live for its silence about the widget to mean anything,
        // so the reference it does refuse is pinned here.
        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["copies/host-reaches-only-supply [Reference] T:Host.Consumer -> T:Ink.Pen at Host.App.cs:6"],
                "the pen is outside the allow-list and the widget, whose reached copy Supply declares, is in it");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAnAllowListNamingAnotherDeclarersCopy_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.AllowListOverASharedName(namingTheReachedCopy: false);

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "copies/host-reaches-only-bureau [Reference] T:Host.Consumer -> T:Common.Widget at Host.App.cs:5",
                    "copies/host-reaches-only-bureau [Reference] T:Host.Consumer -> T:Ink.Pen at Host.App.cs:6"
                ],
                "Bureau declares the widget and the host reached Supply's copy, so the allow-list spares nothing");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The throw verb's strict allow-list, naming the declarer whose copy the host threw: the fault is
    ///     spared and the unlisted snag is not, in the whole-solution check and in the build alike.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAThrowAllowListNamingTheCopyThrown_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.ThrowAllowListOverASharedName(namingTheReachedCopy: true);

        // The difference guard: the snag is outside the allow-list either way, so a leg that reported
        // nothing would be a rule that never ran rather than a fault it spared.
        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["copies/host-throws-only-supply [Throw] T:Host.Scribe -> T:Ink.Snag at Host.App.cs:6"],
                "the fault is Supply's copy, which the law names, and the snag is nobody's");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     The same throw allow-list naming the other declarer of the same fault: both throws are refused,
    ///     though plain membership would have spared the fault.
    /// </summary>
    [Fact]
    public async Task TheBuildTier_OverAThrowAllowListNamingAnotherDeclarersCopy_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.ThrowAllowListOverASharedName(namingTheReachedCopy: false);

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "copies/host-throws-only-bureau [Throw] T:Host.Scribe -> T:Common.Fault at Host.App.cs:5",
                    "copies/host-throws-only-bureau [Throw] T:Host.Scribe -> T:Ink.Snag at Host.App.cs:6"
                ],
                "Bureau declares the fault and the host threw Supply's copy, so the allow-list spares nothing");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAnInboundAllowListAcrossDriftedCopies_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.InboundAllowListOverDriftedCopies();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["copies/pen-only-from-the-writer [Reference] T:Common.Scribe -> T:Ink.Pen at Mirror.Asm.cs:5"],
                "one of two identical references, the writer's being the copy the law names");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverTheSelfAllowanceOfAProjectSubject_AgreesWithTheWholeSolutionCheck()
    {
        ParitySubject subject = SiblingCorpus.SelfAllowanceOverDriftedCopies();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["copies/writer-reaches-only-itself [Reference] T:Common.Scribe -> T:Ink.Pen at Writer.Asm.cs:5"],
                "the writer's own copy reaches outside the writer, and the mirror's copy is not the subject's");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAPartialTypeAcrossTwoDocuments_ReportsEachPartInItsOwnDocument()
    {
        ParitySubject subject = SiblingCorpus.PartialAcrossTwoDocuments();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "partial/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Ledger at Web.Audit.cs:5",
                    "partial/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"
                ],
                "one crossing per part of the controller, in the file that part writes");

        ParityReport perDocument = await PerDocumentAsync(subject);
        IReadOnlyList<ParityKey> inWeb = perDocument.KeysIn("Web.cs");
        IReadOnlyList<ParityKey> inAudit = perDocument.KeysIn("Web.Audit.cs");

        inWeb.Count.ShouldBe(1, "the part in Web.cs reaches the store once and the other part's site is not its");
        inWeb.Describe()
            .ShouldBe(
                ["partial/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"],
                "the store is held by the part Web.cs writes");
        inAudit.Count.ShouldBe(1, "the part in Web.Audit.cs reaches the ledger once");
        inAudit.Describe()
            .ShouldBe(
                [
                    "partial/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Ledger at Web.Audit.cs:5"
                ],
                "the ledger is held by the part Web.Audit.cs writes");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAShapeRuleOnAPartialType_ReportsItAtTheFirstDeclarationAlone()
    {
        ParitySubject subject = SiblingCorpus.ShapeOnAPartialType();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["shapes/controllers-are-sealed [Shape] T:Web.OrderController at A.cs:3"],
                "the declaration that sorts first is where a shape violation is placed");

        ParityReport perDocument = await PerDocumentAsync(subject);
        IReadOnlyList<ParityKey> inA = perDocument.KeysIn("A.cs");

        inA.Count.ShouldBe(1, "the document holding the placed site reports it once");
        inA.Describe()
            .ShouldBe(
                ["shapes/controllers-are-sealed [Shape] T:Web.OrderController at A.cs:3"],
                "the document holding the placed site is the one that reports the violation");
        perDocument.KeysIn("B.cs")
            .ShouldBeEmpty("the other part does not hold the site the violation was placed at");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    /// <summary>
    ///     A captured pair that has grown, with its two sites in two documents: both are red, and each
    ///     document's warning counts the whole pair rather than the share of it that document holds.
    /// </summary>
    /// <remarks>
    ///     The count is the part a per-document view could get wrong quietly. A document that carried only
    ///     its own file's slice of the controller would find one site where the baseline allows one, and
    ///     report nothing at all.
    /// </remarks>
    [Fact]
    public async Task TheBuildTier_OverAGrownPairAcrossTwoDocuments_CountsTheWholePairInEachDocument()
    {
        // Arrange
        ParitySubject subject = SiblingCorpus.GrownPairAcrossTwoDocuments();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "legacy/web-through-the-store [Reference] T:Web.OrderController -> T:Data.Store at Web.More.cs:5",
                    "legacy/web-through-the-store [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"
                ],
                "a pair that has grown is red at every site, the blessed one included");

        // Act
        ParityReport batch = await subject.ReportAsync(subject.Compilations, ParityDriver.Batch, Ct);
        ParityReport perDocument = await PerDocumentAsync(subject);

        // Assert
        perDocument.KeysIn("Web.cs")
            .Count.ShouldBe(1, "the site Web.cs holds");
        perDocument.KeysIn("Web.More.cs")
            .Count.ShouldBe(1, "the site Web.More.cs holds");
        Ids(perDocument)
            .ShouldBe(["SR2002", "SR2002"], "debt a captured baseline does not hold is new debt, at either site");
        ParityHarness.DescribeWithMessages(perDocument)
            .ShouldBe(
                ParityHarness.DescribeWithMessages(batch),
                "a document's warning says of a finding what the whole compilation's warning says of it");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverANestedTypeInASecondFile_ReportsItInTheFileThatDeclaresIt()
    {
        ParitySubject subject = SiblingCorpus.NestedTypeInASecondFile();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                [
                    "nested/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5",
                    "nested/web-not-through-data [Reference] T:Web.OrderController.Audit -> T:Data.Ledger "
                    + "at Web.Nested.cs:7"
                ],
                "the container reaches the store and the type nested in its second part reaches the ledger");

        ParityReport perDocument = await PerDocumentAsync(subject);
        perDocument.KeysIn("Web.Nested.cs")
            .Describe()
            .ShouldBe(
                [
                    "nested/web-not-through-data [Reference] T:Web.OrderController.Audit -> T:Data.Ledger "
                    + "at Web.Nested.cs:7"
                ],
                "the nested type's crossing is the file that declares the nested type's");
        perDocument.KeysIn("Web.cs")
            .Describe()
            .ShouldBe(
                ["nested/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"],
                "the container's own crossing stays in the file that writes it");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverADocumentThatDeclaresNoType_ReportsNothingInIt()
    {
        ParitySubject subject = SiblingCorpus.ADocumentWithNoTypes();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["inbound/data-not-from-web [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"],
                "the one crossing, so the empty document's silence is beside a document that speaks");

        ParityReport perDocument = await PerDocumentAsync(subject);
        perDocument.KeysIn("Empty.cs")
            .ShouldBeEmpty("a document that declares no type has nothing any rule here could be broken by");
        perDocument.KeysIn("Web.cs")
            .Count.ShouldBe(1, "the document that does declare a type is still reported on");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAnInboundRuleWhoseSubjectAnotherDocumentDeclares_ReportsAtTheOffender()
    {
        ParitySubject subject = SiblingCorpus.AnInboundRuleWhoseSubjectIsInAnotherDocument();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["inbound/core-not-from-the-ui [Reference] T:Web.Ui.Page -> T:Web.Core.Store at Ui.cs:5"],
                "the page's reference to the store, which is the one crossing");

        ParityReport perDocument = await PerDocumentAsync(subject);
        perDocument.KeysIn("Ui.cs")
            .Describe()
            .ShouldBe(
                ["inbound/core-not-from-the-ui [Reference] T:Web.Ui.Page -> T:Web.Core.Store at Ui.cs:5"],
                "the document that makes the reference is the one that answers for it");
        perDocument.KeysIn("Core.cs")
            .ShouldBeEmpty("the document that declares the subject makes no reference to it");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverAProgramOfTopLevelStatements_ReportsItInTheFileThatIsItsBody()
    {
        ParitySubject subject = SiblingCorpus.TopLevelStatementsProgram();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["entry/web-not-through-data [Reference] T:Program -> T:Data.Store at Program.cs:1"],
                "the statement that makes the store is attributed to the type the compiler synthesized");

        ParityReport perDocument = await PerDocumentAsync(subject);
        perDocument.KeysIn("Program.cs")
            .Describe()
            .ShouldBe(
                ["entry/web-not-through-data [Reference] T:Program -> T:Data.Store at Program.cs:1"],
                "the document the statements sit in reports the synthesized type's crossing");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverATypeWithAnErrorBaseInAnotherDocument_StillReportsTheReference()
    {
        ParitySubject subject = SiblingCorpus.AnErrorBaseInAnotherDocument();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["inbound/legacy-not-from-the-ui [Reference] T:Web.Ui.Consumer -> T:Web.Legacy.Broken at Ui.cs:5"],
                "the consumer's reference into the legacy corner, whose own base does not resolve");

        ParityReport perDocument = await PerDocumentAsync(subject);
        perDocument.KeysIn("Ui.cs")
            .Describe()
            .ShouldBe(
                ["inbound/legacy-not-from-the-ui [Reference] T:Web.Ui.Consumer -> T:Web.Legacy.Broken at Ui.cs:5"],
                "the screen an unresolved base trips is about a sibling's closure, not about own code");
        perDocument.KeysIn("Legacy.cs")
            .ShouldBeEmpty("the document that declares the broken type makes no reference to it");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    [Fact]
    public async Task TheBuildTier_OverTwoTreesUnderOnePath_ReportsThePathOnce()
    {
        // Arrange
        ParitySubject subject = SiblingCorpus.TwoTreesUnderOnePath();

        subject.Expectation.Keys.Describe()
            .ShouldBe(
                ["paths/web-not-through-data [Reference] T:Web.OrderController -> T:Data.Store at Web.cs:5"],
                "the crossing the first tree under the path makes, which is the only one there is");

        // Act
        ParityReport batch = await subject.ReportAsync(subject.Compilations, ParityDriver.Batch, Ct);
        ParityReport perDocument = await PerDocumentAsync(subject);

        // Assert
        batch.Keys.Count.ShouldBe(1, "one warning per site of the first tree, not one per tree under the path");
        perDocument.Placed.Count.ShouldBe(
            batch.Keys.Count, "the second tree under the path is not its declarer and reports nothing");

        await subject.ShouldHoldOnBothLegsAsync(Ct);
    }

    // What the documents of a subject's own projects said, taken one at a time: the form the placement
    // claims above read, and the one the whole-solution check has nothing to say about.
    private static Task<ParityReport> PerDocumentAsync(ParitySubject subject)
    {
        return subject.ReportAsync(subject.Compilations, ParityDriver.PerDocument, Ct);
    }

    // Which warning each placed finding printed, ordinally sorted: SR2001 for a law broken, SR2002 for debt
    // a baseline does not hold.
    private static IReadOnlyList<string> Ids(ParityReport report)
    {
        return report.Placed
            .Select(placed => placed.Diagnostic.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }
}
