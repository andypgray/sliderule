using SlideRule.Tests.Stubs;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Every vocabulary item the real specs in <see cref="ModelFileCorpus" /> between them never reach,
///     said once each — so that the composed corpus names every node a model file can carry rather than
///     the subset this repository and its fixtures happen to use.
/// </summary>
/// <remarks>
///     <para>
///         It is a real spec, built and validated like any other: the point is that the file carries what
///         the language can say, and a rule no spec builder would accept proves nothing about that. Its
///         subjects name namespaces no codebase here declares, which is why the rules read as plausible
///         laws over a codebase that does not exist rather than as contrivances over one that does.
///     </para>
///     <para>
///         It is a remainder and nothing more. A vocabulary item a real spec in the corpus takes up
///         belongs there and comes out of here; one a real spec drops has to arrive here in the same
///         change. Neither is left to vigilance — the inventory holds the composed corpus to the whole
///         writable set, so whichever way an item moves, the sweep names it.
///     </para>
/// </remarks>
internal static class VocabularyRemainderSpec
{
    internal static void Define(Arch arch)
    {
        DefineTypeShapes(arch);
        DefineHierarchy(arch);
        DefineNames(arch);
        DefineReach(arch);
        DefineMemberShapes(arch);
    }

    // The five type-shape modals. Each takes a subject narrow enough that the law reads as one a codebase
    // could hold, rather than as a blanket claim over every type.
    private static void DefineTypeShapes(Arch arch)
    {
        arch.Rule("remainder/handlers-are-sealed")
            .Enforce(arch.Types.InNamespace("Remainder.Handlers.*")
                .MustBeSealed())
            .Because("A handler nobody may subclass is a handler whose behaviour is the one the registry resolved.");

        arch.Rule("remainder/policies-are-abstract")
            .Enforce(arch.Types.InNamespace("Remainder.Policies.*")
                .MustBeAbstract())
            .Because("A policy base carries the shared decision order; only its cases are instantiable.");

        arch.Rule("remainder/extensions-are-static")
            .Enforce(arch.Types.WithSuffix("Extensions")
                .MustBeStatic())
            .Because("An extensions class holds no state, and an instance of one is a call nobody can reach.");

        arch.Rule("remainder/contracts-are-public")
            .Enforce(arch.Types.InNamespace("Remainder.Contracts.*")
                .MustBePublic())
            .Because("A contract is what callers outside this solution compile against.");

        arch.Rule("remainder/internals-stay-internal")
            .Enforce(arch.Types.InNamespace("Remainder.Internal.*")
                .MustBeInternal())
            .Because("Everything under Internal is free to change, which only holds while nothing outside can name it.");
    }

    // Both directions of the two hierarchy axes.
    private static void DefineHierarchy(Arch arch)
    {
        arch.Rule("remainder/handlers-implement-the-contract")
            .Enforce(arch.Types.WithSuffix("Handler")
                .MustImplement(typeof(IHandler<>)))
            .Because("Dispatch resolves a handler by its contract, so a type named for one that implements nothing is never called.");

        arch.Rule("remainder/values-implement-nothing-dispatchable")
            .Enforce(arch.Types.InNamespace("Remainder.Values.*")
                .MustNotImplement(typeof(IHandler<>)))
            .Because("A value that is also a handler is resolved and driven by the dispatcher, which is not what its callers hold it for.");

        arch.Rule("remainder/controllers-derive-from-the-base")
            .Enforce(arch.Types.WithSuffix("Controller")
                .MustDeriveFrom(typeof(ControllerBase)))
            .Because("The host binds a request to a controller through the base's own machinery.");

        arch.Rule("remainder/values-derive-from-nothing")
            .Enforce(arch.Types.InNamespace("Remainder.Values.*")
                .MustNotDeriveFrom(typeof(ControllerBase)))
            .Because("A value carried into the host is a value; one that is also a controller is bound to a request it knows nothing about.");
    }

    private static void DefineNames(Arch arch)
    {
        arch.Rule("remainder/queries-are-named-for-their-shape")
            .Enforce(arch.Types.InNamespace("Remainder.Queries.*")
                .MustHaveNameMatching("*Query"))
            .Because("A query is found by name at the call site, which only works while every one of them is named alike.");
    }

    private static void DefineReach(Arch arch)
    {
        arch.Rule("remainder/leaves-are-self-contained")
            .Enforce(arch.AnyOf(arch.Namespace("Remainder.Leaf.*"), arch.Namespace("Remainder.Twig.*"))
                .MustOnlyReferenceItself())
            .Because("Each leaf ships on its own cadence, which stops being true the moment one of them reaches outside itself.");
    }

    // The member axis: every member modal no corpus spec states, and the three name-shaped member
    // adjectives none of them narrows by.
    private static void DefineMemberShapes(Arch arch)
    {
        arch.Rule("remainder/api-members-are-public")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Members.MustBePublic())
            .Because("Everything an API type declares is part of the API; a member that is not public is one the caller is left to guess at.");

        arch.Rule("remainder/try-methods-stay-internal")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Methods.WithPrefix("Try")
                .MustBeInternal())
            .Because("The Try forms are the shell's own shortcuts; published, every caller takes the one that cannot fail.");

        arch.Rule("remainder/api-fields-are-private")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Fields.MustBePrivate())
            .Because("A field anyone may write is state with no invariant, and the constructor's checks stop meaning anything.");

        arch.Rule("remainder/factories-are-static")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Methods.WithNameMatching("Create*")
                .MustBeStatic())
            .Because("A factory that needs an instance to call cannot be the way that instance is made.");

        arch.Rule("remainder/policy-cases-are-abstract")
            .Enforce(arch.Types.InNamespace("Remainder.Policies.*")
                .Methods.WithSuffix("Case")
                .MustBeAbstract())
            .Because("Each case is the thing a policy exists to vary, so the base states it and never answers it.");

        arch.Rule("remainder/rendering-hooks-are-virtual")
            .Enforce(arch.Types.InNamespace("Remainder.Rendering.*")
                .Methods.WithSuffix("Hook")
                .MustBeVirtual())
            .Because("A hook is the point a renderer is extended at; sealed, the extension has to fork the renderer instead.");

        arch.Rule("remainder/ambient-properties-are-named-for-it")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Properties.MustHavePrefix("Current"))
            .Because("An ambient read looks like an ordinary one at the call site unless its name says otherwise.");

        arch.Rule("remainder/events-are-named-for-what-happened")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Events.MustHaveNameMatching("On*"))
            .Because("A subscriber reads the name to know when it will be called.");

        arch.Rule("remainder/published-methods-carry-the-attribute")
            .Enforce(arch.Types.InNamespace("Remainder.Api.*")
                .Methods.WithSuffix("Endpoint")
                .MustBeAttributedWith(typeof(ApiControllerAttribute)))
            .Because("The route table is built from the attribute, so an endpoint without one is dead.");

        arch.Rule("remainder/internal-methods-carry-no-routing")
            .Enforce(arch.Types.InNamespace("Remainder.Internal.*")
                .Methods.MustNotBeAttributedWith(typeof(ApiControllerAttribute)))
            .Because("A routing attribute inside Internal publishes a method whose signature is free to change.");
    }
}
