using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The member-use bed for attribution where several projects declare one full name (GRAMMAR §4.1, §4.5):
///     two copies of one type that drifted apart, each using members of a type neither declares, beside a
///     type only one project declares.
/// </summary>
/// <remarks>
///     <para>
///         Three projects, handed to extraction in ordinal order, none referencing another:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Core</c> — its own copy of <c>Drift.Formatter</c>, the winner's, reading
///             <c>DateTime.Now</c>.
///         </item>
///         <item>
///             <c>Satellite</c> — its own copy of the same full name, in a file of its own, reading
///             <c>DateTime.Now</c> and, where Core's copy does not, <c>DateTime.UtcNow</c>.
///         </item>
///         <item>
///             <c>Tools</c> — <c>Cells.Ticker</c>, which only it declares, reading <c>DateTime.Now</c>: the
///             control whose member edge keeps no instances.
///         </item>
///     </list>
///     <para>
///         So the <c>Drift.Formatter</c> use of <c>Now</c> is one member edge standing for two instances whose
///         sites lie in different files, and its use of <c>UtcNow</c> one edge only Satellite's copy makes.
///     </para>
/// </remarks>
internal static class DriftedMemberUsesCodebase
{
    // Core's copy, the winner's: one use of Now, at line 7.
    private const string CoreCopy = """
                                    namespace Drift
                                    {
                                        public class Formatter
                                        {
                                            public System.DateTime Stamp()
                                            {
                                                return System.DateTime.Now;
                                            }
                                        }
                                    }
                                    """;

    // Satellite's copy: the same use of Now at line 7, and a use of UtcNow at line 12 that Core's copy lacks.
    private const string SatelliteCopy = """
                                         namespace Drift
                                         {
                                             public class Formatter
                                             {
                                                 public System.DateTime Stamp()
                                                 {
                                                     return System.DateTime.Now;
                                                 }

                                                 public System.DateTime StampUtc()
                                                 {
                                                     return System.DateTime.UtcNow;
                                                 }
                                             }
                                         }
                                         """;

    // The control, which one project declares: a use of Now at line 5.
    private const string ToolsOwn = """
                                    namespace Cells
                                    {
                                        public class Ticker
                                        {
                                            public System.DateTime At() => System.DateTime.Now;
                                        }
                                    }
                                    """;

    /// <summary>The extracted bed, built once for every row that reads it.</summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput core = CompilationFactory.Compile("Core", ("Core/Drift/Formatter.cs", CoreCopy));
        CompilationInput satellite = CompilationFactory.Compile(
            "Satellite", ("Satellite/Drift/Formatter.cs", SatelliteCopy));
        CompilationInput tools = CompilationFactory.Compile("Tools", ("Tools/Cells/Ticker.cs", ToolsOwn));

        // Input order decides the winner, so Core leads Satellite.
        return Harness.Extract([core, satellite, tools]);
    }
}

/// <summary>
///     The reflectable twin of the type both clock projects in <see cref="CopiedClockCodebase" /> declare, for the
///     rows that anchor a member ban on it.
/// </summary>
/// <remarks>
///     Only the full name and the member's name have to agree with the bed's copies, since a member anchor
///     resolves against the model by (declaring type, name) and nothing here is ever called. It sits in this
///     file's namespace under a name only the bed uses, so the bed's copies declare that namespace too.
/// </remarks>
public static class CopiedClock
{
    /// <summary>The member each consumer in the bed uses, and the rows ban.</summary>
    public static DateTime Read()
    {
        return default;
    }
}

/// <summary>
///     The member-side bed for attribution where several projects declare one full name (GRAMMAR §4.1, §4.5): two
///     copies of one declaring type that drifted apart, and a consumer of each that uses the same member.
/// </summary>
/// <remarks>
///     <para>
///         Four projects, handed to extraction in ordinal order:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Billing</c> — a reference to <c>ClockCore</c>'s compilation, and its own copy of
///             <c>Stamps.Stamp</c>, calling <see cref="CopiedClock.Read" />.
///         </item>
///         <item>
///             <c>ClockCore</c> — its own copy of <see cref="CopiedClock" />, the winner's, reading
///             <c>DateTime.Now</c>.
///         </item>
///         <item>
///             <c>ClockShim</c> — its own copy of the same full name, in a file of its own, reading
///             <c>DateTime.UtcNow</c>.
///         </item>
///         <item>
///             <c>Ledger</c> — a reference to <c>ClockShim</c>'s compilation, and its own copy of
///             <c>Stamps.Stamp</c>, making the same call.
///         </item>
///     </list>
///     <para>
///         So the use is one member edge out of <c>Stamps.Stamp</c> standing for two instances: Billing's, which
///         reached ClockCore's copy, and Ledger's, which reached ClockShim's, whose facts the node does not carry.
///     </para>
/// </remarks>
internal static class CopiedClockCodebase
{
    // ClockCore's copy, the winner's.
    private const string ClockCoreCopy = """
                                         namespace SlideRule.Tests.Checking
                                         {
                                             public static class CopiedClock
                                             {
                                                 public static System.DateTime Read()
                                                 {
                                                     return System.DateTime.Now;
                                                 }
                                             }
                                         }
                                         """;

    // ClockShim's copy of the same full name, drifted to a different body.
    private const string ClockShimCopy = """
                                         namespace SlideRule.Tests.Checking
                                         {
                                             public static class CopiedClock
                                             {
                                                 public static System.DateTime Read()
                                                 {
                                                     return System.DateTime.UtcNow;
                                                 }
                                             }
                                         }
                                         """;

    // Each consumer's own copy of the caller: the call to Read at line 6.
    private const string StampCopy = """
                                     namespace Stamps
                                     {
                                         public class Stamp
                                         {
                                             public System.DateTime Take()
                                                 => SlideRule.Tests.Checking.CopiedClock.Read();
                                         }
                                     }
                                     """;

    /// <summary>The extracted bed, built once for every row that reads it.</summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput clockCore = CompilationFactory.Compile(
            "ClockCore", ("ClockCore/CopiedClock.cs", ClockCoreCopy));
        CompilationInput clockShim = CompilationFactory.Compile(
            "ClockShim", ("ClockShim/CopiedClock.cs", ClockShimCopy));
        CompilationInput billing = CompilationFactory.CompileReferencing(
            "Billing", clockCore.Compilation, "ClockCore", ("Billing/Stamps/Stamp.cs", StampCopy));
        CompilationInput ledger = CompilationFactory.CompileReferencing(
            "Ledger", clockShim.Compilation, "ClockShim", ("Ledger/Stamps/Stamp.cs", StampCopy));

        // Input order decides the winners, so ClockCore leads ClockShim and Billing leads Ledger.
        return Harness.Extract([billing, clockCore, clockShim, ledger]);
    }
}

/// <summary>
///     Member-use attribution where several projects declare one full name (GRAMMAR §4.1, §4.5): a
///     <c>MustNotUse</c> subject owns the uses of the copies it names the type at, and cites their lines alone,
///     while the banned member is still matched by name in every copy of its declaring type.
/// </summary>
/// <remarks>
///     <para>
///         The first four rows read <see cref="DriftedMemberUsesCodebase" />. A project-headed subject names
///         <c>Drift.Formatter</c> at its own project only, so it skips a use only the other copy makes and cites
///         its own copy's line; a namespace-headed subject names the type at every project, so it cites both.
///         The fourth row pins the instances the merge records, which is what the verdicts above read.
///     </para>
///     <para>
///         The last two rows read <see cref="CopiedClockCodebase" />, where the banned member's declaring type is the
///         shared name: the ban reaches the use whichever copy it reached, while the subject still bounds the
///         evidence to the consumers it names.
///     </para>
///     <para>
///         Site lists are asserted exactly and in order, for the reason <see cref="MultiplyDeclaredSitesTests" />
///         gives: a violation citing a superset is the very defect these rows exist for.
///     </para>
/// </remarks>
public sealed class MultiplyDeclaredMemberUseTests
{
    [Fact]
    public void MustNotUse_DriftedCopies_AProjectAnchoredBanSkipsAUseOnlyTheOtherCopyMakes()
    {
        // Only Satellite's copy reads UtcNow, so a ban anchored on Core owns no instance of that use.
        Checker.Run(DriftedMemberUsesCodebase.Model, arch =>
                arch.Rule("time/core-no-utc")
                    .Enforce(arch.Project("Core").MustNotUse(() => DateTime.UtcNow))
                    .Because("The core reads the clock through its own seam."))
            .Single()
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotUse_DriftedCopies_CitesTheAnchoredDeclarersOwnUse()
    {
        // Both copies read Now, each in its own file, so each project-headed ban cites its own copy's line.
        Checker.Run(DriftedMemberUsesCodebase.Model, arch =>
                arch.Rule("time/satellite-no-now")
                    .Enforce(arch.Project("Satellite").MustNotUse(() => DateTime.Now))
                    .Because("The satellite reads the clock through its own seam."))
            .Single()
            .ShouldHaveFailedWithMemberUseAtSites(
                "Drift.Formatter", "System.DateTime.Now", ["Satellite/Drift/Formatter.cs:7"]);

        Checker.Run(DriftedMemberUsesCodebase.Model, arch =>
                arch.Rule("time/core-no-now")
                    .Enforce(arch.Project("Core").MustNotUse(() => DateTime.Now))
                    .Because("The core reads the clock through its own seam."))
            .Single()
            .ShouldHaveFailedWithMemberUseAtSites(
                "Drift.Formatter", "System.DateTime.Now", ["Core/Drift/Formatter.cs:7"]);
    }

    [Fact]
    public void MustNotUse_DriftedCopies_AnAttributionInsensitiveSubjectCitesBothCopiesUses()
    {
        // The complement: a head that is not a project owns every copy's use, so the evidence is the edge's
        // whole list, Core's line then Satellite's in the extractor's (file, line) order, from the edge's own
        // list object.
        MemberEdge now = DriftedMemberUsesCodebase.Model.MemberEdge("Drift.Formatter", "P:System.DateTime.Now");

        Checker.Run(DriftedMemberUsesCodebase.Model, arch =>
                arch.Rule("time/drift-no-now")
                    .Enforce(arch.Namespace("Drift").MustNotUse(() => DateTime.Now))
                    .Because("Formatting reads the clock through its own seam."))
            .Single()
            .ShouldHaveFailedWithMemberUseAtSites(
                "Drift.Formatter", "System.DateTime.Now",
                ["Core/Drift/Formatter.cs:7", "Satellite/Drift/Formatter.cs:7"])
            .Sites.ShouldBeSameAs(now.Sites);
    }

    [Fact]
    public void MemberEdges_DriftedCopies_RecordAnInstancePerCopyThatMadeTheUse()
    {
        // TargetProject is left unpinned throughout: it is the DateTime node's assembly name, which depends on
        // the runtime the bed is compiled against.
        CodebaseModel bed = DriftedMemberUsesCodebase.Model;
        MemberEdge now = bed.MemberEdge("Drift.Formatter", "P:System.DateTime.Now");
        MemberEdge utcNow = bed.MemberEdge("Drift.Formatter", "P:System.DateTime.UtcNow");
        MemberEdge control = bed.MemberEdge("Cells.Ticker", "P:System.DateTime.Now");

        IReadOnlyList<EdgeInstance> nowInstances = now.Evidence.Instances.ShouldNotBeNull();
        IReadOnlyList<EdgeInstance> utcNowInstances = utcNow.Evidence.Instances.ShouldNotBeNull();

        nowInstances.ShouldSatisfyAllConditions(
            () => nowInstances.Select(instance => instance.SourceProject)
                .ShouldBe(["Core", "Satellite"]),
            () => nowInstances.Select(instance => string.Join(", ", instance.Sites))
                .ShouldBe(["Core/Drift/Formatter.cs:7", "Satellite/Drift/Formatter.cs:7"]));

        // The one copy that made the use made every site of it, so its share is the edge's own list.
        EdgeInstance satelliteUtcNow = utcNowInstances.ShouldHaveSingleItem();
        satelliteUtcNow.ShouldSatisfyAllConditions(
            () => satelliteUtcNow.SourceProject.ShouldBe("Satellite"),
            () => satelliteUtcNow.Sites.ShouldBeSameAs(utcNow.Sites));

        control.Evidence.Instances.ShouldBeNull();
    }

    [Fact]
    public void MustNotUse_CopiedDeclaringType_ANonProjectSubjectCitesEveryConsumersUse()
    {
        // The source is one name, Stamps.Stamp, because each consumer compiles its own copy of it, so the two
        // calls are one edge with two instances. The anchor names no project, so the ban reaches both, Billing's
        // into ClockCore's copy and Ledger's into ClockShim's.
        Checker.Run(CopiedClockCodebase.Model, arch =>
                arch.Rule("time/stamps-no-clock")
                    .Enforce(arch.Namespace("Stamps")
                        .MustNotUse(arch.Member(typeof(CopiedClock), nameof(CopiedClock.Read))))
                    .Because("Stamps take the time they are given."))
            .Single()
            .ShouldHaveFailedWithMemberUseAtSites(
                "Stamps.Stamp", "SlideRule.Tests.Checking.CopiedClock.Read",
                ["Billing/Stamps/Stamp.cs:6", "Ledger/Stamps/Stamp.cs:6"]);
    }

    [Fact]
    public void MustNotUse_CopiedDeclaringType_AProjectSubjectCitesItsOwnUseOfTheCopyItReached()
    {
        // Ledger's call reached ClockShim's copy, whose facts the node does not carry, and the ban still matches it;
        // the project-headed subject owns Ledger's instance alone, so only Ledger's line is cited.
        Checker.Run(CopiedClockCodebase.Model, arch =>
                arch.Rule("time/ledger-no-clock")
                    .Enforce(arch.Project("Ledger")
                        .MustNotUse(arch.Member(typeof(CopiedClock), nameof(CopiedClock.Read))))
                    .Because("The ledger takes the time it is given."))
            .Single()
            .ShouldHaveFailedWithMemberUseAtSites(
                "Stamps.Stamp", "SlideRule.Tests.Checking.CopiedClock.Read", ["Ledger/Stamps/Stamp.cs:6"]);

        MemberEdge read = CopiedClockCodebase.Model.MemberEdge(
            "Stamps.Stamp", "M:SlideRule.Tests.Checking.CopiedClock.Read");
        IReadOnlyList<EdgeInstance> instances = read.Evidence.Instances.ShouldNotBeNull();
        instances.Select(instance => $"{instance.SourceProject}->{instance.TargetProject}")
            .ShouldBe(["Billing->ClockCore", "Ledger->ClockShim"]);
    }
}
