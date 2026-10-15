# Coding agents

`AGENTS.md` is the file `sliderule render` writes for coding agents, and this page is for the person who sets them up.

One spec gives an agent four things. Before it writes code, it reads the rules in the files `render` writes. While it works, it queries them through SlideRule's MCP server. With the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) wired in, its own builds report a broken rule as a compiler warning. When its turn ends, a hook runs `check` and returns the verdict. If your solution has no spec yet, start with [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md), where you write the first rules or have an agent draft them.

## Which agents use what

| Surface | Used by |
|---|---|
| the managed block in `AGENTS.md` | any agent that reads `AGENTS.md`, such as Codex, Cursor, Copilot and Claude Code |
| the per-directory cards | agents that read nested `AGENTS.md` files, Claude Code among them |
| the MCP tools and the `derive_spec` prompt | any MCP client |
| build warnings | any agent that runs `dotnet build` |
| the turn-end hook | Claude Code |

An agent with no turn-end hook still meets every rule in CI, and with the build analyzer wired in, many of them in its builds. The hook speaks Claude Code's hook contract: its event on stdin and its exit codes.

## The context files

`sliderule render` writes the rules into a managed block in the `AGENTS.md` in the solution's directory. The block is the text between the `<!-- sliderule:begin -->` and `<!-- sliderule:end -->` lines, and `render` leaves the rest of the file as it is. `render` also writes a card into each scope's directory and into the directory of each layer a rule governs, with the rules that apply there. Commit these files with the spec project.

Claude Code reads `AGENTS.md` from version 2.1.277, but only where no `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` is in the directory it was started in or any directory above it. Your own `~/.claude/CLAUDE.md` does not count. Once it opens a file in a card's directory, it loads that card too. Anthropic's [memory page](https://code.claude.com/docs/en/memory) sets out the whole rule.

A repository that already has a `CLAUDE.md` brings the managed block in with one line, `@AGENTS.md`, in a `CLAUDE.md` next to `AGENTS.md`. The import covers the root file only. A developer who wants the cards as well sets Claude Code's Project instructions option to `claude-md-and-agents-md` in `/config`, a personal setting a repository cannot commit. Before version 2.1.281, some sessions, such as those on Amazon Bedrock or with telemetry turned off, read `CLAUDE.md` files only. The import reaches those sessions too, and it never makes Claude Code read `AGENTS.md` twice.

`render` looks for those three files in the solution's directory and every directory above it. When it finds one and none of them imports `AGENTS.md`, it prints a warning on stderr naming the nearest file and the line to add. The `derive_spec` recipe proposes the same line at its render step and leaves the edit to you.

## Connecting the MCP server

Install the tool first, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) shows. An MCP client launches the tool with the `mcp` command, and the server speaks stdio. In a project's `.mcp.json`, the entry looks like this:

```json
{
  "mcpServers": {
    "sliderule": {
      "command": "sliderule",
      "args": ["mcp", "MyApp.sln"]
    }
  }
}
```

For Claude Code, one command writes that same entry into the project's `.mcp.json`:

```bash
claude mcp add --scope project sliderule -- sliderule mcp MyApp.sln
```

The solution argument is optional. Without it, the server reads `SLIDERULE_SOLUTION_PATH`. When that is unset too, it walks up from its working directory to the first directory with a `sliderule.json` or exactly one solution file, as [Solution and spec resolution](https://github.com/andypgray/sliderule/blob/main/CLI.md#solution-and-spec-resolution) describes. A repository that commits a `sliderule.json` at its root therefore needs no argument, because the file names the solution and the spec for the server and every command alike. The walk climbs past a directory with several solutions, and when no directory above has a `sliderule.json` or exactly one solution, its error names the solution files of the first directory it climbed past. `--spec` names the spec where the solution has no spec project or several. The server resolves the spec on each call, so it starts on a solution that has no spec project yet.

Without a solution argument, the server starts even when it cannot bind one. Every tool call then returns that error, which names any solution one level down when the walk met none at all. Each reply also names the CLI commands that read the same model, because rebinding the server takes a client-config edit and a relaunch. `derive_spec` works on an unbound server too, through those commands. A solution argument that does not resolve is a mistake in the config, so `mcp` exits 2 before the handshake.

`dnx` launches the server straight from nuget.org without the global install, and `--` passes everything after it to the tool:

```bash
dotnet dnx SlideRule.Cli -- mcp MyApp.sln
```

This is how MCP registry clients launch the server. Keep the `mcp` after `--`, since that command is what starts the server. Pin a release by writing the package as `SlideRule.Cli@<version>`. In a shell, spell the command `dotnet dnx`, because on Windows the short `dnx` is a `.cmd` file that a POSIX shell does not resolve.

A config generated from the registry manifest passes no solution argument, so a repository the walk cannot resolve gets an unbound server. Its replies then give the survey command as `dotnet dnx SlideRule.Cli@<version> --yes -- graph <solution>`, because `dnx` installs no `sliderule` command, and `--yes` accepts the download without a prompt. Bind such a repository with a committed `sliderule.json` at its root, which needs no change to the config when the client starts the server inside the repository. Otherwise bind it at install time, with the solution in the config's `args` after `mcp`, or with `SLIDERULE_SOLUTION_PATH` in its `env`.

`dnx` ships with the .NET 10 SDK, so a client that launches `dnx` itself needs only one installed on the machine. The `dnx` command picks the newest installed SDK and never reads `global.json`, so a repository pinning an older SDK launches the server anyway. The `dotnet dnx` spelling goes through `dotnet`, which honours the nearest `global.json`. Inside a repository pinning an SDK below 10 that spelling fails, so run it from outside the repository and pass the solution's absolute path. With no .NET 10 SDK installed, a registry client sees a server that died before the handshake, with the reason on stderr alone.

The server reads your code from source and runs the spec from its last build, so restore the solution and build the spec project before an agent queries it.

## The tools

None of the tools takes a solution, because the server is bound to one at launch. A `?` marks a parameter a call can leave out.

| Tool | Params | Returns |
|---|---|---|
| `arch_check` | `diffBase?`, `rules?`, `grain?` | the `check --json` document |
| `arch_status` | none | the `status --json` burndown |
| `arch_explain` | `ruleId?`, `type?`, `path?` (one of them) | one rule's `explain` text, or who claims a type |
| `arch_context` | `path` | the cards covering a file or directory |
| `arch_graph` | `allowWorkspaceDiagnostics?`, `grain?`, `projects?` | the `graph --json` survey, which needs no spec |
| `derive_spec` | none | a prompt: the recipe for a first spec |

`rules` and `projects` take their globs as one semicolon-separated string; a one-element array is accepted too. The first call loads the solution, and the server keeps it loaded between calls. Each later call checks it against the disk before answering. `SLIDERULE_IDLE_TIMEOUT_MINUTES` sets how long the server waits for a call before it exits, as the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#environment-variables) shows.

Violations are data, returned as findings in the document. A call returns an error when the run itself fails, and the error has the message the CLI would print. A call with an unknown argument key returns an error that lists the tool's parameters.

`arch_check`, `arch_status` and `arch_graph` return the document their command's `--json` writes, line for line. `arch_explain` returns the text `explain` prints. Given a `type` or a `path`, it names the layers, rules and scopes that claim each type, as `explain --type` does. Call it when a rule fires on a type you did not expect it to govern. `arch_context` is the one tool with no CLI command. It returns the cards `render` writes for that path, or a line saying no card covers it. An MCP reply has no exit code to fail on. So where the CLI exits 2 on a partial model, `arch_check` and `arch_status` still return their document, stamped `modelIncomplete`. `arch_graph` stops as `graph` does, unless `allowWorkspaceDiagnostics` is set. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions) gives each document's `schemaVersion`.

## The response budget

The server reads `MAX_MCP_OUTPUT_TOKENS`, Claude Code's limit on an MCP reply, from its environment on every call. It allows two characters for each token, up to 50,000 characters, and an unset variable gives 50,000 as well, which is what other clients get.

Over that budget, `arch_check` and `arch_graph` step down through coarser grains (`overview`, then `skeleton`, then `index`) until a whole document fits. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#grains) explains what each grain leaves out. The document then has a `grain` key naming the grain it landed on, and it matches what the CLI writes at that grain. Any reply still over the budget is cut at a line break, and a footer states how much was left out. For `arch_check` and `arch_graph` the footer also names the parameter that narrows the answer.

The tools have no paging, so narrow a large answer with `rules` or `projects`. `arch_explain` returns any one rule whole.

## Drafting a spec with `derive_spec`

`derive_spec` is the server's prompt for a solution with no spec yet. In Claude Code, run it as `/sliderule:derive_spec`, and `/mcp__sliderule__derive_spec` runs it too. Other MCP clients list it among the server's prompts.

SlideRule does not guess at an architecture, so the recipe has the agent work from evidence, in this order:

1. It lists what the team already wrote down about the architecture, with where each statement was found. That includes ADRs, architecture tests, agent instruction files and the developer docs the README links to.
2. It surveys the solution with `arch_graph`.
3. It scaffolds a spec project.
4. It drafts every candidate rule as `Enforce`, including the directions the code already keeps.
5. It runs `arch_check` to count each rule's violations.
6. It writes a posture for each rule into the spec from that count.
7. It stops for your review, where you accept, edit or drop each rule.
8. You run `sliderule baseline init`, which records each `Migrate` rule's current violations and the existing references into each `Quarantine` scope.
9. You run `sliderule render --model` and commit the spec, the baselines and the rendered files as one change.

The agent closes with a receipt. Its posture counts and the `Enforce` rules it left failing are copied from the last `arch_check` result.

A baseline accepts a rule's current violations, so an agent able to record one could turn a red `Migrate` rule green without fixing anything. The server has no tool that writes a baseline, and the [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny the agent `baseline`, whether it runs the installed command or `dotnet dnx`. A baseline entry edited by hand fails its seal on the next run. The seal can be recomputed, so review a baseline change as you would a change to the spec. The [adoption walk](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) runs the whole recipe on a committed example. The [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#path-2-rules-an-agent-drafts) page offers the recipe as its path 2, and writing the rules yourself as path 1.

## The turn-end hook

`sliderule hook` runs `check` when a Claude Code agent's turn ends. It reads the Claude Code event on stdin and answers in Claude Code's exit codes, so a settings entry is that one command. A red rule blocks the stop, and the report on stderr tells the agent what to fix. A block repeats at each stop until the rule is green, up to a cap of three consecutive continuations for one user prompt. Past the cap, the hook writes a red rule's report to stderr, exits 1, and lets the turn end. A clean run passes any tripwire warnings to the agent as context.

A turn that changed no code runs no check, unless the hook's last check found a red rule. The stop's check uses the [persisted cache](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching). Once a whole check has filled it, a check after a source edit loads only the projects it re-extracts and the projects they reference. Give each parallel session a worktree of its own, because a stop counts every change in its tree, another session's included. The settings to paste, the exit-code contract and the install steps are in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks).
