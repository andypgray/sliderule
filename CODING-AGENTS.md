# Coding agents

`sliderule render` writes architecture rules into `AGENTS.md` files for coding agents.

One spec gives an agent four ways to work with the rules. It reads the rendered files before editing code and queries SlideRule's MCP server while it works. With the [build analyzer](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) wired in, its builds report violations, and a turn-end hook runs `check` and returns the verdict. If you need a first spec, [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md) covers writing rules yourself or having an agent draft them.

## Which agents use what

| Surface | Used by |
|---|---|
| the managed block in `AGENTS.md` | any agent that reads `AGENTS.md`, such as Codex, Cursor, Copilot and Claude Code |
| the per-directory cards | agents that read nested `AGENTS.md` files, Claude Code among them |
| the MCP tools and the `derive_spec` prompt | any MCP client |
| build warnings | any agent that runs `dotnet build` |
| the turn-end hook | Claude Code |

For agents without a turn-end hook, `sliderule check` remains the whole-solution gate in CI. The analyzer checks only rules decidable from the project being built; an `Enforce` violation produces compiler warning `SR2001`. `sliderule hook` implements Claude Code's contract, reading event payloads on stdin and returning its hook exit codes. Its settings entries use `UserPromptSubmit`, `Stop` and `SubagentStop`.

## The context files

`render` writes the root managed block into `AGENTS.md` in the solution's directory. Its boundaries are the lines `<!-- sliderule:begin -->` and `<!-- sliderule:end -->`. Text outside an existing block stays byte for byte unchanged. Without markers, `render` trims trailing line breaks and appends the block after one blank line. The markers contain no version, so rendering an unchanged spec leaves the block unchanged. Each scope and each layer with rules of its own also gets a directory card containing the applicable rules. A card is another `AGENTS.md` with a managed block; cards placed in the same directory merge into one file. Commit the rendered files with the spec project.

Claude Code reads `AGENTS.md` from version 2.1.277, but by default reads none when a `CLAUDE.md`, `.claude/CLAUDE.md` or `CLAUDE.local.md` is in the directory it starts in or any directory above it. `render` checks for the same three files, in that order within each directory. It searches the solution's directory and its ancestors, stopping below the filesystem root. A drive-root file therefore does not count. Neither does the user's memory file, `.claude/CLAUDE.md` under `USERPROFILE` on Windows or `HOME` elsewhere. `CLAUDE.md` under `CLAUDE_CONFIG_DIR` is also excluded when that variable is set. Once Claude Code opens a file in a card's directory, it loads that card too. These checks mirror Claude Code's instruction lookup; Anthropic's [memory page](https://code.claude.com/docs/en/memory) describes how instructions load.

To import the root rules, put the line `@AGENTS.md` in a `CLAUDE.md` next to the generated `AGENTS.md`. An import counts when its path resolves to that file, outside fenced blocks, HTML comments and code spans. A Claude instruction file that is a symbolic link to that `AGENTS.md` counts too. If instruction files exist but none imports or links to the block, every `render` run prints a `warning:` on stderr naming the nearest file. For a plain `CLAUDE.md` next to the block, the advice is `add the line @AGENTS.md to CLAUDE.md`. Otherwise it is `add a CLAUDE.md beside AGENTS.md holding the line @AGENTS.md`. The import covers the root file only. A developer who wants the cards as well sets Claude Code's Project instructions option to `claude-md-and-agents-md` in `/config`, a personal setting a repository cannot commit. As of 2026-11-06, Claude Code on Windows loads a card only when the path of the file it opens spells the drive letter in the same case as the directory it started in. VS Code starts it in a lowercase `c:\` directory, so a session there loads no card for a file it opens under `C:\`. Before version 2.1.281, some sessions, such as those on Amazon Bedrock or with telemetry turned off, read `CLAUDE.md` files only. The import reaches those sessions too, and it never makes Claude Code read `AGENTS.md` twice. `render` never reads Claude Code's settings. One developer's option to load both files cannot silence a warning intended for everyone in the repository. The `derive_spec` recipe proposes the import at its render step and leaves the edit to you.

Codex reads project instructions from the repository root down to its starting directory, root first. Its default combined limit is 32 KiB (32,768 bytes). The file crossing that limit is cut short; later files are skipped. `render` warns on stderr if the root file alone exceeds the limit. Otherwise it warns if a card plus the rendered files above it exceeds the limit, reporting the largest total. The warning gives the byte count and two remedies: raise `project_doc_max_bytes` in Codex's `config.toml`, or shorten the files. It measures the bytes it would write, including under `--check`. It counts only its own output files, excluding handwritten files above the solution, overrides and Codex settings. OpenAI's [AGENTS.md guide](https://learn.chatgpt.com/docs/agent-configuration/agents-md) describes the lookup.

In each directory, Codex chooses `AGENTS.override.md` before `AGENTS.md`; even an empty override hides the generated file. `render` warns on stderr for each output file with an override next to it. Each warning names both paths and recommends moving the override's text into the generated file, outside its managed block, or removing the override. This check does not read the override's contents. It also excludes `~/.codex/AGENTS.override.md`, which Codex reads separately and which hides no project file.

## Connecting the MCP server

Install the `sliderule` command from the `SlideRule.Cli` package, as [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#requirements) shows. An MCP client launches `sliderule mcp [<solution>] [--spec <spec>]` over stdio. The project's `.mcp.json` entry looks like this:

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

The solution argument can name a solution file or a directory to search. Without an argument, the server uses `SLIDERULE_SOLUTION_PATH`; without either, it searches upward from its working directory. The search stops at the first directory containing `sliderule.json` or exactly one `.sln`, `.slnx` or `.slnf`. A `.slnf` is excluded when a full solution is present. `sliderule.json` names the solution and, optionally, the spec, with paths relative to its directory. A solution argument takes precedence over the environment variable, and both take precedence over the file's solution choice. The file takes precedence over ordinary discovery. A committed root settings file therefore binds commands and the server without a solution argument. [Solution and spec resolution](https://github.com/andypgray/sliderule/blob/main/CLI.md#solution-and-spec-resolution) describes the shared lookup.

A directory with several solutions is skipped during the search. If discovery still fails, the error lists the nearest ambiguous directory's files under `Multiple solution files found in '<directory>':`. When no solution was found, it begins `No .sln, .slnf or .slnx file found in '<directory>' or any parent directory.` It also lists up to five nearby solutions under `Solution files one level down:` when available. Both errors recommend a solution argument, including in the MCP client's `args`, or `SLIDERULE_SOLUTION_PATH`. They also recommend a root `sliderule.json` to configure every command and the server once.

Use `--spec` for a built spec DLL or a spec csproj declared in the solution. Otherwise the server uses the settings file's spec, then the unique declared solution project referencing `SlideRule.dll`. With no such project or several, supply `--spec`. Solution discovery runs at startup; spec resolution and workspace loading happen on tool calls. The server can therefore start before a spec project exists.

With no solution argument, including a blank argument, failed discovery leaves the server running unbound. Its instructions begin `This server is running but is not bound to a solution, because solution discovery failed.` Tool calls return the discovery error. Each error reply explains that the tools remain unusable for this session and rebinding requires a client-config edit and relaunch. It also names `sliderule graph <solution>` and `sliderule check <solution>` as alternatives using the same model. `derive_spec` remains available and can guide work through the CLI. An unresolved explicit solution argument instead causes exit 2 before the handshake. A malformed `sliderule.json` reached during resolution, or one naming a missing solution, also causes exit 2 before loading, even without an argument.

`dnx` launches the server from nuget.org without a global installation. Put the package before `--` and the tool's arguments after it:

```bash
dotnet dnx SlideRule.Cli -- mcp MyApp.sln
```

Pin a release by writing the package as `SlideRule.Cli@<version>`. The registry manifest uses package `SlideRule.Cli`, runtime hint `dnx`, stdio transport and `mcp` as its first argument. Its solution argument is optional, so a generated config may omit it. Bind future sessions through a root `sliderule.json` when the client starts inside the repository. Alternatively, put the solution after `mcp` in the server's `args`, or set `SLIDERULE_SOLUTION_PATH` in its `env`. For an unbound registry session, replies recommend `dotnet dnx SlideRule.Cli@<version> --yes -- graph <solution>`. The version matches the running server. The recipe uses that versioned prefix for every CLI command. `--yes` accepts the download without a trust prompt, although it is absent from `dnx` help.

Use `dotnet dnx` in a shell. On Windows, the short command is `dnx.cmd`, which a POSIX shell does not resolve by its bare name. The `dnx` script ignores SDK pins; `dotnet dnx` honours `global.json`. With a pin below SDK 10, run `dotnet dnx` from outside that tree and pass the solution's absolute path. The CLI targets `net10.0` and permits roll-forward to newer major runtimes.

Restore the solution before querying it. `arch_graph` needs no spec or solution build, but reports load or restore failures as errors by default. Other tools use the spec's last build; the CLI never builds it. Build the spec project before the first query and after editing its sources. When a source differs, check and status documents include `staleSpec`, identifying the project and differing file with solution-relative paths. Comparison uses portable-PDB checksums when available, with file write times as the fallback. `arch_explain` and `arch_context` name the differing file above their answers.

## The tools

The server targets one solution for the session, so none of its five tools takes a solution argument. It also provides one prompt. A `?` marks a parameter a call can leave out.

| Tool | Params | Returns |
|---|---|---|
| `arch_check` | `diffBase?`, `rules?`, `grain?` | the `check --json` document |
| `arch_status` | none | the `status --json` burndown |
| `arch_explain` | `ruleId?`, `type?`, `path?` (one of them) | one rule's `explain` text, or who claims a type |
| `arch_context` | `path` | the cards covering a file or directory |
| `arch_graph` | `allowWorkspaceDiagnostics?`, `grain?`, `projects?`, `format?` | the `graph --json` survey, which needs no spec, or with `format: "mermaid"` its drawing |
| `derive_spec` | none | a prompt: the recipe for a first spec |

Pass `rules` and `projects` as semicolon-separated glob strings. A filter matching no rule or project returns an error listing the available IDs or names. For any string parameter, a one-element string array is unwrapped, an empty array counts as absent, and a longer array is rejected. The first tool call loads the solution. Later calls reconcile it against disk, reusing the previous snapshot when the tree is unchanged. Set `SLIDERULE_DISABLE_WARM_WORKSPACE=true` to load it cold on each call. `SLIDERULE_IDLE_TIMEOUT_MINUTES` is how long the server waits with no tool call before it exits, 30 minutes by default, measured from the last call's completion. Set it to `0` to disable the timeout; blank, negative or invalid values use the default. See the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#environment-variables).

A red rule is a finding in the returned document. Tool errors mean the run itself failed and use the CLI's error message for that failure. Unknown argument keys produce `Unknown parameter "<key>" on "<tool>". Valid: <parameters>.` Missing required keys produce `Missing required parameter …` with the same parameter list. `arch_check`, `arch_status` and JSON `arch_graph` use their CLI commands' runners and return the document each command's `--json` writes, line for line. MCP replies have no exit code. On a partial model, such as a project load or restore failure, `arch_check` and `arch_status` return documents stamped `modelIncomplete`. Their CLI commands exit 2 by default. `arch_graph` returns an error naming the failure unless `allowWorkspaceDiagnostics` is enabled. Document `schemaVersion` values are in the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#documents-and-versions).

`arch_explain` requires exactly one of `ruleId`, `type` and `path`; empty strings count as absent. Otherwise it returns `Pass exactly one of ruleId, type and path.` A rule answer includes its reason, fix, citation, exemplar, posture details and linked prose. Type and path answers identify the layers, rules and scopes claiming each type, plus rules naming it as an operand. These match `explain`, `explain --type` and `explain --path`. Use them when a rule governs an unexpected type. `arch_context` has no CLI command. It returns the rendered cards covering the requested file or directory, or a line beginning `No architecture scope covers '<path>'.` With `format: "mermaid"`, `arch_graph` returns the same single fence as `graph --format mermaid`; `json` is the default. Mermaid rejects a grain coarser than `full`.

## The response budget

On every tool call, the server reads `MAX_MCP_OUTPUT_TOKENS` from its environment. The character budget is four characters per whole pair of tokens, capped at 50,000. Odd token counts therefore round down. An unset, blank, invalid or non-positive value uses 25,000 tokens, giving the 50,000-character default. `MAX_MCP_OUTPUT_TOKENS` is Claude Code's limit on an MCP reply; other clients do not set it, so they get that default.

For an oversized JSON check or graph, the server starts at the requested grain and tries the remaining coarser grains in order. The order is `full`, `overview`, `skeleton`, `index`. It returns the first whole document that fits, or the `index` document if none fits. Coarser documents include a `grain` key; full documents omit it. Each matches the CLI document at that grain. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#grains) explains the omissions. Status, explain and context have no coarser grains. A Mermaid diagram also has none. If oversized, it returns an error beginning `The Mermaid diagram is <n> characters, over this response's limit of <m>.` The advice is to narrow with `projects`.

A successful reply still exceeding the budget is cut at the last line break within it, or at the character limit if necessary. Its footer begins `--- RESPONSE TRUNCATED ---`, followed by `Output was <n> characters, limit is <m> (<k> characters omitted).` and `The results above are incomplete.` A cut JSON document is incomplete JSON. Error replies are never cut. The check footer recommends `rules` and `sliderule check --rules <globs> --json`; the graph footer recommends `projects` and `sliderule graph --projects <globs> --json`. There is no paging parameter. Narrow the subject with those filters, or request one rule's full detail through `arch_explain`, subject to the same response cap.

## Drafting a spec with `derive_spec`

`derive_spec` is the server's argument-free prompt for drafting a first spec from an existing codebase. MCP clients list it among the server's prompts. In Claude Code, run it as `/sliderule:derive_spec`; `/mcp__sliderule__derive_spec` runs it too. The server supplies the recipe; the agent derives candidate rules from evidence and validates them with the checker.

The recipe proceeds through nine steps:

1. Collect stated intent and record each statement's source. Read architecture tests, README, CONTRIBUTING, ADRs, architecture documentation and agent instructions, including `AGENTS.md`, `CLAUDE.md` and Cursor rules. Follow developer-documentation links from README and CONTRIBUTING, and search the project's issues and pull requests. List inaccessible links as `unread`, with the reason.
2. Survey the restored solution with `arch_graph`, which needs no spec.
3. Scaffold a spec project, conventionally under `arch/` next to the solution.
4. Draft candidate rules as `Enforce`, including dependency directions the code already obeys. Candidate scopes use `Quarantine`, since scopes have no `Enforce` form.
5. Run `arch_check` to count violations. Rebuild the spec project before every re-check.
6. Write the postures into the spec. On a complete model, rules with zero violations remain `Enforce`. After reading the violation sites, use `Migrate` as the default for remaining violations. Scopes remain `Quarantine` or become `Caution`, according to whether new callers are allowed. Rebuild and check again.
7. Stop for curation. The agent presents every rule with its evidence; you accept, edit or drop each individually. After edits, rebuild and re-check.
8. Run `sliderule baseline init` yourself. It records current `Migrate` violations and existing inbound references into quarantined scopes. The agent never runs this command.
9. Run `sliderule render --model` yourself to generate the managed block, directory cards and `sliderule.model.json`. Commit the spec project, `arch/baselines/**`, the model file and rendered `AGENTS.md` files as one reviewable diff.

The agent closes with a receipt. Its `check:`, `postures:` and `leftRed:` lines transcribe the final whole `arch_check` document, run after the final rebuild without a `rules` filter. They report the check summary, posture counts and every failed `Enforce` rule with its violation count. The [adoption walk](https://github.com/andypgray/sliderule/blob/main/examples/Meridian/ADOPTING.md) follows this recipe on the committed Meridian example. [Getting started](https://github.com/andypgray/sliderule/blob/main/GETTING-STARTED.md#path-2-rules-an-agent-drafts) provides it as path 2, alongside writing rules yourself in path 1.

Baselines grandfather existing violations, so recording them can turn a red `Migrate` rule green without changing the code. Every MCP tool is read-only, and none writes a baseline. The [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny `sliderule baseline*` and `*dnx SlideRule.Cli* baseline*` in both `Bash(...)` and `PowerShell(...)`. Each baseline entry has a `seal` over its rule ID and fields. Editing those values invalidates the seal and the baseline is rejected. Deleting a whole entry is accepted; any violation it grandfathered goes red on the next check. The seal is a hash over a fixed public preamble and the entry text, so it can be recomputed. Review baseline changes as you would changes to the spec.

## The turn-end hook

`sliderule hook` reads a Claude Code event payload on stdin and returns Claude Code's hook exit codes. Use that command for each settings entry. `UserPromptSubmit` records the starting tree without checking it; `Stop` and `SubagentStop` run the turn-end check when due. Exit 0 lets the stop stand. A clean check's tripwire warnings appear in a hook document on stdout, once per agent until they lapse. Exit 2 blocks the stop and reports red rules on stderr. SlideRule's own failures exit 1 without blocking, under `sliderule config error:` on stderr. Red rules block subsequent stops for up to `--max-rounds` consecutive continuations per prompt, default 3 and minimum 1. Blocks and passed-on warnings both count; a silent green resets the count. Past the cap, the hook exits 1 with the red report under a cap notice, and the turn ends.

A stop skips checking when code bytes match the last green verdict or the tree recorded at the prompt. A previous red verdict prevents that skip. Missing state, no HEAD or unreadable records cause a check. Changes from another session count too, since bytes cannot identify their author. Give each parallel session a worktree of its own. The stop also checks other working trees of the project edited through the agent's edit tools during the turn. Checks use the [persisted cache](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching); when only some projects changed, they re-extract those and load them with their transitive references. Run `sliderule install-hook` to merge hook entries and baseline deny rules into `.claude/settings.json` ([reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#install-hook)). It uses the git work-tree root, or the working directory outside git, preserves other settings and leaves the file untouched when it has nothing to add. The settings template, exit-code contract and installation steps are in [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks).
