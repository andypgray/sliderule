using Microsoft.CodeAnalysis;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The sentence each of the sixteen verdicts reads as at the literal: one spec statement, one warning,
///     one expected message written out whole.
/// </summary>
/// <remarks>
///     <para>
///         The wording belongs to the contract rather than to the analyzer, which carries <c>{0}</c> as its
///         whole message format and hands the sentence through. This is the one place it is written down,
///         so every expected message here is typed out rather than composed from the owners that produce
///         it: a golden derived from its subject agrees with whatever that subject says today.
///     </para>
///     <para>
///         One statement per row, so the table can be read straight through. Three of the sixteen need two
///         declarations rather than one, because a collision is what two literals decide between them.
///     </para>
/// </remarks>
public sealed class SpecLiteralSentenceTests
{
    /// <summary>Each warning's ID, the statement that draws it, and the sentence it reads as.</summary>
    public static TheoryData<string, string, string> Sentences =>
        new()
        {
            {
                "SR1000",
                """arch.Rule("area/twice"); arch.Rule("area/twice");""",
                "Duplicate rule ID 'area/twice'."
            },
            {
                "SR1001",
                """arch.Scope("legacy/billing"); arch.Rule("legacy/billing/extra");""",
                "Rule ID 'legacy/billing/extra' extends scope 'legacy/billing'; the 'legacy/billing/…' "
                + "namespace is reserved for its generated children."
            },
            {
                "SR1005",
                """arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("");""",
                "Blank Because."
            },
            {
                "SR1006",
                """arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Because("one\ntwo");""",
                "Multi-line Because; prose fields are single-line."
            },
            {
                "SR1008",
                """arch.Rule("Bad_Id");""",
                "Malformed ID 'Bad_Id'; expected `area/rule-name` matching ^[a-z0-9-]+(/[a-z0-9-]+)*$."
            },
            {
                "SR1010",
                """arch.Layer("Web", "Web.*"); arch.Layer("Web", "Api.*");""",
                "Duplicate layer name 'Web'."
            },
            {
                "SR1012",
                """arch.Member(typeof(string), "");""",
                "Blank member name."
            },
            {
                "SR1016",
                """arch.Types.MustHaveSuffix("");""",
                "Blank suffix."
            },
            {
                "SR1017",
                """arch.Namespace("MyApp.*.Controllers.*");""",
                "The namespace pattern 'MyApp.*.Controllers.*' has a trailing `.*` subtree operator but its "
                + "literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix."
            },
            {
                "SR1024",
                """arch.Projects.Named("");""",
                "Blank project name."
            },
            {
                "SR1025",
                """arch.Projects.Named("A").MustOnlyTarget("");""",
                "Blank target framework."
            },
            {
                "SR1026",
                """arch.Types.MustHaveExactlyOneCounterpart(arch.Types, "IService");""",
                "The counterpart name template 'IService' contains no '{Name}' placeholder, so every subject "
                + "derives the same fixed name — a cardinality claim, not a correspondence; use a template "
                + "such as 'I{Name}' (substitution is case-sensitive)."
            },
            {
                "SR1030",
                """arch.Rule("area/rule").Enforce(arch.Types.MustHavePrefix("I")).Citation("ADR 7");""",
                "Malformed citation: 'ADR 7' is not an absolute http(s) URL."
            },
            {
                "SR1031",
                """arch.Layer("", "MyApp.Core.*");""",
                "Blank layer name; name the layer in the first argument to arch.Layer(...)."
            },
            {
                "SR1032",
                """arch.Projects.Named("A").MustNotReferencePackages("");""",
                "Blank package pattern."
            },
            {
                "SR1033",
                """arch.Projects.Named("A").MustSetProperty("Nullable", " ");""",
                "Blank property value."
            },
            {
                "SR1035",
                """arch.Types.Under("src\\Widgets");""",
                "The path glob 'src\\Widgets' contains a backslash; separate segments with `/`."
            }
        };

    [Theory]
    [MemberData(nameof(Sentences))]
    public async Task EachVerdict_ReadsAtTheLiteralAsThisSentence(string id, string statement, string sentence)
    {
        IReadOnlyList<Diagnostic> reported =
            await SpecSnippet.ShouldCompileAndDiagnoseAsync(SpecSnippet.Around(statement), cancellationToken: Ct);

        reported.ShouldBeTheOnly(id)
            .ShouldSay(sentence);
    }
}
