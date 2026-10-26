using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The members the compiler-hosted leaves and the xUnit adapter each carry a copy of, held token for token
///     against the copy they must agree with.
/// </summary>
/// <remarks>
///     Each leaf ships alone beside its own dependencies, and the code copied here runs where Core may be a
///     foreign version, so no assembly can hold one copy for all of them. What is compared is the member's
///     tokens without its modifiers and attributes: a copy may differ in those, in documentation, comments and
///     layout, and in nothing else.
/// </remarks>
public sealed class CopiedMemberParityTests
{
    private const string BuildTierAnalyzer = "src/SlideRule.Analyzers/BuildTierAnalyzer.cs";
    private const string BuildTierDiagnostics = "src/SlideRule.Analyzers/BuildTierDiagnostics.cs";
    private const string SpecLiteralAnalyzer = "src/SlideRule.SpecAnalyzers/SpecLiteralAnalyzer.cs";
    private const string SpecLiteralDiagnostics = "src/SlideRule.SpecAnalyzers/SpecLiteralDiagnostics.cs";
    private const string ContractVersion = "src/SlideRule.Xunit/ContractVersion.cs";
    private const string SlotBinding = "src/SlideRule.SpecCompletion/SlotBinding.cs";

    [Theory]
    [InlineData(BuildTierAnalyzer, SpecLiteralAnalyzer, "InformationalVersionOf")]
    [InlineData(BuildTierAnalyzer, SpecLiteralAnalyzer, "ReleasedVersion")]
    [InlineData(BuildTierAnalyzer, ContractVersion, "InformationalVersionOf")]
    [InlineData(BuildTierAnalyzer, ContractVersion, "ReleasedVersion")]
    [InlineData(BuildTierDiagnostics, SpecLiteralDiagnostics, "Opening")]
    [InlineData(SpecLiteralAnalyzer, SlotBinding, "SlotNameOn")]
    [InlineData(SpecLiteralAnalyzer, SlotBinding, "CarriesValuesThrough")]
    [InlineData(SpecLiteralAnalyzer, SlotBinding, "CollectionExpressionKind")]
    public void TheCopy_IsTheOriginalTokenForToken(string originalFile, string copyFile, string member)
    {
        string original = ShouldDeclareMember(originalFile, member);
        string copy = ShouldDeclareMember(copyFile, member);

        copy.ShouldBe(
            original,
            customMessage: $"{member} in {copyFile} must be the one in {originalFile} token for token.");
    }

    /// <summary>
    ///     The tokens of the one method or field named <paramref name="member" /> in the type the file is named
    ///     after, modifiers and attributes left out, failing with the type's member names when there is not
    ///     exactly one.
    /// </summary>
    private static string ShouldDeclareMember(string repoRelativePath, string member)
    {
        string type = Path.GetFileNameWithoutExtension(repoRelativePath);
        SyntaxNode root = CSharpSyntaxTree.ParseText(RepoRoot.ReadText(repoRelativePath)).GetRoot();
        TypeDeclarationSyntax declaration = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(candidate => candidate.Identifier.Text == type)
            .ShouldHaveSingleItem(customMessage: $"{repoRelativePath} should declare one type named {type}.");

        string declared = string.Join(", ", declaration.Members.Select(NameOf).OfType<string>());
        MemberDeclarationSyntax found = declaration.Members
            .Where(candidate => NameOf(candidate) == member)
            .ShouldHaveSingleItem(
                customMessage: $"{type} in {repoRelativePath} should declare one method or field named " +
                               $"{member}; it declares {declared}.");

        MemberDeclarationSyntax stripped = found switch
        {
            MethodDeclarationSyntax method => method.WithModifiers(default).WithAttributeLists(default),
            FieldDeclarationSyntax field => field.WithModifiers(default).WithAttributeLists(default),
            _ => found
        };

        return string.Join(" ", stripped.DescendantTokens().Select(static token => token.Text));
    }

    private static string? NameOf(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.Count == 1
                ? field.Declaration.Variables[0].Identifier.Text
                : null,
            _ => null
        };
    }
}
