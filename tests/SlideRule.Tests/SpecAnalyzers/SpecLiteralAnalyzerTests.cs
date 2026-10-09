using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using SlideRule.SpecAnalyzers;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The spec-literal analyzer over one literal at a time: which value in which slot draws which
///     warning, where the warning sits, and the cases in which it says nothing at all.
/// </summary>
/// <remarks>
///     <para>
///         Every fact pins the span as well as the ID, because the span is the feature. The verdict itself
///         already exists at load time; what this analyzer adds is a position, and a warning over the
///         whole call rather than over the operand it is about would be the position of the thing the
///         author did not get wrong.
///     </para>
///     <para>
///         Silence is the subject of a good half of them, and on purpose: a warning the spec loader would
///         not give is the one defect this analyzer can have. Each silent fact names a value that a walk
///         over the argument's operations would have found a literal inside, and says nothing.
///     </para>
///     <para>
///         Four of those silent facts are about the same mistake, which is worth naming because it is the
///         one that is easy to make twice: an argument that is array-typed is not thereby an argument that
///         carries literals. A helper returning an array, a method called on a constant receiver and a
///         conditional choosing between two arrays each compute their value, and an array field holds one
///         its initializer computed. The strings written inside any of them are the caller's workings
///         rather than anything the slot receives.
///     </para>
/// </remarks>
public sealed class SpecLiteralAnalyzerTests
{
    [Fact]
    public async Task AnExpandedParamsCall_IsReportedAtTheElementThatIsBlank()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", "B", "", "C");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 36, "\"\"");
        blank.ShouldSay("Blank type name.");
        blank.ShouldCarry("code=BlankPattern", "head=Blank type name", "slot=ExactTypeName", "tail=.");
    }

    [Fact]
    public async Task AnExplicitArrayArgument_IsReportedAtTheElementThatIsBlank()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", new[] { "B", "" });
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 44, "\"\"");
        blank.ShouldSay("Blank type name.");
    }

    [Fact]
    public async Task ACollectionExpressionArgument_IsReportedAtTheElementThatIsBlank()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", ["B", ""]);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 37, "\"\"");
        blank.ShouldSay("Blank type name.");
    }

    [Fact]
    public async Task ACollectionExpressionWithASpread_ReportsItsOwnElementAndNothingFromWhatItSpreads()
    {
        // Both blanks are written in the source and only the second is an element of this collection.
        // What the spread contributes is another collection's elements, so a walk into it would report a
        // string that reaches the slot only through whatever that collection turns out to hold.
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", [.. new[] { "" }, ""]);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 49, "\"\"");
        blank.ShouldSay("Blank type name.");
    }

    [Fact]
    public async Task AHelperReturningAnArray_IsSilentEvenThoughItsOwnArgumentIsALiteral()
    {
        // The blank is written at the helper's parameter, which carries no slot. Reading it here would
        // report a string the type selection never receives.
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", GetNames(""));
                }

                private static string[] GetNames(string seed)
                {
                    return new[] { seed };
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task AMethodCalledOnAConstantReceiver_IsSilentAboutTheReceiver()
    {
        // The receiver is a string constant sitting directly under an array-typed invocation. The second
        // line is the one that bites: a blank receiver is exactly what the slot reports, so reading it
        // would warn about a value the call splits rather than passes.
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Named("A", "Order, Invoice".Split(','));
                    arch.Types.Named("A", " ".Split(','));
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task AConditionalChoosingBetweenTwoArrays_IsSilentAboutEither()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                private static readonly string[] Fallback = new[] { "Order" };

                public void Define(Arch arch, bool flag)
                {
                    arch.Types.Named("A", flag ? new[] { "" } : Fallback);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public void TheCollectionExpressionKind_MatchedByNumber_IsTheOneTheCompilerNames()
    {
        // The analyzer is built against a compiler API with no member for this kind and matches the
        // number instead. This test project compiles against one that has the member, which is the only
        // place the two can be held to each other.
        ((int)OperationKind.CollectionExpression)
            .ShouldBe(SpecLiteralAnalyzer.CollectionExpressionKind);
    }

    [Fact]
    public async Task AGlobBuiltFromTwoHalves_IsOneWarningOverTheWholeExpression()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Namespace("MyApp.*" + ".Controllers.*");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic dead = reported.ShouldBeTheOnly("SR1017");
        dead.ShouldSitOn(line: 7, column: 24, "\"MyApp.*\" + \".Controllers.*\"");
        dead.ShouldSay(
            "The namespace pattern 'MyApp.*.Controllers.*' has a trailing `.*` subtree operator but its "
            + "literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public async Task ABlankBuiltFromTwoHalves_IsOneWarningRatherThanOnePerHalf()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Namespace("" + "");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 24, "\"\" + \"\"");
        blank.ShouldSay("Blank namespace pattern.");
    }

    [Fact]
    public async Task AnInterpolatedStringWithAHole_IsSilentRatherThanReportedOnItsLiteralParts()
    {
        // The interpolation's text part is a single space, which is exactly the value the prose slot
        // reports as blank. A walk that took the literals out of the interpolation would warn here.
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch, string reason)
                {
                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because($"{reason} ");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task ANameofAsAMemberName_IsTheConstantItFoldsToAndDrawsNothing()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Member(typeof(string), nameof(string.Length));
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task ANullArgument_IsAConstantThatIsNotAStringAndIsSkipped()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Namespace(null!);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task AConstantFieldHoldingABlank_IsReportedWhereItIsUsed()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                private const string Anywhere = "";

                public void Define(Arch arch)
                {
                    arch.Namespace(Anywhere);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 9, column: 24, "Anywhere");
        blank.ShouldSay("Blank namespace pattern.");
    }

    [Fact]
    public async Task AnArrayFieldPassedToAParamsSlot_IsSilentBecauseItsElementsAreNotConstants()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                private static readonly string[] Names = { "Order", "" };

                public void Define(Arch arch)
                {
                    arch.Types.Named("Order", Names);
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task NamedArgumentsWrittenOutOfOrder_BindToTheSlotEachParameterCarries()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Layer(glob: " ", name: "");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1016", "SR1031");

        Diagnostic glob = reported.ShouldHaveSingle("SR1016");
        glob.ShouldSitOn(line: 7, column: 26, "\" \"");
        glob.ShouldSay("Blank namespace pattern.");

        Diagnostic name = reported.ShouldHaveSingle("SR1031");
        name.ShouldSitOn(line: 7, column: 37, "\"\"");
        name.ShouldSay("Blank layer name; name the layer in the first argument to arch.Layer(...).");
    }

    [Fact]
    public async Task ASlotOnAStageInterface_IsReadFromTheInterfaceDeclaration()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because("");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1005");
        blank.ShouldSitOn(line: 9, column: 22, "\"\"");
        blank.ShouldSay("Blank Because.");
    }

    [Fact]
    public async Task ASlotOnAGenericExtension_IsReadFromTheOpenDefinition()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.Methods.WithSuffix("");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic blank = reported.ShouldBeTheOnly("SR1016");
        blank.ShouldSitOn(line: 7, column: 39, "\"\"");
        blank.ShouldSay("Blank member suffix.");
    }

    [Fact]
    public async Task ACounterpartTemplate_DrawsOneCodeWhenBlankAndAnotherWhenItHasNoPlaceholder()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Types.MustHaveExactlyOneCounterpart(arch.Types, "");
                    arch.Types.MustHaveExactlyOneCounterpart(arch.Types, "IService");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1016", "SR1026");

        Diagnostic blank = reported.ShouldHaveSingle("SR1016");
        blank.ShouldSitOn(line: 7, column: 62, "\"\"");
        blank.ShouldSay("Blank counterpart name template.");

        Diagnostic fixedName = reported.ShouldHaveSingle("SR1026");
        fixedName.ShouldSitOn(line: 8, column: 62, "\"IService\"");
        fixedName.ShouldSay(
            "The counterpart name template 'IService' contains no '{Name}' placeholder, so every subject "
            + "derives the same fixed name — a cardinality claim, not a correspondence; use a template such "
            + "as 'I{Name}' (substitution is case-sensitive).");
    }

    [Fact]
    public async Task ACitation_DrawsTheFirstOfItsThreeCodesThatApplies()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Rule("shape/one")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Citation("");

                    arch.Rule("shape/two")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Citation("one\ntwo");

                    arch.Rule("shape/three")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Citation("ADR 7");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1005", "SR1006", "SR1030");

        Diagnostic blank = reported.ShouldHaveSingle("SR1005");
        blank.ShouldSitOn(line: 9, column: 23, "\"\"");
        blank.ShouldSay("Blank Citation.");

        Diagnostic multiLine = reported.ShouldHaveSingle("SR1006");
        multiLine.ShouldSitOn(line: 13, column: 23, "\"one\\ntwo\"");
        multiLine.ShouldSay("Multi-line Citation; prose fields are single-line.");

        Diagnostic notAUrl = reported.ShouldHaveSingle("SR1030");
        notAUrl.ShouldSitOn(line: 17, column: 23, "\"ADR 7\"");
        notAUrl.ShouldSay("Malformed citation: 'ADR 7' is not an absolute http(s) URL.");
    }

    [Fact]
    public async Task ABlankProjectName_DrawsOneCodeAsANounAndAnotherOnTheProjectStratum()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Project("");
                    arch.Projects.Named("");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1016", "SR1024");

        Diagnostic asTypes = reported.ShouldHaveSingle("SR1016");
        asTypes.ShouldSitOn(line: 7, column: 22, "\"\"");
        asTypes.ShouldSay("Blank project name.");

        Diagnostic asProjects = reported.ShouldHaveSingle("SR1024");
        asProjects.ShouldSitOn(line: 8, column: 29, "\"\"");
        asProjects.ShouldSay("Blank project name.");
    }

    [Fact]
    public async Task ACompilationThatDoesNotReferenceTheContract_HasNothingRegisteredOverIt()
    {
        // The same call written against a fluent surface of the project's own. Nothing here carries the
        // marker, so nothing resolves it, and the analyzer must not fall back to reading method names.
        const string source =
            """
            public sealed class Arch
            {
                public void Namespace(string glob)
                {
                }
            }

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Namespace("");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(
            [(SpecSnippet.FilePath, source)], SpecSnippet.WithoutTheContract, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }
}
