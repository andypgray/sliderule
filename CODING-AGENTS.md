# Coding agents

`AGENTS.md` is the file `sliderule render` writes for coding agents, and this page is for whoever sets them up.

One spec gives an agent three things. Before it writes code, it reads the rules in the files `render` writes. While it works, it queries them through SlideRule's MCP server. When its turn ends, a hook runs `check` and hands back the verdict. If your solution has no spec yet, start with [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md), where you write the first rules or have an agent draft them.

## The context files

`sliderule render` writes the rules into a managed block in `AGENTS.md` beside the solution. The block sits between two markers, and `render` leaves the rest of the file as it is. `render` also writes a card into each scope's directory and into the directory of each layer a rule governs, holding what applies there. Commit these files with the spec project.

`AGENTS.md` is the convention file Codex, Cursor and Copilot read. Claude Code reads it from version 2.1.277, and loads a card once it opens a file in that card's directory. It does so only where no `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` sits in the directory it was started in or any directory above it. Your own `~/.claude/CLAUDE.md` does not count. Anthropic's [memory page](https://code.claude.com/docs/en/memory) sets out the whole rule.

A repository that already has a `CLAUDE.md` brings the managed block in with one line, `@AGENTS.md`, in a `CLAUDE.md` beside `AGENTS.md`. Before version 2.1.281, some sessions, such as those on Amazon Bedrock or with telemetry turned off, read `CLAUDE.md` files only. The import reaches those sessions too, and it never makes Claude Code read `AGENTS.md` twice.

The import covers the root file only. Where a `CLAUDE.md` counts, a developer gets the cards by setting Claude Code's Project instructions option to `claude-md-and-agents-md` in `/config`. That option is a personal setting, and a repository cannot commit it.

`render` looks for those three files in the solution's directory and every directory above it. When it finds one and none of them imports `AGENTS.md`, it prints a warning on stderr naming the nearest file and the line to add. The `derive_spec` recipe proposes the same line at its render step and leaves the edit to you.

## Connecting the MCP server

Install the tool first, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) shows. An MCP client launches the tool with the `mcp` verb, and the server speaks stdio. In a project's `.mcp.json`, the entry looks like this:

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

The solution argument is optional. A repository that commits a `sliderule.json` needs none, because the file names the solution and the spec for the server and every command alike, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#next-steps) shows. Without an argument, the server reads `SLIDERULE_SOLUTION_PATH`, and when that is unset too it walks up from its working directory. The walk stops at the first directory holding a `sliderule.json` or exactly one solution file. A `.slnf` filter counts there only when no `.sln` or `.slnx` stands beside it. Without the file, the walk finds nothing where the solution sits under `src/` and refuses as ambiguous where several sit side by side, so such a repository passes the argument.

A solution file passed as the argument beats both, and still takes its spec from a `sliderule.json` above it that names the same solution. A directory passed as the argument is where the walk starts, once the variable has been read. `--spec` names the spec where the solution holds no spec project or several, as a built DLL or a csproj in the solution. The server resolves the spec on each call, so it starts on a solution that has no spec project yet.

Without a solution argument, the server starts even when it cannot bind one. Every tool call then returns the reason, naming any solution it saw one level down. The reply also names the CLI commands that read the same model, because rebinding the server takes a client-config edit and a relaunch. `derive_spec` works on an unbound server too. A solution argument that does not resolve is a mistake in the config, so `mcp` exits 2 before the handshake.

`dnx` launches the server straight from nuget.org without the global install, and `--` hands everything after it to the tool:

```bash
dotnet dnx SlideRule.Cli -- mcp MyApp.sln
```

This is how MCP registry clients launch the server. Keep the `mcp` after `--`, since that verb is what starts the server. In a shell, spell it `dotnet dnx`, because on Windows the short `dnx` is a `.cmd` file that a POSIX shell does not resolve.

A config generated from the registry manifest passes no solution argument. A repository the walk cannot resolve then gets an unbound server. Its replies also give the survey command as `dotnet dnx SlideRule.Cli@<version> --yes -- graph <solution>`, because `dnx` installs no `sliderule` command. Bind such a repository with a committed `sliderule.json` at its root, which needs no change to the config when the client starts the server inside the repository. Otherwise bind it at install time: put the solution in the config's `args` after `mcp`, or set `SLIDERULE_SOLUTION_PATH` in its `env`.

`dnx` ships with the .NET 10 SDK. A client that launches `dnx` itself needs only one installed on the machine. The `dnx` command picks the newest installed SDK and never reads `global.json`, so a repository pinning an older SDK launches the server anyway. The `dotnet dnx` spelling goes through `dotnet`, which honours the nearest `global.json`. Inside a repository pinning an SDK below 10 that spelling fails, so run it from outside the repository and pass the solution's absolute path. With no .NET 10 SDK installed, a registry client sees a server that died before the handshake, with the reason on stderr alone.

The server never builds, so restore and build the solution before an agent queries it, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) says.

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

`rules` and `projects` take their globs as one semicolon-separated string, never as an array. The first call loads the solution, which takes seconds on a large one, and the server keeps it loaded between calls. Each later call checks it against the disk before answering. `SLIDERULE_IDLE_TIMEOUT_MINUTES` sets how long the server waits for a call before it exits, as the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#environment-variables) shows.

Violations are data, returned as findings in the document. A call returns an error only when the run itself fails, and the error carries the message the CLI would print.

`arch_check`, `arch_status` and `arch_graph` return the document their verb's `--json` writes, line for line. `arch_explain` returns the text `explain` prints. Given a `type` or a `path`, it names the layers, rules and scopes that claim each type, as `explain --type` does. Call it when a rule fires on a type you did not expect it to govern. `arch_context` returns the cards `render` writes for that path, or a line saying none covers it. An MCP reply has no exit code to fail on. So where the CLI refuses a partial model, `arch_check` and `arch_status` answer anyway and stamp the document `modelIncomplete`. `arch_graph` refuses as `graph` does, unless `allowWorkspaceDiagnostics` is set. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions) gives each document's `schemaVersion`.

## The response budget

The server reads `MAX_MCP_OUTPUT_TOKENS`, Claude Code's limit on an MCP reply, from its environment on every call. It allows two characters for each token, up to 50,000 characters, and an unset variable gives 50,000 as well.

Over that budget, `arch_check` and `arch_graph` step down through coarser grains (`overview`, then `skeleton`, then `index`) until a whole document fits. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#grains) says what each grain leaves out. The document then carries a `grain` key naming the grain it landed on, and it matches what the CLI writes at that grain. Any reply still over the budget is cut at a line break, and a footer says how much was left out. For `arch_check` and `arch_graph` the footer also names the parameter that narrows the answer.

The tools have no paging, so narrow a large answer with `rules` or `projects`. `arch_explain` returns any one rule whole.

## Drafting a spec with `derive_spec`

`derive_spec` is the server's prompt for a solution with no spec yet. In Claude Code, run it as `/sliderule:derive_spec`, and `/mcp__sliderule__derive_spec` runs it too.

SlideRule does not guess at an architecture, so the recipe has the agent work from evidence, in this order:

1. It reads what the team already wrote down about the architecture, such as ADRs, architecture tests and agent instruction files.
2. It surveys the solution with `arch_graph`.
3. It scaffolds a spec project.
4. It drafts every candidate rule as `Enforce`, including the directions the code already keeps.
5. It runs `arch_check` to count each rule's violations.
6. It proposes a posture for each rule from that count.
7. It stops at the curation gate, where you accept, edit or drop each rule.
8. You run `sliderule baseline init`, which records each `Migrate` rule's current violations and the existing references into each `Quarantine` scope.
9. You run `sliderule render` and commit the spec, the baselines and the rendered files as one change.

A baseline accepts a rule's current violations, so an agent able to record one could turn a red `Migrate` rule green without fixing anything. The server has no tool that writes a baseline, and the [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny the agent `baseline`, whether it runs the installed command or `dotnet dnx`. The [adoption walk](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) runs the whole recipe on a committed example. The [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#path-2-rules-an-agent-drafts) page offers the recipe as its path 2, and writing the rules yourself as path 1.

## The turn-end hook

`sliderule hook` runs `check` when an agent's turn ends. It reads the Claude Code event on stdin and answers in Claude Code's exit codes, so a settings entry is that one command. A red rule refuses the stop, and the report on stderr tells the agent what to fix. A clean run hands any tripwire warnings to the agent as context. The settings to paste, the exit-code contract and the install steps are in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks).
