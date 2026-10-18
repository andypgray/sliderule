namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     A two-project solution and a spec that breaks every verb over it, so the in-memory corpus reds each
///     verb whether or not a bed does.
/// </summary>
/// <remarks>
///     <para>
///         <c>ParityInventoryTests</c> holds the in-memory corpus to every verb the decidability table lets
///         through, and it never sweeps the beds. So a verb the table comes to decide needs a rule here in
///         the same change, even where a bed already reds it.
///     </para>
///     <para>
///         Every rule's subject is an own type of <c>Own</c>, because a verdict about a sibling's own
///         types is sited at the sibling and is not this project's to report. Every rule's subject also
///         matches something: a rule whose subject matched nothing would report an empty subject rather
///         than a violation, which the pin holds out and the inventory would then miss.
///     </para>
/// </remarks>
internal static class ParityRemainderSpec
{
    /// <summary>The project of the solution whose build every fact analyses.</summary>
    internal const string OwnProject = "Remainder.Own";

    /// <summary>The assembly it compiles to, which is not its name.</summary>
    internal const string OwnAssembly = "Remainder.Own.Lib";

    /// <summary>The sibling project every outward reference reaches.</summary>
    internal const string SideProject = "Remainder.Side";

    /// <summary>The assembly the sibling compiles to, which is not its name either.</summary>
    internal const string SideAssembly = "Remainder.Side.Lib";

    /// <summary>The sibling's types: everything the own project reaches across the boundary.</summary>
    internal const string SideSource =
        """
        namespace Side;

        public class Store
        {
            public void Open() { }
        }

        public class Ledger { }

        public class Failure : System.Exception { }

        public interface IPlug { }

        public class Root { }

        [System.AttributeUsage(System.AttributeTargets.All)]
        public class StampAttribute : System.Attribute { }
        """;

    /// <summary>The own project's types, one per verb the spec below states.</summary>
    internal const string OwnSource =
        """
        namespace Own;

        public class Unsealed { }

        public class Instanced { }

        public class Concrete { }

        internal class Hidden { }

        public class Exposed { }

        public class Reacher
        {
            public Side.Store Held;
        }

        public class Builder
        {
            public void Make() { var made = new Side.Store(); }
        }

        public class Injected
        {
            public Injected(Side.Store store) { }
        }

        public class Exposer
        {
            public Side.Ledger Held;
        }

        public class Catcher
        {
            public void Run()
            {
                try { System.Console.Write(1); } catch (Side.Failure) { System.Console.Write(2); }
            }
        }

        public class Unfiltered
        {
            public void Run()
            {
                try { System.Console.Write(1); } catch (System.Exception) { System.Console.Write(2); }
            }
        }

        public class Swallower
        {
            public void Run()
            {
                try { System.Console.Write(1); } catch (System.InvalidOperationException) { }
            }
        }

        public class Thrower
        {
            public void Run() { throw new Side.Failure(); }
        }

        public class Plugless { }

        public class Rootless { }

        public class Unstamped { }

        public class Plugged : Side.IPlug { }

        public class Rooted : Side.Root { }

        [Side.Stamp]
        public class Stamped { }

        public class Misplaced { }

        public class WrongTail { }

        public class WrongHead { }

        public class WrongShape { }

        public class Clock
        {
            public void Read() { var now = System.DateTime.Now; }
        }

        public class Fields
        {
            public int Writable;
            internal int Shy;
        }

        public class Properties
        {
            public int Settable { get; set; }
            public int Loud { get; set; }
        }

        public class Methods
        {
            public void Instance() { }
            public void Fixed() { }
            public void TailMissing() { }
            public void HeadMissing() { }
            public void ShapeMissing() { }
            public void Unmarked() { }
            [Side.Stamp]
            public void Marked() { }
            public void NoToken() { }
            private void Loud() { }
        }
        """;

    /// <summary>Every rule of the remainder, one per verb.</summary>
    internal static void Define(Arch arch)
    {
        DefineShapes(arch);
        DefineReach(arch);
        DefineEdges(arch);
        DefineHierarchy(arch);
        DefinePlaceAndName(arch);
        DefineMembers(arch);
    }

    private static void DefineShapes(Arch arch)
    {
        arch.Rule("remainder/sealed")
            .Enforce(arch.Types.Named("Unsealed").MustBeSealed())
            .Because("A type nobody may subclass is the one the caller resolved.");

        arch.Rule("remainder/static")
            .Enforce(arch.Types.Named("Instanced").MustBeStatic())
            .Because("It holds no state, and an instance of it is a call nobody can reach.");

        arch.Rule("remainder/abstract")
            .Enforce(arch.Types.Named("Concrete").MustBeAbstract())
            .Because("The base carries the shared decision order; only its cases are instantiable.");

        arch.Rule("remainder/public")
            .Enforce(arch.Types.Named("Hidden").MustBePublic())
            .Because("It is what callers outside this solution compile against.");

        arch.Rule("remainder/internal")
            .Enforce(arch.Types.Named("Exposed").MustBeInternal())
            .Because("It is free to change, which only holds while nothing outside can name it.");
    }

    private static void DefineReach(Arch arch)
    {
        arch.Rule("remainder/only-references-itself")
            .Enforce(arch.Types.Named("Reacher").MustOnlyReferenceItself())
            .Because("It ships on its own cadence, which stops being true the moment it reaches outside itself.");

        arch.Rule("remainder/only-references-the-ledger")
            .Enforce(arch.Types.Named("Reacher").MustOnlyReference(arch.Types.Named("Ledger")))
            .Because("The ledger is the one thing it was given to read.");

        arch.Rule("remainder/does-not-reference-the-store")
            .Enforce(arch.Types.Named("Reacher").MustNotReference(arch.Types.Named("Store")))
            .Because("The store is the sibling's to open, and holding one puts persistence in this layer.");

        arch.Rule("remainder/store-not-from-here")
            .Enforce(arch.Types.Named("Store").MustNotBeReferencedBy(arch.Namespace("Own")))
            .Because("Nothing here is a caller of the store.");

        arch.Rule("remainder/store-only-from-the-ledger")
            .Enforce(arch.Types.Named("Store").MustOnlyBeReferencedBy(arch.Types.Named("Ledger")))
            .Because("The ledger is the store's one caller.");

        arch.Rule("remainder/store-only-from-itself")
            .Enforce(arch.Types.Named("Store").MustOnlyBeReferencedByItself())
            .Because("The store is reached through the ledger, never directly.");
    }

    private static void DefineEdges(Arch arch)
    {
        arch.Rule("remainder/does-not-construct")
            .Enforce(arch.Types.Named("Builder").MustNotConstruct(arch.Types.Named("Store")))
            .Because("A store is handed over by the host, never made here.");

        arch.Rule("remainder/does-not-inject")
            .Enforce(arch.Types.Named("Injected").MustNotInject(arch.Types.Named("Store")))
            .Because("A constructor parameter of that type makes the store this type's lifetime problem.");

        arch.Rule("remainder/does-not-expose")
            .Enforce(arch.Types.Named("Exposer").MustNotExpose(arch.Types.Named("Ledger")))
            .Because("A ledger in a public signature is a ledger every caller now compiles against.");

        arch.Rule("remainder/does-not-catch")
            .Enforce(arch.Types.Named("Catcher").MustNotCatch(arch.Types.Named("Failure")))
            .Because("That failure is the sibling's to handle, and catching it here hides it.");

        arch.Rule("remainder/does-not-catch-unfiltered")
            .Enforce(arch.Types.Named("Unfiltered").MustNotCatchUnfiltered(typeof(Exception)))
            .Because("A catch with no filter takes the faults it was not written for.");

        arch.Rule("remainder/does-not-swallow")
            .Enforce(arch.Types.Named("Swallower").MustNotSwallow(typeof(InvalidOperationException)))
            .Because("A catch with an empty body is a fault nobody will ever hear about.");

        arch.Rule("remainder/does-not-throw")
            .Enforce(arch.Types.Named("Thrower").MustNotThrow(arch.Types.Named("Failure")))
            .Because("That failure belongs to the sibling, and throwing it from here misnames the fault.");

        arch.Rule("remainder/only-throws-its-own")
            .Enforce(arch.Types.Named("Thrower").MustOnlyThrow(arch.Namespace("Own.Errors")))
            .Because("A caller filters on this layer's own error types, so nothing else may leave it.");

        arch.Rule("remainder/does-not-read-the-clock")
            .Enforce(arch.Types.Named("Clock").MustNotUse(() => DateTime.Now))
            .Because("An ambient clock read is a test that cannot be written.");
    }

    private static void DefineHierarchy(Arch arch)
    {
        arch.Rule("remainder/implements-the-plug")
            .Enforce(arch.Types.Named("Plugless").MustImplement("Side.IPlug"))
            .Because("Dispatch resolves it by that contract, so one that implements nothing is never called.");

        arch.Rule("remainder/derives-from-the-root")
            .Enforce(arch.Types.Named("Rootless").MustDeriveFrom("Side.Root"))
            .Because("The host binds it through the base's own machinery.");

        arch.Rule("remainder/carries-the-stamp")
            .Enforce(arch.Types.Named("Unstamped").MustBeAttributedWith("Side.StampAttribute"))
            .Because("The table is built from the stamp, so one without it is dead.");

        arch.Rule("remainder/implements-no-plug")
            .Enforce(arch.Types.Named("Plugged").MustNotImplement("Side.IPlug"))
            .Because("A value that is also dispatchable is driven by machinery it knows nothing about.");

        arch.Rule("remainder/derives-from-no-root")
            .Enforce(arch.Types.Named("Rooted").MustNotDeriveFrom("Side.Root"))
            .Because("A value carried into the host is a value, not something the host binds.");

        arch.Rule("remainder/carries-no-stamp")
            .Enforce(arch.Types.Named("Stamped").MustNotBeAttributedWith("Side.StampAttribute"))
            .Because("A stamp here publishes something whose signature is free to change.");
    }

    private static void DefinePlaceAndName(Arch arch)
    {
        arch.Rule("remainder/resides-in-the-namespace")
            .Enforce(arch.Types.Named("Misplaced").MustResideInNamespace("Own.Elsewhere"))
            .Because("A reader finds it by where it lives before they find it by name.");

        arch.Rule("remainder/has-the-suffix")
            .Enforce(arch.Types.Named("WrongTail").MustHaveSuffix("Handler"))
            .Because("A handler is found by its tail at the call site.");

        arch.Rule("remainder/has-the-prefix")
            .Enforce(arch.Types.Named("WrongHead").MustHavePrefix("Default"))
            .Because("The default implementation is the one a reader looks for first.");

        arch.Rule("remainder/has-the-name-shape")
            .Enforce(arch.Types.Named("WrongShape").MustHaveNameMatching("*Query"))
            .Because("A query is found by name at the call site, which only works while they are named alike.");
    }

    private static void DefineMembers(Arch arch)
    {
        arch.Rule("remainder/members-are-public")
            .Enforce(arch.Types.Named("Fields").Fields.WithPrefix("Shy").MustBePublic())
            .Because("Everything this type declares is part of what callers compile against.");

        arch.Rule("remainder/members-are-internal")
            .Enforce(arch.Types.Named("Fields").Fields.WithPrefix("Writable").MustBeInternal())
            .Because("It is this assembly's own bookkeeping and nothing outside should name it.");

        arch.Rule("remainder/members-are-private")
            .Enforce(arch.Types.Named("Fields").Fields.WithPrefix("Writable").MustBePrivate())
            .Because("A field anyone may write is state with no invariant.");

        arch.Rule("remainder/members-are-protected")
            .Enforce(arch.Types.Named("Fields").Fields.ThatArePublic().MustBeProtected())
            .Because("An inheritor reaches the base's state through the fields it is given, and no one else does.");

        arch.Rule("remainder/members-are-static")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("Instance").MustBeStatic())
            .Because("A factory that needs an instance to call cannot be the way that instance is made.");

        arch.Rule("remainder/members-are-abstract")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("Instance").MustBeAbstract())
            .Because("Each case is the thing the base exists to vary, so the base states it and never answers it.");

        arch.Rule("remainder/members-are-virtual")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("Fixed").MustBeVirtual())
            .Because("A hook is the point this type is extended at; without one the extension has to fork it.");

        arch.Rule("remainder/members-are-get-only")
            .Enforce(arch.Types.Named("Properties").Properties.WithPrefix("Settable").MustBeGetOnly())
            .Because("A property anyone may set is state the constructor's checks stop meaning anything about.");

        arch.Rule("remainder/members-are-readonly")
            .Enforce(arch.Types.Named("Fields").Fields.WithPrefix("Writable").MustBeReadonly())
            .Because("Whoever wrote to it last decides what the next caller reads.");

        arch.Rule("remainder/members-have-the-suffix")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("TailMissing").MustHaveSuffix("Async"))
            .Because("A reader greps the tail to find every await point.");

        arch.Rule("remainder/members-have-the-prefix")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("HeadMissing").MustHavePrefix("Try"))
            .Because("The Try forms are the ones that cannot fail, and the head is what says so.");

        arch.Rule("remainder/members-have-the-name-shape")
            .Enforce(arch.Types.Named("Methods").Methods.WithPrefix("ShapeMissing").MustHaveNameMatching("On*"))
            .Because("A subscriber reads the name to know when it will be called.");

        arch.Rule("remainder/members-carry-the-stamp")
            .Enforce(arch.Types.Named("Methods")
                .Methods.WithPrefix("Unmarked")
                .MustBeAttributedWith("Side.StampAttribute"))
            .Because("The route table is built from the stamp, so a method without one is dead.");

        arch.Rule("remainder/members-carry-no-stamp")
            .Enforce(arch.Types.Named("Methods")
                .Methods.WithPrefix("Marked")
                .MustNotBeAttributedWith("Side.StampAttribute"))
            .Because("A stamp inside publishes a method whose signature is free to change.");

        arch.Rule("remainder/members-accept-the-token")
            .Enforce(arch.Types.Named("Methods")
                .Methods.WithPrefix("NoToken")
                .MustAcceptParameter<CancellationToken>())
            .Because("A caller that has given up can only say so through a token the method takes.");
    }
}
