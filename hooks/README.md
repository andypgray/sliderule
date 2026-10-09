# Agent hooks

An agent hook checks the working tree when an agent's turn ends and refuses the stop while a rule is red. `sliderule hook` is that hook. It reads the Claude Code event on stdin, decides whether the event calls for a check, runs `check` in the same process and answers in Claude Code's exit codes. A settings entry is that one command, with no script to copy and no shell to find. This directory holds the configuration to paste. It also holds the launcher a warm MCP server needs when it is run from a source checkout.

| File | What it is |
|---|---|
| [`settings.snippet.json`](settings.snippet.json) | hook entries and four deny rules for `.claude/settings.json` |
| [`mcp-launch.sh`](mcp-launch.sh) | runs the MCP server from a copy, so a live server and `dotnet build` stop contending |

## When it fires

The snippet wires the hook to `Stop`, and to `SubagentStop` so a worker is held to the same rules. Both fire when the agent says it is done, which is where a verdict is worth having. The code has stopped moving, and there is still a turn left to spend fixing it. A third entry, on `UserPromptSubmit`, checks nothing and prints nothing. It records the tree as each prompt finds it, so that the stop can tell the code its turn wrote from the code it found.

A stop names no edited file, so the hook reads the working tree. That is also what makes it complete. A hook matched on `Edit` sees nothing when an agent writes through a shell or a language server. Over six weeks of this repository's own sessions, 27% of the turns that changed code changed it only that way. Reading the tree covers every edit however it arrived.

Any other event takes the per-edit path, and so does a run with nothing on stdin. An edit to a file that is not code skips the check, and so does an edit that landed in another working tree. Otherwise the hook checks the project directory, and keeps no state between edits.

A turn that changed no code costs nothing. The prompt's record is HEAD and a digest of each changed code file's bytes, and the stop compares the tree with it. If no code file holds different bytes, the check is skipped. A file written back unchanged keeps the skip, and so does a commit that takes in code as it stood. That holds however much uncommitted code the tree carries from other sessions or earlier turns. An agent that only answered a question is never handed warnings about code it did not write. Each green verdict is kept the same way, beside the tree it covered, and the next stop skips while no code file's bytes have moved since. A hook wired without the prompt entry has only the verdict, so a session's first stop always checks. A red verdict is never skipped, because a tree nobody has touched still carries what the last check found.

The main agent's stop leaves its tree as the record for a subagent that finishes after the turn has ended, and the next prompt replaces it. The record cannot tell one session's edits from another's. Code another session writes into the same checkout during a turn is a change at that turn's stop, so give each parallel session a worktree of its own.

A block repeats on each stop until the rule is green, under a count. Three consecutive continuations for one user prompt is the cap, which `--max-rounds` moves. Past it, the hook writes the report to stderr, exits 1, and lets the turn end. A rule the agent cannot satisfy becomes something you read instead of a loop you watch.

The state is a few small files per session under `sliderule-hook/` in the temp directory, or under `SLIDERULE_HOOK_STATE_DIR` where that is set. Nothing in there needs keeping, and if you delete it, the next stop checks. Where it cannot be written at all, the hook falls back to the single round the payload carries in `stop_hook_active` and blocks at most once per turn.

A session inside a linked worktree gets its own tree checked. The payload names the directory the session ran from, and git names the working tree that directory belongs to. The hook finds the solution and the spec the way every command does, through the [`sliderule.json`](../sliderule.json) at the root of that tree, so one settings entry serves the main checkout and every worktree under it. A solution or `--spec` path on the command line is relative to the same tree. A session whose directory is in another repository, nested in the project or beside it, still gets the project checked. Only a worktree that shares the project's git directory counts as one of its trees.

The tree comes from the session's directory alone. A session in the main checkout that edits a worktree by absolute path has the main checkout checked, so change into the worktree before you edit it. A subagent's shell goes back to the directory its session was in when the subagent started, so start a subagent that works in a worktree from inside that worktree.

## The exit-code contract

SlideRule and Claude Code number exit codes differently, and translating between them is the hook's whole job.

| The check exits | Meaning | `sliderule hook` exits |
|---|---|---|
| `0` | clean; tripwire warnings reach the agent as context | `0`, and the stop stands |
| `1` | a rule is red | `2`, which refuses the stop, with the report on stderr |
| `2` | SlideRule's own error | `1`, a config problem rather than a violation |

Claude Code refuses a stop on exit 2 and feeds that process's stderr back to the agent as the reason to keep working. The hook therefore hands over the whole violation report: rule ID, reason, fix, and every offending `file:line`. Any other non-zero exit reaches the user as a non-blocking message. That is where an unresolvable spec, a bad git ref or a malformed settings file belongs, and where the round cap's last report goes. An agent told to "fix" a misconfiguration will try. So everything the hook cannot do itself, a command line it cannot parse included, exits 1 with the report under the line `sliderule config error:`.

A check that outruns the hook's timeout renders no decision at all. Claude Code cancels it, the stop passes, and the transcript says nothing about it. The snippet allows 600 seconds, well over a warm run on this repository, and the two skips keep most stops from spending anything.

A clean run is where a scope's tripwire has its say. A warning never moves the exit code, and an exit-0 hook's plain stdout reaches only the debug log. So a clean check that warned writes its report as `hookSpecificOutput.additionalContext`, which Claude Code turns into a transcript message the agent reads. The document names the event that fired, because Claude Code reads that context only from a document naming it. A clean check with nothing to say writes nothing.

That report leaves out the rules that passed with nothing to say. What remains is each rule that warned, with its dragons, and the summary line. The agent already has the rule board in its instructions. Claude Code also shows hook output inline only up to a set length, and a line for every rule would run this repository's own report past it. The agent would then see a preview of pass lines, with the warnings beyond its end. A red run's report is written whole.

A stop hands the check only the code files whose bytes changed since the prompt's record, so a tripwire warns about the turn's own changes and not about everything uncommitted. Each agent is told a given set of warnings once. A file the agent keeps editing is changed again at each stop, so the hook keeps the last report it passed to each agent and does not pass the same one again. A clean run with nothing to say clears that record, and warnings that come back after it are passed on again.

Every behaviour above is executed by tests. `HookRunnerTests` drives the verb in process against a scripted check, once per row of the table and once per turn-end outcome. It also fires it once per shape the working-tree question has to tell apart, against real git repositories and a real linked worktree. `HookCommandE2ETests` runs it end to end, from the command line through a full check of a fixture solution. The table above is held to the rows those runs use.

## Options

| Option | Default | What it sets |
|---|---|---|
| `<solution>` | the settings file, or a walk up from the tree | the solution to check, relative to the tree |
| `--spec` | the settings file, or the convention | the spec project or built spec DLL |
| `--diff-base` | `HEAD` | the ref the tripwires read without a turn record |
| `--max-rounds` | `3` | the continuations one prompt may get |

A stop with the prompt's record hands the check the files the turn changed and reads no ref. `--diff-base` serves the rest: an edit, a hand run, and a stop with no state. `HEAD` suits those, where the changes the tripwires should see are the ones not yet committed.

## Turning it on in a clone of this repository

`.claude/` and `.mcp.json` are not committed. They are local dev aids, and a registration pointing straight at `bin/Debug` would hand every clone the build lock the launcher exists to prevent. What a clone gets instead is these files and two steps.

Build first. The CLI has to exist before anything can run it:

```bash
dotnet build SlideRule.slnx
```

Paste [`settings.snippet.json`](settings.snippet.json) into `.claude/settings.json`. It carries a `UserPromptSubmit` entry, a `Stop` entry and a `SubagentStop` entry, each running `sliderule hook`. The hook reads this repository's solution and spec from its committed `sliderule.json`, so nothing needs editing. The snippet also denies the agent `baseline`, the command that records, shrinks and renames a baseline, because that step is yours. One rule stops the installed `sliderule baseline`. The other stops the same command run through `dotnet dnx`, which is how an agent is told to run the tool when no `sliderule` command is installed. Each rule is written twice, once for the Bash tool and once for the PowerShell tool, which Claude Code turns on by default on Windows. A rule matches the command as the agent writes it and stops no other spelling. `check` and `status` still show the agent where each ratchet stands.

The entries run the `sliderule` on your PATH, so install the tool as [below](#lifting-it-into-your-own-repository) to put one there. A `command` pointed at `bin/Debug` instead would bring back the build lock described above.

Register the MCP server, if you also want the `arch_*` query tools in the session. In `.mcp.json`, through the launcher, for the reason the next section gives. The registration names no solution and no spec: the launcher starts the server at the repository root, which Claude Code names in `CLAUDE_PROJECT_DIR`, and the server reads both from the committed [`sliderule.json`](../sliderule.json) there.

```json
{
  "mcpServers": {
    "sliderule": {
      "command": "sh",
      "args": ["hooks/mcp-launch.sh"]
    }
  }
}
```

On Windows, the same registration with the shell named by where it lives:

```json
{
  "mcpServers": {
    "sliderule": {
      "command": "C:\\Program Files\\Git\\bin\\sh.exe",
      "args": ["hooks/mcp-launch.sh"]
    }
  }
}
```

A bare `sh` cannot resolve there. Stdio servers are spawned through `cmd.exe` with the PATH a desktop process inherits. A default Git for Windows install contributes only `Git\cmd` to it. That directory holds `git.exe` and its siblings, but no shell. The shells git does install, under `Git\bin` and `Git\usr\bin`, sit on no PATH outside a Git Bash session. So the bare spelling fails every connect before the handshake with `'sh' is not recognized`.

Naming the path is less general, but a wrong one fails loudly. With Git installed anywhere else, the connect fails on the spot and the error says which path it wanted. The portable alternative would be a committed `.cmd` shim that finds the shell itself. It would relay the stdio channel faithfully (the relay fault is PowerShell's, not cmd's). But it stands between the client and the server for the life of the session, as one more process a kill has to reach through. The launcher's closing `exec` exists to remove such a process.

## Why a source checkout needs the launcher

A connected server holds open every assembly it loaded. In a source checkout, those are exactly the files the next build has to overwrite. `dotnet build` then fails with `MSB3021`/`MSB3027` copy errors for as long as a client is connected. Every connected session runs its own server, so stopping one process may not be enough.

Stopping them is the obvious way out and a bad one. A stdio server whose connection dies is never reconnected, so every architecture query the rest of the session would have made dies with it. The hook is untouched by that. It is a fresh process per firing, and it does not go through the server at all. What the launcher protects is the warm server you query.

`mcp-launch.sh` takes the contention away instead. It copies the CLI build output to a directory outside the tree, keyed by that build's identity, and execs the server from the copy. Sessions on one build share a directory and copy nothing, and a rebuild mints a new one. No directory is overwritten while a server may still be running from it. Two values configure it, both read from the environment. `SLIDERULE_MCP_BUILD_OUTPUT` is the CLI build output, defaulting to this repository's `Debug` path. `SLIDERULE_MCP_RUN_ROOT` is where copies live, defaulting to `~/.sliderule/mcp-run`. Everything after the script name is passed straight to `sliderule mcp`. The copy is taken at connect, so a live server is the build as of the handshake. `/mcp` → reconnect is how you pick up a rebuild.

The launcher fixes the server's own binaries, and only those. Spec assemblies are the other half, and they need nothing from you. The server loads spec DLLs and their dependencies from their bytes rather than their paths. The spec project's output therefore stays replaceable, and builds of it succeed while a client is connected. The launcher's copy is the fix available on the server side, where the path is the launcher's to choose; the spec's path is yours.

There is no PowerShell sibling here. The launcher's last act is `exec`, which replaces the shell with the server. That leaves nothing standing between the client and the stdio channel it speaks JSON-RPC over. PowerShell has no equivalent, and a launcher that relays that channel instead of getting out of its way is a fault nobody wants to debug. A source checkout implies git, and git on Windows installs the POSIX shell the launcher needs. Git does not put that shell on the PATH stdio servers are spawned with. That is why the Windows registration above names it by its full path.

## Lifting it into your own repository

Install the tool:

```bash
dotnet tool install -g SlideRule.Cli
```

If your repository's `global.json` pins an SDK below 10, run that install from outside the repository, because the pinned SDK cannot install a tool built for .NET 10.

Then tell the hook what to check. Commit a `sliderule.json` at the root of your repository, naming your solution and your spec, as [Getting started](../GETTING-STARTED.md#next-steps) shows. Every command reads it, the hook included. Without one, pass the solution in each entry's command, as `sliderule hook src/MyApp.sln`, and add `--spec` where the convention does not find your spec.

Then paste the snippet. It needs no change for the warnings above. The channel is the hook's own stdout, and the entries already route it. The launcher is not part of that move. An installed tool's binaries live outside your tree, so nothing there contends with your build, and the server registers as `sliderule` directly.

[The Meridian storyboard](../examples/Meridian/hooks/storyboard.md) walks the whole loop on an example codebase, beat by beat with captured output. Its snippet names that example's solution, because the example sits inside this repository, whose own settings file names another.

## In CI

Run the same `check` in the pipeline, and pass `--diff-base <the pull request's base ref>` there too. This repository's pipeline names no path, because its `sliderule.json` names the solution and the spec:

```bash
sliderule check --diff-base origin/main --sarif sliderule.sarif
```

Without a ref every quarantine tripwire is skipped, not clean, so the scope you fenced because it is dangerous to edit is the one thing CI never mentions. The checkout has to be deep enough for `origin/main` to resolve. `--sarif` takes the run to code scanning, where a red rule becomes an error-level alert and a tripwire touch becomes a warning-level alert. A grandfathered site becomes a note whose message names the baseline entry that blessed it.
