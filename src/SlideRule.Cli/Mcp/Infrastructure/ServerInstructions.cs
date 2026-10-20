using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     Loads the embedded <c>server-instructions.md</c> resource for use as the MCP server's
///     <c>ServerInstructions</c> (surfaced to clients on <c>initialize</c>), optionally banner-prefixed with
///     the reason the server could not bind to a solution.
/// </summary>
internal static class ServerInstructions
{
    /// <summary>
    ///     The recovery an unbound session can actually perform: the same engine, driven from the CLI with
    ///     the solution named. Embedded verbatim in the banner and appended to every tool call's error
    ///     result, so the two channels a client reads cannot drift apart.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is silent about <c>args</c> and the environment variable, which the banner names after it:
    ///         those bind the next session, and an agent inside this one cannot edit the client config that
    ///         launched it.
    ///     </para>
    ///     <para>
    ///         Both spellings are named because this process cannot know which one resolves for whoever is
    ///         reading. A config generated from the registry manifest passes no solution, so that session binds
    ///         only through <c>SLIDERULE_SOLUTION_PATH</c> or the walk-up, and the <c>dnx</c> launch the manifest
    ///         prescribes runs the package without installing the tool, so <c>sliderule</c> is absent there. The
    ///         installed spelling still leads, because it is right for every other unbound shape.
    ///     </para>
    ///     <para>
    ///         Four things here are required and easy to undo by accident. <c>dotnet dnx</c>, never bare
    ///         <c>dnx</c>: on Windows that command is <c>dnx.cmd</c>, and a POSIX shell resolves a bare name
    ///         to <c>.exe</c> alone, so the short spelling is itself a command not found in the shell an agent
    ///         drives. <c>--yes</c>, which is a real option though it appears in no <c>--help</c> — without it
    ///         a cold machine can block on a trust prompt, and a blocked stdio session has no second channel
    ///         to say so on. <see cref="ServerVersion.SemVer" /> rather than a floating id, because
    ///         "same engine, same verdicts" is a claim this sentence makes two clauses earlier; pinned, it
    ///         also resolves from the cache the server is already running out of. And the <c>global.json</c>
    ///         clause, because <c>dotnet dnx</c> selects its SDK the way every <c>dotnet</c> command does:
    ///         inside a repository pinning an SDK below 10 the verb does not exist (measured on 9.0.313), though
    ///         the <c>dnx</c> script that launched this server ignores the pin.
    ///     </para>
    /// </remarks>
    internal static readonly string UnboundCallCoda =
        "The MCP tools stay unusable for this session; rebinding takes a client-config edit and a relaunch. "
        + "Use the CLI with the solution named instead — same engine, same verdicts: "
        + "`sliderule graph <solution>` surveys the codebase, `sliderule check <solution>` runs the rules. "
        + $"A registry-manifest session has no such command: `dotnet dnx SlideRule.Cli@{ServerVersion.SemVer} --yes -- graph <solution>`, "
        + "run from outside any `global.json` that pins an SDK below 10.";

    internal static readonly string Text = EmbeddedResourceText.Load("server-instructions.md");

    /// <summary>
    ///     The instructions to hand the client: <see cref="Text" /> unchanged when the server bound
    ///     normally, and otherwise a banner carrying <paramref name="bindingFailure" /> verbatim above it.
    /// </summary>
    /// <remarks>
    ///     The banner is only ever prepended and the embedded resource never edited, so a bound server and an
    ///     unbound one describe the same tools in the same words.
    /// </remarks>
    internal static string For(string? bindingFailure)
    {
        if (bindingFailure is null) return Text;

        return "**This server is running but is not bound to a solution.** Solution discovery failed:\n\n"
               + bindingFailure
               + "\n\n"
               + UnboundCallCoda
               + "\n\nTo bind future sessions, add the solution to this server's `args` in the client config "
               + $"(after `mcp`), or set `{SlideRuleEnvVars.SolutionPath}` in its `env`. Nothing else about "
               + "this server changes.\n\n"
               + Text;
    }
}
