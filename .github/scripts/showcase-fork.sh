# Sourced by the steps that need the pinned showcase fork checked out with its spec built, never run on its own.
#
# fetch_showcase_fork <root> checks out the commit examples/eShopOnWeb/showcase.json pins into <root>, restores
# the fork's solution, builds its spec project, and appends SHOWCASE_ROOT, SHOWCASE_SOLUTION and SHOWCASE_SPEC
# to $GITHUB_ENV for the steps after it. The pin names a commit, which a shallow fetch reaches without its
# history. The restore runs with the NuGet audit off, because the advisories change with the date and nothing
# the fork is fetched for reads them. The pin path is relative, so a caller runs it before any cd.
#
# Every key is read into a variable before it is used, with jq -e, so a missing key or an unreadable pin stops
# the step at the read: a substitution inside a command's arguments, or inside a `local` declaration, would
# hand on its output and drop its status.
#
# It sets no shell options: a sourced file runs in the caller's shell, and the callers run under the
# runner's default bash -e.
fetch_showcase_fork() {
  local root pin repository commit solution spec
  root="$1"
  pin=examples/eShopOnWeb/showcase.json
  repository="$(jq -er '.fork.repository' "$pin")"
  commit="$(jq -er '.fork.commit' "$pin")"
  solution="$root/$(jq -er '.solution' "$pin")"
  spec="$root/$(jq -er '.specProject' "$pin")"
  git init -q "$root"
  git -C "$root" fetch -q --depth 1 "$repository" "$commit"
  git -C "$root" checkout -q FETCH_HEAD
  dotnet restore "$solution" -p:NuGetAudit=false
  dotnet build "$spec" --no-restore
  {
    echo "SHOWCASE_ROOT=$root"
    echo "SHOWCASE_SOLUTION=$solution"
    echo "SHOWCASE_SPEC=$spec"
  } >> "$GITHUB_ENV"
}
