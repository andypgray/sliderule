using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Pins that each sequence overload builds what its <c>(first, more)</c> sibling builds from the same
///     operands: the same rendered sentence and the same model text, so a spec that computes its list in a
///     helper method reads and checks exactly as one that writes the operands out.
/// </summary>
public sealed class SequenceOverloadTests
{
    [Fact]
    public void AnyOf_LayerList_IsTakenAsASelectionSequence()
    {
        // IEnumerable<T> is covariant, so a List<Layer> a helper built binds the sequence overload as it is.
        var arch = new Arch();
        Layer web = arch.Layer("Web", "MyApp.Web.*");
        Layer api = arch.Layer("Api", "MyApp.Api.*");
        List<Layer> layers = [web, api];

        Checker.ShouldReifyAlike(arch.AnyOf(layers).MustBeSealed(), arch.AnyOf(web, api).MustBeSealed());
    }

    [Fact]
    public void AnyOf_TypeSequence_BuildsTheParamsUnion()
    {
        var arch = new Arch();
        List<Type> types = [typeof(Uri), typeof(Version)];

        Checker.ShouldReifyAlike(
            arch.Types.MustNotReference(arch.AnyOf(types)),
            arch.Types.MustNotReference(arch.AnyOf(typeof(Uri), typeof(Version))));
    }

    [Fact]
    public void ProjectsNamed_Sequence_BuildsTheParamsAdjective()
    {
        var arch = new Arch();
        List<string> names = ["MyApp.Web", "MyApp.Api"];

        Checker.ShouldReifyAlike(
            arch.Projects.Named(names).MustNotBePackable(),
            arch.Projects.Named("MyApp.Web", "MyApp.Api").MustNotBePackable());
    }

    [Fact]
    public void ProjectsMatching_Sequence_BuildsTheParamsAdjective()
    {
        var arch = new Arch();
        List<string> globs = ["MyApp.*", "Shared.*"];

        Checker.ShouldReifyAlike(
            arch.Projects.Matching(globs).MustLockPackages(),
            arch.Projects.Matching("MyApp.*", "Shared.*").MustLockPackages());
    }

    [Fact]
    public void MemberNamed_Sequence_BuildsTheParamsAdjective()
    {
        var arch = new Arch();
        List<string> names = ["Main", "Run"];

        Checker.ShouldReifyAlike(
            arch.Types.Methods.Named(names).MustBePublic(),
            arch.Types.Methods.Named("Main", "Run").MustBePublic());
    }

    [Fact]
    public void MemberExcept_MethodSelectionList_IsTakenAsAMemberSelectionSequence()
    {
        // The covariant conversion again: a helper's List<MethodSelection> needs no cast to MemberSelection.
        var arch = new Arch();
        MethodSelection main = arch.Types.Named("Program").Methods.Named("Main");
        MethodSelection run = arch.Types.Named("Worker").Methods.Named("Run");
        List<MethodSelection> exclusions = [main, run];

        Checker.ShouldReifyAlike(
            arch.Types.Methods.Except(exclusions).MustBePublic(),
            arch.Types.Methods.Except(main, run).MustBePublic());
    }

    [Fact]
    public void MustNotUse_MemberSequence_BuildsTheParamsConstraint()
    {
        var arch = new Arch();
        Member now = arch.Member(typeof(DateTime), nameof(DateTime.Now));
        Member utcNow = arch.Member(typeof(DateTime), nameof(DateTime.UtcNow));
        List<Member> members = [now, utcNow];

        Checker.ShouldReifyAlike(arch.Types.MustNotUse(members), arch.Types.MustNotUse(now, utcNow));
    }
}
