using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Build.Locator;
using SlideRule.Packs.DotNet;

namespace SlideRule.Architecture;

/// <summary>
///     SlideRule's own architecture spec: the rules this repository is held to, over its real code. Each
///     is a boundary that nothing in the build system states and almost nothing else enforces, and the block
///     it renders into the committed root <c>AGENTS.md</c> is kept current by the self-spec tests.
///     Declaration order is reading order, the order the managed block and the scope cards render in, so
///     <see cref="Define" /> declares the layers and then calls one method per area, in the order they read;
///     the constants an area cites sit beside its method.
/// </summary>
public sealed class SlideRuleArchitectureSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        // The unit of architecture here is the assembly, so each shipping layer is the project it is; the
        // three cuts inside Core are namespace cones, the pieces layering/model-independent needs to name.
        Layer core = arch.Layer("Core", arch.Project("SlideRule"))
            .Purpose("Core is the package a spec is written against: the fluent language, the model a spec " +
                     "compiles to, and the readers of that model that need no compiler.");
        Layer model = arch.Layer("Model", core.InNamespace("SlideRule.Model.*"))
            .Purpose("Model is the reified spec: the nodes a spec compiles to, and the one thing the checker and " +
                     "the renderers both read.");
        Layer checking = arch.Layer("Checking", core.InNamespace("SlideRule.Checking.*"))
            .Purpose("Checking evaluates each rule of the model against an extracted codebase: a verdict per rule, " +
                     "its violations, and what the baselines grandfather.");
        Layer rendering = arch.Layer("Rendering", core.InNamespace("SlideRule.Rendering.*"))
            .Purpose("Rendering turns the model and a check's results into what people and agents read: the " +
                     "managed block and cards, the diagrams, and the reports.");
        Layer extraction = arch.Layer("Extraction", arch.Project("SlideRule.Extraction"))
            .Purpose("Extraction reads a compilation into a fragment of the codebase model and merges the fragments " +
                     "into the model the checker evaluates against, using the compiler API and nothing else.");
        Layer analyzers = arch.Layer("Analyzers", arch.Project("SlideRule.Analyzers"))
            .Purpose("Analyzers runs the extractor and the checker inside the C# compiler, over the one project " +
                     "being built, and reports a broken rule as a warning at the offending code, in the editor " +
                     "as it is typed and in the build.");
        Layer specAnalyzers = arch.Layer("SpecAnalyzers", arch.Project("SlideRule.SpecAnalyzers"))
            .Purpose("SpecAnalyzers runs Core's literal checks inside the C# compiler, over a spec project as " +
                     "it is written, and reports a bad string literal as a warning on the literal.");
        Layer specCompletion = arch.Layer("SpecCompletion", arch.Project("SlideRule.SpecCompletion"))
            .Purpose("SpecCompletion offers the namespaces and types a spec can anchor on inside the editor a " +
                     "spec is written in, reading the same slot markers the spec analyzer reads.");
        Layer workspace = arch.Layer("Workspace", arch.Project("SlideRule.Roslyn"))
            .Purpose("Workspace is the Roslyn host: it loads a solution through MSBuild or a build log, hands each " +
                     "compilation to Extraction, and keeps the extraction cache and the baseline files.");
        Layer host = arch.Layer("Host", arch.Project("SlideRule.Cli"))
            .Purpose("Host is the `sliderule` command: the CLI commands, the MCP server, and the spec loading and " +
                     "pipeline behind both.");
        Layer adapter = arch.Layer("Adapter", arch.Project("SlideRule.Xunit"))
            .Purpose("Adapter runs every rule of a spec as an individually named xUnit test in the consumer's own " +
                     "test project.");
        Layer pack = arch.Layer("Pack", arch.Project("SlideRule.Packs.DotNet"))
            .Purpose("Pack is the shared rule pack: canonical .NET rules as an ordinary class library that a spec " +
                     "takes one method at a time.");

        // `repo` is every type this repository declares, spec and test projects included; `shipping` is the
        // nine layers that ship, which is `repo` minus those two.
        Selection repo = arch.Namespace("SlideRule.*");
        Selection shipping = arch.AnyOf(
            core, extraction, analyzers, specAnalyzers, specCompletion, workspace, host, adapter, pack);

        Layering(
            arch, core, model, checking, rendering, extraction, analyzers, specAnalyzers, specCompletion, workspace,
            host, adapter, pack);
        FrontDoors(arch);
        ModelNodes(arch, core);
        Packaging(arch);
        DependencyInjection(arch, host, repo);
        Cli(arch, host);
        McpServer(arch, host, workspace, extraction, repo);
        RoslynHost(arch, repo);
        Adapter(arch, adapter);
        CompilerHost(arch, core, extraction);
        Exceptions(arch, shipping);
        StaticState(arch, shipping);
        Naming(arch, shipping, repo);
        Scopes(arch, model, workspace);
    }

    private static void Layering(
        Arch arch, Layer core, Layer model, Layer checking, Layer rendering, Layer extraction, Layer analyzers,
        Layer specAnalyzers, Layer specCompletion, Layer workspace, Layer host, Layer adapter, Layer pack)
    {
        arch.Rule("layering/core-no-roslyn")
            .Enforce(core.MustNotReference(
                extraction,
                workspace,
                arch.Namespace("Microsoft.CodeAnalysis.*"),
                arch.Namespace("Microsoft.Build.*")))
            .Because("Core is the netstandard2.0 reified model both render targets consume; " +
                     "reading a compilation is the work of the two layers above it, and a Microsoft.CodeAnalysis " +
                     "or Microsoft.Build package reference would leak compiler types into Core just as a project " +
                     "reference to either layer would.")
            .Fix("Depend on the Codebase model types in Core; keep Microsoft.CodeAnalysis behind " +
                 "SlideRule.Extraction and SlideRule.Roslyn.");

        arch.Rule("layering/extraction-compiler-only")
            .Enforce(extraction.MustNotReference(
                workspace,
                host,
                adapter,
                pack,
                arch.Namespace("Microsoft.Build.*"),
                arch.Namespace("Microsoft.CodeAnalysis.MSBuild.*")))
            .Because("The extractor runs wherever a compilation exists, with no workspace and no build engine " +
                     "behind it, and that holds only while the compiler API is the whole of what it needs. The " +
                     "build already refuses a project reference up to Workspace, Host or Adapter, since each of " +
                     "them depends on this project. What it does not refuse is a package: a reference to " +
                     "Microsoft.Build or to the MSBuild workspace compiles, passes every test, and leaves an " +
                     "assembly that loads only in a process that can also load MSBuild.")
            .Fix("Take the fact as data instead. A caller that has a workspace reads what the extractor needs " +
                 "out of it and passes that in; anything that needs MSBuild belongs in SlideRule.Roslyn.")
            .Exemplar(arch.Types.Named("CompilationInput", "ProjectArtifactFacts"));

        arch.Rule("layering/analyzers-compiler-only")
            .Enforce(analyzers.MustNotReference(
                workspace,
                arch.Namespace("Microsoft.Build.*"),
                arch.Namespace("Microsoft.CodeAnalysis.MSBuild.*")))
            .Because("The analyzer is loaded by whichever compiler is building the project: csc on the command " +
                     "line, the compiler server, Visual Studio. None of them has a workspace or a build engine to " +
                     "give it, and an assembly that needs either does not load there. The compiler reports that " +
                     "as one warning about the analyzer and carries on without it, so the build goes quiet " +
                     "where it should have gone red.")
            .Fix("Take what the analyzer needs from the compilation and the additional files the build hands " +
                 "it; anything that needs a workspace or MSBuild belongs in SlideRule.Roslyn, behind " +
                 "`sliderule check`.");

        arch.Rule("layering/model-independent")
            .Enforce(model.MustNotReference(checking, rendering))
            .Because("The Model is the one reified thing this product is built around: a spec compiles to it, " +
                     "and checking and rendering are two independent readers of it. A reference the other way " +
                     "would make the model know about a consumer, and the next render target could no longer be " +
                     "added without touching it.")
            .Fix("Keep the dependency one-way: give Model the data, and let Checking or Rendering read it.");

        arch.Rule("layering/no-circular-references")
            .Enforce(arch.Each(model, checking, rendering).MustNotHaveCircularReferences())
            .Because("Model, Checking and Rendering are three cuts inside one assembly with one intended direction: " +
                     "a model and two readers of it. A circle among them means a reader has become something the " +
                     "other reader depends on, and the next render target or checker change could no longer land " +
                     "without touching both.")
            .Fix("Move what a reader borrows from another reader down to where both can reach it: Model for " +
                 "shared data, a helper namespace for a helper. A reader depends on the model, never on the other reader.");

        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(host, adapter, pack, analyzers, specAnalyzers, specCompletion)
                .MustNotReferenceEachOther())
            .Because("Host, Adapter, Pack, Analyzers, SpecAnalyzers and SpecCompletion are the six leaves of the " +
                     "project graph: each is a different way to consume Core, and none is a dependency of " +
                     "another. A reference between two leaves would pull one consumer's closure — the CLI's " +
                     "MSBuild and MCP machinery, the adapter's xunit, the pack's canonical rules, an analyzer's " +
                     "compiler host — into a project that ships without it.")
            .Fix("Move the shared piece down into Core, or into Extraction or Workspace if it needs Roslyn; " +
                 "a leaf takes what it needs from below, never from a sibling.");

        arch.Rule("layering/pack-depends-on-core-only")
            .Enforce(pack.MustOnlyReference(core))
            .Because("A spec project takes the rule pack as a reference of its own, and the whole of what " +
                     "the pack needs is the Arch it composes onto. A reference to the Workspace layer or " +
                     "the CLI would pull MSBuild and the rest of the tool into the closure of every spec " +
                     "project that takes the pack — and nothing in the project graph stops it, because the " +
                     "pack is a leaf and no reference out of it would be circular.")
            .Fix("Express the rule in the Core vocabulary the pack already has; anything that needs a " +
                 "workspace belongs in the tool, not in a pack.");

        arch.Rule("layering/spec-analyzers-depend-on-core-only")
            .Enforce(specAnalyzers.MustOnlyReference(core))
            .Because("The spec analyzer ships inside the contract package, in a directory that holds it and " +
                     "one neighbour, Core, and a compiler resolves an analyzer's dependencies from that " +
                     "directory and nowhere else. A reference to Extraction or to any other project here " +
                     "compiles and passes every test, because the test process can load it, and then fails " +
                     "to load inside the compiler of everyone who references the package. The sibling " +
                     "analyzer project references Extraction and is the obvious thing to copy, and nothing " +
                     "in the project graph stops it, because this project is a leaf.")
            .Fix("Take what the analyzer needs from Core, and put a verdict it lacks into Core's validation, " +
                 "where loading a spec reads it too; anything that needs the extractor belongs in " +
                 "SlideRule.Analyzers.");

        arch.Rule("layering/spec-completion-stands-alone")
            .Enforce(specCompletion.MustOnlyReferenceItself())
            .Because("An editor builds a project's completion providers by reflection, inside handlers that " +
                     "report nothing, so an assembly it cannot load produces no list and no message anywhere. " +
                     "This one ships in the contract package's analyzer directory beside the contract and the " +
                     "spec analyzer, and a reference to either compiles, passes every test and then fails to " +
                     "load in the editor of everyone who references the package, where the only symptom is a " +
                     "list that never appears. Nothing in the project graph stops it, because this project is " +
                     "a leaf.")
            .Fix("Mirror the handful of names it needs and hold each copy to its owner with a test that sees " +
                 "both sides; a verdict it lacks belongs in Core's validation, where loading a spec reads it " +
                 "too.");

        arch.Rule("layering/adapter-is-a-leaf")
            .Enforce(adapter.MustNotBeReferencedBy(core, extraction, workspace))
            .Because("The adapter rides xunit.v3; a product reference would ship a test framework to " +
                     "every consumer of the referencing package. Host, Pack, Analyzers, SpecAnalyzers and " +
                     "SpecCompletion are the other five leaves, and layering/leaves-independent already holds " +
                     "all six off each other, so this rule names only what sits below the adapter.")
            .Fix("Keep the dependency one-way: the adapter consumes Core and Workspace, never the reverse.");
    }

    private static void FrontDoors(Arch arch)
    {
        // The list is the curated root of the contract package, what a spec author gets from
        // `using SlideRule;` and a dot: the entry point and the spec interface, the nouns and enums
        // an author writes as arguments or compares in a predicate, the static classes that hold the verbs,
        // the builder, info and Member types an author spells in type position even though a chain never has
        // to, the selection types an author holds in a variable or a helper's signature, and the one validation
        // entry a rule pack's own tests call. Each entry is a typeof, which the
        // checker matches by full name: a removed type fails the build, and one that leaves the root stops
        // matching anything instead of blessing a newcomer of the same name.
        arch.Rule("api/core-front-door")
            .Enforce(arch.Types.InNamespace("SlideRule")
                .MustBelongTo(arch.AnyOf(
                    typeof(Accessibility),
                    typeof(Arch),
                    typeof(Constraint),
                    typeof(FieldSelection),
                    typeof(FieldSelectionConstraints),
                    typeof(IArchitectureSpec),
                    typeof(IAttributeInfo),
                    typeof(IMemberInfo),
                    typeof(IPackageReferenceInfo),
                    typeof(IParameterInfo),
                    typeof(IProjectInfo),
                    typeof(IProjectPropertyInfo),
                    typeof(IRuleBuilder),
                    typeof(IScopeBuilder),
                    typeof(ITypeInfo),
                    typeof(Layer),
                    typeof(Lifetime),
                    typeof(Member),
                    typeof(MemberKind),
                    typeof(MemberSelection),
                    typeof(MemberSelectionAdjectives),
                    typeof(MemberSelectionConstraints),
                    typeof(MethodSelection),
                    typeof(MethodSelectionConstraints),
                    typeof(MigrationPolicy),
                    typeof(ProjectSelection),
                    typeof(ProjectSelectionAdjectives),
                    typeof(ProjectSelectionConstraints),
                    typeof(PropertySelection),
                    typeof(PropertySelectionConstraints),
                    typeof(Selection),
                    typeof(SelectionAdjectives),
                    typeof(SelectionConstraints),
                    typeof(SpecValidation),
                    typeof(TypeKind))))
            .Because("The root namespace is the authoring surface. Someone writes `using SlideRule;` " +
                     "and what completion offers from there is, in practice, the whole language they believe " +
                     "exists — so a type that lands in it uninvited is one more thing to read past before " +
                     "finding the verb they wanted, and that cost falls in the first hour with the product. " +
                     "Hiding the rest is free: member resolution on a return type ignores using directives, " +
                     "so a chain keeps flowing through types nobody imports.")
            .Fix("Put the type where it belongs — Fluent for an intermediate a chain only passes through, " +
                 "Hosting for a node of the built model, Model/Checking/Rendering/Validation for the " +
                 "machinery behind them. If it really is a type an author spells, add it to this rule's " +
                 "list in the spec and say in review what an author writes it for.");

        // The list is the extraction seam: what a host fills in and calls to turn compilations into a
        // codebase model. It is names and not typeofs because this project declares no reference to the
        // extraction assembly, and all seven are internal to it. FragmentExtractor and
        // FragmentMerger keep their working state in private nested types, and a nested type reports the
        // namespace of the type that nests it while answering to its own name — so a namespace-scoped
        // subject reaches every one of them and nothing about the name picks them out. Only a type
        // nested in another can be private, which is what makes ThatArePrivate the way to subtract them;
        // no other front door has a nested type to leave out.
        arch.Rule("api/extraction-front-door")
            .Enforce(arch.Types.InNamespace("SlideRule.Extraction")
                .Except(arch.Types.ThatArePrivate())
                .MustBelongTo(arch.Types.Named(
                    "CodebaseModelBuilder",
                    "CompilationInput",
                    "ExtractionInputs",
                    "FragmentExtractor",
                    "FragmentMerger",
                    "ProjectArtifactFacts",
                    "SiblingProjects")))
            .Because("The root of this assembly is what a host calls to turn compilations into a codebase " +
                     "model: the input it fills in, the extractor, the merger, and the builder that runs " +
                     "both. The fragment records and the passes sit in namespaces behind it so that they " +
                     "stay free to change shape. A type left in the root reads as one more thing a host may " +
                     "call, and the first host to take it up turns the next rearrangement of the " +
                     "extractor's internals into a break in every process that loads it.")
            .Fix("Put the type in Fragments if the extractor and the merger exchange it, or in Passes if it " +
                 "is a step of the walk. If a host genuinely calls it, add its name to this rule's list " +
                 "in the spec.");

        // The list is the workspace host's seam: what the CLI and the xUnit adapter call to get a
        // workspace, a codebase model, or a refusal out of it. Everything else the project declares is
        // machinery behind it. Names and not typeofs, because every type in the assembly is internal.
        arch.Rule("api/workspace-front-door")
            .Enforce(arch.Types.InNamespace("SlideRule.Roslyn")
                .MustBelongTo(arch.Types.Named(
                    "CodebaseExtractor",
                    "LoadedSolution",
                    "PathCanonicalizer",
                    "SolutionDiscovery",
                    "UserErrorException",
                    "WorkspaceLoader",
                    "WorkspaceSession",
                    "WorkspaceSnapshot")))
            .Because("This assembly travels inside the tool and the xUnit adapter, and its root is the seam " +
                     "those two hosts call, which leaves everything behind it free to be rearranged. A type " +
                     "left sitting in the root reads as part of that seam, and the first host to take it up " +
                     "ties the next rearrangement of the internals to a change in both hosts.")
            .Fix("Put the type beside its siblings in Baselines, Caching, Checking, Diagnostics, Files, " +
                 "Hosting, Replay, Sessions or Solutions. If it genuinely belongs on the seam, add it to " +
                 "this rule's list in the spec.");

        // The list is the tool's entry path: arguments to a command, a command to a runner, and an
        // exception to an exit code, as names because the CLI is not a reference of this project. Program
        // is absent because top-level statements synthesize it into the global namespace, which this
        // subject does not reach; a self-spec test holds it instead.
        arch.Rule("api/host-front-door")
            .Enforce(arch.Types.InNamespace("SlideRule.Cli")
                .MustBelongTo(arch.Types.Named(
                    "CliEntry",
                    "CliErrorMapper",
                    "CommandEntryPoint",
                    "CommandFactory")))
            .Because("Namespaces are not API here — this project ships as a tool — so the only thing the " +
                     "root namespace buys is legibility, and that is worth holding. The entry path is the " +
                     "first thing anyone reads to learn how the tool starts, and a root that also holds the " +
                     "verb requests, runners and pipeline stages it dispatches to hides that path among the " +
                     "thirty types it calls.")
            .Fix("Put the type beside its siblings in Diff, Mcp, Pipeline, Rendering, Replay, SpecLoading " +
                 "or Verbs. If it is really part of the entry path, add its name to this rule's list in " +
                 "the spec.");
    }

    private static void ModelNodes(Arch arch, Layer core)
    {
        arch.Rule("model/constraint-nodes")
            .Enforce(core.WithSuffix("Constraint").Except(typeof(Constraint))
                .MustResideInNamespace("SlideRule.Model.*"))
            .Because("A `*Constraint` is a node of the reified model — what a spec compiles to and what both " +
                     "render targets read back. The checker's dispatch and the sentence renderer each switch " +
                     "over the whole set, so a constraint node declared somewhere else is one they would " +
                     "silently not handle. `Constraint` itself is the public base a spec author names, so it " +
                     "stays in the root namespace with the rest of the authoring surface.")
            .Fix("Put the new constraint node in SlideRule.Model beside its siblings, and handle it " +
                 "in ConstraintEvaluator and SentenceRenderer.");

        // The internal adjective and noun hierarchies stay outside this union: a typeof anchor cannot reach
        // them without InternalsVisibleTo, and the string form is kept for types a spec cannot reference at all.
        arch.Rule("model/reified-nodes-immutable")
            .Enforce(arch.AnyOf(
                    arch.Types.DerivedFrom<Selection>(),
                    arch.Types.DerivedFrom<Constraint>(),
                    arch.Types.DerivedFrom<MemberSelection>(),
                    arch.Types.DerivedFrom<ProjectSelection>())
                .Properties.MustBeGetOnly())
            .Because("A spec compiles to these nodes and both render targets read them back, so a node is a " +
                     "value: the same spec must render the same sentence and check to the same verdict " +
                     "whoever walks it, and a settable property is the one way a walker could change what " +
                     "the next walker sees. The four roots are the whole reified hierarchy an author can " +
                     "name — a selection, a constraint, the member selection a projection mints, and the " +
                     "project selection the artifact noun mints — and their private-protected constructors " +
                     "mean no assembly outside Core can add a fifth. " +
                     "The BUILDERS are deliberately outside this law and stay mutable: a RuleRegistration " +
                     "accumulates a posture and a constraint across chained calls, which is what makes the " +
                     "stage machine a stage machine.")
            .Fix("Take the value in the constructor and expose it `{ get; }`. If a node genuinely has to " +
                 "accumulate, it is a builder, not a node — put it beside RuleRegistration.");
    }

    private static void Packaging(Arch arch)
    {
        // The packaging rules judge projects, so their sites are lines in a project or props file. Their
        // broad subjects are bounded to `SlideRule*`: the MyApp fixture solutions ride into this universe as
        // passengers of the spec fixtures' project references, packable and unlocked, and a bare artifact
        // subject would red on code this repository does not ship.
        arch.Rule("packaging/core-netstandard-only")
            .Enforce(arch.Projects.Named("SlideRule").MustOnlyTarget("netstandard2.0"))
            .Because("netstandard2.0 is the one TFM a net48 spec project and the net10 host can both load; " +
                     "Core is the contract every spec references, so its TFM is the product's reach.")
            .Fix("Anything needing a newer API belongs in SlideRule.Roslyn or the CLI, never Core.");

        arch.Rule("packaging/core-carries-nothing")
            .Enforce(arch.Projects.Named("SlideRule").MustReferenceNoPackages())
            .Because("Every consumer's spec csproj takes Core directly; a package Core drags along is imposed " +
                     "on every estate that adopts the tool, and a net48 spec has to resolve it too.")
            .Fix("Use what the netstandard2.0 surface already supplies, or put the dependency behind " +
                 "SlideRule.Roslyn or the CLI, where a package reference costs only the tool.");

        // The lock rule's subject stays `.Packable()`: it is the law for what ships, and a project that
        // commits a lock file without shipping is already held from both sides — the Dependabot coverage
        // gate pairs every committed lock file with its update entry, and src/Directory.Build.props makes
        // locking the default for everything beneath it.
        arch.Rule("packaging/shipping-locks-restore")
            .Enforce(arch.Projects.Matching("SlideRule*").Packable().MustLockPackages())
            .Because("CI restores locked; a shipping project without a lock file floats its dependency graph " +
                     "under audit.")
            .Citation("https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies")
            .Fix("Set `RestorePackagesWithLockFile` to true and commit the packages.lock.json the next " +
                 "restore writes; src/Directory.Build.props already carries the policy for everything under it.");

        arch.Rule("packaging/only-the-named-ship")
            .Enforce(arch.Projects.Matching("SlideRule*")
                .Except(arch.Projects.Named(
                    "SlideRule",
                    "SlideRule.Analyzers",
                    "SlideRule.Xunit",
                    "SlideRule.Cli"))
                .MustNotBePackable())
            .Because("Only the packages named in this rule ship, and the SDK packs by default, so a project " +
                     "that stays silent about it is one release-pipeline accident away from publishing this " +
                     "repository's internals under a name nobody reviewed.")
            .Fix("Set `IsPackable` to false in the project file. A project that genuinely should ship joins " +
                 "the names in this rule's subject in the same change.");
    }

    private static void DependencyInjection(Arch arch, Layer host, Selection repo)
    {
        arch.Rule("di/no-captive-dependencies")
            .Enforce(arch.Registered(Lifetime.Singleton).InNamespace("SlideRule.*")
                .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)))
            .Because("The MCP server is one long-lived process wired all-singleton by design (IEnvironment, " +
                     "WorkspaceSession, SessionFragmentStore, SpecModelCache, SpecResolutionCache, " +
                     "ISolutionSource); a scoped or transient service injected into a singleton is captured " +
                     "for the whole process and silently shared across every tool call.")
            .Citation("https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines")
            .Fix("Keep singletons depending only on singletons; resolve any scoped or transient work per call " +
                 "inside an IServiceScopeFactory scope instead of injecting it into the singleton.");

        // Two rules come from the shared pack; the rest of it is declined, since a pack is a menu and not
        // calling a method is the whole opt-out.
        DotNetGuidance.NoServiceLocator(arch, host,
            arch.Types.Named("McpServerCommand", "GlobalCallToolFilter"),
            PackPosture.Enforce,
            "Take the dependency in the constructor; McpServerCommand's composition root and GlobalCallToolFilter's request context are the only sanctioned resolve sites.");

        DotNetGuidance.NoBuildServiceProvider(arch, repo, PackPosture.Enforce);
    }

    private static void Cli(Arch arch, Layer host)
    {
        arch.Rule("cli/no-stdout")
            .Enforce(host
                .MustNotUse(
                    arch.Member(() => Console.Out),
                    arch.Member(typeof(Console), nameof(Console.Write)),
                    arch.Member(() => Console.WriteLine())))
            .Because("Stdout is a protocol channel here — the MCP server speaks JSON-RPC over it and CLI " +
                     "output flows through System.CommandLine's console — so a direct Console write corrupts " +
                     "the wire and is invisible to the in-process tests.")
            .Citation("https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#stdio")
            .Fix("Write CLI output through the command's InvocationConfiguration console; route server " +
                 "diagnostics to the logger or Console.Error.");
    }

    private static void McpServer(Arch arch, Layer host, Layer workspace, Layer extraction, Selection repo)
    {
        arch.Rule("mcp/tools-accept-cancellation")
            .Enforce(repo
                .Methods.AttributedWith(McpServerToolAttribute)
                .MustAcceptParameter<CancellationToken>())
            .Because("Tool calls run inside one long-lived server process; a tool method without a " +
                     "CancellationToken cannot honor a client cancel and holds the idle watchdog open.")
            .Citation("https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/cancellation")
            .Fix("Add a trailing CancellationToken parameter (default it) and flow it into the runner.");

        arch.Rule("mcp/tool-types-attributed")
            .Enforce(arch.Namespace("SlideRule.Cli.Mcp.Tools.*")
                .MustBeAttributedWith(McpServerToolTypeAttribute))
            .Because("Tool discovery is the attribute walk: WithCoercingTools registers the [McpServerTool] " +
                     "methods of [McpServerToolType]-annotated classes, so a tool class without the type " +
                     "attribute compiles, registers nothing, and its tools silently vanish from the server.")
            .Fix("Put [McpServerToolType] on the tool class, and keep tool classes in SlideRule.Cli.Mcp.Tools.")
            .Exemplar(arch.Types.Named("ArchTools"));

        arch.Rule("mcp/tool-types-in-cli")
            .Enforce(arch.Types.AttributedWith(McpServerToolTypeAttribute)
                .MustResideInProject("SlideRule.Cli"))
            .Because("The converse of mcp/tool-types-attributed: that rule keeps every type in the tools " +
                     "namespace attributed, and this one keeps every attributed type in the CLI. The " +
                     "ModelContextProtocol SDK is the CLI's package reference alone, so a tool class declared " +
                     "in Core or Roslyn would drag the SDK into that package's closure — Core's rides into " +
                     "every spec project that references the contract library.")
            .Fix("Move the tool class into SlideRule.Cli.Mcp.Tools beside ArchTools; only the CLI " +
                 "references the MCP SDK.");

        // One definition of a blocking wait, shared with the pack instead of copied beside it: this rule
        // keeps its own subject, exemption and reason, and picks up whatever the pack's list gains. It
        // arrives split because MustNotUse takes one member and the rest, not because the first differs.
        (Member blockingWait, Member[] moreBlockingWaits) = DotNetGuidance.BlockingWaitAnchors(arch);

        arch.Rule("mcp/no-blocking-waits")
            .Enforce(arch.AnyOf(host, workspace, extraction)
                .Except(arch.Types.Named("ServerShutdown"))
                .MustNotUse(blockingWait, moreBlockingWaits))
            .Because("The CLI and the two layers under it are one long-lived MCP server as often as they are " +
                     "a one-shot command, and a blocking wait there costs twice: it holds a thread-pool thread " +
                     "for as long as the child process or workspace load it waits on, and it drops the " +
                     "CancellationToken the tool call was handed, so a client cancel leaves a zombie running " +
                     "the timeout out instead. ServerShutdown is the one sanctioned block — a bounded drain " +
                     "at process exit, with nothing left to starve.")
            .Citation("https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios")
            .Fix("Await the task and flow the CancellationToken. Where a call chain genuinely cannot be " +
                 "async — the MSBuild registration path is JIT-quarantined behind a synchronous seam — wait " +
                 "on the resource itself, a process handle rather than a Task.");

        arch.Rule("mcp/no-path-assembly-loads")
            .Enforce(host
                .Except(arch.Types.Named("SpecLoadContext"))
                .MustNotUse(arch.Member(typeof(AssemblyLoadContext), nameof(AssemblyLoadContext.LoadFromAssemblyPath))))
            .Because("A host that loads an assembly from its path pins that file for as long as the host " +
                     "lives, and this host lives for a whole session: the model roots the spec's Types, so " +
                     "the collectible context never collects and the operating system never releases the " +
                     "build output. Every rebuild of what was loaded then fails, and the only cure is killing " +
                     "the server — which for a stdio server is terminal, because the client never reconnects " +
                     "one, so the session's architecture queries go dead with no error anywhere. " +
                     "SpecLoadContext is the single sanctioned caller: it reads bytes, and reaches for the " +
                     "loader only when the file is already gone.")
            .Fix("Read the assembly's bytes and load from the stream, the way LoadWithoutLocking does in the " +
                 "exemplar; never hand a path to the loader from the CLI.")
            .Exemplar(arch.Types.Named("SpecLoadContext"));

        arch.Rule("mcp/warm-state-constructed-once")
            .Enforce(host
                .Except(arch.Types.Named("McpServerCommand"))
                .MustNotConstruct(arch.Types.Named(
                    "WorkspaceSession",
                    "SessionFragmentStore",
                    "SpecModelCache",
                    "SpecResolutionCache")))
            .Because("The warm server holds exactly one of each piece of session state for its lifetime — " +
                     "the workspace session, the fragment store, and the two spec caches that hang off the " +
                     "same load. A second construction forks the reconcile state and the caches silently " +
                     "diverge: the fork answers from an empty cache while the original keeps ratcheting, so " +
                     "the cost the cache exists to remove comes back and nothing reports it.")
            .Fix("Resolve them from DI; only McpServerCommand's composition root constructs them.");

        arch.Rule("mcp/env-through-seam")
            .Enforce(arch.Types.InNamespace("SlideRule.Cli.Mcp.Infrastructure.*")
                .Except(arch.Types.Named("SystemEnvironment"))
                .MustNotUse(
                    arch.Member(typeof(Environment), nameof(Environment.GetEnvironmentVariable)),
                    arch.Member(typeof(Environment), nameof(Environment.GetEnvironmentVariables)),
                    arch.Member(typeof(Environment), nameof(Environment.ExpandEnvironmentVariables)),
                    arch.Member(typeof(Environment), nameof(Environment.SetEnvironmentVariable))))
            .Because("A single IEnvironment seam keeps the MCP pipeline testable without mutating real " +
                     "process state: a test sets a variable on the fake, never on the process.")
            .Fix("Inject IEnvironment and read through GetVariable; SystemEnvironment alone reads the real process " +
                 "environment.")
            .Exemplar(arch.Types.Named("ResponseBudget"));
    }

    /// <summary>
    ///     The MCP SDK's tool attribute by full name. The SDK is the CLI's package reference alone, so the
    ///     spec cannot name its types, and an empty subject fails loudly, so a rename there reds the rules
    ///     anchored on these names rather than quietly emptying them.
    /// </summary>
    private const string McpServerToolAttribute = "ModelContextProtocol.Server.McpServerToolAttribute";

    /// <summary>The type-level twin of <see cref="McpServerToolAttribute" />, which marks a tool class.</summary>
    private const string McpServerToolTypeAttribute = "ModelContextProtocol.Server.McpServerToolTypeAttribute";

    private static void RoslynHost(Arch arch, Selection repo)
    {
        arch.Rule("roslyn/no-msbuildlocator-query")
            .Enforce(repo
                .MustNotUse(arch.Member(typeof(MSBuildLocator), nameof(MSBuildLocator.QueryVisualStudioInstances))))
            .Because("On .NET hosts QueryVisualStudioInstances returns no VS Setup instances, so code " +
                     "consulting it silently picks the wrong MSBuild; the sanctioned path is vswhere " +
                     "(VsWhereLocator). The quarantine's dragons warn this in prose — this rule is the teeth.")
            .Fix("Go through MsBuildBootstrap / VsWhereLocator; never query the Locator for instances.");
    }

    private static void Adapter(Arch arch, Layer adapter)
    {
        arch.Rule("xunit/throws-setup-errors-only")
            .Enforce(adapter.MustOnlyThrow(typeof(FileNotFoundException), typeof(InvalidOperationException)))
            .Because("The adapter runs inside consumers' test processes; its own failures must surface " +
                     "as the two documented setup errors, not as arbitrary exception noise beside the " +
                     "rule results.")
            .Fix("Route new failure modes through FileNotFoundException (missing solution) or " +
                 "InvalidOperationException (bad configuration).");
    }

    // The banned list is RS1035's, which the compiler enforces on the three projects that declare
    // EnforceExtendedAnalyzerRules — and only on those, because the rule inspects the project it is set
    // for. These two travel beside them and answer to the same host.
    private static void CompilerHost(Arch arch, Layer core, Layer extraction)
    {
        arch.Rule("analyzer/no-ambient-types")
            .Enforce(arch.AnyOf(core, extraction)
                .MustNotReference(
                    typeof(File),
                    typeof(Directory),
                    typeof(Environment),
                    typeof(Console),
                    typeof(Process),
                    typeof(ProcessStartInfo),
                    typeof(Random)))
            .Because("The analyzer package ships Core and Extraction in the directory the compiler loads the " +
                     "analyzer from, and the contract package ships a second Core copy beside its own " +
                     "analyzer, so both of these run inside csc: there is no console, the working directory " +
                     "belongs to whoever started the build, and one compiler server outlives many builds. " +
                     "RS1035 holds the analyzer projects to this list and cannot see the assemblies beside " +
                     "them.")
            .Citation("https://github.com/dotnet/roslyn/blob/3c9fedee40c44979ca893b6490ad5748bbb137b5/" +
                      "src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/Core/AnalyzerBannedSymbols.txt")
            .Fix("Take the value as a parameter, or through a host seam the CLI fills in; an assembly the " +
                 "compiler loads reads only what the compilation hands it.");

        arch.Rule("analyzer/no-ambient-members")
            .Enforce(arch.AnyOf(core, extraction)
                .MustNotUse(
                    arch.Member(typeof(Path), nameof(Path.GetTempPath)),
                    arch.Member(typeof(Assembly), nameof(Assembly.Load)),
                    arch.Member(typeof(CultureInfo), nameof(CultureInfo.CurrentCulture)),
                    arch.Member(typeof(CultureInfo), nameof(CultureInfo.CurrentUICulture))))
            .Because("These four reach the same ambient world through types both layers otherwise use " +
                     "correctly — Path for path arithmetic, CultureInfo for the invariant culture, Assembly " +
                     "for reflection-only discovery — so the ban is member-level, where banning the types " +
                     "would red on code that is right.")
            .Fix("Take the directory as a parameter, compare with InvariantCulture or Ordinal, and let the " +
                 "runtime resolve dependencies from the directory it loaded this assembly from.");
    }

    private static void Exceptions(Arch arch, Selection shipping)
    {
        // The nine exempted handlers each hold any failure and continue down a sanctioned degraded path:
        //   ArchChecker           one rule's fault becomes that rule's errored result, so the others still report
        //   ArchRuleTests         a spec that fails to build becomes one failing test row, not an empty theory
        //   BuildTierAnalyzer     an analyzer fault becomes one warning of its own, not an AD0001 that fails a
        //                         warnings-as-errors build
        //   CommandEntryPoint     every CLI failure becomes an exit code and a message, not a stack trace
        //   IdleTimeoutWatchdog   a poll fault logs and disables the watchdog rather than take the server down
        //   ParentProcessWatcher  a probe and a loop that both fail toward the safe side for a leak guard
        //   ServerShutdown        a faulting disposer must not stop the rest, or the process exits holding a lock
        //   SpecLiteralAnalyzer   the same boundary as BuildTierAnalyzer, inside a spec author's compiler: a
        //                         fault becomes one warning of its own and the spec still builds
        //   VsWhereLocator        any vswhere failure degrades to an empty list and the RegisterDefaults fallback
        arch.Rule("exceptions/no-swallowed-broad-catches")
            .Enforce(shipping
                .Except(arch.Types.Named(
                    "ArchChecker",
                    "ArchRuleTests",
                    "BuildTierAnalyzer",
                    "CommandEntryPoint",
                    "IdleTimeoutWatchdog",
                    "ParentProcessWatcher",
                    "ServerShutdown",
                    "SpecLiteralAnalyzer",
                    "VsWhereLocator"))
                .MustNotSwallow(typeof(Exception)))
            .Because("A broad catch that holds the failure and continues holds the ones nobody thought " +
                     "about — a cancellation, an out-of-memory, the bug introduced two lines up — and hands " +
                     "the caller a wrong answer that reads like a right one. Two shapes are not that, and " +
                     "this rule passes both: a `when` filter is where a handler writes down what it is " +
                     "actually for, so everything else keeps travelling (`catch (Exception ex) when (ex is " +
                     "IOException or UnauthorizedAccessException)`); and a clause that ends in a `throw` — " +
                     "cleanup-and-rethrow, or translate-and-throw — suppresses nothing at all. The " +
                     "sanctioned handlers are exempt by type name rather than by site, the same granularity " +
                     "a baseline would give, stated in the spec instead of recorded in a file.")
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Rethrow after the cleanup, translate to a type that names the failure, or add a `when` " +
                 "filter naming the exceptions this handler is for. A handler that genuinely has to hold " +
                 "everything and continue, like a process boundary or a background loop, goes on the spec's " +
                 "sanctioned list instead.");

        arch.Rule("exceptions/no-bare-bcl-throws")
            .Enforce(shipping
                .MustNotThrow(typeof(Exception), typeof(SystemException), typeof(ApplicationException)))
            .Because("A bare BCL exception type says only that something went wrong. A caller cannot filter " +
                     "on it, so throwing one forces every handler above into the swallowed broad catch " +
                     "`exceptions/no-swallowed-broad-catches` exists to prevent, and leaves a message string " +
                     "as the only thing left to match on. Nothing here throws one today; this rule is what " +
                     "stops the first one arriving unnoticed.")
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types")
            .Fix("Throw a type that names the failure — one of this repo's own error types, or the closest " +
                 "BCL type such as InvalidOperationException or IOException — so a handler can filter on it.");
    }

    private static void StaticState(Arch arch, Selection shipping)
    {
        // The subject is fields only, so the static `{ get; private set; }` diagnostics property
        // (MsBuildBootstrap.LastSelection) is outside it. Leaving it out is a choice:
        // `.Properties.ThatAreStatic().MustBeGetOnly()` would reach it (GRAMMAR §11).
        arch.Rule("state/no-static-mutable")
            .Enforce(shipping
                .Authored()
                .Fields.ThatAreStatic()
                .Where(m => !SanctionedMutableStatics.Contains(m.DeclaringType.Name + "." + m.Name),
                    description: "that are not one of the sanctioned mutable statics (the Interlocked " +
                                 "locations, the lock-guarded one-shot state, and the clock seam)")
                .MustBeReadonly())
            .Because("A writable static is process-wide state in a library a long-lived MCP server calls " +
                     "concurrently and every spec project links: whoever wrote to it last decides what the " +
                     "next caller reads, and nothing in the type system says so. A const counts as readonly " +
                     "here — it is readonly's superset — so the law asks for the weakest thing that closes " +
                     "the hole.")
            .Fix("Make it `readonly` or `const`. If it genuinely has to be written — an Interlocked location, " +
                 "a lock-guarded one-shot, or a test seam — add it to SanctionedMutableStatics in this spec " +
                 "with the family it belongs to, and say in review which one.");
    }

    /// <summary>
    ///     The static fields whose mutability is intended, exempted from <c>state/no-static-mutable</c>.
    ///     Keyed by declaring type and name rather than by type alone, because a second mutable static in the
    ///     same class is exactly what the law should still catch.
    /// </summary>
    private static readonly HashSet<string> SanctionedMutableStatics =
    [
        // Interlocked and volatile locations: a compare-and-swap needs a writable field.
        "IdleTimeoutWatchdog.s_inFlightCount",
        "IdleTimeoutWatchdog.s_lastActivityTicks",
        "ServerShutdown.s_hasExited",
        "WorkspaceLoader._loadCount",
        // One-shot state written under a lock.
        "ArchRuleTests.s_run",
        "IdleTimeoutWatchdog.s_drainWaiter",
        // The clock seam: the only way to test an idle timeout without sleeping for it.
        "IdleTimeoutWatchdog.s_timestampProvider"
    ];

    private static void Naming(Arch arch, Selection shipping, Selection repo)
    {
        arch.Rule("naming/async-suffix")
            .Enforce(shipping
                .Authored()
                .Methods.Returning(typeof(Task), typeof(Task<>), typeof(ValueTask), typeof(ValueTask<>))
                .Where(m => m.Name != "Rule_Holds" && m.Name != "Workspace_LoadedCompletely"
                                                   && m.Name != "WhenAllCallsComplete",
                    description: "whose name is not Rule_Holds or Workspace_LoadedCompletely (consumer-facing " +
                                 "test display names) or WhenAllCallsComplete (a Task.WhenAll-style combinator)")
                .MustHaveSuffix("Async"))
            .Because("House convention held repo-wide: an agent grepping *Async sees every await point; " +
                     "the three named exceptions are deliberate, not drift.")
            .Citation("https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap")
            .Fix("Name Task-returning methods with the Async suffix.");

        arch.Rule("naming/interfaces")
            .Enforce(repo.OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("Nearly every seam here is an interface — ISolutionSource, IEnvironment, " +
                     "IArchitectureSpec — and the `I` prefix is what lets a reader or an agent tell the port " +
                     "from its implementation at a glance, and what makes `I*` a reliable grep for the " +
                     "seams. A convention that holds for most types tells you nothing; this one is total.")
            .Citation("https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-classes-structs-and-interfaces")
            .Fix("Give the interface the `I` prefix.");
    }

    private static void Scopes(Arch arch, Layer model, Layer workspace)
    {
        // The scope is spelled as a region of Workspace rather than a bare namespace so the drawing can
        // say where it sits: Workspace is its project, which carries no globs for containment to be
        // inferred from. Both spellings select the same four types, this being the only project that
        // declares into that namespace.
        arch.Scope("roslyn/msbuild-bootstrap")
            .Quarantine(workspace.InNamespace("SlideRule.Roslyn.MsBuild.*"))
            .BoundaryOnlyVia(arch.Types.Named("MsBuildBootstrap"))
            .Dragons("We pick a VS 16/17 via vswhere and hand it to the out-of-process BuildHost through " +
                     "VSINSTALLDIR/VSCMD_VER=99.0. That preference is caution about a moving target, not a " +
                     "live workaround: the TypeInitializationException (XMakeElements) on legacy-namespace " +
                     "projects came from an early VS 18 preview and does not reproduce on VS 18.6. Where no " +
                     "16/17 is installed, the highest available is taken and MsBuildBootstrap.LastSelection " +
                     "says so — the CLI prints it beside any workspace-load diagnostic, and " +
                     "SLIDERULE_VS_INSTALL_PATH overrides the choice. Do NOT switch to " +
                     "MSBuildLocator.QueryVisualStudioInstances — on .NET it returns no VS Setup instances.")
            .Because("Fragile host bootstrap; contain it behind MsBuildBootstrap.");

        arch.Scope("model/prose-fragments")
            .Caution(model)
            .Dragons("Every node here declares the prose fragment the renderers assemble into its sentence, and " +
                     "the fragments are position-blind: one phrase serves subject and reference position, so a " +
                     "fragment never closes its own parenthetical — the closing comma belongs to the junction " +
                     "(SentenceRenderer.EndsOpen and CloseBefore). A strict verb renders its strictness by the " +
                     "absence of the external-packages caveat; do not add one. A changed fragment moves its pin " +
                     "in the same commit.")
            .Because("Every reader in Core reaches into Model and every renderer assembles its fragments, so " +
                     "nothing here can be fenced; the hazard is an edit that reads as tidying.");
    }
}
