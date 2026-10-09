# The agent loop, closed by a hook

This directory exists for [storyboard.md](storyboard.md). There, a coding agent working a routine
task writes the pattern Meridian is retiring, and the architecture rule reaches it before the turn
is over. The storyboard walks that loop beat by beat with real captured output. It runs from the
task brief to the refused stop to the self-correction that lands instead. A fifth beat walks the
other outcome, the one an exit code cannot carry. That outcome is an edit the rules allow, into the
quarantined clearance engine. The tripwire there warns rather than blocks, and the warning reaches
the agent as hook context.

Beside the storyboard sits the one piece that makes the loop run. `settings.snippet.json` is the
paste-in that wires the `UserPromptSubmit`, `Stop` and `SubagentStop` entries into a repo's own
Claude Code configuration. Each entry runs `sliderule hook` on Meridian's solution, and the verb
maps a red check to the exit code a Claude Code hook treats as blocking. The storyboard's file table
says what the snippet holds and how to lift it into your repo.

The general recipe this snippet instantiates lives at the repository root under
[hooks/](../../../hooks/), and the codebase it guards is [Meridian itself](../README.md).
