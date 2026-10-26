# Agent-loop storyboard: the rule that corrects the agent

A coding agent working a routine task writes something the architecture rules do not allow. The
rule reaches it before the turn ends, and the agent corrects itself before it returns the work.
This page walks that loop beat by beat. Then it walks the other half of the loop, an edit the rules
allow into code the spec marks as dangerous. There the hook warns the agent without blocking.

The agent's code, its words, and Claude Code's line are quoted from the session transcripts as
captured. The check output is re-run. This repository's tests make the agent's edit in memory and
check it with the current tool. They fail when a report below stops matching what that check prints.
Every report here is real `sliderule` output, and [Reproduce it](#reproduce-it) walks the same loop
at the command line.

**How it was captured (2026-09-09).** Two `claude -p` runs produced it, against this repository with
only a `Stop` and a `SubagentStop` hook wired. Both runs were given the Beat 1 brief and one steer,
to follow the prevailing data-access style of the other controllers. Beats 2 to 4 are the Haiku 4.5
run; Beat 5 is the Opus run. What neither run did is written into Beat 2, because it is the more
interesting half of the result. The Opus run's context also included the seven rules that passed,
which the hook's warning now leaves out. Both runs wired a wrapper script, `arch-hook.sh`, that
`sliderule hook` has since replaced, which is why Claude Code's line in Beat 3 names it. The wrapper
ran the same check and answered with the same exit codes.

## The file in this directory

The repository keeps `.claude/` out of version control as a local dev aid, so the hook config is
here as a paste-in. Lift it into your own repo:

| File | What it is |
|---|---|
| [`settings.snippet.json`](settings.snippet.json) | Hook entries and deny rules for `.claude/settings.json` |

The snippet is the Meridian instantiation of SlideRule's agent-hook recipe. It has a
`UserPromptSubmit` entry, a `Stop` entry and a `SubagentStop` twin, each running `sliderule hook`.
The `UserPromptSubmit` entry records the tree when a prompt arrives, so a stop can tell the turn's
own changes from the code before the prompt. Four deny rules, two for the `sliderule` form of the
command and two for the `dnx` form, keep `baseline` a human decision. Each entry names Meridian's
solution, because this repository's own `sliderule.json` names another, and the hook finds
Meridian's spec by convention. To lift the snippet, replace that solution path with yours, or remove
the argument where a `sliderule.json` at your repository root names the solution. Each entry runs
the installed `sliderule` command, so the global tool has to be on the `PATH`.

The one thing the hook has to get right is the exit code. A clean check returns 0 and the turn ends.
A red rule returns 2, which is how a Claude Code hook blocks a stop. The violation report goes to
stderr, where the agent reads it and fixes the code. SlideRule's own errors return 1, which Claude
Code shows the user without blocking, because a config problem is the user's to fix. Exit 0 can
still come with a report. Where the check warned, the hook returns the report as hook context, which
is Beat 5.

The stop's check uses the [persisted cache](../../../CLI.md#caching). Once a whole check has filled
it, a check after a source edit loads only the projects it re-extracts and the projects they
reference. Its report is byte for byte what a cold run prints.

## Beat 1: the task

> Add a lookup endpoint to `BookingsController` that returns a booking by its reference, or 404
> when there is no such booking.

`BookingsController` is one of the two controllers already migrated to a repository and an
injected clock. The other six open a `SqlConnection` and run inline SQL in the request path.

## Beat 2: what the agent got right, and what it did not

Six of the eight controllers do data access with inline SQL, so an agent reading the codebase for
house style has a majority to copy. Neither run copied it. Both read the committed
[`AGENTS.md`](../AGENTS.md) block. It states in the spec's own words that the 12 inline-SQL
references are grandfathered debt and new code must not add to them. Both runs wrote the lookup as
a call through the already-injected `IBookingRepository`:

```csharp
[HttpGet("{reference}")]
public async Task<IActionResult> GetByReference(string reference)
{
    Booking? booking = await bookings.Get(reference);
    if (booking is null) return NotFound();

    return Ok(booking);
}
```

The rendered block steered the first attempt away from the pattern Meridian is retiring, before any
check ran. The method's name did not follow the block. `GetByReference` returns a `Task`, and
`naming/async-suffix` is a second ratchet with 13 sites already on the record.
`IBookingRepository.Get`, the repository method the new endpoint calls, is one of the 13. So the
agent copied a name that only the baseline allows. The code compiles and runs, and it is new debt.

## Beat 3: the turn ends, and the hook blocks the stop

The agent finished and tried to end its turn. The `Stop` hook ran `check` over the working tree,
and the ratchet went red on the one new site. The hook exited 2 with this report on stderr:

```text
pass layering/domain-independent — The Domain layer must not reference the Web layer.
pass naming/controllers — Types derived from `ControllerBase` must be named `*Controller`.
pass data-access/no-inline-sql — Types in `Meridian.Web.Controllers.*` must not reference `SqlConnection` or `SqlCommand`.
  grandfathered: 12 (baselined; run 'sliderule status' for burndown)
pass time/inject-clock — Types in the Web layer, except types named `SystemClock`, must not use `DateTime.Now` or `DateTime.UtcNow`.
  grandfathered: 7 (baselined; run 'sliderule status' for burndown)
FAIL naming/async-suffix — Methods returning `Task`, `Task<TResult>`, `ValueTask` or `ValueTask<TResult>` declared in authored types in the Domain or Web layers must be named `*Async`.
  because: Task- and ValueTask-returning methods carry the Async suffix so callers see at the call site that a method must be awaited.
  citation: https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap
  fix: Rename the method to end in Async and update its callers; see the interface and its implementation together.
  src/Meridian.Web/Controllers/BookingsController.cs:60 — Meridian.Web.Controllers.BookingsController.GetByReference()
  grandfathered: 13 (baselined; run 'sliderule status' for burndown)
pass di/no-buildserviceprovider — Types must not use `ServiceCollectionContainerBuilderExtensions.BuildServiceProvider()`.
pass clearance/engine/containment — Types in `Meridian.Clearance.*`, except `IClearanceGateway` or `ClearanceGateway`, must be referenced only by types in `Meridian.Clearance.*`, `IClearanceGateway` or `ClearanceGateway`.
  grandfathered: 1 (baselined; run 'sliderule status' for burndown)
pass clearance/engine/tripwire

Checked 8 rules: 7 passed, 1 failed, 0 skipped (1 violation, 0 warnings).
```

Claude Code turns exit 2 into a blocked stop and gives that stderr to the agent as the reason. This
is how it arrives in the transcript:

```text
Stop hook feedback:
[sh "${CLAUDE_PROJECT_DIR}/examples/Meridian/hooks/arch-hook.sh"]: pass layering/domain-independent — …
```

The report gives the five things an agent needs to act on. They are the rule ID
(`naming/async-suffix`), the reason, the page that reason rests on, the fix, and the exact
`file:line`. The sites already on the record stay quiet, counted in the same block as
`grandfathered: 13`; only the new name is red. Two rules above it, the inline-SQL sites are quiet
for the same reason. That is the debt the block had already steered the agent away from.

## Beat 4: the agent self-corrects

The agent read the report and renamed the method in one edit, unaided:

> The arch hook caught the async naming violation. I need to rename the method to follow the
> async suffix convention.

```csharp
[HttpGet("{reference}")]
public async Task<IActionResult> GetByReferenceAsync(string reference)
```

Then it stopped again. The hook ran the check a second time, found the tree clean, and exited 0 with
no output, so the turn ended. Run the same check by hand with `--diff-base HEAD`. It prints the
board with `naming/async-suffix` back among the passes; only its summary line is shown here:

```text
Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 0 warnings).
```

The method shipped with its `Async` suffix. The correction came from the report's own fix line, with
no reviewer needed to catch it later. The hook runs at each stop, so the fix cost one check, at the
second stop.

## Beat 5: a different task, and the warning that does not block

> Ops report that container numbers from one partner are rejected as invalid. ISO 6346 allows `J`
> and `Z` in the category position as well as `U`; `ContainerNumberValidator` accepts only `U`.

That is a real bug, the fix is one line, and the line is inside `Meridian.Clearance`, the scope the
spec quarantines. The agent widened the check:

```csharp
// ISO 6346 category identifiers: U freight, J detachable equipment, Z trailers and chassis.
if (containerNumber[3] is not ('U' or 'J' or 'Z')) return false;
```

Every rule still passes. Containment governs who may *reference* the scope from outside, and this
edit is inside it, so the check is clean and the hook exits 0. The check does warn that the edit
landed in dragon territory, and a warning can only travel on exit 0. So the hook writes the report
as the one exit-0 object Claude Code reads, naming the event that fired. The object's
`additionalContext` is cut short here and printed in full below it:

```json
{
  "hookSpecificOutput": {
    "hookEventName": "Stop",
    "additionalContext": "warn clearance/engine/tripwire\n  warning: Changed file 'src/Meridian.Clearance/ContainerNumberValidator.cs' …"
  }
}
```

Inside it is the report without the seven rules that passed, which the agent already has in its
instructions. What remains is the rule that warned and the summary line:

```text
warn clearance/engine/tripwire
  warning: Changed file 'src/Meridian.Clearance/ContainerNumberValidator.cs' is inside quarantined scope 'clearance/engine' — does the task actually require editing dragon territory? Dragons: sliderule explain clearance/engine/tripwire.
  dragons: ISO 6346 check digit: the letter-value table skips every multiple of 11 (A=10, B=12 … U=32); the gaps are structural — linearizing the table breaks every real container number. Call in only through IClearanceGateway.

Checked 8 rules: 8 passed, 0 failed, 0 skipped (0 violations, 1 warning).
```

Claude Code delivers that as a `Stop hook additional context` message and lets the turn continue
once. The warning is the tripwire's only effect. The dragons arrive under the warning that fired, so
the agent reads them where it is working, with no round trip to `explain`. The agent answered them
point by point:

> **The dragons warn about the letter-value table specifically** — `A=10, B=12 … U=32`, every
> multiple of 11 skipped, gaps structural. I didn't touch `ContainerCheckDigit`. The table is
> what makes `J` and `Z` score correctly without any change, which is why the fix is one line in
> the validator and nothing in the check digit.

The category fix ships. The warning is about the edit that could come next. An agent one file from
a letter-value table with three gaps learns that the gaps are structural before it decides they are
a typo. Those are the two postures on one codebase. Beat 3's ratchet blocks a stop that would have
left new code in a retired shape. The tripwire lets an allowed edit through and puts the dragons in
front of the agent who made it.

## Reproduce it

From a checkout, build the CLI and the example once, then walk the loop by hand. This repository is
a source checkout, so each `sliderule …` below runs as
`dotnet run --no-build --project src/SlideRule.Cli -- …` from the repository root. Install
the [global tool](../README.md#run-it-yourself) to use the real `sliderule` command.

```bash
dotnet build src/SlideRule.Cli
dotnet build examples/Meridian/Meridian.slnx
sliderule check examples/Meridian/Meridian.slnx      # exit 0: the committed baseline is clean
```

Add the Beat 2 method to `BookingsController` above its `sample` endpoint, and check again. The
check reads source, so no rebuild is needed. The second line drives the hook the way Claude Code
does, with a `Stop` payload on stdin:

```bash
sliderule check examples/Meridian/Meridian.slnx --diff-base HEAD   # exit 1: the Beat 3 board
echo '{"hook_event_name":"Stop","session_id":"storyboard"}' | sliderule hook examples/Meridian/Meridian.slnx   # exit 2: the board, on stderr
```

The `--diff-base HEAD` on the `check` line arms the quarantined-scope tripwire (the
`pass clearance/engine/tripwire` line, where a plain check prints a skip) and does not change the
failing rule. The hook arms the same tripwire without a ref, by passing its check the code files
that changed. On this first stop, with no prompt recorded, those are all the code files
`git status` lists.

A block repeats at each stop until the rule is green, up to a
[cap](../../../hooks/README.md#the-round-cap). The cap is three consecutive continuations for one
user prompt, and `--max-rounds` moves it. Past the cap, the hook writes a red rule's report to
stderr under the line `sliderule: still red after 3 rounds; not blocking again.`, exits 1, and lets
the turn end. The hook exits 0 with no output once you switch to the Beat 4 name. It keeps a record
for each `session_id` under the temp directory, so a second run over an untouched clean tree skips
the check. Revert `BookingsController` when you are done so the example tree stays clean.

Beat 5 is the same loop with the tripwire armed. Widen the category check in
`ContainerNumberValidator` as the beat does, and check again:

```bash
sliderule check examples/Meridian/Meridian.slnx --diff-base HEAD   # exit 0: the Beat 5 board
echo '{"hook_event_name":"Stop","session_id":"storyboard"}' | sliderule hook examples/Meridian/Meridian.slnx   # the warning, as hook context
```

The hook's output is the JSON object Claude Code reads at the stop, one `hookSpecificOutput` naming
the event that fired. The rule that warned and the summary line are escaped into its
`additionalContext`. Run both against the reverted tree and the check prints a clean board, while
the hook exits 0 with no output. A clean check with no warning sends the agent no message.
