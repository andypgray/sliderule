using System.Reflection;
using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.SpecLoading;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The spec contract binds on the assembly, never on its version. A host carries exactly one copy of
///     <c>SlideRule</c>, and the default binder rejects a copy older than the reference a spec was
///     compiled against — so while the shipped identity tracked the package version, every release minted a
///     new one and any spec built against a newer contract failed to load at all, reported as a plain
///     missing file. These pin the bind that replaced it, and the failures it defers to.
/// </summary>
public sealed class SpecContractBindTests
{
    private static Version HostContractVersion => typeof(IArchitectureSpec).Assembly.GetName()
        .Version!;

    [Theory]
    [InlineData("0.1.0.0")] // identities older than the pin
    [InlineData("0.2.0.0")]
    [InlineData("1.0.0.0")] // the pinned identity, and what a source-built or vendored contract carries
    [InlineData("9.9.9.9")] // and a version no host will ever carry
    public void LoadFromAssemblyName_ContractAtAnyVersion_ResolvesToTheHostsOwnCopy(string version)
    {
        var context = new SpecLoadContext(CliRunner.CleanSpecDll);
        var requested = new AssemblyName(SpecLoadContext.ContractAssemblyName) { Version = Version.Parse(version) };

        try
        {
            Assembly resolved = context.LoadFromAssemblyName(requested);

            // Same instance, not merely the same name: this is what makes `spec is IArchitectureSpec` hold
            // across the load-context boundary, and it is the whole reason the contract is short-circuited.
            resolved.ShouldBeSameAs(
                typeof(IArchitectureSpec).Assembly,
                $"a spec compiled against contract {version} must bind to the contract this host carries; "
                + "binding on the version instead is what failed specs that would have run perfectly.");
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void Map_SpecBuiltAgainstANewerContract_NamesBothVersionsTheMissingMemberAndTheRemedy()
    {
        Assembly spec = SpecReferencing(new Version(2, 5, 0, 0));
        var missing = new MissingMethodException("Method not found: 'Void SlideRule.Arch.FutureVerb()'.");

        string message = SpecContractMismatch.Map(spec, Path.Combine("out", "Sync.Architecture.dll"), missing)
            .Message;

        message.ShouldBe(
            "The spec assembly 'Sync.Architecture' uses SlideRule API this tool's contract does not have "
            + $"(the spec was built against contract 2.5.0.0, this tool carries {HostContractVersion}):\n"
            + "  Method not found: 'Void SlideRule.Arch.FutureVerb()'.\n"
            + "That is a spec built against a newer SlideRule package than the tool running it.\n"
            + "Update the tool to at least the spec's version (dotnet tool update -g SlideRule.Cli — this "
            + $"one is {ServerVersion.SemVer}), or reference the SlideRule package that matches this tool "
            + "from the spec project and rebuild it.");
    }

    [Fact]
    public void MapInaccessible_SpecNamingAnEngineType_NamesWhatWasRefusedAndTheRemedy()
    {
        // The opposite skew: a spec built against a contract whose engine types were public, run by one that keeps
        // them internal. The runtime reports an access failure, and the remedy is the authoring surface.
        Assembly spec = SpecReferencing(HostContractVersion);
        var denied = new TypeAccessException(
            "Attempt by method 'Sync.Architecture.Define(SlideRule.Arch)' to access type "
            + "'SlideRule.Rendering.PathFormat' failed.");

        string message = SpecContractMismatch.MapInaccessible(spec, "Sync.Architecture.dll", denied)
            .Message;

        message.ShouldBe(
            "The spec assembly 'Sync.Architecture' uses SlideRule API that is no longer public:\n"
            + "  Attempt by method 'Sync.Architecture.Define(SlideRule.Arch)' to access type "
            + "'SlideRule.Rendering.PathFormat' failed.\n"
            + "That is a spec built against an older SlideRule package, whose engine types were public; a spec "
            + "now reaches the engine only through the types in the SlideRule namespace.\n"
            + "Rewrite what it names with those types, reference the SlideRule package that matches this tool "
            + $"({ServerVersion.SemVer}) from the spec project, and rebuild it.");
    }

    [Fact]
    public void MapSkew_MissingMemberNamingAMovedSelectionType_SaysTheSpecIsOlder()
    {
        // What a spec built before the selection types left SlideRule.Fluent raised against this contract: the
        // projection still exists, but its return type moved. The runtime words it as any missing member, and a
        // newer-contract refusal would tell the operator to update a tool that is already the newer side.
        Assembly spec = SpecReferencing(HostContractVersion);
        var missing = new MissingMethodException(
            "Method not found: 'SlideRule.Fluent.MethodSelection SlideRule.Selection.get_Methods()'.");

        string message = SpecContractMismatch.MapSkew(spec, "Sync.Architecture.dll", missing)
            .Message;

        message.ShouldBe(
            "The spec assembly 'Sync.Architecture' uses SlideRule types under a namespace this tool's contract "
            + "moved them out of:\n"
            + "  Method not found: 'SlideRule.Fluent.MethodSelection SlideRule.Selection.get_Methods()'.\n"
            + "That is a spec built against an older SlideRule package, where MemberSelection, MethodSelection, "
            + "FieldSelection, PropertySelection and ProjectSelection were in the SlideRule.Fluent namespace; they "
            + "are now in SlideRule.\n"
            + $"Reference the SlideRule package that matches this tool ({ServerVersion.SemVer}) from the spec "
            + "project and rebuild it.");
    }

    [Fact]
    public void MapSkew_MissingTypeThatIsAMovedSelectionType_SaysTheSpecIsOlder()
    {
        // A spec that held a selection in a local of the old type fails as a type that will not load instead.
        Assembly spec = SpecReferencing(HostContractVersion);
        var missing = new TypeLoadException(
            "Could not load type 'SlideRule.Fluent.ProjectSelection' from assembly 'SlideRule, Version=1.0.0.0'.");

        string message = SpecContractMismatch.MapSkew(spec, "Sync.Architecture.dll", missing)
            .Message;

        message.ShouldContain("That is a spec built against an older SlideRule package");
        message.ShouldNotContain("newer");
    }

    [Fact]
    public void MapSkew_MissingMemberNamingNoMovedType_KeepsTheNewerContractRefusal()
    {
        Assembly spec = SpecReferencing(HostContractVersion);
        var missing = new MissingMethodException("Method not found: 'Void SlideRule.Arch.FutureVerb()'.");

        string message = SpecContractMismatch.MapSkew(spec, "Sync.Architecture.dll", missing)
            .Message;

        message.ShouldStartWith(
            "The spec assembly 'Sync.Architecture' uses SlideRule API this tool's contract does not have");
        message.ShouldContain("That is a spec built against a newer SlideRule package than the tool running it.");
    }

    [Fact]
    public void Map_SpecBuiltAgainstThisContract_OmitsTheVersionParenthetical()
    {
        // The normal case, with the identity pinned: the versions match and naming them twice would
        // say nothing. Whatever went missing is still named, because that is the part the operator can act on.
        Assembly spec = SpecReferencing(HostContractVersion);
        var missing = new MissingMethodException("Method not found: 'Void SlideRule.Arch.FutureVerb()'.");

        string message = SpecContractMismatch.Map(spec, "Sync.Architecture.dll", missing)
            .Message;

        message.ShouldStartWith(
            "The spec assembly 'Sync.Architecture' uses SlideRule API this tool's contract does not have:\n"
            + "  Method not found: 'Void SlideRule.Arch.FutureVerb()'.");
    }

    [Theory]
    [InlineData("SlideRule.FutureConstraints")] // a class a newer contract adds to the root namespace…
    [InlineData("SlideRule.Fluent.IFutureStage")] // …or to a namespace below it…
    [InlineData("SlideRule.Previews.FutureVerbs")] // …including a namespace this copy never declared
    public void IsMissingContractType_NameUnderTheContractThatThisHostLacks_IsTrue(string typeName)
    {
        SpecContractMismatch.IsMissingContractType(typeName)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("System.Web.IHttpHandler")] // the .NET Framework-only type the other remedy is written for
    [InlineData("SlideRule.Arch")] // a contract type this host does define
    [InlineData("SlideRuleExtras.Future")] // the root's spelling without its namespace boundary
    [InlineData("")] // what a TypeLoadException carries when the runtime did not raise it
    public void IsMissingContractType_AnyOtherName_IsFalse(string typeName)
    {
        SpecContractMismatch.IsMissingContractType(typeName)
            .ShouldBeFalse();
    }

    [Fact]
    public void SkewCause_AMissingMemberAConstructorRaised_IsUnwrappedFromTheReflectionThatRanIt()
    {
        // Activator.CreateInstance wraps whatever a spec class's constructor throws.
        var missing = new MissingMethodException("Method not found: 'Void SlideRule.Arch.FutureVerb()'.");

        SpecContractMismatch.SkewCause(new TargetInvocationException(missing))
            .ShouldBeSameAs(missing);
    }

    [Fact]
    public void SkewCause_AMissingMemberBehindATypeInitializer_IsUnwrapped()
    {
        var missing = new MissingMethodException("Method not found: 'Void SlideRule.Arch.FutureVerb()'.");

        SpecContractMismatch.SkewCause(new TypeInitializationException("ContractSkew.EagerSpec", missing))
            .ShouldBeSameAs(missing);
    }

    [Fact]
    public void MapSkew_AnAccessFailure_IsTheOlderContractRefusal()
    {
        // A TypeAccessException is also a TypeLoadException; it must still be refused as the older contract, not
        // the newer one.
        Assembly spec = SpecReferencing(HostContractVersion);
        var denied = new TypeAccessException(
            "Attempt by method 'Sync.Architecture.Define(SlideRule.Arch)' to access type "
            + "'SlideRule.Rendering.PathFormat' failed.");

        Exception? cause = SpecContractMismatch.SkewCause(denied);

        cause.ShouldBeSameAs(denied);
        SpecContractMismatch.MapSkew(spec, "Sync.Architecture.dll", denied)
            .Message
            .ShouldStartWith("The spec assembly 'Sync.Architecture' uses SlideRule API that is no longer public");
    }

    // The runtime's own messages, recorded on .NET 10 from a spec compiled against a contract with these members and
    // run against one without them, one row per shape a spec can reach.
    [Theory]
    [InlineData("Method not found: 'Void SlideRule.Arch.FutureVerb(System.String)'.")] // an instance method
    [InlineData( // an extension method on a constraints class
        "Method not found: 'SlideRule.Constraint SlideRule.SelectionConstraints.MustBeFuture(SlideRule.Selection)'.")]
    [InlineData("Method not found: 'SlideRule.Selection SlideRule.Arch.Type()'.")] // a generic method
    [InlineData("Method not found: '!!0 SlideRule.Arch.Shape(!!0)'.")] // one returning its type parameter
    [InlineData( // a generic return type
        "Method not found: 'System.Collections.Generic.Dictionary`2<System.String,SlideRule.Selection> SlideRule.Arch.FutureMap()'.")]
    [InlineData("Method not found: 'SlideRule.Selection SlideRule.Arch.get_FutureProp()'.")] // a property
    [InlineData( // a method of a stage interface
        "Method not found: 'SlideRule.Fluent.IEnforceRule SlideRule.Fluent.IEnforceRule.FutureStage(System.String)'.")]
    [InlineData("Method not found: 'Void SlideRule.Arch..ctor(Int32)'.")] // a constructor
    [InlineData("Field not found: 'SlideRule.Arch.FutureField'.")] // a field
    public void SkewCause_AMissingContractMember_IsSkew(string message)
    {
        MissingMemberException missing = MissingMember(message);

        SpecContractMismatch.SkewCause(missing)
            .ShouldBeSameAs(missing);
    }

    [Theory]
    [InlineData( // a rule pack: under the contract's root namespace, in an assembly of its own
        "Method not found: 'SlideRule.Constraint SlideRule.Packs.DotNet.NamingRules.MustBePolite(SlideRule.Selection, System.String)'.")]
    [InlineData( // another dependency, whose return and parameter types are the contract's
        "Method not found: 'SlideRule.Constraint Acme.Rules.Naming.MustBePolite(SlideRule.Selection)'.")]
    [InlineData("Field not found: 'Acme.Rules.Naming.Default'.")]
    public void SkewCause_AMissingMemberOfAnotherAssembly_IsNotSkew(string message)
    {
        SpecContractMismatch.SkewCause(MissingMember(message))
            .ShouldBeNull();
    }

    [Fact]
    public void SkewCause_AMissingPackMemberAConstructorRaised_IsNotSkewOnceUnwrapped()
    {
        var missing = new MissingMethodException(
            "Method not found: 'SlideRule.Constraint SlideRule.Packs.DotNet.NamingRules.MustBePolite(SlideRule.Selection, System.String)'.");

        SpecContractMismatch.SkewCause(new TargetInvocationException(missing))
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("Method not found.")] // no quoted signature
    [InlineData("")]
    [InlineData("Method not found: '")] // one quote
    [InlineData("Method not found: 'FutureVerb'.")] // a name with no type
    [InlineData("Method not found: 'Void Inner.FutureNested()'.")] // how the runtime prints a nested type
    [InlineData("Method not found: 'Void SlideRule.Arch&.FutureVerb()'.")] // what the type parser reads as Arch's byref
    public void SkewCause_AMissingMemberMessageNamingNoContractType_IsNotSkew(string message)
    {
        SpecContractMismatch.SkewCause(MissingMember(message))
            .ShouldBeNull();
    }

    [Fact]
    public void SkewCause_AnythingElse_IsNotSkew()
    {
        Exception[] others =
        [
            new TypeLoadException("Could not load type."), // not raised by the runtime, so it names no type
            new FileNotFoundException("Could not load file or assembly 'Some.Dependency'."),
            new InvalidOperationException("The spec failed for its own reasons."),
            new TargetInvocationException(new InvalidOperationException("A constructor failed for its own reasons."))
        ];

        others.Select(SpecContractMismatch.SkewCause)
            .ShouldAllBe(cause => cause == null);
    }

    // The runtime raises the field form for a field and the method form for everything else.
    private static MissingMemberException MissingMember(string message)
    {
        return message.StartsWith("Field", StringComparison.Ordinal)
            ? new MissingFieldException(message)
            : new MissingMethodException(message);
    }

    /// <summary>
    ///     A stand-in spec assembly whose only interesting property is the contract version it references —
    ///     faked rather than built, so each arm pins exactly one variable and none needs a second contract
    ///     compiled at a different identity. Hand-rolled rather than mocked: this project carries no mocking
    ///     package, and one override is not worth adding one to a locked dependency set.
    /// </summary>
    private static Assembly SpecReferencing(Version contractVersion)
    {
        return new SpecAssemblyStub(contractVersion);
    }

    private sealed class SpecAssemblyStub(Version contractVersion) : Assembly
    {
        public override AssemblyName[] GetReferencedAssemblies()
        {
            return [new AssemblyName(SpecLoadContext.ContractAssemblyName) { Version = contractVersion }];
        }
    }
}
