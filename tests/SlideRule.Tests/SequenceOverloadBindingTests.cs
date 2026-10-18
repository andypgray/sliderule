using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using SlideRule.Tests.SpecAnalyzers;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Pins which overload a list argument binds on the seven calls that carry a sequence form beside their
///     <c>(first, more)</c> form: a single operand stays on the params form, an array or a list binds the
///     sequence form, a lambda keeps its expression form, and a covariant list needs no cast.
/// </summary>
/// <remarks>
///     Every row compiles twice: at C# 14 with nullable annotations on, as this repository's spec projects
///     build, and at C# 7.3 with them off, as a net48 spec project builds by default. Overload resolution
///     differs between the two in exactly the places a new overload can move a call, so a row that held at
///     one version alone would be the half of the claim nobody checked.
/// </remarks>
public sealed class SequenceOverloadBindingTests
{
    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static TheoryData<LanguageVersion> BothVersions => [LanguageVersion.CSharp14, LanguageVersion.CSharp7_3];

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void OneProjectName_StaysOnTheParamsForm(LanguageVersion version)
    {
        BoundOverload(version, "Named", "arch.Projects.Named(\"A\");")
            .ShouldBe("ProjectSelectionAdjectives.Named(ProjectSelection, string, params string[])");
    }

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void OneMemberName_StaysOnTheParamsForm(LanguageVersion version)
    {
        BoundOverload(version, "Named", "arch.Types.Methods.Named(\"Main\");")
            .ShouldBe("MemberSelectionAdjectives.Named(TSelf, string, params string[])");
    }

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void ALayerArray_BindsTheSelectionSequenceForm(LanguageVersion version)
    {
        BoundOverload(
                version, "AnyOf",
                "Layer[] layers = { arch.Layer(\"A\", \"MyApp.A.*\"), arch.Layer(\"B\", \"MyApp.B.*\") };",
                "arch.AnyOf(layers);")
            .ShouldBe("Arch.AnyOf(IEnumerable<Selection>)");
    }

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void AMemberArray_BindsTheMemberSequenceForm(LanguageVersion version)
    {
        BoundOverload(
                version, "MustNotUse",
                "Member[] members = { arch.Member(typeof(DateTime), \"Now\") };",
                "arch.Types.MustNotUse(members);")
            .ShouldBe("SelectionConstraints.MustNotUse(Selection, IEnumerable<Member>)");
    }

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void AMemberLambda_KeepsTheExpressionForm(LanguageVersion version)
    {
        BoundOverload(version, "MustNotUse", "arch.Types.MustNotUse(() => DateTime.Now);")
            .ShouldBe(
                "SelectionConstraints.MustNotUse(Selection, Expression<Func<object>>, params Expression<Func<object>>[])");
    }

    [Theory]
    [MemberData(nameof(BothVersions))]
    public void AMethodSelectionList_BindsTheMemberSelectionSequenceFormWithoutACast(LanguageVersion version)
    {
        BoundOverload(
                version, "Except",
                "List<MethodSelection> exemptions = new List<MethodSelection> { arch.Types.Named(\"Program\").Methods };",
                "arch.Types.Methods.Except(exemptions);")
            .ShouldBe("MemberSelectionAdjectives.Except(TSelf, IEnumerable<MemberSelection>)");
    }

    [Fact]
    public void AnEmptyCollectionExpression_IsAmbiguousBetweenTheTwoUnionForms()
    {
        // Both sequence forms take an empty collection, and nothing in it says which element type was meant.
        Compile(LanguageVersion.CSharp14, "arch.AnyOf([]);").Compilation
            .GetDiagnostics(Ct)
            .Select(diagnostic => diagnostic.Id)
            .ShouldContain("CS0121");
    }

    [Fact]
    public void AnEmptyCollectionExpressionOfProjectNames_BindsTheSequenceForm()
    {
        // The params form needs a string first, which a collection expression cannot be, so only one form applies.
        BoundOverload(LanguageVersion.CSharp14, "Named", "arch.Projects.Named([]);")
            .ShouldBe("ProjectSelectionAdjectives.Named(ProjectSelection, IEnumerable<string>)");
    }

    // The overload the last call to `method` in the statements bound, spelled by its declaring type, name and
    // parameter types; an extension method is spelled as declared, with its receiver as the first parameter.
    private static string BoundOverload(LanguageVersion version, string method, params string[] statements)
    {
        (CSharpCompilation compilation, SyntaxTree tree) = Compile(version, statements);
        compilation.GetDiagnostics(Ct)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();

        InvocationExpressionSyntax call = tree.GetRoot(Ct)
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Last(invocation => invocation.Expression is MemberAccessExpressionSyntax access
                                && access.Name.Identifier.Text == method);
        var bound = (IMethodSymbol)compilation.GetSemanticModel(tree)
            .GetSymbolInfo(call, Ct)
            .Symbol!;
        IMethodSymbol declared = (bound.ReducedFrom ?? bound).OriginalDefinition;

        IEnumerable<string> parameters = declared.Parameters
            .Select(parameter => (parameter.IsParams ? "params " : "") + parameter.Type.ToDisplayString(TypeFormat));
        return $"{declared.ContainingType.Name}.{declared.Name}({string.Join(", ", parameters)})";
    }

    private static (CSharpCompilation Compilation, SyntaxTree Tree) Compile(LanguageVersion version, params string[] statements)
    {
        string spec = SpecSnippet.Around(statements);
        string source = string.Join(Environment.NewLine, "using System;", "using System.Collections.Generic;", spec);

        CSharpParseOptions parseOptions = SpecSnippet.Language.WithLanguageVersion(version);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: Ct);
        NullableContextOptions nullable = version == LanguageVersion.CSharp7_3
            ? NullableContextOptions.Disable
            : NullableContextOptions.Enable;
        CSharpCompilationOptions options = SpecSnippet.Options.WithNullableContextOptions(nullable);
        var compilation = CSharpCompilation.Create("Binding", [tree], SpecSnippet.WithTheContract, options);

        return (compilation, tree);
    }
}
