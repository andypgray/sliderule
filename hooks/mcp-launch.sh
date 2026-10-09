#!/bin/sh
# Runs `sliderule mcp` from a copy of the CLI build output, so a warm server and `dotnet build`
# stop contending for the same files.
#
# Needed only when the server is run from a source checkout rather than the installed global tool.
# A live server holds open every assembly it loaded, and those are exactly the files the next build
# has to overwrite: `dotnet build` fails with MSB3021/MSB3027 copy errors for as long as a client is
# connected. Stopping the server clears that and costs more than it looks, because a stdio server
# whose connection dies is never reconnected — every hook armed against it then fails quietly for
# the rest of the session, which is a gate you no longer have and were not told about.
#
# So the server runs from a copy outside the build tree, keyed by the identity of the build it was
# taken from. That key is what makes it safe with several sessions live at once: sessions on one build
# share a directory and copy nothing, a rebuild mints a new one, and no directory is ever overwritten
# while a server may still be running from it.
#
# The identity covers every assembly and JSON file at the top of the build output, by modification
# time and size, not sliderule-cli.dll alone. When a project builds a reference assembly and a change
# leaves its public surface alone, nothing that depends on it is recompiled: the change replaces that
# one DLL in the build output and leaves sliderule-cli.dll exactly as it was, so a key on that file would
# go on serving the old dependency.
#
# This covers the server's own binaries. The spec assembly is the other half and needs nothing here:
# the server loads spec DLLs from their bytes rather than their paths, so the spec project's output
# stays replaceable while a client is connected.
#
# Use it in place of the command and the `mcp` verb; every argument is passed straight through.
#
#   {"mcpServers": {"sliderule": {
#     "command": "sh",
#     "args": ["hooks/mcp-launch.sh", "MyApp.sln", "--spec", "./arch/bin/Debug/net10.0/MyApp.Architecture.dll"]}}}
#
# On Windows, name the shell absolutely — "command": "C:\\Program Files\\Git\\bin\\sh.exe" — because
# stdio servers are spawned through cmd.exe, whose PATH reaches git.exe but not the shell a Git
# install brings with it.
#
# stdout is the JSON-RPC channel, so everything this script has to say goes to stderr instead.

# Relative paths below resolve from the launcher's working directory, which is the client's to
# choose; Claude Code names the repository root in CLAUDE_PROJECT_DIR.
[ -n "${CLAUDE_PROJECT_DIR:-}" ] && cd "$CLAUDE_PROJECT_DIR"

# The two values to set for your checkout: where the CLI was built, and where copies are kept.
BUILD_OUTPUT="${SLIDERULE_MCP_BUILD_OUTPUT:-src/SlideRule.Cli/bin/Debug/net10.0}"
RUN_ROOT="${SLIDERULE_MCP_RUN_ROOT:-$HOME/.sliderule/mcp-run}"

# Which stat this host has: GNU first, then BSD/macOS. Resolved once, because the flavour cannot change
# while one launch runs and the identity below is read on every attempt. With neither it stays empty and
# the copy is skipped rather than keyed on a guess: a directory under a key that never changes would
# serve a stale build for as long as it survives, which is worse than the lock this script exists to lift.
if stat -c '%Y' "$BUILD_OUTPUT/sliderule-cli.dll" >/dev/null 2>&1; then
  STAT_FLAG=-c
  STAT_FORMAT='%Y %s %n'
elif stat -f '%m' "$BUILD_OUTPUT/sliderule-cli.dll" >/dev/null 2>&1; then
  STAT_FLAG=-f
  STAT_FORMAT='%m %z %N'
else
  STAT_FLAG=''
fi

# The build's identity: the newest modification time among those files, then a checksum over each
# file's time, size and name.
build_identity() {
  [ -n "$STAT_FLAG" ] || return 0
  listing=$(
    cd "$BUILD_OUTPUT" 2>/dev/null || exit 0
    # A pattern that matches nothing reaches stat as a literal and fails quietly; the files that do
    # exist are still listed.
    stat "$STAT_FLAG" "$STAT_FORMAT" -- *.dll *.json 2>/dev/null
  )
  [ -n "$listing" ] || return 0
  newest=$(printf '%s\n' "$listing" | awk '$1 > newest { newest = $1 } END { print newest }')
  checksum=$(printf '%s\n' "$listing" | cksum | awk '{ print $1 }')
  printf '%s_%s\n' "$newest" "$checksum"
}

{
  if [ ! -f "$BUILD_OUTPUT/sliderule-cli.dll" ]; then
    printf 'sliderule mcp launcher: %s/sliderule-cli.dll not found; build the CLI first.\n' "$BUILD_OUTPUT"
    exit 1
  fi

  stamp=$(build_identity)
  unpublished=''

  if [ -z "$stamp" ]; then
    printf 'sliderule mcp launcher: no usable stat here, so the build cannot be identified; running from the build output, which builds will contend with.\n'
    dest="$BUILD_OUTPUT"
  else
    dest="$RUN_ROOT/$stamp"
    mkdir -p "$RUN_ROOT"

    # Mark the copy as launched from before the sweep below runs, so the sweep ages copies by last use
    # rather than by when they were made, and can never take the copy this launch is about to use: a
    # build that stays current for a week is still in service on day three, and a copy swept while a
    # client is connected to it is one that cannot be fully deleted either. -c, because a plain touch
    # that loses a race with another launcher's sweep creates a regular file where the copy was, and
    # every later launch then falls back to the build output.
    touch -c "$dest" 2>/dev/null

    # Drop everything nothing has launched from in days: old copies, staging directories a killed
    # launcher left behind, and a stray regular file sitting where a copy should be. That last one is
    # why nothing here restricts the sweep to directories: the -c above declines to create such a file,
    # but a launcher that lost the race already left one, and every launch after it falls back to the
    # build output until the file goes. Age alone, so nothing is removed merely because a newer build
    # exists; best-effort, and never fatal.
    #
    # find is named by path, because Windows ships a find.exe of its own: wherever System32 comes ahead of
    # Git's tools on PATH, a bare find is that one, which refuses these options, and the sweep fails without
    # a word. Nearly every host keeps the POSIX find in one of these two places, and under Git for Windows /usr/bin
    # is Git's own directory whatever PATH says; PATH is the fallback for a host that keeps it in neither.
    sweep_find=find
    for candidate in /usr/bin/find /bin/find; do
      if [ -x "$candidate" ]; then
        sweep_find="$candidate"
        break
      fi
    done
    "$sweep_find" "$RUN_ROOT" -mindepth 1 -maxdepth 1 -mtime +2 -exec rm -rf {} + 2>/dev/null

    # Populate through a private staging directory and rename into place, so a half-copied tree is
    # never visible at the destination. A launch that loses the rename to another session moves its
    # staging directory inside the winner's copy, and the rm takes it back out.
    #
    # The identity is read again once the copy is done: a build that wrote the output meanwhile leaves a
    # copy that may mix two builds, so that copy is not published. Copying again at once would, while a
    # build is still writing, copy the whole output only to throw it away, over and over. So the launcher
    # waits for the build to hold still instead: it reads the identity once a second, stat alone, until
    # two reads agree, and only then copies the build as it stands. Ten reads in all.
    polls=0
    while [ ! -f "$dest/sliderule-cli.dll" ]; do
      staging=$(mktemp -d "$RUN_ROOT/.staging.XXXXXX" 2>/dev/null) || break
      cp -R "$BUILD_OUTPUT/." "$staging/"
      copied=$?
      last=$(build_identity)
      if [ "$last" = "$stamp" ]; then
        if [ "$copied" -ne 0 ]; then
          rm -rf "$staging"
          break
        fi
        rmdir "$dest" 2>/dev/null
        mv "$staging" "$dest" 2>/dev/null
        rm -rf "$staging" "$dest/${staging##*/}"
        break
      fi

      stamp=''
      while [ "$polls" -lt 10 ]; do
        sleep 1
        polls=$((polls + 1))
        again=$(build_identity)
        if [ "$again" = "$last" ]; then
          stamp="$again"
          break
        fi
        last="$again"
      done

      if [ -z "$stamp" ]; then
        # Keep the copy only if it was made whole. A cp that failed leaves a tree that may be missing
        # the assemblies the server loads, and the build output at least still has them.
        if [ "$copied" -eq 0 ]; then
          unpublished="$staging"
        else
          rm -rf "$staging"
        fi
        break
      fi
      rm -rf "$staging"
      dest="$RUN_ROOT/$stamp"
    done

    if [ -n "$unpublished" ]; then
      # The build never held still for as long as the launcher waited. The copy it took still beats a
      # build output that a build is writing to; it is never published under a key, and the sweep
      # removes it once it has aged out.
      printf 'sliderule mcp launcher: the build output kept changing for as long as the launcher waited; running from an unpublished copy.\n'
      dest="$unpublished"
    elif [ ! -f "$dest/sliderule-cli.dll" ]; then
      # The copy could not be made, so fall back to the build output. The lock comes back with it,
      # but a working server beats no server, and the reason is on stderr where the client shows it.
      printf 'sliderule mcp launcher: could not stage a copy under %s; running from the build output, which builds will contend with.\n' "$RUN_ROOT"
      dest="$BUILD_OUTPUT"
    fi

    # Mark whatever copy this launch settled on, not just the key it started from: the loop re-points
    # dest when the build moves underneath it, and a copy another session staged carries that session's
    # mark, not this one's. Harmless on the build output, which no sweep reaches.
    touch -c "$dest" 2>/dev/null
  fi
} 1>&2

exec dotnet exec "$dest/sliderule-cli.dll" mcp "$@"
