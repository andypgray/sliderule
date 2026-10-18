using SlideRule.Baselines;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The in-memory half of the pin's corpus: the shapes no bed reds, each as a small solution built from
///     source strings and a spec written to break it.
/// </summary>
/// <remarks>
///     <para>
///         One factory per shape rather than one world with every rule on it, because these are separate
///         claims and a spec carrying two of them would have the first failure hide the second. The subjects
///         are built fresh on each call: a compilation is cheap here, and sharing one would have a leg's
///         substitution reach another fact.
///     </para>
///     <para>
///         Two families sit here. The first is about what one compilation can settle — the verbs, the
///         reference forms, the copies of a shared name. The second is about which <em>document</em> of a
///         compilation answers for a finding.
///     </para>
/// </remarks>
internal static class SiblingCorpus
{
    private const string BaselineDeclared = "arch/baselines/legacy.json";

    /// <summary>The one source text two projects of the shared-name rows each compile as their own.</summary>
    private const string SharedWidget = "namespace Common;\n\npublic class Widget { }";

    /// <summary>The same for the throw row, where the shared name is what a throw edge reaches.</summary>
    private const string SharedFault = "namespace Common;\n\npublic class Fault : System.Exception { }";

    /// <summary>The same for the drifted-copy rows, where the shared name is an edge's source.</summary>
    private const string SharedScribe =
        "namespace Common;\n\npublic class Scribe\n{\n    public Ink.Pen Pen;\n}";

    /// <summary>
    ///     The three inbound verbs, crossed both ways: a subject in a sibling with the offender in own
    ///     code, and a subject in own code with the offender in a sibling.
    /// </summary>
    /// <remarks>
    ///     The second direction is the one worth having. A project's own types can be referenced from a
    ///     project it has never heard of, and the build of the referrer is where that shows: no single
    ///     compilation sees both claims, and the union over the solution's projects is what makes the two
    ///     sets meet.
    /// </remarks>
    internal static ParitySubject Inbound()
    {
        return new SiblingWorld()
            .Project("Contracts", "Contracts.Api", "namespace Contracts;\n\npublic interface IThing { }")
            .Project(
                "Data", "Data.Store",
                "namespace Data;\n\npublic class Store\n{\n    public Contracts.IThing Thing;\n}\n\npublic class Ledger { }",
                "Contracts.Api")
            .Project(
                "Tool", "Tool.Cli",
                "namespace Tool;\n\npublic class Command\n{\n    public Contracts.IThing Thing;\n}",
                "Contracts.Api")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store Store;\n}",
                "Data.Store", "Contracts.Api")
            .Subject(
                "Sibling/inbound",
                arch =>
                {
                    arch.Rule("inbound/data-not-from-web")
                        .Enforce(arch.Namespace("Data").MustNotBeReferencedBy(arch.Namespace("Web")))
                        .Because("The web layer reaches the store through a contract, never directly.");

                    arch.Rule("inbound/contracts-only-from-web")
                        .Enforce(arch.Namespace("Contracts").MustOnlyBeReferencedBy(arch.Namespace("Web")))
                        .Because("The contracts are the web layer's vocabulary and nobody else's.");

                    arch.Rule("inbound/data-only-itself")
                        .Enforce(arch.Namespace("Data").MustOnlyBeReferencedByItself())
                        .Because("The store's callers are inside the store's own project.");
                });
    }

    /// <summary>
    ///     A quarantine with a captured baseline: the crossing the baseline holds is silent and a second
    ///     one is a broken law.
    /// </summary>
    internal static ParitySubject CapturedContainment()
    {
        string baseline = BaselineComposer.Compose(
            "legacy/data/containment", BaselineEntry.ForEdge("T:Web.Blessed", "T:Data.Store"));

        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }\n\npublic class Ledger { }")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class Blessed\n{\n    public Data.Store Store;\n}\n\n"
                + "public class Fresh\n{\n    public Data.Ledger Ledger;\n}",
                "Data.Store")
            .Subject(
                "Sibling/captured containment",
                arch => arch.Scope("legacy/data")
                    .Quarantine(arch.Namespace("Data"))
                    .Baseline(BaselineDeclared)
                    .Dragons("The store's connection handling is structural and is not to be tidied.")
                    .Because("Everything left inside the quarantine is reached through the one blessed door."),
                (BaselineDeclared, baseline));
    }

    /// <summary>
    ///     A project noun as a rule's own subject, over a project whose assembly is named something else:
    ///     the assembly name is the only thing a build has in hand when it asks whose code it is reading, so
    ///     the noun has to answer from the model file's table instead.
    /// </summary>
    /// <remarks>
    ///     Outbound, so the noun stands in subject position. A project noun that selects other types is read
    ///     over the whole solution — a project that compiles another's source file declares its types too —
    ///     and the linked-file bed pins that reading.
    /// </remarks>
    internal static ParitySubject RenamedAssembly()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store Store;\n}",
                "Data.Store")
            .Subject(
                "Sibling/renamed assembly",
                arch => arch.Rule("projects/web-not-through-data")
                    .Enforce(arch.Project("Web").MustNotReference(arch.Namespace("Data")))
                    .Because("The web project reaches the store project through a contract, never directly."));
    }

    /// <summary>
    ///     A captured pair that has grown: the baseline holds one site and the code has two, so both are
    ///     reported rather than the one that is new.
    /// </summary>
    internal static ParitySubject GrownPair()
    {
        SiblingWorld world = new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store First;\n    public Data.Store Second;\n}",
                "Data.Store");

        return GrownPairOver(world, "Sibling/grown pair");
    }

    /// <summary>
    ///     A sibling whose base type lives in a third assembly, with <paramref name="vouched" /> deciding
    ///     whether the project being built references that assembly too.
    /// </summary>
    /// <remarks>
    ///     Unreferenced, the sibling's hierarchy stops at a type the compilation has no metadata for, so
    ///     the extraction cannot vouch for it and the build says nothing about it. That is a difference the
    ///     reference form has no bearing on: a compilation resolves a referenced assembly's own references
    ///     against its own reference set, so a compilation reference carries the sibling's closure no more
    ///     than an image does.
    /// </remarks>
    /// <param name="vouched">Whether the own project references the third assembly.</param>
    internal static ParitySubject ErrorHierarchy(bool vouched)
    {
        string[] webReferences = vouched ? ["Data.Store", "Base.Lib"] : ["Data.Store"];

        return new SiblingWorld()
            .Package("Base.Lib", "namespace Base;\n\npublic class Root { }")
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store : Base.Root { }", "Base.Lib")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class Client\n{\n    public Data.Store Store;\n}",
                webReferences)
            .Subject(
                vouched ? "Sibling/error hierarchy (third assembly referenced)" : "Sibling/error hierarchy",
                arch => arch.Rule("hierarchy/web-holds-no-root")
                    .Enforce(arch.Namespace("Web").MustNotReference(arch.Types.DerivedFrom("Base.Root")))
                    .Because("Anything under that root is the framework's to hold, not the web layer's."));
    }

    /// <summary>
    ///     An attribute class the sibling's own build conditions away: present on the source symbol,
    ///     absent from the metadata the same project compiles to.
    /// </summary>
    internal static ParitySubject ConditionalAttribute()
    {
        return new SiblingWorld()
            .Package(
                "Markers.Lib",
                "namespace Markers;\n\n[System.Diagnostics.Conditional(\"NEVER_DEFINED\")]\n"
                + "[System.AttributeUsage(System.AttributeTargets.All)]\npublic class KeepAttribute : System.Attribute { }")
            .Project("Data", "Data.Store", "namespace Data;\n\n[Markers.Keep]\npublic class Store { }", "Markers.Lib")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class Client\n{\n    public Data.Store Store;\n}",
                "Data.Store", "Markers.Lib")
            .Subject(
                "Sibling/conditional attribute",
                arch => arch.Rule("markers/web-holds-nothing-marked")
                    .Enforce(arch.Namespace("Web")
                        .MustNotReference(arch.Namespace("Data").AttributedWith("Markers.KeepAttribute")))
                    .Because("A marked type is the sibling's own bookkeeping and nothing here should hold one."));
    }

    /// <summary>
    ///     A type-level attribute no author wrote and every metadata image carries: the compiler emits
    ///     <c>[DefaultMember]</c> onto a class with an indexer, so the same filter selects one type from
    ///     source and two from an image.
    /// </summary>
    /// <remarks>
    ///     <c>Marked</c> writes the attribute out by hand, so the filter has something to select on both
    ///     sides — an operand nothing matches is a different case, and a bed that measured that one would
    ///     say nothing about this one.
    /// </remarks>
    internal static ParitySubject SynthesizedAttribute()
    {
        return new SiblingWorld()
            .Project(
                "Data", "Data.Store",
                "namespace Data;\n\n[System.Reflection.DefaultMember(\"Thing\")]\npublic class Marked\n"
                + "{\n    public int Thing;\n}\n\npublic class Catalogue\n{\n"
                + "    public int this[int index] => index;\n}")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class Client\n{\n    public Data.Marked Marked;\n"
                + "    public Data.Catalogue Catalogue;\n}",
                "Data.Store")
            .Subject(
                "Sibling/synthesized attribute",
                arch => arch.Rule("markers/web-holds-nothing-defaulted")
                    .Enforce(arch.Namespace("Web")
                        .MustNotReference(arch.Namespace("Data")
                            .AttributedWith("System.Reflection.DefaultMemberAttribute")))
                    .Because("A type that names a default member is reached through that member, and the "
                             + "web layer calls what it means by name."));
    }

    /// <summary>A rule over the container's registrations, which a build has none of.</summary>
    internal static ParitySubject RegisteredElsewhere()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store Store;\n}",
                "Data.Store")
            .Subject(
                "Sibling/registered elsewhere",
                arch => arch.Rule("di/registered-types-are-sealed")
                    .Enforce(arch.Registered().MustBeSealed())
                    .Because("A registered type nobody may subclass is the one the container resolved."));
    }

    /// <summary>
    ///     A project-headed allow-list operand over a name two projects compile, with
    ///     <paramref name="namingTheReachedCopy" /> deciding which of the two declarers the law names.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The shape the allow-list operand's row rests on. An allow entry is asked at the
    ///         project whose copy the instance reached (GRAMMAR §4.1), never of the type, so naming the
    ///         other declarer refuses the very reference naming this one allows — and a reader of one
    ///         compilation answers both the same way, because the copy it bound is the copy it attributes.
    ///         Plain membership would answer "allowed" either way, both declarers being declarers, which is
    ///         what makes the two legs of this pair a measurement rather than a restatement.
    ///     </para>
    ///     <para>
    ///         <c>Bureau</c> sorts first and is therefore the attribution winner, and the host reaches
    ///         <c>Supply</c>'s copy — so a verdict that fell back to the winner would answer the wrong way
    ///         round, which a row whose two declarers sorted the other way could not show. The host's
    ///         reference to <c>Ink.Pen</c> is the violation both spellings share, so neither leg is a row
    ///         about a rule that never reported.
    ///     </para>
    /// </remarks>
    /// <param name="namingTheReachedCopy">Whether the law names the declarer whose copy the host reached.</param>
    internal static ParitySubject AllowListOverASharedName(bool namingTheReachedCopy)
    {
        string allowed = namingTheReachedCopy ? "Supply" : "Bureau";

        return new SiblingWorld()
            .Project("Bureau", "Bureau.Asm", SharedWidget)
            .Project("Ink", "Ink.Lib", "namespace Ink;\n\npublic class Pen { }")
            .Project("Supply", "Supply.Asm", SharedWidget)
            .Project(
                "Host", "Host.App",
                "namespace Host;\n\npublic class Consumer\n{\n    public Common.Widget Widget;\n"
                + "    public Ink.Pen Pen;\n}",
                "Supply.Asm", "Ink.Lib")
            .Subject(
                $"Sibling/allow-list naming {allowed}",
                arch => arch.Rule($"copies/host-reaches-only-{allowed.ToLowerInvariant()}")
                    .Enforce(arch.Namespace("Host").MustOnlyReference(arch.Project(allowed)))
                    .Because($"{allowed} owns every widget the host is allowed to hold."));
    }

    /// <summary>
    ///     The same pair over the throw verb, whose allow-list is the strict one: a thrown type outside it
    ///     is a violation whoever declares it, externals included.
    /// </summary>
    /// <remarks>
    ///     A row of its own rather than a line in the one above, because this is a second verb and not a
    ///     second spelling: it walks its own edge index, and a row that took its verdict on the reference
    ///     verb's word would be the argument the reference verb's row was measured to avoid. The shape is
    ///     the same otherwise, down to the declarer that sorts first being the one the host does not reach.
    /// </remarks>
    /// <param name="namingTheReachedCopy">Whether the law names the declarer whose copy the host threw.</param>
    internal static ParitySubject ThrowAllowListOverASharedName(bool namingTheReachedCopy)
    {
        string allowed = namingTheReachedCopy ? "Supply" : "Bureau";

        return new SiblingWorld()
            .Project("Bureau", "Bureau.Asm", SharedFault)
            .Project("Ink", "Ink.Lib", "namespace Ink;\n\npublic class Snag : System.Exception { }")
            .Project("Supply", "Supply.Asm", SharedFault)
            .Project(
                "Host", "Host.App",
                "namespace Host;\n\npublic class Scribe\n{\n"
                + "    public void Write() { throw new Common.Fault(); }\n"
                + "    public void Stall() { throw new Ink.Snag(); }\n}",
                "Supply.Asm", "Ink.Lib")
            .Subject(
                $"Sibling/throw allow-list naming {allowed}",
                arch => arch.Rule($"copies/host-throws-only-{allowed.ToLowerInvariant()}")
                    .Enforce(arch.Namespace("Host").MustOnlyThrow(arch.Project(allowed)))
                    .Because($"{allowed} owns every fault the host is allowed to raise."));
    }

    /// <summary>
    ///     The same question at the other end of an edge: a project-headed allow-list operand over the
    ///     <em>source</em> of a reference two projects both compile.
    /// </summary>
    /// <remarks>
    ///     One file's worth of source in two projects, each making the same reference out of its own copy.
    ///     The law allows the writer's copy and refuses the mirror's, so the whole-solution check reports
    ///     one of the two identical references — and the two copies sit in files of their own here, where
    ///     a real linked file would put both at one path, so a verdict that named the wrong copy would move
    ///     the reported site rather than hide behind a shared line.
    /// </remarks>
    internal static ParitySubject InboundAllowListOverDriftedCopies()
    {
        return DriftedCopies()
            .Subject(
                "Sibling/inbound allow-list over drifted copies",
                arch => arch.Rule("copies/pen-only-from-the-writer")
                    .Enforce(arch.Namespace("Ink").MustOnlyBeReferencedBy(arch.Project("Writer")))
                    .Because("The pen is the writer's to hold, and a copy of the writer is not the writer."));
    }

    /// <summary>
    ///     The implicit self-allowance of a <c>MustOnly*</c> reference verb over a project subject: the
    ///     subject is the whole allow-set, so it spares as much as it bounds.
    /// </summary>
    /// <remarks>
    ///     Over the same two copies, which is what makes the row about attribution rather than about the
    ///     verb. The subject owns an instance only at a project it names, so the mirror's identical
    ///     reference is nobody's business here and the writer's is the one violation — and a reader of the
    ///     mirror's compilation, which sees that copy as its own, says nothing at all.
    /// </remarks>
    internal static ParitySubject SelfAllowanceOverDriftedCopies()
    {
        return DriftedCopies()
            .Subject(
                "Sibling/self-allowance over drifted copies",
                arch => arch.Rule("copies/writer-reaches-only-itself")
                    .Enforce(arch.Project("Writer").MustOnlyReferenceItself())
                    .Because("What the writer compiles reaches what the writer compiles."));
    }

    /// <summary>
    ///     A partial type declared in two documents, each part reaching a sibling type of its own: the
    ///     crossing a part makes belongs to the file that part sits in, and neither file answers for the
    ///     other's.
    /// </summary>
    /// <remarks>
    ///     The shape a per-document view has to carry a type whole for. A document that extracted only the
    ///     slice its own file holds would answer a rule about the controller from half a controller; one
    ///     that reported every part's site would print the other file's warning twice over.
    /// </remarks>
    internal static ParitySubject PartialAcrossTwoDocuments()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }\n\npublic class Ledger { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("Web.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public Data.Store Store;\n}"),
                    ("Web.Audit.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public Data.Ledger Ledger;\n}")
                ],
                "Data.Store")
            .Subject(
                "Sibling/partial across two documents",
                arch => arch.Rule("partial/web-not-through-data")
                    .Enforce(arch.Namespace("Web").MustNotReference(arch.Namespace("Data")))
                    .Because("The web layer reaches the store through a contract, whichever file it writes."));
    }

    /// <summary>
    ///     A shape rule broken by a type declared in two documents: the violation is about the subject rather
    ///     than about any one of its declarations, so it is reported once, at the declaration that sorts
    ///     first, and the other file says nothing.
    /// </summary>
    internal static ParitySubject ShapeOnAPartialType()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("A.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public string Reference;\n}"),
                    ("B.cs", "namespace Web;\n\npublic partial class OrderController\n{\n    public string Note;\n}")
                ])
            .Subject(
                "Sibling/shape on a partial type",
                arch => arch.Rule("shapes/controllers-are-sealed")
                    .Enforce(arch.Types.Named("OrderController").MustBeSealed())
                    .Because("A controller nobody may subclass is the one the host resolved."));
    }

    /// <summary>
    ///     The captured pair that has grown, with its two sites split across two documents: each file is red
    ///     for the site it holds, and each warning counts the whole pair rather than the file's share of it.
    /// </summary>
    internal static ParitySubject GrownPairAcrossTwoDocuments()
    {
        SiblingWorld world = new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("Web.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public Data.Store First;\n}"),
                    ("Web.More.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public Data.Store Second;\n}")
                ],
                "Data.Store");

        return GrownPairOver(world, "Sibling/grown pair across two documents");
    }

    /// <summary>
    ///     A nested type declared inside the second file of a partial container: the container's own crossing
    ///     is the first file's and the nested type's is the second's, though one type contains the other.
    /// </summary>
    internal static ParitySubject NestedTypeInASecondFile()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }\n\npublic class Ledger { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("Web.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public Data.Store Store;\n}"),
                    ("Web.Nested.cs",
                        "namespace Web;\n\npublic partial class OrderController\n{\n    public class Audit\n    {\n"
                        + "        public Data.Ledger Ledger;\n    }\n}")
                ],
                "Data.Store")
            .Subject(
                "Sibling/nested type in a second file",
                arch => arch.Rule("nested/web-not-through-data")
                    .Enforce(arch.Namespace("Web").MustNotReference(arch.Namespace("Data")))
                    .Because("The web layer reaches the store through a contract, nested types included."));
    }

    /// <summary>
    ///     A document that declares no type at all, beside one that breaks a rule: the empty file is analysed
    ///     like any other and has nothing to say.
    /// </summary>
    internal static ParitySubject ADocumentWithNoTypes()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("Web.cs", "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store Store;\n}"),
                    ("Empty.cs", "using System;\n\n// A file that declares no type, which no rule can be broken in.\n")
                ],
                "Data.Store")
            .Subject(
                "Sibling/a document with no types",
                arch => arch.Rule("inbound/data-not-from-web")
                    .Enforce(arch.Namespace("Data").MustNotBeReferencedBy(arch.Namespace("Web")))
                    .Because("The web layer reaches the store through a contract, never directly."));
    }

    /// <summary>
    ///     An inbound rule whose subject one document declares and whose offender another does, inside a
    ///     single project: the own project is a sibling of itself per document, which is what lets the
    ///     document holding the offender answer a rule about a type it does not declare.
    /// </summary>
    internal static ParitySubject AnInboundRuleWhoseSubjectIsInAnotherDocument()
    {
        return new SiblingWorld()
            .Project(
                "Web", "Web.Site",
                [
                    ("Core.cs", "namespace Web.Core;\n\npublic class Store { }"),
                    ("Ui.cs", "namespace Web.Ui;\n\npublic class Page\n{\n    public Web.Core.Store Store;\n}")
                ])
            .Subject(
                "Sibling/an inbound rule whose subject is in another document",
                arch => arch.Rule("inbound/core-not-from-the-ui")
                    .Enforce(arch.Namespace("Web.Core").MustNotBeReferencedBy(arch.Namespace("Web.Ui")))
                    .Because("A page reaches the store through the contract the core publishes."));
    }

    /// <summary>
    ///     A console project whose entry point is a file of top-level statements: the type the compiler
    ///     synthesizes for them is declared by no type declaration, and the file it is the body of is the
    ///     document that answers for it.
    /// </summary>
    internal static ParitySubject TopLevelStatementsProgram()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .ConsoleProject(
                "Web", "Web.Site",
                [("Program.cs", "var store = new Data.Store();\nSystem.Console.WriteLine(store);")],
                "Data.Store")
            .Subject(
                "Sibling/top-level statements",
                arch => arch.Rule("entry/web-not-through-data")
                    .Enforce(arch.Project("Web").MustNotReference(arch.Namespace("Data")))
                    .Because("The entry point composes the application and holds none of the store itself."));
    }

    /// <summary>
    ///     A type whose base the compilation has no metadata for, declared by one document and reached from
    ///     another inside the same project: the half-resolved hierarchy is no reason to drop a type compiled
    ///     here, so the document holding the reference reports it.
    /// </summary>
    /// <remarks>
    ///     The screen an unresolved base trips is about a sibling whose reference closure this compilation
    ///     cannot see, which is not something that can happen to its own code — so this is the row that says
    ///     the own project being a sibling of itself per document did not bring the screen with it.
    /// </remarks>
    internal static ParitySubject AnErrorBaseInAnotherDocument()
    {
        return new SiblingWorld()
            .Project(
                "Web", "Web.Site",
                [
                    ("Legacy.cs", "namespace Web.Legacy;\n\npublic class Broken : Missing.Base { }"),
                    ("Ui.cs", "namespace Web.Ui;\n\npublic class Consumer\n{\n    public Web.Legacy.Broken Broken;\n}")
                ])
            .Subject(
                "Sibling/an error base in another document",
                arch => arch.Rule("inbound/legacy-not-from-the-ui")
                    .Enforce(arch.Namespace("Web.Ui").MustNotReference(arch.Namespace("Web.Legacy")))
                    .Because("The legacy corner is reached through the seam that wraps it."));
    }

    /// <summary>
    ///     Two documents under one path, which a compilation built by hand can hold and a build cannot: the
    ///     first declarer of the path answers for it, and the second says nothing rather than reporting the
    ///     same path twice.
    /// </summary>
    internal static ParitySubject TwoTreesUnderOnePath()
    {
        return new SiblingWorld()
            .Project("Data", "Data.Store", "namespace Data;\n\npublic class Store { }")
            .Project(
                "Web", "Web.Site",
                [
                    ("Web.cs", "namespace Web;\n\npublic class OrderController\n{\n    public Data.Store Store;\n}"),
                    ("Web.cs", "namespace Web;\n\npublic class Other\n{\n    public string Name;\n}")
                ],
                "Data.Store")
            .Subject(
                "Sibling/two trees under one path",
                arch => arch.Rule("paths/web-not-through-data")
                    .Enforce(arch.Namespace("Web").MustNotReference(arch.Namespace("Data")))
                    .Because("The web layer reaches the store through a contract, however its files are named."));
    }

    /// <summary>The remainder: one rule per verb, over a two-project solution.</summary>
    internal static ParitySubject Remainder()
    {
        return new SiblingWorld()
            .Project(ParityRemainderSpec.SideProject, ParityRemainderSpec.SideAssembly, ParityRemainderSpec.SideSource)
            .Project(
                ParityRemainderSpec.OwnProject, ParityRemainderSpec.OwnAssembly,
                [
                    ($"{ParityRemainderSpec.OwnAssembly}.cs", ParityRemainderSpec.OwnSource),
                    (ParityRemainderSpec.FolderedPath, ParityRemainderSpec.FolderedSource)
                ],
                ParityRemainderSpec.SideAssembly)
            .Subject("Sibling/remainder", ParityRemainderSpec.Define);
    }

    // Two projects compiling one file's worth of source, each making the same reference out of its own
    // copy, and the project that reference lands in. The file paths are the assemblies' own, so the two
    // copies' sites are told apart by the key rather than sharing a line the way a real link's would.
    private static SiblingWorld DriftedCopies()
    {
        return new SiblingWorld()
            .Project("Ink", "Ink.Lib", "namespace Ink;\n\npublic class Pen { }")
            .Project("Mirror", "Mirror.Asm", SharedScribe, "Ink.Lib")
            .Project("Writer", "Writer.Asm", SharedScribe, "Ink.Lib");
    }

    /// <summary>The grown pair's rule and its one-site baseline, over <paramref name="world" />.</summary>
    private static ParitySubject GrownPairOver(SiblingWorld world, string name)
    {
        string baseline = BaselineComposer.Compose(
            "legacy/web-through-the-store",
            BaselineEntry.ForEdge("T:Web.OrderController", "T:Data.Store").WithSiteCount(1));

        return world.Subject(
            name,
            arch => arch.Rule("legacy/web-through-the-store")
                .Migrate(
                    from: "Controllers hold the store directly.",
                    to: arch.Namespace("Web").MustNotReference(arch.Namespace("Data")))
                .Baseline(BaselineDeclared)
                .Because("A controller that reaches the store puts persistence in the delivery path."),
            (BaselineDeclared, baseline));
    }

    /// <summary>
    ///     The in-memory subjects the verb sweep runs, which is every one whose verbs the classifier
    ///     decides and whose rules red over the whole solution — enough vocabulary between them to hold
    ///     every decidable constraint class to a diagnostic.
    /// </summary>
    /// <remarks>
    ///     Not every subject above, and it does not have to be: the sweep unions the verbs a subject
    ///     reported and asserts nothing about parity, which each subject's own fact does. A row whose verb
    ///     is held out, or that is there to pin an under-report, adds nothing here.
    /// </remarks>
    internal static IReadOnlyList<ParitySubject> ForInventory()
    {
        return [Remainder(), .. ForInventoryWithoutRemainder()];
    }

    /// <summary>The same subjects less the remainder, in the same order.</summary>
    internal static IReadOnlyList<ParitySubject> ForInventoryWithoutRemainder()
    {
        return
        [
            Inbound(),
            CapturedContainment(),
            RenamedAssembly(),
            GrownPair(),
            ErrorHierarchy(vouched: true),
            PartialAcrossTwoDocuments(),
            ShapeOnAPartialType(),
            GrownPairAcrossTwoDocuments(),
            NestedTypeInASecondFile(),
            ADocumentWithNoTypes(),
            AnInboundRuleWhoseSubjectIsInAnotherDocument(),
            TopLevelStatementsProgram(),
            AnErrorBaseInAnotherDocument(),
            TwoTreesUnderOnePath()
        ];
    }
}
