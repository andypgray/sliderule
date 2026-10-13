# Agent hooks

An agent hook checks the working tree when an agent's turn ends and blocks the stop while a rule is red. `sliderule hook` is that hook. It reads the Claude Code event on stdin and runs `check` in the same process when the event calls for one. It answers in Claude Code's exit codes. A settings entry is that one command, with no script to copy and no shell to find. This directory contains the configuration to paste, and the launcher a warm MCP server needs when it runs from a source checkout.

| File | What it is |
|---|---|
| [`settings.snippet.json`](settings.snippet.json) | hook entries and four deny rules for `.claude/settings.json` |
| [`mcp-launch.sh`](mcp-launch.sh) | runs the MCP server from a copy, so a live server and `dotnet build` stop contending |

## When it fires

The snippet wires the hook to `Stop`, and to `SubagentStop` so a subagent's work is checked the same way. Both fire when the agent finishes, which is where a verdict is worth having. The code has stopped moving, and there is still a turn left to spend fixing it. A third entry, on `UserPromptSubmit`, runs no check and prints no output. It records the tree as each prompt finds it, so that the stop can tell the code its turn wrote from the code it found.

Any other event takes the per-edit path, and so does a run with no payload on stdin. To the hook, a code file is one whose name ends in `.cs`, `.csproj`, `.props`, `.targets`, `.sln`, `.slnx`, `.razor` or `.cshtml`, in any case. An edit to a file that is not code skips the check, and so does an edit that landed in another working tree. Otherwise the hook checks the project directory and keeps no state between edits.

### What a stop checks

A stop names no edited file, so the hook reads the working tree, and reading the tree is also what makes the check complete. A hook matched on `Edit` misses the edits an agent makes through a shell or a language server. Over six weeks of this repository's own sessions, 27% of the turns that changed code changed it only that way. Reading the tree covers every edit however it arrived.

The check judges every rule against the whole solution. It reads the code from source, so an agent's edit counts as soon as it is saved, built or not. It runs the spec from the spec project's last build. A stop also gives it the code files whose bytes changed since the prompt's record, and a scope's tripwire reads that list. So the tripwire warns about the turn's own changes, whatever else the tree has uncommitted.

### What a stop skips

A turn that changed no code runs no check. The prompt's record is HEAD and a digest of each changed code file's bytes, and the stop compares the tree with it. If no code file's bytes differ from the record, the stop skips the check. A file written back unchanged keeps the skip, and so does a commit that takes in code as it stood. The skip applies however much uncommitted code the tree has from other sessions or earlier turns. An agent that only answered a question gets no warnings about code it did not write.

The main agent's stop leaves its tree as the record for a subagent that finishes after the turn has ended, and the next prompt replaces that record. Each green verdict is recorded with the tree it covered, and the next stop skips while no code file's bytes have changed since. A hook wired without the prompt entry has only the verdict, so a session's first stop always checks. A red verdict is never skipped, because an untouched tree still has whatever the last check found.

### Worktrees

The prompt's record cannot tell one session's edits from another's. Code another session writes into the same checkout during a turn counts as a change at that turn's stop, so give each parallel session a worktree of its own.

A session inside a linked worktree gets its own tree checked. The payload names the directory the session ran from, and git names the working tree that directory belongs to. The hook finds the solution and the spec the way every command does, through the [`sliderule.json`](../sliderule.json) at the root of that tree, so one settings entry serves the main checkout and each of its worktrees. A solution or `--spec` path on the command line is relative to the same tree. A session whose directory is in another repository, nested in the project or next to it, still gets the project checked. Only a worktree that shares the project's git directory counts as one of its trees.

The tree comes from the session's directory alone. A session in the main checkout that edits a worktree by absolute path has the main checkout checked, so change into the worktree before you edit it. A subagent's shell goes back to the directory its session was in when the subagent started, so start a subagent that works in a worktree from inside that worktree.

### The round cap

A block repeats at each stop until the rule is green, up to a cap. The cap is three consecutive continuations for one user prompt, and `--max-rounds` moves it. A block and a warning passed on to the agent each count as one. Past the cap, the hook writes a red rule's report to stderr under the line `sliderule: still red after 3 rounds; not blocking again.`, exits 1, and lets the turn end. A rule the agent cannot satisfy then reaches you as a report.

The hook stores a few small files per session under a `sliderule-hook/` directory. That directory is in the temp directory, or in `SLIDERULE_HOOK_STATE_DIR` where that is set. Every file there is safe to delete, and the next stop then runs a check. Where the hook cannot write those files at all, it falls back to the payload's `stop_hook_active` flag and blocks at most once per turn.

## The exit-code contract

SlideRule and Claude Code number exit codes differently, and translating between them is the hook's whole job.

| The check exits | Meaning | `sliderule hook` exits |
|---|---|---|
| `0` | clean; tripwire warnings reach the agent as context | `0`, and the stop stands |
| `1` | a rule is red | `2`, which blocks the stop, with the report on stderr |
| `2` | SlideRule's own error | `1`, a config problem rather than a violation |

Claude Code blocks a stop on exit 2 and feeds that process's stderr back to the agent as the reason to keep working. The hook therefore writes the whole violation report there, with the rule ID, reason, fix and every offending `file:line`. Any other non-zero exit reaches the user as a non-blocking message. That is where an unresolvable spec, a bad git ref or a malformed settings file belongs, and where the round cap's last report goes. An agent told to "fix" a misconfiguration will try. So SlideRule's own errors, including a hook command line that does not parse, exit 1 with the report under the line `sliderule config error:`. A solution that only partly loads is one of those errors, because the hook never opts into a partial model. While a project fails to load or to restore, every stop that runs a check ends with that report in place of a verdict.

A check that outruns the hook's timeout gives no verdict. Claude Code cancels it, the stop passes, and the transcript has no record of it. The snippet allows 600 seconds for each stop, well over a warm run on this repository. The stop's check also uses the [persisted cache](../CLI.md#caching). Once a whole check has filled it, a check after a source edit loads only the projects it re-extracts and the projects they reference. A project that references an edited one, directly or through another, is re-extracted only when the edit changed a declaration, even a private one. The other projects come from the cache, and the report matches a cold run byte for byte. The two skips under [What a stop skips](#what-a-stop-skips) keep most stops from running a check at all. On a large solution, time a `sliderule check --no-cache` and keep the timeout above it. That run loads the whole solution, as the first stop after a change to the solution's structure does.

A clean run can still report warnings, such as a tripwire touch. A warning never moves the exit code, and an exit-0 hook's plain stdout reaches only the debug log. So a clean check that warned writes its report as `hookSpecificOutput.additionalContext`, which Claude Code turns into a transcript message the agent reads. The document names the event that fired, because Claude Code reads that context only from a document that names it. A clean check with no warning writes no document.

That report leaves out the rules that passed without a warning. What remains is each rule that warned, with its dragons, any rule the run skipped, and the summary line. The agent already has the rule board in its instructions. Claude Code also shows hook output inline only up to a set length, and a line for every rule would run this repository's own report past it. The agent would then see a preview of pass lines, with the warnings beyond its end. A red run's report is written whole.

Each agent is told a given set of warnings once. A file the agent keeps editing is changed again at each stop, so the hook stores the last report it passed to each agent and does not pass the same one again. A clean run with no warning clears that record, and warnings that come back after it are passed on again.

Tests exercise every behaviour above. `HookRunnerTests` drives the verb in process against a scripted check, once per row of the table and once per turn-end outcome. It also runs the verb once per shape the working-tree question has to tell apart, against real git repositories and a real linked worktree. `HookCommandE2ETests` runs it end to end, from the command line through a full check of a fixture solution. `HookReadmeTests` checks the table's two code columns against the rows those runs use.

## Options

| Option | Default | What it sets |
|---|---|---|
| `<solution>` | the settings file, or a walk up from the tree | the solution to check, relative to the tree |
| `--spec` | the settings file, or the convention | the spec project or built spec DLL |
| `--diff-base` | `HEAD` | the ref the tripwires read when the hook has no snapshot of the tree |
| `--max-rounds` | `3` | the continuations one prompt may get, at least 1 |

A stop with the prompt's record gives the check the code files the turn changed. A stop with state but no record gives it every changed code file git lists. Neither reads a ref. `--diff-base` covers the other cases (an edit, a hand run, and a stop with no snapshot of the tree, because it has no state or git did not answer). `HEAD` suits those, because the changes the tripwires should see there are the ones not yet committed.

## Turning it on in a clone of this repository

`.claude/` and `.mcp.json` are local dev aids, so this repository does not commit them. A committed registration pointing straight at `bin/Debug` would give every clone the build lock the launcher exists to prevent. A clone gets these files and the steps below instead.

Build first. The CLI has to exist before anything can run it:

```bash
dotnet build SlideRule.slnx
```

Paste [`settings.snippet.json`](settings.snippet.json) into `.claude/settings.json`. It has a `UserPromptSubmit` entry, a `Stop` entry and a `SubagentStop` entry, each running `sliderule hook`. The hook reads this repository's solution and spec from its committed `sliderule.json`, so the snippet needs no edits.

The snippet also denies the agent `baseline`, the command that records, shrinks and renames a baseline, because that step is yours. One rule stops the installed `sliderule baseline`. The other stops the same command run through `dotnet dnx`, which is how an agent is told to run the tool when no `sliderule` command is installed. Each rule is written twice, once for the Bash tool and once for the PowerShell tool, which Claude Code turns on by default on Windows. A rule matches the command as the agent writes it and stops no other spelling. `check` and `status` still show the agent where each ratchet stands.

The entries run the `sliderule` on your PATH, so install the tool as [below](#lifting-it-into-your-own-repository) to put one there. A `command` pointed at `bin/Debug` instead would bring back the build lock described above.

To get the `arch_*` query tools in the session as well, register the MCP server in `.mcp.json` through the launcher. The [next section](#why-a-source-checkout-needs-the-launcher) explains why. The registration names no solution and no spec. The launcher starts the server at the repository root, which Claude Code names in `CLAUDE_PROJECT_DIR`, and the server reads both from the committed [`sliderule.json`](../sliderule.json) there.

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

On Windows, the same registration names the shell by its full path:

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

A bare `sh` cannot resolve on Windows. Stdio servers are spawned through `cmd.exe` with the PATH a desktop process inherits, and a default Git for Windows install adds only `Git\cmd` to it. That directory has `git.exe` and its siblings, but no shell. The shells Git installs, under `Git\bin` and `Git\usr\bin`, are on the PATH only inside a Git Bash session. So the bare spelling fails every connect before the handshake with `'sh' is not recognized`. Naming the full path is less general, but a wrong one fails loudly. With Git installed anywhere else, the connect fails on the spot and the error names the path it wanted.

The launcher has no PowerShell or `.cmd` sibling. A source checkout implies git, and git on Windows installs the POSIX shell the launcher needs. The launcher's last act is `exec`, which replaces the shell with the server and leaves no process between the client and the server's stdio channel. PowerShell has no equivalent, so a PowerShell launcher would relay the channel, and PowerShell's relay is faulty. A committed `.cmd` shim that found the shell itself would work wherever Git is installed and relay the channel faithfully. It would also stay between the client and the server for the whole session, as one more process a kill has to reach through.

## Why a source checkout needs the launcher

A connected server keeps open every assembly it loaded. In a source checkout, those are exactly the files the next build has to overwrite. `dotnet build` then fails with `MSB3021`/`MSB3027` copy errors for as long as a client is connected. Every connected session runs its own server, so stopping one process may not be enough.

Stopping them is the obvious way out and a bad one. A stdio server whose connection dies is never reconnected, so every architecture query the rest of the session would have made dies with it. The hook keeps working, because it is a fresh process per firing and does not go through the server at all. The launcher protects the warm server you query.

`mcp-launch.sh` takes the contention away instead. It copies the CLI build output to a directory outside the tree, keyed by that build's identity, and execs the server from the copy. Sessions on the same build share one copy, and a rebuild makes a new one. The launcher never overwrites a directory a server may still be running from. The copy is taken at connect, so a live server is the build as of the handshake. `/mcp` → reconnect is how you pick up a rebuild.

Two values configure the launcher, both read from the environment. `SLIDERULE_MCP_BUILD_OUTPUT` is the CLI build output, defaulting to this repository's `Debug` path. `SLIDERULE_MCP_RUN_ROOT` is where copies live, defaulting to `~/.sliderule/mcp-run`. Everything after the script name is passed straight to `sliderule mcp`.

The launcher covers the server's own binaries, whose path is the launcher's to choose. Spec assemblies are the other half, and their path is yours, so the server handles them with no setup from you. The server loads spec DLLs and their dependencies from their bytes, which leaves the files free to replace. Builds of the spec project therefore succeed while a client is connected.

## Lifting it into your own repository

Install the tool:

```bash
dotnet tool install -g SlideRule.Cli
```

If your repository's `global.json` pins an SDK below 10, run that install from outside the repository, because the pinned SDK cannot install a tool built for .NET 10.

Then tell the hook what to check. Commit a `sliderule.json` at the root of your repository, naming your solution and your spec, as [Getting started](../GETTING-STARTED.md#next-steps) shows. Every command reads it, the hook included. Without one, pass the solution in each entry's command, as `sliderule hook src/MyApp.sln`, and add `--spec` where the convention does not find your spec.

Then paste the snippet. It needs no change for the warnings above, because their channel is the hook's own stdout and the entries already route it. Leave the launcher out. An installed tool's binaries live outside your tree, where your build never writes, so the server registers as `sliderule` directly.

[The Meridian storyboard](../examples/Meridian/hooks/storyboard.md) walks the whole loop on an example codebase, beat by beat with captured output. Its snippet names that example's solution, because the example is inside this repository, whose own settings file names a different solution.

## In CI

Run the same `check` in the pipeline, and pass `--diff-base <the pull request's base ref>` there too. This repository's pipeline names no path, because its `sliderule.json` names the solution and the spec:

```bash
sliderule check --diff-base origin/main --sarif sliderule.sarif
```

Without a ref, every quarantine tripwire is reported as skipped. CI then never warns about a change inside the scope you fenced because it is dangerous to edit. The checkout has to be deep enough for `origin/main` to resolve. `--sarif` takes the run to code scanning, where a red rule becomes an error-level alert and a tripwire touch becomes a warning-level alert. A grandfathered site becomes a note whose message gives its baseline entry's reason, or the baseline file when the entry has no reason. The CI section of [Operating SlideRule](../OPERATING.md#ci) has the whole job, from the install to the SARIF upload.
