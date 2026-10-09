using System.Globalization;
using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using SlideRule.Hosting;
using SlideRule.SpecAnalyzers;
using SlideRule.Tests.DocHygiene;
using SlideRule.Validation;
using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The spec-literal analyzer held against spec-load validation over the whole validation fixture
///     corpus: every fixture class the catalog is asserted through, walked on both sides, with the two
///     sets of verdicts paired one to one.
/// </summary>
/// <remarks>
///     <para>
///         Spec-load validation is the authority, so the claim is not that the two agree about everything.
///         It is that for each fixture the literal-decidable errors loading it reports and the warnings the
///         analyzer draws inside it are the same multiset of verdicts. A multiset rather than a sequence
///         because the two sides order their own reports differently — the validator walks a rule's verb
///         before its subject, the analyzer walks the file — and a sequence comparison would red on that
///         alone.
///     </para>
///     <para>
///         The pairing is on the <c>code</c>, <c>head</c> and <c>tail</c> a warning carries as properties,
///         never on its printed message. The two frames of one verdict differ by design: the validator
///         names what the literal was written on, and the analyzer, already pointing at the literal, leaves
///         that clause out. So a validator message pairs when it opens with the head, closes with the tail,
///         and carries <c>" on "</c> and a subject between them — or carries nothing between them, for the
///         five verdicts whose sentence names no subject because the value it would name is the one that is
///         wrong.
///     </para>
///     <para>
///         <b>Where this view can be wider than the loader's.</b> One direction is the whole defect a
///         literal analyzer can have: a warning validation would not give is a warning an author can only
///         act on by changing a spec that was right. Five shapes could produce one. None of them fires in
///         this corpus, which is what the pairing below establishes, and each is a known limit rather than
///         an oversight:
///     </para>
///     <list type="number">
///         <item>
///             <b>Overwritten registrations.</b> A posture verb called twice on a held builder replaces the
///             first selection, and validation then never walks it, while the analyzer sees both literals.
///             <c>DoubleQuarantineScopeSpec</c> comes closest: it writes one namespace glob and overwrites
///             it with another on the next line. Both are well-formed today, so nothing diverges; blank one
///             of them and the analyzer would report a glob no loaded spec contains.
///             <c>DoublePostureRuleSpec</c> and <c>MixedPostureScopeSpec</c> have the same shape.
///         </item>
///         <item>
///             <b>A foreign <c>Arch</c>.</b> Layer validation walks the spec's own layers, and the analyzer
///             cannot tell which instance a call was made on, so it would pool another instance's layer
///             names into the collision set. <c>ForeignFamilyCellSpec</c> declares a layer on a second
///             <c>Arch</c>; it is the only one there, so no collision arises. Give the spec's own layers a
///             layer of that name and the analyzer would report a duplicate across two models. The other
///             <c>Foreign</c> fixtures have the same shape for their own operands.
///         </item>
///         <item>
///             <b>The foreign-member short-circuit.</b> Member checking returns once it has reported a
///             member minted elsewhere, so a blank name on that member is never reported by validation while
///             the analyzer reports it. <c>ForeignMemberSpec</c> comes closest, and names its member with a
///             <c>nameof</c>; blank that name and the two sides part.
///         </item>
///         <item>
///             <b>An operand minted and never used.</b> Validation walks what the rules and layers reach, so
///             a selection or member assigned to a local nothing reads is never validated, while the
///             analyzer reports its literal wherever it is written. No fixture mints one it does not use.
///         </item>
///         <item>
///             <b>A call the spec never makes.</b> A declaration inside a helper nothing calls, or under a
///             condition never taken, is a declaration to the analyzer and not to the loader. The analyzer's
///             own remarks say the same thing, and the two statements have to stay in agreement.
///         </item>
///     </list>
/// </remarks>
public sealed class SpecLiteralParityTests(SpecFixtureCorpus corpus) : IClassFixture<SpecFixtureCorpus>
{
    // The fixture count stands on its own rather than being read off the walk, because the walk is what
    // would go quiet: a class the syntax pass stopped recognising would simply not be measured, and parity
    // over fewer fixtures reads exactly like parity over all of them.
    private const int FixtureCount = 144;

    private const string SubjectClause = " on ";

    /// <summary>The verdicts one literal, or the literals of one method body, can decide on their own.</summary>
    private static readonly IReadOnlySet<Code> LiteralDecidable = new HashSet<Code>
    {
        Code.DuplicateId, Code.IdExtendsScope, Code.BlankProse, Code.MultiLineProse, Code.MalformedId,
        Code.DuplicateLayerName, Code.BlankMemberName, Code.BlankPattern, Code.UnanchoredSubtreePattern,
        Code.BlankProjectPattern, Code.BlankTargetFramework, Code.CounterpartTemplateWithoutPlaceholder,
        Code.MalformedCitation, Code.BlankLayerName
    };

    /// <summary>
    ///     The five whose sentence has no subject clause, so the two frames read the same and a paired
    ///     validator message carries nothing at all between head and tail.
    /// </summary>
    private static readonly IReadOnlySet<Code> SubjectFree = new HashSet<Code>
    {
        Code.MalformedId, Code.DuplicateId, Code.IdExtendsScope, Code.DuplicateLayerName, Code.BlankLayerName
    };

    /// <summary>The fixtures the catalog asserts build clean, which both sides therefore have to pass over.</summary>
    private static readonly string[] NegativeControls =
    [
        nameof(ValidCautionSpec), nameof(ValidMemberUseSpec), nameof(ValidMemberSubjectSpec),
        nameof(InteriorWildcardSpec), nameof(ValidExpressionMemberSpec), nameof(ValidVerbMemberSpec),
        nameof(ValidAcceptParameterSpec), nameof(ValidHierarchyAnchorsSpec),
        nameof(ValidMemberAttributeAnchorsSpec), nameof(NonsenseStringAttributeAnchorSpec),
        nameof(NonsenseStringHierarchyAnchorSpec), nameof(NonsenseStringMemberTypeAnchorSpec),
        nameof(ValidProjectNamesSpec)
    ];

    [Fact]
    public void TheCorpus_HoldsEveryConcreteSpecTheFixtureFileDeclares()
    {
        // Act
        List<string> unresolved = corpus.Unresolved.ToList();

        // Assert
        unresolved.ShouldReportNothing(
            "Class(es) declared in the fixture file as spec fixtures resolve to no spec type in this "
            + "assembly, so parity would be measured over fewer fixtures than the file holds");
        corpus.Classes.Count.ShouldBe(FixtureCount,
            $"The fixture file declares {corpus.Classes.Count} spec classes against the {FixtureCount} it "
            + "is known to hold. A fixture added or removed brings this number with it.");
    }

    [Fact]
    public void EveryWarning_SitsInsideTheFixtureClassItIsAbout()
    {
        // Act
        List<string> stray = corpus.Stray()
            .Select(Describe)
            .ToList();

        // Assert: an unattributable warning takes no part in the pairing, so a difference it stands for
        // would be dropped rather than reported.
        stray.ShouldReportNothing(
            "Warning(s) fall inside no fixture class, so nothing below compares them against a validator "
            + "verdict");
    }

    [Fact]
    public void TheAnalyzer_NeverFailedOverTheCorpus()
    {
        // Act
        List<string> faults = corpus.Reported
            .Where(diagnostic => diagnostic.Id == SpecLiteralDiagnostics.AnalyzerFault.Id)
            .Select(Describe)
            .ToList();

        // Assert: a fault means the analyzer stopped somewhere, so every silence after it is silence about
        // a literal it never read.
        faults.ShouldReportNothing("The analyzer reported a fault of its own over the fixture corpus");
    }

    [Fact]
    public void EveryFixture_ReachesValidationRatherThanBeingRefusedAtTheCall()
    {
        // Act
        ParityRun run = ParityRun.Over(corpus);

        // Assert: a Define that throws something other than a validation exception was refused where it was
        // written, so it carries no verdicts at all and there is nothing for the analyzer's to pair with.
        run.Refused.ShouldReportNothing(
            "Fixture(s) threw out of Define rather than reporting validation errors, so their literals have "
            + "no validator verdict to be held against");
    }

    [Fact]
    public void EveryFixture_DrawsTheSameLiteralVerdictsFromTheAnalyzerAsFromSpecLoad()
    {
        // Act
        ParityRun run = ParityRun.Over(corpus);

        // Assert
        run.Findings.ShouldReportNothing(
            "The spec analyzer and spec-load validation disagree about the fixture corpus. A warning with "
            + "no validator verdict behind it is the defect this analyzer can have, because an author who "
            + "acts on it changes a spec that was right; a verdict with no warning is a literal the "
            + "analyzer could have reported where it was written and did not");
    }

    [Fact]
    public void EveryLiteralDecidableCode_IsPairedSomewhereInTheCorpus()
    {
        // Act
        ParityRun run = ParityRun.Over(corpus);
        List<string> unexercised = LiteralDecidable
            .Select(code => code.ToString())
            .Where(code => !run.PairedCodes.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert: parity proves nothing about a code no fixture draws, and the whole set passing would
        // read as if it had.
        unexercised.ShouldReportNothing(
            "Literal-decidable code(s) are drawn by no fixture, so the pairing above says nothing about "
            + "them; write a fixture that draws each one");
    }

    [Fact]
    public void TheNegativeControls_AreSilentOnBothSides()
    {
        // Arrange
        var findings = new List<string>();

        // Act
        foreach (string name in NegativeControls)
        {
            SpecFixtureCorpus.FixtureClass? fixture =
                corpus.Classes.SingleOrDefault(candidate => candidate.Name == name);
            if (fixture is null)
            {
                findings.Add($"{name}: the fixture file declares no such spec class.");
                continue;
            }

            findings.AddRange(LoadErrors(fixture.Type)
                .Select(error => $"{name}: spec load reported {error.Code} \"{error.Message}\"."));
            findings.AddRange(corpus.WarningsIn(fixture)
                .Select(warning => $"{name}: the analyzer reported {warning.Id} \"{Sentence(warning)}\"."));
        }

        // Assert
        findings.ShouldReportNothing(
            "Fixture(s) the catalog holds to building clean drew something. A negative control is what "
            + "makes the rest of the corpus mean anything: without it a walk that reported everything and a "
            + "walk that reported the right things would look alike");
    }

    [Fact]
    public void TheCrossClassDuplicateId_IsSpecLoadsAloneBecauseTheAnalyzerComparesOneMethodBody()
    {
        // Arrange: the one duplicate in the corpus that no single method body carries.
        // Act
        var loaded = Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(new DuplicateIdSpecA(), new DuplicateIdSpecB()));

        // Assert
        loaded.ShouldHaveError(Code.DuplicateId, "area/rule");
        corpus.WarningsIn(nameof(DuplicateIdSpecA))
            .ShouldBeSilent();
        corpus.WarningsIn(nameof(DuplicateIdSpecB))
            .ShouldBeSilent();
    }

    // Whether a validator message pairs with a warning: the same code, and the same sentence once the
    // location prefix and the subject clause are set aside. The two are independent — a malformed ID
    // carries a prefix and no subject — so neither is inferred from the other.
    private static bool Pairs(SpecValidationError error, Diagnostic warning)
    {
        if (warning.Property("code") != error.Code.ToString()) return false;
        if (warning.Property("head") is not { } head) return false;
        if (warning.Property("tail") is not { } tail) return false;

        string body = Body(error);
        if (body.Length < head.Length + tail.Length) return false;
        if (!body.StartsWith(head, StringComparison.Ordinal)) return false;
        if (!body.EndsWith(tail, StringComparison.Ordinal)) return false;

        string between = body.Substring(head.Length, body.Length - head.Length - tail.Length);
        if (SubjectFree.Contains(error.Code)) return between.Length == 0;

        return between.StartsWith(SubjectClause, StringComparison.Ordinal)
               && between.Length > SubjectClause.Length;
    }

    // The message with its file:line prefix taken off, which is the sentence the two sides share. Taken by
    // the location the error carries rather than by looking for a colon, because several of these messages
    // contain one of their own.
    private static string Body(SpecValidationError error)
    {
        if (error.Location is null) return error.Message;

        var prefix = $"{error.Location}: ";

        return error.Message.StartsWith(prefix, StringComparison.Ordinal)
            ? error.Message[prefix.Length..]
            : error.Message;
    }

    private static string Sentence(Diagnostic warning)
    {
        return warning.GetMessage(CultureInfo.InvariantCulture);
    }

    private static string Describe(Diagnostic warning)
    {
        FileLinePositionSpan span = warning.Location.GetLineSpan();
        string place = string.IsNullOrEmpty(span.Path)
            ? "(no location)"
            : $"{span.Path}:{span.StartLinePosition.Line + 1}";

        return $"{warning.Id} at {place}: {Sentence(warning)}";
    }

    // What loading one spec on its own reports: nothing at all when it builds. Anything thrown that is not a
    // validation exception is the caller's to record.
    private static IReadOnlyList<SpecValidationError> LoadErrors(Type specType)
    {
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        try
        {
            ArchModelBuilder.Build(spec);

            return [];
        }
        catch (SpecValidationException exception)
        {
            return exception.Errors;
        }
    }

    // Both sides of the whole corpus walked once: what failed to pair, which codes did pair, and which
    // fixtures never reached validation at all. Held together rather than recomputed per arm, so every arm
    // is reading one answer about one corpus.
    private sealed class ParityRun
    {
        private ParityRun(IReadOnlyList<string> findings, IReadOnlySet<string> pairedCodes, IReadOnlyList<string> refused)
        {
            Findings = findings;
            PairedCodes = pairedCodes;
            Refused = refused;
        }

        internal IReadOnlyList<string> Findings { get; }

        internal IReadOnlySet<string> PairedCodes { get; }

        internal IReadOnlyList<string> Refused { get; }

        internal static ParityRun Over(SpecFixtureCorpus corpus)
        {
            var findings = new List<string>();
            var paired = new SortedSet<string>(StringComparer.Ordinal);
            var refused = new List<string>();

            foreach (SpecFixtureCorpus.FixtureClass fixture in corpus.Classes)
            {
                List<SpecValidationError> verdicts;
                try
                {
                    verdicts = LoadErrors(fixture.Type)
                        .Where(error => LiteralDecidable.Contains(error.Code))
                        .ToList();
                }
                catch (Exception exception)
                {
                    refused.Add($"{fixture.Name}: Define threw {exception.GetType().Name} "
                                + $"(\"{exception.Message}\") rather than reaching validation.");
                    continue;
                }

                Pair(fixture, verdicts, corpus.WarningsIn(fixture), findings, paired);
            }

            return new ParityRun(findings, paired, refused);
        }

        // One fixture's two sides, matched greedily. Greedy is exact here because two verdicts pair with one
        // warning only when they are the same verdict, in which case either match leaves the same remainder.
        private static void Pair(
            SpecFixtureCorpus.FixtureClass fixture, IReadOnlyList<SpecValidationError> verdicts,
            IReadOnlyList<Diagnostic> warnings, List<string> findings, ISet<string> paired)
        {
            List<Diagnostic> unmatched = warnings.ToList();

            foreach (SpecValidationError verdict in verdicts)
            {
                int match = unmatched.FindIndex(warning => Pairs(verdict, warning));
                if (match < 0)
                {
                    IEnumerable<string> sameCode = unmatched
                        .Where(warning => warning.Property("code") == verdict.Code.ToString())
                        .Select(Sentence);
                    findings.Add($"{fixture.Name}: spec load reported {verdict.Code} \"{Body(verdict)}\", and "
                                 + "no warning inside the class pairs with it"
                                 + Alongside(sameCode, "the analyzer said") + ".");
                    continue;
                }

                paired.Add(verdict.Code.ToString());
                unmatched.RemoveAt(match);
            }

            foreach (Diagnostic left in unmatched)
            {
                IEnumerable<string> sameCode = verdicts
                    .Where(verdict => verdict.Code.ToString() == left.Property("code"))
                    .Select(Body);
                findings.Add($"{fixture.Name}: the analyzer reported {left.Id} \"{Sentence(left)}\", and no "
                             + "verdict from spec load pairs with it"
                             + Alongside(sameCode, "spec load said") + ".");
            }
        }

        // The other side's texts under the same code, so a red shows the near miss rather than only the half
        // that went missing.
        private static string Alongside(IEnumerable<string> texts, string lead)
        {
            List<string> quoted = texts
                .Select(text => $"\"{text}\"")
                .ToList();

            return quoted.Count == 0 ? string.Empty : $"; {lead} {string.Join(", ", quoted)}";
        }
    }
}
