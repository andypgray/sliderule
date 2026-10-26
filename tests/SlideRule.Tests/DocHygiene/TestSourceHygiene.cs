using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Three habits held out of the test sources, read from syntax alone: a member that asserts carries a
///     <c>Should*</c> name, a test never returns early on an operating-system check, and a test passes the
///     running test's token rather than one that never cancels. Each check reports a finding as
///     <c>path:line Member — what is wrong</c>.
/// </summary>
internal static class TestSourceHygiene
{
    /// <summary>The directory the sweep covers, with its trailing slash.</summary>
    internal const string Root = "tests/SlideRule.Tests/";

    // Fixtures and stubs are compiled by the tests rather than run as tests, and their goldens pin their
    // lines, so neither is a test source.
    private static readonly string[] ExcludedRoots = [Root + "Fixtures/", Root + "Stubs/"];

    private static readonly Regex AssertionName = new(@"^Should[A-Z]\w*$");

    /// <summary>
    ///     A member outside a test that invokes a <c>Should*</c> assertion or a member of Shouldly's static
    ///     <c>Should</c> class, or throws <c>ShouldAssertException</c> itself, and is not named <c>Should*</c>; a
    ///     lambda or local function counts as its enclosing member. A <c>void</c> or <c>Task</c> member named
    ///     <c>Assert*</c> already announces that it asserts, so it is not reported.
    /// </summary>
    internal static readonly SourceCheck AssertingHelpers = new(
        FindAssertingHelpers,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Root + "Analyzers/Parity/ParityHarness.cs::Prepare"] =
                "The harness's one construction path; a Should name would carry into each entry point that builds a "
                + "subject, and its guard is that the model file reads back.",
            [Root + "Analyzers/Parity/SiblingWorld.cs::Subject"] =
                "A harness factory whose guards check the baselines the corpus writes for itself; a Should name would "
                + "carry into every corpus subject.",
            [Root + "Caching/FragmentSiteRemapperTests.cs::InsertionMaps"] =
                "A static fixture field cannot take a Should name; its guard is the Should* helper its initializer "
                + "calls.",
            [Root + "Checking/CheckerHarness.cs::Pinned"] =
                "The model-file round trip its summary names; a Should name would carry into Run.",
            [Root + "Cli/FilteredSolutionE2ETests.cs::BillingOnlyFilter"] =
                "A fixture path whose summary names its arrange-time guard.",
            [Root + "Cli/PartialLoadWorkspaceE2ETests.cs::BrokenApp"] =
                "A fixture factory whose summary names its arrange-time guard.",
            [Root + "DocHygiene/CliHelpFences.cs::Render"] =
                "The Should*-named check it calls throws InvalidOperationException rather than asserting.",
            [Root + "DocHygiene/Walkthroughs/ExampleReplay.cs::RunAsync"] =
                "The replay behind a static cache of runs; its load guards vouch for the committed tree every "
                + "walkthrough fact reads.",
            [Root + "Hooks/McpLaunchScriptTests.cs::Launch"] =
                "Runs the launcher; its summary names the empty-stdout guard every case holds it to.",
            [Root + "Mcp/McpUnboundServerTests.cs::ConverseAsync"] =
                "An orchestrator whose summary names the handshake guards it asserts inline.",
            [Root + "SpecAnalyzers/SpecFixtureCorpus.cs::InitializeAsync"] =
                "The fixture's lifetime hook, whose name the interface fixes.",
            [Root + "SpecCompletion/SpecCompletionFixture.cs::AskAsync"] =
                "Guards that a completion service was composed at all; a Should name would carry into the Offered "
                + "entry points.",
            [Root + "SpecCompletion/SpecCompletionFixture.cs::WithoutCaret"] =
                "Guards the caret marker a test wrote; a Should name would carry into every document the fixture "
                + "builds.",
            [Root + "SpecCompletion/SpecCompletionFixture.cs::Triggers"] =
                "ShouldTriggerCompletion is the completion provider's own API, not an assertion."
        });

    /// <summary>
    ///     A <c>return</c> in a test body, outside any lambda or local function, under an <c>if</c> whose
    ///     condition calls <c>OperatingSystem.Is*</c> or <c>RuntimeInformation.IsOSPlatform</c>.
    /// </summary>
    internal static readonly SourceCheck PlatformEarlyReturns = new(
        FindPlatformEarlyReturns,
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    ///     <c>CancellationToken.None</c> or <c>default(CancellationToken)</c> anywhere in a test body. A
    ///     bare <c>default</c> literal is not reported, because syntax cannot tell which parameter it fills.
    /// </summary>
    internal static readonly SourceCheck UncancellableTokens = new(
        FindUncancellableTokens,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Root
             + "Cli/CommandEntryPointTests.cs::RunAsync_CancellationNobodyAskedFor_PrintsTheStackTraceAndExitsTwo"] =
                "The subject is a cancellation nobody asked for, so the token it passes must be one that never cancels."
        });

    private static readonly Lazy<IReadOnlyList<string>> LazySources =
        new(() => TrackedFiles.CSharp
            .Where(IsSwept)
            .ToArray());

    private static readonly Lazy<IReadOnlyList<(string Path, SyntaxNode Root)>> LazyParsed =
        new(() => LazySources.Value
            .AsParallel()
            .Select(static path => (Path: path, Root: Parse(RepoRoot.ReadText(path))))
            .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
            .ToArray());

    /// <summary>The tracked C# sources under <see cref="Root" />, less the fixture and stub trees.</summary>
    internal static IReadOnlyList<string> Sources => LazySources.Value;

    private static bool IsSwept(string path)
    {
        return path.StartsWith(Root, StringComparison.Ordinal)
               && !ExcludedRoots.Any(excluded => path.StartsWith(excluded, StringComparison.Ordinal));
    }

    private static SyntaxNode Parse(string source)
    {
        return CSharpSyntaxTree.ParseText(source)
            .GetRoot();
    }

    private static IEnumerable<Hit> FindAssertingHelpers(string path, SyntaxNode root)
    {
        IEnumerable<MemberDeclarationSyntax> members = root
            .DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(static member => member is BaseMethodDeclarationSyntax
                or BasePropertyDeclarationSyntax
                or BaseFieldDeclarationSyntax);

        foreach (MemberDeclarationSyntax member in members)
        {
            if (IsTest(member) || IsOutcomeHelper(member)) continue;

            (string name, SyntaxToken anchor) = Identity(member);
            if (AssertionName.IsMatch(name)) continue;

            bool asserts = member
                .DescendantNodes()
                .Any(static node => node switch
                {
                    InvocationExpressionSyntax invocation => IsAssertion(invocation),
                    ObjectCreationExpressionSyntax creation => IsAssertionFailure(creation),
                    _ => false
                });
            if (!asserts) continue;

            yield return new Hit(path, LineOf(anchor), name, "asserts but is not named Should*");
        }
    }

    private static IEnumerable<Hit> FindPlatformEarlyReturns(string path, SyntaxNode root)
    {
        foreach (MethodDeclarationSyntax test in Tests(root))
        {
            IEnumerable<ReturnStatementSyntax> returns = test
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(statement => !IsInsideNestedFunction(statement, test))
                .Where(statement => IsUnderPlatformCheck(statement, test));

            foreach (ReturnStatementSyntax statement in returns)
                yield return new Hit(
                    path,
                    LineOf(statement.ReturnKeyword),
                    test.Identifier.ValueText,
                    "returns early on an operating-system check, so the other legs report a pass for a test that "
                    + "ran nothing; skip with Assert.SkipUnless or Assert.SkipWhen and a reason");
        }
    }

    private static IEnumerable<Hit> FindUncancellableTokens(string path, SyntaxNode root)
    {
        foreach (MethodDeclarationSyntax test in Tests(root))
        {
            IEnumerable<ExpressionSyntax> tokens = test
                .DescendantNodes()
                .OfType<ExpressionSyntax>()
                .Where(IsUncancellableToken);

            foreach (ExpressionSyntax token in tokens)
                yield return new Hit(
                    path,
                    LineOf(token.GetFirstToken()),
                    test.Identifier.ValueText,
                    $"passes {token}, which never cancels with the run; pass Ct");
        }
    }

    private static IEnumerable<MethodDeclarationSyntax> Tests(SyntaxNode root)
    {
        return root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(IsTest);
    }

    // A property-based test runs under the test framework as a fact and a theory do.
    private static bool IsTest(MemberDeclarationSyntax member)
    {
        return member.AttributeLists
            .SelectMany(static list => list.Attributes)
            .Select(static attribute => RightmostName(attribute.Name))
            .Any(static name => name is "Fact" or "FactAttribute" or "Theory" or "TheoryAttribute"
                or "Property" or "PropertyAttribute");
    }

    private static bool IsOutcomeHelper(MemberDeclarationSyntax member)
    {
        return member is MethodDeclarationSyntax method
               && method.Identifier.ValueText.StartsWith("Assert", StringComparison.Ordinal)
               && method.ReturnType is PredefinedTypeSyntax { Keyword.ValueText: "void" }
                   or IdentifierNameSyntax { Identifier.ValueText: "Task" }
                   or QualifiedNameSyntax { Right.Identifier.ValueText: "Task" };
    }

    private static bool IsAssertion(InvocationExpressionSyntax invocation)
    {
        SimpleNameSyntax? invoked = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            SimpleNameSyntax simple => simple,
            _ => null
        };
        if (invoked is null) return false;
        if (AssertionName.IsMatch(invoked.Identifier.ValueText)) return true;

        return invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiver }
               && RightmostName(receiver) == "Should";
    }

    // Constructing Shouldly's failure is how a helper reds without a Should* call, whether it throws the
    // exception at once or hands it to a throw expression.
    private static bool IsAssertionFailure(ObjectCreationExpressionSyntax creation)
    {
        return RightmostName(creation.Type) == "ShouldAssertException";
    }

    private static bool IsUnderPlatformCheck(SyntaxNode statement, MethodDeclarationSyntax test)
    {
        return statement
            .Ancestors()
            .TakeWhile(ancestor => ancestor != test)
            .OfType<IfStatementSyntax>()
            .Any(static branch => branch.Condition
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(IsPlatformCheck));
    }

    private static bool IsPlatformCheck(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access) return false;

        string? receiver = RightmostName(access.Expression);
        string member = access.Name.Identifier.ValueText;

        return (receiver == "OperatingSystem" && member.StartsWith("Is", StringComparison.Ordinal))
               || (receiver == "RuntimeInformation" && member == "IsOSPlatform");
    }

    private static bool IsUncancellableToken(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText == "None"
                                                   && RightmostName(access.Expression) == "CancellationToken",
            DefaultExpressionSyntax defaulted => RightmostName(defaulted.Type) == "CancellationToken",
            _ => false
        };
    }

    // A lambda or local function inside a test is its own body: a return there leaves that function, not
    // the test.
    private static bool IsInsideNestedFunction(SyntaxNode node, MethodDeclarationSyntax test)
    {
        return node
            .Ancestors()
            .TakeWhile(ancestor => ancestor != test)
            .Any(static ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    private static (string Name, SyntaxToken Anchor) Identity(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => (method.Identifier.ValueText, method.Identifier),
            ConstructorDeclarationSyntax constructor => (constructor.Identifier.ValueText, constructor.Identifier),
            DestructorDeclarationSyntax destructor => ("~" + destructor.Identifier.ValueText, destructor.Identifier),
            OperatorDeclarationSyntax @operator => ("operator " + @operator.OperatorToken.ValueText,
                @operator.OperatorToken),
            ConversionOperatorDeclarationSyntax conversion => ("operator " + conversion.Type,
                conversion.OperatorKeyword),
            PropertyDeclarationSyntax property => (property.Identifier.ValueText, property.Identifier),
            EventDeclarationSyntax @event => (@event.Identifier.ValueText, @event.Identifier),
            IndexerDeclarationSyntax indexer => ("this[]", indexer.ThisKeyword),
            BaseFieldDeclarationSyntax field => FieldIdentity(field),
            _ => throw new UnreachableException($"A {member.Kind()} is not a member kind the sweep selects.")
        };
    }

    private static (string Name, SyntaxToken Anchor) FieldIdentity(BaseFieldDeclarationSyntax field)
    {
        SeparatedSyntaxList<VariableDeclaratorSyntax> variables = field.Declaration.Variables;
        string names = string.Join(", ", variables.Select(static variable => variable.Identifier.ValueText));

        return (names, variables[0].Identifier);
    }

    private static string? RightmostName(ExpressionSyntax expression)
    {
        return expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static int LineOf(SyntaxToken token)
    {
        return token.GetLocation()
            .GetLineSpan()
            .StartLinePosition.Line + 1;
    }

    /// <summary>One check's finding before its allow-list is consulted.</summary>
    internal readonly record struct Hit(string Path, int Line, string Member, string Problem)
    {
        /// <summary>The allow-list key, <c>path::Member</c>.</summary>
        internal string Key => $"{Path}::{Member}";

        /// <summary>The finding as a gate reports it.</summary>
        internal string Finding => $"{Path}:{Line} {Member} — {Problem}";
    }

    /// <summary>
    ///     One check and the members it forgives, keyed <c>path::Member</c> with the reason each one stays.
    /// </summary>
    internal sealed class SourceCheck(
        Func<string, SyntaxNode, IEnumerable<Hit>> find,
        IReadOnlyDictionary<string, string> allowed)
    {
        /// <summary>The findings in <paramref name="source" />, read as the file at <paramref name="path" />.</summary>
        internal IReadOnlyList<string> Run(string path, string source)
        {
            IEnumerable<Hit> hits = find(path, Parse(source));

            return Unforgiven(hits);
        }

        /// <summary>The findings across every swept source, in path order.</summary>
        internal IReadOnlyList<string> RunOverTree()
        {
            IEnumerable<Hit> hits = LazyParsed.Value.SelectMany(entry => find(entry.Path, entry.Root));

            return Unforgiven(hits);
        }

        /// <summary>The allow-list keys that match no hit across the swept sources.</summary>
        internal IReadOnlyList<string> DeadAllowances()
        {
            HashSet<string> hitKeys = LazyParsed.Value
                .SelectMany(entry => find(entry.Path, entry.Root))
                .Select(static hit => hit.Key)
                .ToHashSet(StringComparer.Ordinal);

            return allowed.Keys
                .Where(key => !hitKeys.Contains(key))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        private List<string> Unforgiven(IEnumerable<Hit> hits)
        {
            return hits
                .Where(hit => !allowed.ContainsKey(hit.Key))
                .Select(static hit => hit.Finding)
                .ToList();
        }
    }
}
