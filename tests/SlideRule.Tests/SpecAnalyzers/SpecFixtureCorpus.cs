using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The whole validation fixture corpus, read from the repository as text, compiled once, and run past
///     the spec-literal analyzer: the warnings it drew, and the span of every fixture class each warning
///     can be attributed to.
/// </summary>
/// <remarks>
///     <para>
///         The corpus is two files and nothing else. The fixture file carries no <c>using</c> of its own —
///         it names the authoring surface by namespace nesting from <c>SlideRule.Tests</c> and
///         reaches its framework types through the implicit-usings set — and the anchor helper types it
///         binds against sit beside it, carrying no test-framework dependency. That is why the two compile
///         alone, with nothing of the test project around them.
///     </para>
///     <para>
///         The suite's own assembly is taken out of the reference set on purpose. Left in, the fixture file
///         would bind <c>AnchorWidget</c> and its neighbours from metadata, and a helper file that failed to
///         be supplied would compile anyway; taking it out makes the zero-errors assertion the guard that
///         the corpus really is these two files. The compilation still takes the suite's own <em>name</em>,
///         because the foreign-instance fixtures mint an <c>Arch</c> of their own and that constructor is
///         internal to the contract with the suite named as a friend. Under any other identity those
///         fixtures do not bind, which is a fact about the fixture file rather than about a spec: a real
///         spec is handed its <c>Arch</c>.
///     </para>
///     <para>
///         Held as a class fixture because the compilation is the expensive half and every fact reads the
///         same answer from it. The facts themselves are cheap: building a spec and matching sentences.
///     </para>
/// </remarks>
public sealed class SpecFixtureCorpus : IAsyncLifetime
{
    /// <summary>The fixture file, by the repo-relative path the suite reads committed files under.</summary>
    public const string FixtureFile = "tests/SlideRule.Tests/SpecValidationSpecs.cs";

    /// <summary>The anchor helper types the fixture file's member anchors name.</summary>
    public const string HelperFile = "tests/SlideRule.Tests/MemberExpressionAnchorFixtures.cs";

    private const string FixtureNamespace = "SlideRule.Tests";

    /// <summary>Every warning the analyzer drew over the corpus, in source order.</summary>
    public IReadOnlyList<Diagnostic> Reported { get; private set; } = [];

    /// <summary>
    ///     Every concrete spec class the fixture file declares, in declaration order, each with the span its
    ///     warnings fall inside and the runtime type its validator verdicts come from.
    /// </summary>
    public IReadOnlyList<FixtureClass> Classes { get; private set; } = [];

    /// <summary>
    ///     The classes the file declares that this assembly declares no matching spec type for, which would
    ///     leave parity measured over fewer fixtures than the file holds.
    /// </summary>
    public IReadOnlyList<string> Unresolved { get; private set; } = [];

    /// <summary>Compiles the two files and collects what the analyzer said about them.</summary>
    public async ValueTask InitializeAsync()
    {
        string fixtures = RepoRoot.ReadText(FixtureFile);
        string helpers = RepoRoot.ReadText(HelperFile);

        Reported = await SpecSnippet.ShouldCompileOwnSourcesAndDiagnoseAsync(
            [(FixtureFile, fixtures), (HelperFile, helpers)], SpecSnippet.Suite, cancellationToken: Ct);

        TakeInventory(fixtures);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>The warnings that sit inside <paramref name="fixture" />'s own declaration.</summary>
    public IReadOnlyList<Diagnostic> WarningsIn(FixtureClass fixture)
    {
        return Reported
            .Where(diagnostic => IsInside(diagnostic, fixture))
            .ToList();
    }

    /// <summary>The same for the fixture of this name, which is how a named difference asks about one class.</summary>
    /// <param name="name">The fixture class's name.</param>
    public IReadOnlyList<Diagnostic> WarningsIn(string name)
    {
        FixtureClass fixture = Classes.Single(candidate => candidate.Name == name);

        return WarningsIn(fixture);
    }

    /// <summary>
    ///     The warnings that sit inside no fixture class at all: another file, the space between two
    ///     declarations, or no source position whatever.
    /// </summary>
    /// <remarks>
    ///     A warning here is unattributable rather than merely unexpected, so it would drop out of the
    ///     pairing silently instead of reading as a difference between the two sides.
    /// </remarks>
    public IReadOnlyList<Diagnostic> Stray()
    {
        return Reported
            .Where(diagnostic => !Classes.Any(fixture => IsInside(diagnostic, fixture)))
            .ToList();
    }

    private static bool IsInside(Diagnostic diagnostic, FixtureClass fixture)
    {
        Location location = diagnostic.Location;
        if (location.SourceTree?.FilePath != FixtureFile) return false;

        return fixture.Span.Contains(location.SourceSpan);
    }

    // The inventory is taken from the syntax rather than from the assembly, because the question parity
    // asks is about the fixtures this file holds: a type reached by reflection alone could be declared
    // anywhere, and a class the walk failed to recognise would go unmeasured.
    private void TakeInventory(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, SpecSnippet.Language, FixtureFile);
        List<ClassDeclarationSyntax> declared = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(IsConcreteSpec)
            .ToList();

        var resolved = new List<FixtureClass>();
        var unresolved = new List<string>();
        foreach (ClassDeclarationSyntax declaration in declared)
        {
            string name = declaration.Identifier.ValueText;
            Type? type = typeof(SpecFixtureCorpus).Assembly.GetType($"{FixtureNamespace}.{name}");

            if (type is null || !typeof(IArchitectureSpec).IsAssignableFrom(type))
            {
                unresolved.Add(name);
                continue;
            }

            resolved.Add(new FixtureClass(name, declaration.Span, type));
        }

        Classes = resolved;
        Unresolved = unresolved;
    }

    private static bool IsConcreteSpec(ClassDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(SyntaxKind.AbstractKeyword)) return false;
        if (declaration.BaseList is null) return false;

        return declaration.BaseList.Types
            .Any(entry => entry.Type.ToString() == nameof(IArchitectureSpec));
    }

    /// <summary>One fixture class of the corpus, on both sides at once.</summary>
    /// <param name="name">The name the file declares it under.</param>
    /// <param name="span">The declaration's own span, which every warning about it falls inside.</param>
    /// <param name="type">The type in this assembly that the name resolved to.</param>
    public sealed class FixtureClass(string name, TextSpan span, Type type)
    {
        /// <summary>The name the file declares it under.</summary>
        public string Name { get; } = name;

        /// <summary>The declaration's own span in the fixture file.</summary>
        public TextSpan Span { get; } = span;

        /// <summary>The spec type the name resolved to, which is what the validator is driven over.</summary>
        public Type Type { get; } = type;
    }
}
