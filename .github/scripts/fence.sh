# Sourced by the pack-check steps that execute a published document, never run on its own: the step that
# scaffolds the derive_spec recipe's spec project, and the step that walks GETTING-STARTED.md and the README
# quickstart. What they share is the cut.
#
# fence <file> <lang> <marker> prints the body of the first fenced block in <file> whose language is <lang>
# and whose body carries <marker>. The cut is anchored on content rather than on fence order, so re-ordering
# a document's fences can never select the wrong one, and a marker that has gone yields nothing, which each
# caller's empty-cut guard turns into a failure. It sets no shell options: a sourced file runs in the
# caller's shell, and the callers run under the runner's default bash -e.
fence() {
  awk -v want="$2" -v marker="$3" '
    /^```/ {
      if (!open) { open = 1; lang = substr($0, 4); sub(/[ \t]+$/, "", lang); body = ""; next }
      open = 0
      if (lang == want && index(body, marker) > 0) { printf "%s", body; exit }
      next
    }
    open { body = body $0 "\n" }
  ' "$1"
}
