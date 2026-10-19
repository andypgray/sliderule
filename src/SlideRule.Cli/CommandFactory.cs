using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Settings;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;

namespace SlideRule.Cli;

/// <summary>
///     Builds the command tree (System.CommandLine): every verb the CLI exposes, each with its options and
///     its action. Exposed so the in-process e2e tests drive the exact same tree the real entry point does,
///     capturing output via a redirected <see cref="InvocationConfiguration" />.
/// </summary>
internal static class CommandFactory
{
    /// <summary>
    ///     The name of the <c>hook</c> verb, which answers in Claude Code's exit codes rather than the CLI's, down to
    ///     a command line that does not parse.
    /// </summary>
    internal const string HookCommandName = "hook";

    /// <summary>
    ///     The command a user types, as the CLI project's <c>ToolCommandName</c> declares it; help prints it on every
    ///     Usage line in place of the running assembly's name.
    /// </summary>
    private const string ToolCommandName = "sliderule";

    /// <summary>
    ///     The command tree. <paramref name="hostSource" /> serves any run that would otherwise open a fresh one-shot
    ///     workspace (<see cref="MsBuildGate" /> says what it displaces); <paramref name="input" /> is the stdin the
    ///     <c>hook</c> verb reads its payload from, or null when nothing was piped in, and is passed here because
    ///     <see cref="InvocationConfiguration" /> carries an output and an error writer and no input.
    /// </summary>
    public static RootCommand BuildRootCommand(
        ISolutionSource hostSource, IEnvironment environment, TextReader? input = null)
    {
        RootCommand root = new(
            "SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for "
            + "coding agents.")
        {
            BuildCheckCommand(hostSource, environment),
            BuildExplainCommand(hostSource, environment),
            BuildRenderCommand(hostSource, environment),
            BuildBaselineCommand(hostSource, environment),
            BuildStatusCommand(hostSource, environment),
            BuildGraphCommand(hostSource, environment),
            BuildMcpCommand(environment),
            BuildHookCommand(hostSource, environment, input)
        };

        // Attached here rather than per verb, so a positional argument added later cannot go without it, and
        // walked to the leaves, so a subcommand's argument is guarded the same as a verb's.
        foreach (Argument argument in Descendants(root).SelectMany(static command => command.Arguments))
            argument.Validators.Add(RefuseUnknownOptions);

        HelpOption help = root.Options.OfType<HelpOption>()
            .Single();
        help.Action = new ToolNamedHelpAction((HelpAction)help.Action!, ToolCommandName);

        return root;
    }

    /// <summary>Every command below <paramref name="command" />, parents before their subcommands.</summary>
    private static IEnumerable<Command> Descendants(Command command)
    {
        return command.Subcommands.SelectMany(static subcommand => Descendants(subcommand).Prepend(subcommand));
    }

    private static Command BuildCheckCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON document instead of human-readable output."
        };
        Option<string?> diffBase = new("--diff-base")
        {
            Description =
                "A git ref; files changed since it are checked against quarantined and cautioned scopes (the scope tripwire) — warnings only, never failures."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Check against the partial model even when some projects fail to load or to restore, instead of "
            + "failing the run with exit 2.");
        Option<string?> sarif = new("--sarif")
        {
            Description =
                "Write a SARIF 2.1.0 report to <path> (GitHub code scanning et al.); human/--json output is unchanged."
        };
        Option<string?> rules = new("--rules")
        {
            Description =
                "Check only the rules whose ID matches these globs (semicolon-separated, '*' allowed and it "
                + "spans '/'); the report, the summary counts and the 0/1 verdict then cover that subset alone. "
                + "A filter matching no rule refuses the run (exit 2) and lists the available rule IDs."
        };
        Option<string?> grain = GrainOption(
            "How much of the --json report to keep, each rung coarser than the one before it and never "
            + "narrower: full (the default); overview, every rule and violation with each violation's sites "
            + "replaced by their count; skeleton, every rule with its prose, status, baseline and warnings and "
            + "its violations replaced by their count; index, a verdict per rule ID with the prose, the NuGet "
            + "audit advisories and the workspace diagnostics elided (packageAuditCount and "
            + "workspaceDiagnosticCount say how many) and every trust stamp kept, which is the menu --rules "
            + "globs pick from.");
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();

        Command check = new(
            "check",
            "Evaluate rules against a target solution; a project that fails to load, or whose NuGet packages did "
            + "not resolve, fails the run (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            json,
            diffBase,
            allowWorkspaceDiagnostics,
            sarif,
            rules,
            grain,
            noCache,
            binlog
        };

        SetRequestAction(
            check,
            parseResult => Resolved(parseResult, solution, spec, environment, targets => new CheckRequest(
                targets.Solution,
                targets.Spec,
                parseResult.GetValue(json),
                HookJson: false,
                parseResult.GetValue(diffBase),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                parseResult.GetValue(sarif),
                parseResult.GetValue(rules),
                DocumentGrains.Parse(parseResult.GetValue(grain)))),
            (request, output, error, ct) =>
                MsBuildGate.RunCheckAsync(request, output, error, hostSource, environment, ct));

        return check;
    }

    private static Command BuildExplainCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> ruleId = new("rule-id")
        {
            Description = "The rule ID to explain (a post-desugar ID, e.g. legacy/billing/containment). Omit it "
                          + "with --type or --path.",
            Arity = ArgumentArity.ZeroOrOne
        };
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> noCache = NoCacheOption();
        Option<string?> type = new("--type")
        {
            Description = "Explain a type instead of a rule: which layers, rules and scopes claim it, and why. A "
                          + "full name as check reports it, or a simple name.",
            HelpName = "name"
        };
        Option<string?> path = new("--path")
        {
            Description = "Explain every type a file or directory declares instead of a rule, absolute or "
                          + "relative to the solution.",
            HelpName = "path"
        };

        Command explain = new(
            "explain",
            "Print a rule's because, fix, citation, exemplar, posture payload, and linked prose; or, with --type or "
            + "--path, which layers, rules and scopes claim a type, and why.")
        {
            ruleId,
            solution,
            spec,
            noCache,
            type,
            path
        };

        SetRequestAction(
            explain,
            ExplainRequestFrom,
            (request, output, error, ct) =>
                MsBuildGate.RunExplainAsync(request, output, error, hostSource, environment, ct));

        return explain;

        // With --type or --path the rule ID is not asked for, so a lone positional is the solution: the parser binds
        // positionals in order and would otherwise read `explain --type Order App.slnx` as a rule ID and no solution.
        // Exactly one form is required here rather than by a validator, so the refusal is the run's exit 2 and its
        // text is the same whichever form is missing.
        ExplainRequest ExplainRequestFrom(ParseResult parseResult)
        {
            string? id = parseResult.GetValue(ruleId);
            string? solutionValue = parseResult.GetValue(solution);
            string? typeName = parseResult.GetValue(type);
            string? pathValue = parseResult.GetValue(path);

            if ((typeName is not null || pathValue is not null) && id is not null && solutionValue is null)
            {
                solutionValue = id;
                id = null;
            }

            int forms = new[] { id, typeName, pathValue }.Count(form => form is not null);
            if (forms != 1) throw new UserErrorException(ExplainFormsRefusal);

            string? specValue = parseResult.GetValue(spec);
            return Resolved(solutionValue, specValue, environment, targets => new ExplainRequest(
                id, targets.Solution, targets.Spec, Directory.GetCurrentDirectory(), parseResult.GetValue(noCache),
                typeName, pathValue));
        }
    }

    internal const string ExplainFormsRefusal = "Pass exactly one of a rule ID, --type and --path.";

    private static Command BuildRenderCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Render from the partial model even when some projects fail to load or to restore, instead of refusing "
            + "the command with exit 2.");
        Option<bool> model = new("--model")
        {
            Description =
                "Write sliderule.model.json, the built model as data, beside the root AGENTS.md. Needed "
                + "once: any later render refreshes a model file that exists."
        };
        Option<string?> diagram = new("--diagram")
        {
            Description =
                "Also render two Mermaid diagrams into <path>'s managed block: the codebase graph "
                + "(projects and their cross-project references; solid = observed, dotted = declared but unobserved), "
                + "then the spec's law (the places it names, what they must not reference, and the debt it grandfathers). "
                + "The graph draws the projects the solution file declares, so a project a ProjectReference "
                + "dragged into the build is not drawn; `graph` lists every loaded project and says which is which."
        };
        Option<string?> diagramOnly = new("--diagram-only")
        {
            Description =
                "Draw only the declared projects matching these name globs (semicolon-separated, '*' allowed) — "
                + "for legibility on a large solution, not to keep a foreign project out. "
                + "Scopes the codebase survey fence only, never the law fence. With --diagram."
        };
        Option<string?> diagramExclude = new("--diagram-exclude")
        {
            Description =
                "Drop the declared projects matching these name globs (semicolon-separated, '*' allowed) — "
                + "for legibility on a large solution, not to keep a foreign project out. "
                + "Scopes the codebase survey fence only, never the law fence. With --diagram."
        };
        Option<bool> check = new("--check")
        {
            Description =
                "Write nothing: compare every file this render would write with the one on disk, name each that "
                + "differs, and exit 1 if any does. For CI, in place of a render followed by git diff."
        };
        Option<bool> noCache = NoCacheOption();

        Command render = new(
            "render",
            "Render the managed AGENTS.md block(s) from the spec, and sliderule.model.json where --model is "
            + "passed or the file exists; a project that fails to load, or whose NuGet packages did not "
            + "resolve, refuses the command (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            allowWorkspaceDiagnostics,
            model,
            diagram,
            diagramOnly,
            diagramExclude,
            check,
            noCache
        };

        SetRequestAction(
            render,
            parseResult => Resolved(parseResult, solution, spec, environment, targets =>
            {
                // The diagram the settings file declares, when the command line names none of its own: --diagram on
                // the command line replaces the declared one whole, so the declared scope never lands on a file it
                // was not for.
                string? diagramOnCommandLine = parseResult.GetValue(diagram);
                DiagramSettings? declared = diagramOnCommandLine is null ? targets.Settings?.Render.Diagram : null;
                return new RenderRequest(
                    targets.Solution,
                    targets.Spec,
                    Directory.GetCurrentDirectory(),
                    parseResult.GetValue(noCache),
                    parseResult.GetValue(allowWorkspaceDiagnostics),
                    diagramOnCommandLine ?? declared?.Target,
                    parseResult.GetValue(diagramOnly) ?? GlobList.Join(declared?.Only),
                    parseResult.GetValue(diagramExclude) ?? GlobList.Join(declared?.Exclude),
                    parseResult.GetValue(model) || targets.Settings?.Render.Model == true,
                    parseResult.GetValue(check));
            }),
            (request, output, error, ct) =>
                MsBuildGate.RunRenderAsync(request, output, error, hostSource, environment, ct));

        return render;
    }

    // A command group: the four modes of the ratchet are subcommands, so choosing exactly one is the shape of
    // the command line rather than a check, and the add and rename companions exist only where they mean something.
    private static Command BuildBaselineCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        return new Command(
            "baseline",
            "Grandfather, shrink, add one attributed exception to, or rename the ratcheted baselines. init, "
            + "accept-reductions and add refuse (exit 2) when a project fails to load or its NuGet packages did not "
            + "resolve, unless --allow-workspace-diagnostics is passed; rename reads only the spec and the baseline "
            + "files.")
        {
            BuildBaselineModeCommand(
                "init",
                "Grandfather each uncaptured ratcheted rule's current violations into its baseline. Always "
                + "extracts fresh, because a rule it cannot see would be recorded as zero debt.",
                BaselineMode.Init, hostSource, environment),
            BuildBaselineModeCommand(
                "accept-reductions",
                "Remove baseline entries whose violations no longer occur and lower recorded site counts; never "
                + "adds. Always extracts fresh, because a violation it cannot see would be deleted as fixed.",
                BaselineMode.AcceptReductions, hostSource, environment),
            BuildBaselineAddCommand(hostSource, environment),
            BuildBaselineRenameCommand(hostSource, environment)
        };
    }

    // init and accept-reductions take the same arguments: neither has companions, and both extract cache-free
    // whatever an operator asks, so neither offers --no-cache.
    private static Command BuildBaselineModeCommand(
        string name, string description, BaselineMode mode, ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> allowWorkspaceDiagnostics = BaselineAllowWorkspaceDiagnosticsOption();

        Command command = new(name, description)
        {
            solution,
            spec,
            allowWorkspaceDiagnostics
        };

        SetRequestAction(
            command,
            parseResult => Resolved(parseResult, solution, spec, environment, targets => new BaselineRequest(
                targets.Solution,
                targets.Spec,
                mode,
                Directory.GetCurrentDirectory(),
                NoCache: false,
                AllowWorkspaceDiagnostics: parseResult.GetValue(allowWorkspaceDiagnostics))),
            (request, output, error, ct) =>
                MsBuildGate.RunBaselineAsync(request, output, error, hostSource, environment, ct));

        return command;
    }

    private static Command BuildBaselineAddCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<string?> rule = new("--rule")
        {
            Description = "The ratcheted rule ID to add the entry to."
        };
        Option<string?> because = new("--because")
        {
            Description = "Why this entry is grandfathered — recorded in the baseline entry (single line)."
        };
        Option<string?> source = new("--source")
        {
            Description =
                "The referencing type of the edge to grandfather — a full type name or 'T:' symbol ID — or the project "
                + "a package, framework or property violation names."
        };
        Option<string?> target = new("--target")
        {
            Description =
                "The referenced type of the edge to grandfather — a full type name or 'T:' symbol ID, or a banned "
                + "member's full name (System.DateTime.Now) or member symbol ID (P:System.DateTime.Now), or the "
                + "project's package, target framework, or property with its framework (Serilog, net48, "
                + "Nullable@net8.0)."
        };
        Option<string?> subject = new("--subject")
        {
            Description = "The offending type of the shape violation to grandfather — a full type name or 'T:' symbol ID."
        };
        Option<bool> allowWorkspaceDiagnostics = BaselineAllowWorkspaceDiagnosticsOption();
        // The one baseline mode the cache may serve: add records a violation the run did see, so a stale hit
        // can only fail to find it, never bless something that is not there.
        Option<bool> noCache = NoCacheOption();

        Command add = new(
            "add",
            "Grandfather exactly one currently observed violation of a captured rule, with attribution: an edge "
            + "(--source with --target) or a shape (--subject).")
        {
            solution,
            spec,
            rule,
            because,
            source,
            target,
            subject,
            allowWorkspaceDiagnostics,
            noCache
        };

        SetRequestAction(
            add,
            parseResult => Resolved(parseResult, solution, spec, environment, targets =>
            {
                BaselineAddition addition = new(
                    parseResult.GetValue(rule),
                    parseResult.GetValue(because),
                    parseResult.GetValue(source),
                    parseResult.GetValue(target),
                    parseResult.GetValue(subject));
                return new BaselineRequest(
                    targets.Solution,
                    targets.Spec,
                    BaselineMode.Add,
                    Directory.GetCurrentDirectory(),
                    NoCache: parseResult.GetValue(noCache),
                    AllowWorkspaceDiagnostics: parseResult.GetValue(allowWorkspaceDiagnostics),
                    Addition: addition);
            }),
            (request, output, error, ct) =>
                MsBuildGate.RunBaselineAsync(request, output, error, hostSource, environment, ct));

        return add;
    }

    // rename measures nothing, so it offers neither --allow-workspace-diagnostics (there is no gate to opt out
    // of) nor --no-cache (there is no extraction to take fresh). Both companions are required by the parser.
    private static Command BuildBaselineRenameCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<string> from = new("--from")
        {
            Description =
                "The rule ID the baseline section is filed under: the rule's old ID, which the spec no longer declares.",
            Required = true
        };
        Option<string> to = new("--to")
        {
            Description = "The rule's new ID: a ratcheted rule in the spec with no baseline section yet.",
            Required = true
        };

        Command rename = new(
            "rename",
            "Move a renamed rule's baseline section from its old rule ID to its new one, with every entry kept and "
            + "re-sealed. Reads the spec and the baseline files, never the code.")
        {
            solution,
            spec,
            from,
            to
        };

        SetRequestAction(
            rename,
            parseResult => Resolved(parseResult, solution, spec, environment, targets => new BaselineRequest(
                targets.Solution,
                targets.Spec,
                BaselineMode.Rename,
                Directory.GetCurrentDirectory(),
                NoCache: false,
                AllowWorkspaceDiagnostics: false,
                Rename: new BaselineRename(parseResult.GetRequiredValue(from), parseResult.GetRequiredValue(to)))),
            (request, output, error, ct) =>
                MsBuildGate.RunBaselineAsync(request, output, error, hostSource, environment, ct));

        return rename;
    }

    private static Option<bool> BaselineAllowWorkspaceDiagnosticsOption()
    {
        return AllowWorkspaceDiagnosticsOption(
            "Write baselines from the partial model even when some projects fail to load or to restore, instead of "
            + "refusing the command with exit 2.");
    }

    private static Command BuildStatusCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON burndown document instead of human-readable output."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Report the burndown from the partial model even when some projects fail to load or to restore, "
            + "instead of exiting 2 after rendering it.");
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();

        Command status = new(
            "status",
            "Report per-rule burndown and promotion suggestions; red rules never fail the run, but a project that "
            + "fails to load or to restore does (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            spec,
            json,
            allowWorkspaceDiagnostics,
            noCache,
            binlog
        };

        SetRequestAction(
            status,
            parseResult => Resolved(parseResult, solution, spec, environment, targets => new StatusRequest(
                targets.Solution,
                targets.Spec,
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics))),
            (request, output, error, ct) =>
                MsBuildGate.RunStatusAsync(request, output, error, hostSource, environment, ct));

        return status;
    }

    private static Command BuildGraphCommand(ISolutionSource hostSource, IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<bool> json = new("--json")
        {
            Description = "Emit the machine-readable JSON survey document instead of human-readable output."
        };
        Option<bool> allowWorkspaceDiagnostics = AllowWorkspaceDiagnosticsOption(
            "Survey the partial model even when some projects fail to load or to restore, instead of refusing with "
            + "exit 2.");
        Option<bool> noCache = NoCacheOption();
        Option<string?> binlog = BinlogOption();
        Option<string?> grain = GrainOption(
            "How much of the survey to keep, each rung coarser than the one before it and never narrower: "
            + "full (the default); overview, the whole survey with each project's namespace inventory elided; "
            + "skeleton, the structural spine, every project with its frameworks, packaging facts, declared "
            + "project references and type count and the observed project edges, with its package references "
            + "elided and the external references, multiply declared types and shadowed types each reported as "
            + "a count; index, the project roster, every project with its solution membership and type count "
            + "alone and the observed edges, NuGet audit advisories and workspace diagnostics each reported as "
            + "a count, which is the menu --projects globs pick from.");
        Option<string?> projects = new("--projects")
        {
            Description =
                "Survey only the projects matching these name globs (semicolon-separated, '*' allowed); "
                + "references in both directions are kept, so an edge can name a project outside the scope. "
                + "A filter matching no project refuses the survey (exit 2) and lists the available names."
        };

        // Deliberately no --spec: the survey is a property of the codebase, and derive runs before any
        // spec exists (a spec project, once present, appears here as an ordinary project).
        Command graph = new(
            "graph",
            "Summarize the codebase: projects, declared vs observed project references, namespace inventory, and "
            + "grouped external references. Needs no spec; a project that fails to load or to restore refuses the "
            + "survey (exit 2) unless --allow-workspace-diagnostics is passed.")
        {
            solution,
            json,
            allowWorkspaceDiagnostics,
            noCache,
            binlog,
            grain,
            projects
        };

        SetRequestAction(
            graph,
            parseResult => Resolved(parseResult, solution, null, environment, targets => new GraphRequest(
                targets.Solution,
                parseResult.GetValue(json),
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(noCache),
                parseResult.GetValue(binlog),
                parseResult.GetValue(allowWorkspaceDiagnostics),
                DocumentGrains.Parse(parseResult.GetValue(grain)),
                parseResult.GetValue(projects))),
            (request, output, error, ct) =>
                MsBuildGate.RunGraphAsync(request, output, error, hostSource, environment, ct));

        return graph;
    }

    private static Command BuildMcpCommand(IEnvironment environment)
    {
        Argument<string?> solution = SolutionArgument();
        Option<string?> spec = SpecOption();

        Command mcp = new("mcp", "Run the MCP stdio server bound to a solution and spec.")
        {
            solution,
            spec
        };

        // The one command with nothing to write to stdout: the server owns it as a JSON-RPC channel, so the
        // output writer is discarded here rather than handed on.
        SetRequestAction(
            mcp,
            parseResult => Resolved(parseResult, solution, spec, environment, targets => new McpServerBinding(
                targets.Solution,
                targets.Spec,
                Directory.GetCurrentDirectory())),
            (binding, _, error, ct) => McpServerCommand.RunAsync(binding, environment, error, ct));

        return mcp;
    }

    // The hook resolves nothing in its request builder: its paths are relative to the working tree the hook fires
    // for, which only the payload names, and a refusal there would reach the handler below as exit 2, the one code
    // that tells an agent to keep working. The runner resolves inside its own handling instead.
    private static Command BuildHookCommand(ISolutionSource hostSource, IEnvironment environment, TextReader? input)
    {
        Argument<string?> solution = SolutionArgument(
            "Solution file, or a directory to search, relative to the working tree the hook fires for. Omit it "
            + "to use the settings file, or the walk up from that tree.");
        Option<string?> spec = SpecOption(
            "A built spec DLL, or a csproj that is a member of the target solution, relative to the working "
            + "tree the hook fires for. Omit it to use the settings file, or the convention.");
        Option<string> diffBase = new("--diff-base")
        {
            Description =
                "The git ref the check's scope tripwires list changed files from. The hook reads it on an edit, a "
                + "hand run, or a stop with no snapshot of the tree (no state, or no answer from git). Any other "
                + "stop passes the check the changed code files it listed itself. Warnings only, never failures.",
            DefaultValueFactory = static _ => "HEAD"
        };
        Option<int> maxRounds = new("--max-rounds")
        {
            Description =
                "How many consecutive continuations one prompt may get from a red rule or a warning. Past it the "
                + "hook reports the red rule without blocking, and the turn ends.",
            DefaultValueFactory = static _ => 3
        };
        maxRounds.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<int>() < 1) result.AddError("--max-rounds must be at least 1.");
        });

        Command hook = new(
            HookCommandName,
            "Run as a Claude Code hook: read the event's payload on stdin, check the working tree when an agent's "
            + "turn ends, and answer in Claude Code's exit codes. 0 lets the stop stand, with any tripwire warnings "
            + "on stdout as context for the agent; 2 refuses it, with the report on stderr; 1 is SlideRule's own "
            + "error, reported on stderr without blocking.")
        {
            solution,
            spec,
            diffBase,
            maxRounds
        };

        SetRequestAction(
            hook,
            parseResult => new HookRequest(
                parseResult.GetValue(solution),
                parseResult.GetValue(spec),
                parseResult.GetRequiredValue(diffBase),
                parseResult.GetValue(maxRounds),
                Directory.GetCurrentDirectory()),
            (request, output, error, ct) => new HookRunner(
                    output, error, input, environment, new InProcessHookCheck(hostSource, environment))
                .RunAsync(request, ct));

        return hook;
    }

    // Every command resolves its solution and spec through the settings file the same way, and every one but the
    // hook does it inside the request builder, so a malformed or misdirected settings file is refused like a bad
    // argument: exit 2, before any load.
    private static TRequest Resolved<TRequest>(
        ParseResult parseResult, Argument<string?> solution, Option<string?>? spec, IEnvironment environment,
        Func<InvocationTargets, TRequest> build)
    {
        string? solutionValue = parseResult.GetValue(solution);
        string? specValue = spec is null ? null : parseResult.GetValue(spec);
        return Resolved(solutionValue, specValue, environment, build);
    }

    // The same resolution over values a builder settled itself, which explain needs: its positional is the
    // solution only once the rule ID turns out to be absent.
    private static TRequest Resolved<TRequest>(
        string? solution, string? spec, IEnvironment environment, Func<InvocationTargets, TRequest> build)
    {
        InvocationTargets targets = InvocationTargets.Resolve(
            solution, spec, Directory.GetCurrentDirectory(), environment);
        return build(targets);
    }

    // The action shape every command shares: build the request off the parse and run it, both under the
    // top-level error handler and with the SAME error writer the run itself writes to. The request is built
    // inside the handler so a binding failure is reported as a failure of the run: a user error's message alone,
    // and exit 2. One place, so a verb added later cannot wire the two apart, and CliErrorMapper cannot end up
    // writing somewhere the verb's own diagnostics did not.
    internal static void SetRequestAction<TRequest>(
        Command command,
        Func<ParseResult, TRequest> requestFrom,
        Func<TRequest, TextWriter, TextWriter, CancellationToken, Task<int>> run)
    {
        command.SetAction((parseResult, ct) =>
        {
            TextWriter output = parseResult.InvocationConfiguration.Output;
            TextWriter error = parseResult.InvocationConfiguration.Error;
            return CommandEntryPoint.RunAsync(
                () =>
                {
                    TRequest request = requestFrom(parseResult);
                    return run(request, output, error, ct);
                },
                error,
                ct);
        });
    }

    // System.CommandLine hands a token that matches no option to the next free positional argument before it
    // would report the token as unrecognized, so without this `check --bogus` looks for a solution file named
    // --bogus. A path that begins with a dash is still reachable, written as ./-name.
    private static void RefuseUnknownOptions(ArgumentResult result)
    {
        foreach (Token token in result.Tokens.Where(static token => token.Value.StartsWith('-')))
            result.AddError($"Unrecognized option '{token.Value}'.");
    }

    private static Argument<string?> SolutionArgument(
        string description = "Solution file, a directory to search, or omitted to walk up from the working directory.")
    {
        return new Argument<string?>("solution")
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne
        };
    }

    private static Option<string?> SpecOption(
        string description =
            "A built spec DLL, or a csproj that is a member of the target solution. Omit to use the convention.")
    {
        return new Option<string?>("--spec")
        {
            Description = description
        };
    }

    // The rung names, the parse and the refusal all come from DocumentGrains, so this option and the grain tool
    // parameter accept the same words in any case and refuse the rest with one text. The validator adds that
    // refusal as the parse error; the completions offer the rungs, and the help label lists them finest first,
    // the order the refusal names them in.
    private static Option<string?> GrainOption(string description)
    {
        Option<string?> grain = new("--grain")
        {
            Description = description,
            HelpName = string.Join("|", DocumentGrains.Names)
        };
        grain.CompletionSources.Add([.. DocumentGrains.Names]);
        grain.Validators.Add(static result =>
        {
            var name = result.GetValueOrDefault<string?>();
            if (!DocumentGrains.TryParse(name, out _)) result.AddError(DocumentGrains.Refusal(name));
        });
        return grain;
    }

    private static Option<bool> NoCacheOption()
    {
        return new Option<bool>("--no-cache")
        {
            Description =
                "Bypass the persisted caches (extraction fragments and the build capture): always load and extract "
                + "fresh, and write nothing back."
        };
    }

    // The one opt-out of the incomplete-model gate, worded per verb but always the same flag name: the
    // five verbs the gate guards answer a partial load the same way, so an operator learns one flag.
    private static Option<bool> AllowWorkspaceDiagnosticsOption(string description)
    {
        return new Option<bool>("--allow-workspace-diagnostics") { Description = description };
    }

    private static Option<string?> BinlogOption()
    {
        return new Option<string?>("--binlog")
        {
            Description =
                "A .binlog from a real build of this solution on this machine. Replays the captured structure "
                + "instead of a design-time build; the capture persists, and later runs replay it automatically "
                + "while structurally valid."
        };
    }
}
