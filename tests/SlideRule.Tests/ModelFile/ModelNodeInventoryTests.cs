using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Walks every node class of the reified model and holds each one to being writable, readable, or
///     declared unwritable — so a node that ships without a place in the file reds here rather than
///     silently costing a rule.
/// </summary>
/// <remarks>
///     <para>
///         The sweep is over Core's own assembly, so the deliberately-unknown node subclasses the
///         evaluator's fail-closed rows mint in this assembly are outside it.
///     </para>
///     <para>
///         The family and total counts below are floors rather than pins. The claim is that the walk
///         reached the model rather than an empty set; a number that only ever grows would otherwise have
///         to be edited by everyone who adds a verb, and the first person to do so would make it a pin of
///         nothing.
///     </para>
/// </remarks>
public sealed class ModelNodeInventoryTests
{
    private static readonly Assembly Core = typeof(Constraint).Assembly;

    // A discriminated node writes its discriminator as its first key, so the file's own text records
    // which node classes a corpus ever reached.
    private static readonly Regex Discriminator = new("\"node\": \"([A-Za-z]+)\"");

    private static readonly (NodeFamily Family, Type Base, int Floor)[] Families =
    [
        (NodeFamily.Noun, typeof(SelectionNoun), 6),
        (NodeFamily.Adjective, typeof(SelectionAdjective), 10),
        (NodeFamily.MemberAdjective, typeof(MemberAdjective), 5),
        (NodeFamily.ProjectAdjective, typeof(ProjectAdjective), 4),
        (NodeFamily.Constraint, typeof(Constraint), 55)
    ];

    private static readonly Type[] SelectionRoots = [typeof(Selection), typeof(MemberSelection), typeof(ProjectSelection)];

    private const int NodeClassFloor = 96;

    [Fact]
    public void EveryDiscriminatedNode_IsEitherWritableAndReadable_OrOneOfTheSixPredicateClasses()
    {
        var unregistered = new List<string>();
        var total = 0;
        foreach ((NodeFamily family, Type root, int floor) in Families)
        {
            List<Type> nodes = Concrete(root);
            nodes.Count.ShouldBeGreaterThan(floor, $"{family} classes reached by the sweep");
            total += nodes.Count;

            IReadOnlyCollection<string> discriminators = NodeReaders.Discriminators(family);
            foreach (Type node in nodes)
            {
                if (NodeWriters.DelegateTypes.Contains(node)) continue;

                string discriminator = NodeWriters.DiscriminatorOf(node);
                if (!NodeWriters.WritableTypes.Contains(node)) unregistered.Add(node.Name + " (no write arm)");
                if (!discriminators.Contains(discriminator))
                    unregistered.Add($"{node.Name} (no read arm under '{discriminator}')");
            }
        }

        unregistered.ShouldReportNothing("Node class(es) the writer or the reader has no arm for");

        // The five families plus the eight shape-written selections are the whole node set.
        (total + NodeWriters.ShapeWrittenTypes.Count).ShouldBeGreaterThanOrEqualTo(NodeClassFloor);
    }

    [Fact]
    public void EverySelectionClass_IsWrittenByItsShape_AndTheSweepFindsExactlyThoseDeclared()
    {
        List<Type> selections = SelectionRoots.SelectMany(Concrete)
            .ToList();

        // A selection carries no discriminator — it is told apart by the keys it writes — so a ninth one
        // would be written by nothing and read as one of the eight. The sweep is what makes that loud.
        selections.Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(NodeWriters.ShapeWrittenTypes.Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        selections.Count.ShouldBe(8);
    }

    [Fact]
    public void ThePredicateClasses_AreExactlyTheSixThatHoldADelegate()
    {
        NodeWriters.DelegateTypes.Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(
            [
                "MemberMustConstraint",
                "MemberWhereAdjective",
                "MustConstraint",
                "ProjectMustConstraint",
                "ProjectWhereAdjective",
                "WhereAdjective"
            ]);

        // Each of them really does hold one, so the list cannot become a way of excusing a node nobody
        // taught the writer about.
        foreach (Type node in NodeWriters.DelegateTypes)
            node.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Any(property => typeof(Delegate).IsAssignableFrom(property.PropertyType))
                .ShouldBeTrue(node.Name);
    }

    [Fact]
    public void TheDiscriminatorRule_StripsOneTrailingRoleWordAndLowersTheFirstLetter()
    {
        NodeWriters.DiscriminatorOf(typeof(MustNotReferenceConstraint))
            .ShouldBe("mustNotReference");
        NodeWriters.DiscriminatorOf(typeof(MemberWithSuffixAdjective))
            .ShouldBe("memberWithSuffix");
        NodeWriters.DiscriminatorOf(typeof(ProjectNamedAdjective))
            .ShouldBe("projectNamed");
        NodeWriters.DiscriminatorOf(typeof(TypesNoun))
            .ShouldBe("types");
        NodeWriters.DiscriminatorOf(typeof(TypeNoun))
            .ShouldBe("type");
    }

    [Fact]
    public void Write_ANodeWithNoArm_ThrowsNamingTheClass()
    {
        // Fail closed like the evaluator's own dispatch: a node the writer was never taught about would
        // otherwise leave a rule enforcing less than it says.
        var writer = new NodeWriter(new Dictionary<string, string>(StringComparer.Ordinal));

        Should.Throw<InvalidOperationException>(() => NodeWriters.Write(writer, TypesNoun.Instance.Locative))
            .Message.ShouldBe("Unhandled model node 'String'.");
    }

    /// <summary>
    ///     The registries say which nodes are writable; this says the corpus actually writes them. Without
    ///     it every round-trip pin in this folder could stay green over a file that named a handful of the
    ///     node classes, because a rule the writer refuses round trips as a name and a reason.
    /// </summary>
    /// <remarks>
    ///     An equality rather than a floor: a discriminator no corpus spec reaches is a shape nothing has
    ///     ever written and read back, and the vocabulary-remainder spec exists precisely so that the
    ///     remainder is said somewhere rather than left uncovered.
    /// </remarks>
    [Fact]
    public void EveryWritableDiscriminator_IsWrittenByTheCorpus()
    {
        // Arrange
        List<string> declared = NodeWriters.WritableTypes.Select(NodeWriters.DiscriminatorOf)
            .OrderBy(discriminator => discriminator, StringComparer.Ordinal)
            .ToList();

        // Act
        HashSet<string> written = new(StringComparer.Ordinal);
        foreach (ModelFileCorpus.CorpusSpec spec in ModelFileCorpus.All)
        foreach (Match match in Discriminator.Matches(
                     ModelDocumentComposer.Compose(ModelFileCorpus.DocumentOf(spec))))
            written.Add(match.Groups[1]
                .Value);

        // Assert
        declared.Where(discriminator => !written.Contains(discriminator))
            .ToList()
            .ShouldReportNothing("Writable node(s) no corpus spec ever writes, so nothing reads them back either");
        written.Where(discriminator => !declared.Contains(discriminator))
            .ToList()
            .ShouldReportNothing("Discriminator(s) written that the registries do not declare");
    }

    /// <summary>
    ///     The same claim for the selections, which carry no discriminator and so are invisible to the
    ///     sweep above: each of the eight is told apart by the keys it writes, and one nothing writes is a
    ///     shape the reader has never had to tell from its siblings.
    /// </summary>
    /// <remarks>
    ///     Read off the <em>parsed</em> documents rather than the models they came from, so the claim is
    ///     that the reader minted each shape rather than that the writer met one.
    /// </remarks>
    [Fact]
    public void EverySelectionShape_IsMintedByTheReaderSomewhereInTheCorpus()
    {
        // Arrange
        List<string> declared = NodeWriters.ShapeWrittenTypes.Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Act
        HashSet<string> minted = new(StringComparer.Ordinal);
        foreach (ModelFileCorpus.CorpusSpec spec in ModelFileCorpus.All)
        foreach (ArchRule rule in ModelDocumentComposer.Compose(ModelFileCorpus.DocumentOf(spec))
                     .ShouldReadBack(spec.Name)
                     .Rules)
        foreach (object node in ModelNodeWalk.From(rule)
                     .Nodes)
            if (declared.Contains(node.GetType()
                    .Name))
                minted.Add(node.GetType()
                    .Name);

        // Assert
        declared.Where(name => !minted.Contains(name))
            .ToList()
            .ShouldReportNothing("Selection shape(s) no corpus spec puts through the file");
    }

    /// <summary>
    ///     A scope read back from a file carries no boundary — the containment rule's own operands are the
    ///     boundary, so the file would be storing it twice — which is sound only while nothing the checker
    ///     runs reads that slot. This is what holds it sound.
    /// </summary>
    /// <remarks>
    ///     Over the tracked source text rather than over references, because the claim is about a whole
    ///     directory rather than about one caller, and because a mention in a comment is a reader being
    ///     told that the slot is available there. The renderer that legitimately draws from it is asserted
    ///     to still match, so a pattern that stopped matching anything cannot pass as "nothing reads it".
    /// </remarks>
    [Fact]
    public void NoCheckingSource_ReadsAScopesBoundary()
    {
        // Arrange
        IReadOnlyList<string> checkingSources = CheckerSources.All;

        // Act
        List<string> mentions = checkingSources.SelectMany(Boundaries)
            .ToList();

        // Assert: the floor first, so a sweep that reached no file says so rather than reporting nothing.
        checkingSources.Count.ShouldBeGreaterThan(5, "the sweep reached the checker's own sources");
        Boundaries("src/SlideRule/Rendering/LawDiagramRenderer.cs")
            .ShouldNotBeEmpty("the search no longer finds the one place a boundary is legitimately read");
        mentions.ShouldReportNothing(
            "The checker now names a scope's Boundary, which a model file does not carry — a parsed model "
            + "would silently check differently from the spec it came from");
    }

    /// <summary>
    ///     The same sweep against the one-project decidability table: every node class it finds has a row
    ///     there, so a node that ships without one reds here rather than throwing the classifier's
    ///     fail-closed refusal at whoever first writes a rule using it.
    /// </summary>
    /// <remarks>
    ///     Over all eight roots at once, the five discriminated families plus the three selection
    ///     hierarchies, because that table classifies the selection shapes too.
    /// </remarks>
    [Fact]
    public void EveryNodeClass_HasARowInTheOneProjectDecidabilityTable()
    {
        // Arrange
        List<Type> nodes = Families.Select(row => row.Base)
            .Concat(SelectionRoots)
            .SelectMany(Concrete)
            .ToList();

        // Act
        List<string> unclassified = nodes.Where(node => !OneProjectDecidability.ClassifiedTypes.Contains(node))
            .Select(node => node.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Assert: the floor first, so a sweep that reached nothing says so rather than reporting nothing.
        nodes.Count.ShouldBeGreaterThanOrEqualTo(NodeClassFloor, "concrete node classes reached by the sweep");
        unclassified.ShouldReportNothing("Node class(es) with no row in the one-project decidability table");
    }

    private static List<Type> Concrete(Type root)
    {
        return Core.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && root.IsAssignableFrom(type) && type != root)
            .ToList();
    }

    private static IEnumerable<string> Boundaries(string trackedPath)
    {
        string[] lines = RepoRoot.ReadLines(trackedPath);

        for (var index = 0; index < lines.Length; index++)
            if (lines[index]
                .Contains("Boundary", StringComparison.Ordinal))
                yield return $"{trackedPath}:{index + 1}";
    }
}
