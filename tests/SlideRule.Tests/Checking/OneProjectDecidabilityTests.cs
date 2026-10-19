using Shouldly;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.ModelFile;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The one-project decidability classifier: one fact per reason it can give, the three axes a row can
///     key on — the side <c>.Authored()</c> reads, and the polarity and reading a project noun reads
///     together — the order the walk meets an out in, a representative decidable rule of every family, and
///     the claim that a rule classifies the same after a round trip through a model file.
/// </summary>
/// <remarks>
///     <para>
///         The reasons are spelled once each below and every fact asserts a whole one: they are what a
///         reader is told, so a reworded reason should move deliberately rather than pass on a substring.
///     </para>
///     <para>
///         The per-reason facts and the walk facts deliberately overlap on <c>.Authored()</c>, the
///         registration noun and the tripwire — the first group pins the sentence each row gives, the
///         second pins which node the walk reaches to get there, and a change to either should red on its
///         own terms.
///     </para>
///     <para>
///         A layer is read at the position the layer itself stands in, on both axes, so every fact about
///         a position-sensitive node has a layer-shaped twin: what the bare node says inside a layer
///         definition is what the layer says where it is named.
///     </para>
/// </remarks>
public sealed class OneProjectDecidabilityTests
{
    private const string Code = "Holds a predicate written in code.";

    private const string Registered =
        "Reads dependency-injection registrations, which are collected across the whole solution.";

    private const string Authored =
        "Filters generated code out of a selection of other types, and one project cannot tell whether a "
        + "type another project declares is generated.";

    private const string AttributedWith =
        "Selects other types by attribute, and a project compiled against another's assembly does not see "
        + "every attribute its source declares.";

    private const string Under =
        "Selects other types by the files declaring them, and a project compiled against another's assembly does "
        + "not see its source files.";

    private const string Declarer =
        "Spares a type by any project that declares it, and one project's compilation cannot tell when "
        + "another project compiles the same type, so it would report what the whole solution exempts.";

    private const string Family = "Ranges over a family, which is judged across the whole solution.";

    private const string Among = "Judges references among a family of layers, which takes the whole solution.";

    private const string Circle =
        "A circle of references can pass through any project, so finding one takes the whole solution.";

    private const string Counterpart =
        "A counterpart may be declared in any project, so the rule takes the whole solution.";

    private const string ProjectFile = "Reads project-file facts, and one project's compilation carries none.";

    private const string Residency =
        "A type compiled into several projects resides in each of them, so where a type resides takes the "
        + "whole solution.";

    private const string Membership =
        "Which layer a type belongs to can depend on every project that compiles it, so coverage is left to "
        + "the whole solution.";

    private const string Tripwire =
        "A tripwire compares against a diff, which one project's compilation does not carry.";

    [Fact]
    public void Classify_APredicateVerb_ReportsTheCodeReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.Must(_ => true, description: "hold"));

        verdict.Reason.ShouldBe(Code);
    }

    [Fact]
    public void Classify_TheRegistrationVerb_ReportsTheRegistrationReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.MustBeRegistered());

        verdict.Reason.ShouldBe(Registered);
    }

    [Fact]
    public void Classify_AuthoredInAnOperand_ReportsTheAuthoredReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.MustNotReference(arch.Types.Authored()));

        verdict.Reason.ShouldBe(Authored);
    }

    [Fact]
    public void Classify_AFamilySubject_ReportsTheFamilyReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Each(arch.Layer("Domain", "App.Domain.*")).MustBeSealed());

        verdict.Reason.ShouldBe(Family);
    }

    [Fact]
    public void Classify_TheCrossCellBan_ReportsTheAmongReason()
    {
        OneProjectVerdict verdict = Enforcing(arch =>
        {
            Layer domain = arch.Layer("Domain", "App.Domain.*");
            Layer web = arch.Layer("Web", "App.Web.*");

            return arch.Each(domain, web).MustNotReferenceEachOther();
        });

        verdict.Reason.ShouldBe(Among);
    }

    [Fact]
    public void Classify_ALeafVerbOverAFamily_GoesOutThroughTheFamilyAndNotThroughTheVerb()
    {
        // The two leaf verbs are ordinary allow-list rules over a plain subject, so their rows are in; it is
        // the family they range over that takes the whole solution, and the reason says which it was.
        OneProjectVerdict verdict = Enforcing(arch =>
        {
            Layer domain = arch.Layer("Domain", "App.Domain.*");
            Layer web = arch.Layer("Web", "App.Web.*");

            return arch.Each(domain, web).MustOnlyBeReferencedByItself();
        });

        verdict.Reason.ShouldBe(Family);
    }

    [Fact]
    public void Classify_TheCycleGate_ReportsTheCircleReason()
    {
        OneProjectVerdict verdict = Enforcing(arch =>
        {
            Layer domain = arch.Layer("Domain", "App.Domain.*");
            Layer web = arch.Layer("Web", "App.Web.*");

            return arch.Each(domain, web).MustNotHaveCircularReferences();
        });

        verdict.Reason.ShouldBe(Circle);
    }

    [Fact]
    public void Classify_TheCorrespondenceVerb_ReportsTheCounterpartReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.WithSuffix("Service")
            .MustHaveExactlyOneCounterpart(arch.Types, "{Name}Tests"));

        verdict.Reason.ShouldBe(Counterpart);
    }

    [Fact]
    public void Classify_APackagingVerb_ReportsTheProjectFileReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Projects.MustNotBePackable());

        verdict.Reason.ShouldBe(ProjectFile);
    }

    [Fact]
    public void Classify_TheResidencyVerb_ReportsTheResidencyReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.WithSuffix("Controller")
            .MustResideInProject("App.Web"));

        verdict.Reason.ShouldBe(Residency);
    }

    [Fact]
    public void Classify_TheCoverageVerb_ReportsTheMembershipReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.MustBelongTo(arch.Namespace("App.Domain.*")));

        verdict.Reason.ShouldBe(Membership);
    }

    [Fact]
    public void Classify_ARuleWithNoConstraint_ReportsTheTripwireReason()
    {
        // A caution declares one rule and it is the tripwire, so the model carries exactly the rule under
        // test and nothing about scopes has to be spelled here twice.
        OneProjectVerdict verdict = Classify(arch => arch.Scope("legacy/cautioned")
            .Caution(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha is structural.")
            .Because("b"));

        verdict.Reason.ShouldBe(Tripwire);
    }

    [Fact]
    public void Classify_AuthoredOnTheSubject_IsDecidable()
    {
        // The subject's own types are the ones this rule reports at, so a project that cannot tell whether
        // another project's type is generated never reports about that type anyway. The layer says the same
        // thing: a layer is read where it stands, so a filter inside its definition is read there too.
        OneProjectVerdict bare = Enforcing(arch => arch.Types.Authored().MustBeSealed());
        OneProjectVerdict inALayer = Enforcing(arch => arch.Layer("Handwritten", arch.Types.Authored())
            .MustBeSealed());

        bare.Reason.ShouldBeNull();
        inALayer.Reason.ShouldBeNull();
    }

    [Fact]
    public void Classify_AuthoredReadOverTheWholeSolution_ReportsTheAuthoredReason()
    {
        // Arrange — the three ways a rule reads a selection of types that are not its subject, one of them
        // reached from inside the subject.
        (string Where, Func<Arch, Constraint> Rule)[] shapes =
        [
            ("in an operand", arch => arch.Types.MustNotReference(arch.Types.Authored())),
            ("in an Except payload of the subject", arch => arch.Types.Except(arch.Types.Authored()).MustBeSealed()),
            ("in a layer definition an operand names",
                arch => arch.Types.MustNotReference(arch.Layer("Handwritten", arch.Types.Authored())))
        ];

        // Act + Assert
        ShouldAllClassifyAs(shapes, Authored, "Authored read over the whole solution but not classified as such");
    }

    [Fact]
    public void Classify_AnInboundVerbWithAnAuthoredSubject_ReportsTheAuthoredReason()
    {
        // Arrange — the three verbs whose violations are sited on the referencing side. The concession the
        // subject-position row makes is that a wrong verdict lands at the subject's own declaration, and
        // for these it lands in the referrer's code instead, which is where this reader is looking.
        (string Verb, Func<Arch, Constraint> Rule)[] inbound =
        [
            ("MustNotBeReferencedBy", arch => arch.Namespace("App.Domain.*")
                .Authored()
                .MustNotBeReferencedBy(arch.Namespace("App.Web.*"))),
            ("MustOnlyBeReferencedBy", arch => arch.Namespace("App.Domain.*")
                .Authored()
                .MustOnlyBeReferencedBy(arch.Namespace("App.Web.*"))),
            ("MustOnlyBeReferencedByItself", arch => arch.Namespace("App.Domain.*")
                .Authored()
                .MustOnlyBeReferencedByItself())
        ];

        // Act + Assert
        ShouldAllClassifyAs(
            inbound, Authored, "Inbound verb(s) whose Authored subject is not read over the whole solution");
    }

    [Fact]
    public void Classify_AttributedWithOnTheSubject_IsDecidable()
    {
        // The subject's own types are the ones this rule reports at, and this project compiled them, so it
        // reads their attributes off its own source — through a layer definition as much as directly.
        OneProjectVerdict bare = Enforcing(arch => arch.Types.AttributedWith<ObsoleteAttribute>()
            .MustBeSealed());
        OneProjectVerdict inALayer = Enforcing(arch => arch
            .Layer("Marked", arch.Types.AttributedWith<ObsoleteAttribute>())
            .MustBeSealed());

        bare.Reason.ShouldBeNull();
        inALayer.Reason.ShouldBeNull();
    }

    [Fact]
    public void Classify_AttributedWithReadOverTheWholeSolution_ReportsTheAttributedWithReason()
    {
        // Arrange — the same three readings as Authored's: a home-made attribute can be conditioned away in
        // the assembly a sibling compiles to, so an attribute filter over other projects' types answers one
        // way from source and another from metadata.
        (string Where, Func<Arch, Constraint> Rule)[] shapes =
        [
            ("in an operand", arch => arch.Types.MustNotReference(arch.Types.AttributedWith<ObsoleteAttribute>())),
            ("in an Except payload of the subject",
                arch => arch.Types.Except(arch.Types.AttributedWith<ObsoleteAttribute>())
                    .MustBeSealed()),
            ("in a layer definition an operand names",
                arch => arch.Types.MustNotReference(
                    arch.Layer("Marked", arch.Types.AttributedWith<ObsoleteAttribute>())))
        ];

        // Act + Assert
        ShouldAllClassifyAs(
            shapes, AttributedWith, "AttributedWith read over the whole solution but not classified as such");
    }

    [Fact]
    public void Classify_UnderOnTheSubject_IsDecidable()
    {
        // This project compiled its own types from files it can name, so a glob over them is read off its own
        // source, through a layer definition as much as directly.
        OneProjectVerdict bare = Enforcing(arch => arch.Types.Under("src/Widgets").MustBeSealed());
        OneProjectVerdict inALayer = Enforcing(arch => arch.Layer("Widgets", arch.Types.Under("src/Widgets"))
            .MustBeSealed());

        bare.Reason.ShouldBeNull();
        inALayer.Reason.ShouldBeNull();
    }

    [Fact]
    public void Classify_UnderReadOverTheWholeSolution_ReportsTheUnderReason()
    {
        // Arrange — the three readings Authored's row leaves out, for the reason a sibling's types arrive with no
        // declaring file to read a path from.
        (string Where, Func<Arch, Constraint> Rule)[] shapes =
        [
            ("in an operand", arch => arch.Types.MustNotReference(arch.Types.Under("src/Legacy"))),
            ("in an Except payload of the subject",
                arch => arch.Types.Except(arch.Types.Under("src/Legacy")).MustBeSealed()),
            ("in a layer definition an operand names",
                arch => arch.Types.MustNotReference(arch.Layer("Legacy", arch.Types.Under("src/Legacy"))))
        ];

        // Act + Assert
        ShouldAllClassifyAs(shapes, Under, "Under read over the whole solution but not classified as such");
    }

    [Fact]
    public void Classify_AProjectNounThatBans_IsDecidable()
    {
        // Arrange — the positions where membership decides who is IN trouble. One compilation's
        // membership is a subset of the whole solution's, so each of these can only miss a type and report
        // fewer violations than the check does, which is the direction the build tier is allowed to err in.
        (string Shape, Func<Arch, Constraint> Rule)[] banning =
        [
            ("the subject of a shape verb", arch => arch.Project("App.Data").MustBeSealed()),
            ("the subject of an outbound ban",
                arch => arch.Project("App.Data").MustNotReference(arch.Namespace("App.Web.*"))),
            ("the subject of an inbound ban",
                arch => arch.Project("App.Domain").MustNotBeReferencedBy(arch.Namespace("App.Web.*"))),
            ("a ban's operand", arch => arch.Types.MustNotReference(arch.Project("App.Data"))),
            // A payload's own payload subtracts from an exemption, which puts its members back under the law,
            // so the polarity flips back and a project noun there bans like a subject's.
            ("the source of a member Except payload's own payload",
                arch => arch.Types.Methods
                    .Except(arch.Types.Methods.Except(arch.Project("App.Data").Methods.Named("Run")))
                    .MustBeStatic())
        ];

        // Act + Assert
        ShouldAllClassifyAs(banning, null, "Position(s) where a project noun only bans and yet is not decidable");
    }

    [Fact]
    public void Classify_AProjectNounThatExemptsByPlainMembership_ReportsTheDeclarerReason()
    {
        // Arrange — the positions where membership decides who is OUT of trouble and is asked of the type
        // rather than of one copy of it. An exclusion subtracts a node from the set before any edge is
        // walked, so it drops every copy of a name whichever project compiles it; a project that compiles
        // another's source file declares its types too, so a name that exempts there exempts nothing here
        // and the build would report what the check allows. That holds wherever the host selection stands,
        // since an Except payload takes its own position rather than the one it was written inside.
        (string Where, Func<Arch, Constraint> Rule)[] exempting =
        [
            ("in an Except payload of a shape verb's subject",
                arch => arch.Namespace("App.Web.*").Except(arch.Project("App.Data")).MustBeSealed()),
            ("in an Except payload of a ban's subject",
                arch => arch.Namespace("App.Web.*").Except(arch.Project("App.Data"))
                    .MustNotReference(arch.Namespace("App.Domain.*"))),
            ("in an Except payload of a ban's operand",
                arch => arch.Types.MustNotReference(arch.Namespace("App.Domain.*").Except(arch.Project("App.Data")))),
            // A member Except payload draws its members from a type selection standing where the exclusion
            // stands, so a project noun there spares by plain membership exactly as a type-side payload's does.
            ("as the source of a member Except payload",
                arch => arch.Types.Methods.Except(arch.Project("App.Data").Methods).MustBeStatic())
        ];

        // Act + Assert
        ShouldAllClassifyAs(
            exempting, Declarer, "Project noun(s) standing where plain membership exempts but not classified as such");
    }

    [Fact]
    public void Classify_AProjectNounThatExemptsAtOneEdgeEnd_IsDecidable()
    {
        // Arrange — the same exempting polarity, narrowed to one edge instance. An allow-list entry is
        // asked at the project that instance reached (GRAMMAR §4.1), and a compilation attributes the
        // instances it makes exactly, so the declarers it cannot see are never the project in question.
        // The last two are the trap the polarity axis exposed and the reading axis answers: a MustOnly*
        // reference verb folds its own subject into its allow-set, so that subject spares as well as
        // bounds — and spares it one copy at a time. Measured over the beds the parity pin carries.
        (string Where, Func<Arch, Constraint> Rule)[] exempting =
        [
            ("in an outbound allow-list operand",
                arch => arch.Types.MustOnlyReference(arch.Project("App.Data"))),
            ("in an inbound allow-list operand",
                arch => arch.Types.MustOnlyBeReferencedBy(arch.Project("App.Data"))),
            ("in a throw allow-list operand",
                arch => arch.Namespace("App.Web.*").MustOnlyThrow(arch.Project("App.Data"))),
            ("as the subject of an outbound allow-list",
                arch => arch.Project("App.Data").MustOnlyReference(arch.Namespace("App.Domain.*"))),
            ("as the subject of an inbound allow-list",
                arch => arch.Project("App.Domain").MustOnlyBeReferencedBy(arch.Namespace("App.Web.*")))
        ];

        // Act + Assert
        ShouldAllClassifyAs(exempting, null, "Project noun(s) exempting at one edge end and yet not decidable");
    }

    [Fact]
    public void Classify_TheLeafAllowListOverAProjectSubject_IsDecidable()
    {
        // The leaf verbs carry no operands at all, so the subject IS the allow-set and there is nowhere
        // else for the exemption to come from. Both are decidable for the reason the operand forms are:
        // the subject owns an instance only at a project it names, which is a project whose own build is
        // the one that sees that instance.
        OneProjectVerdict outbound = Enforcing(arch => arch.Project("App.Data").MustOnlyReferenceItself());
        OneProjectVerdict inbound = Enforcing(arch => arch.Project("App.Data")
            .MustOnlyBeReferencedByItself());

        outbound.Reason.ShouldBeNull();
        inbound.Reason.ShouldBeNull();
    }

    [Fact]
    public void Classify_ALayerAProjectDefines_IsReadWhereTheLayerStands()
    {
        // Arrange — a layer is transparent to its definition, so the definition answers wherever the layer
        // is named and the layer's four positions are the bare project noun's four. This is the unit twin
        // of the linked-file bed's rows.
        (string Where, string? Expected, Func<Arch, Constraint> Rule)[] positions =
        [
            ("as the subject of an outbound ban", null,
                arch => Data(arch).MustNotReference(arch.Namespace("App.Web.*"))),
            ("as a ban's operand", null, arch => arch.Types.MustNotReference(Data(arch))),
            ("as an allow-list operand", null, arch => arch.Types.MustOnlyReference(Data(arch))),
            ("in an Except payload of the subject", Declarer,
                arch => arch.Namespace("App.Web.*").Except(Data(arch)).MustBeSealed())
        ];

        // Act
        List<(string Where, string? Expected, string? Reason)> read = positions
            .Select(position => (position.Where, position.Expected, Enforcing(position.Rule).Reason))
            .ToList();

        // Assert
        read.Where(position => position.Reason != position.Expected)
            .Select(position =>
                $"{position.Where}: expected {Describe(position.Expected)}, got {Describe(position.Reason)}")
            .ToList()
            .ShouldReportNothing("Position(s) where a layer a project defines classified against its position");
    }

    [Fact]
    public void Classify_AnExceptPayloadInsideAnAllowListOperand_IsDecidable()
    {
        // It reads backwards at a glance and it is right, which is why it has a fact of its own. An
        // allow-list operand spares, so subtracting from it bans: a subtraction this compilation
        // under-computes leaves the allow-set LARGER than the whole solution's, and a larger allow-set
        // reports less. The same payload inside a ban's operand shrinks what is forbidden, which is an
        // exemption, and goes out. Do not "make these consistent".
        OneProjectVerdict fromAnAllowList = Enforcing(arch => arch.Types
            .MustOnlyReference(arch.Namespace("App.Domain.*").Except(arch.Project("App.Data"))));
        OneProjectVerdict fromABan = Enforcing(arch => arch.Types
            .MustNotReference(arch.Namespace("App.Domain.*").Except(arch.Project("App.Data"))));

        fromAnAllowList.Reason.ShouldBeNull();
        fromABan.Reason.ShouldBe(Declarer);
    }

    private static Layer Data(Arch arch)
    {
        return arch.Layer("Data", arch.Project("App.Data"));
    }

    [Fact]
    public void Classify_TheCaptiveDependencyVerbOverPlainTargets_IsDecidable()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.InNamespace("App.Domain.*")
            .MustNotInject(arch.Types.WithSuffix("Context")));

        verdict.Reason.ShouldBeNull();
    }

    [Fact]
    public void Classify_TheCaptiveDependencyVerbOverRegisteredTargets_ReportsTheRegistrationReason()
    {
        // The verb itself is in: it goes out only through the registration noun the walk meets in what the
        // rule reads.
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.InNamespace("App.Domain.*")
            .MustNotInject(arch.Registered(Lifetime.Scoped)));

        verdict.Reason.ShouldBe(Registered);
    }

    [Fact]
    public void Classify_ARegisteredSubject_ReportsTheRegistrationReason()
    {
        OneProjectVerdict verdict = Enforcing(arch => arch.Registered().MustBeSealed());

        verdict.Reason.ShouldBe(Registered);
    }

    [Fact]
    public void Classify_AVerbThatIsOutOverASubjectHoldingAWhere_ReportsTheVerbsReason()
    {
        // Both would answer, and the walk reads the verb first, so the reason is reproducible rather than
        // whichever node happened to be reached.
        OneProjectVerdict verdict = Enforcing(arch => arch.Types.Where(_ => true, description: "that qualify")
            .MustBeRegistered());

        verdict.Reason.ShouldBe(Registered);
    }

    [Fact]
    public void Classify_AMemberWhereOverALayerAProjectDefines_ReportsTheCodeReason()
    {
        // The walk passes the project noun that defines the subject's layer, which is decidable where it stands,
        // and goes out one node further along, at the Where the member subject carries.
        OneProjectVerdict verdict = Enforcing(arch => Data(arch).Methods.Where(_ => true, description: "that qualify")
            .MustHaveSuffix("Async"));

        verdict.Reason.ShouldBe(Code);
    }

    [Fact]
    public void Classify_ARepresentativeRuleOfEachDecidableFamily_IsDecidable()
    {
        // Arrange — one rule per shape the classifier is meant to let through, so a row that quietly went
        // out reds naming the family rather than a rule id.
        (string Family, Func<Arch, Constraint> Rule)[] families =
        [
            ("a reference ban", arch => arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*"))),
            ("an inbound rule", arch => arch.Namespace("App.Domain.*")
                .MustOnlyBeReferencedBy(arch.Namespace("App.Web.*"))),
            ("a convention on types", arch => arch.Types.Implementing(typeof(IDisposable)).MustHaveSuffix("Handle")),
            ("a member convention with a Returning anchor list",
                arch => arch.Types.Methods.Returning(typeof(Task), typeof(Task<>)).MustHaveSuffix("Async")),
            ("MemberMustAcceptParameter", arch => arch.Types.Methods.WithSuffix("Async")
                .MustAcceptParameter<CancellationToken>()),
            ("a member convention narrowed by accessibility and name",
                arch => arch.Types.Fields.ThatArePrivate().Named("_state", "_cache").MustBeProtected()),
            ("an overridability law with a member Except over a plain payload",
                arch => arch.Namespace("App.Web.*").Methods.ThatArePublic()
                    .Except(arch.Types.Named("Program").Methods.Named("Main"))
                    .MustBeOverridable()),
            ("MustOnlyReferenceItself on a plain subject",
                arch => arch.Namespace("App.Domain.*").MustOnlyReferenceItself()),
            ("MustOnlyBeReferencedByItself on a plain subject",
                arch => arch.Namespace("App.Domain.*").MustOnlyBeReferencedByItself())
        ];

        // Act + Assert
        ShouldAllClassifyAs(families, null, "Rule(s) that should be decidable from one project but are not");
    }

    /// <summary>
    ///     A rule read back from a model file classifies exactly as the rule it was written from, over every
    ///     spec the model-file corpus carries — this repository's own and the canonical sample included.
    /// </summary>
    /// <remarks>
    ///     The classifier reads node classes and the reader mints its own nodes, so nothing holds the two
    ///     together but this. The two counts below are the anti-vacuity half: a corpus that carried no rules,
    ///     or a classifier that answered the same thing everywhere, would otherwise pass.
    /// </remarks>
    [Fact]
    public void Classify_TheCorpusReadBackFromAModelFile_AgreesWithTheLiveModel()
    {
        // Arrange
        var drift = new List<string>();
        var decidable = 0;
        var undecidable = 0;

        // Act
        foreach (ModelFileCorpus.CorpusSpec spec in ModelFileCorpus.All)
        {
            ModelDocument written = ModelFileCorpus.DocumentOf(spec);
            Dictionary<string, string?> live = written.Rules.ToDictionary(
                rule => rule.Id, rule => OneProjectDecidability.Classify(rule).Reason, StringComparer.Ordinal);
            ModelDocument parsed = ModelDocumentComposer.Compose(written).ShouldReadBack(written.Spec);

            foreach (ArchRule rule in parsed.Rules)
            {
                string? read = OneProjectDecidability.Classify(rule).Reason;
                if (read is null) decidable++;
                else undecidable++;

                if (!live.TryGetValue(rule.Id, out string? wrote))
                    drift.Add($"{spec.Name} {rule.Id}: carried by the file but not by the model");
                else if (!string.Equals(read, wrote, StringComparison.Ordinal))
                    drift.Add($"{spec.Name} {rule.Id}: live {Describe(wrote)}, parsed {Describe(read)}");
            }
        }

        // Assert
        decidable.ShouldBeGreaterThan(20, "carried rules the corpus classifies as decidable");
        undecidable.ShouldBeGreaterThan(5, "carried rules the corpus leaves to the whole solution");
        drift.ShouldReportNothing("Rule(s) whose one-project verdict moved when the model went through a file");
    }

    private static string Describe(string? reason)
    {
        return reason ?? "decidable";
    }

    /// <summary>Asserts every row's rule classifies as <paramref name="expected" />, null meaning decidable.</summary>
    private static void ShouldAllClassifyAs(
        (string Label, Func<Arch, Constraint> Rule)[] rows, string? expected, string header)
    {
        rows.Select(row => (row.Label, Enforcing(row.Rule).Reason))
            .Where(row => row.Reason != expected)
            .Select(row => $"{row.Label}: {Describe(row.Reason)}")
            .ToList()
            .ShouldReportNothing(header);
    }

    private static OneProjectVerdict Enforcing(Func<Arch, Constraint> constraint)
    {
        return Classify(arch => arch.Rule("area/rule")
            .Enforce(constraint(arch))
            .Because("b"));
    }

    private static OneProjectVerdict Classify(Action<Arch> define)
    {
        return OneProjectDecidability.Classify(Checker.Model(define)
            .Rules.Single());
    }

    [Fact]
    public void Classify_AUnionWithOnePartThatNeedsTheSolution_TakesThatPartsReason()
    {
        // The minimum over everything the rule reads: one operand of a union is enough to take the rule out.
        OneProjectVerdict verdict = Enforcing(arch => arch.AnyOf(arch.Namespace("App.Domain.*"), arch.Registered())
            .MustBeSealed());

        verdict.Reason.ShouldBe(Registered);
    }

    [Fact]
    public void Classify_ANodeClassWithNoRow_ThrowsRatherThanAnsweringDecidable()
    {
        // The dangerous answer for a node nobody has thought about is "decidable", so there is no default
        // row. A hand-built model is the only way to hold such a node: spec validation never sees it.
        var arch = new Arch();
        var selection = new RefinedSelection(arch, new UnclassifiedNoun(), []);
        ArchRule rule = Checker.HandBuilt("area/rule", selection.MustBeSealed())
            .Rules.Single();

        var thrown = Should.Throw<InvalidOperationException>(() => OneProjectDecidability.Classify(rule));

        thrown.Message.ShouldBe("Unclassified model node 'UnclassifiedNoun'.");
    }

    private sealed class UnclassifiedNoun : SelectionNoun
    {
        internal override string Locative => string.Empty;
    }
}
