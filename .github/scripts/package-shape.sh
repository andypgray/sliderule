# Sourced by the package-shape scripts beside it, never run on its own. Each of those scripts checks one
# package and keeps its own lists, its own reasons and the checks only it makes; what they share is here:
# finding the package, reading it, the checks both make over different lists, and the report.
#
# The functions share the caller's variables rather than returning values. open_package sets package, name,
# entries and nuspec and starts an empty findings array, every check appends to findings, and report prints
# them. A sourced file runs in the caller's shell, so the caller's set -euo pipefail governs these functions
# too.

# Found by glob because the version is whatever was packed, with a digit pinned after the id because the
# contract package's id is a prefix of every other package's here and the bare pattern would match them all.
# Exactly one, since two would mean an artifacts directory holding more than one build and neither answer
# would be about this one.
open_package() {
  local artifacts="$1" id="$2" packages
  shopt -s nullglob
  packages=("$artifacts"/$id.[0-9]*.nupkg)
  shopt -u nullglob
  if [ "${#packages[@]}" -ne 1 ]; then
    echo "::error::$artifacts holds ${#packages[@]} $id.<version>.nupkg files, not one"
    exit 1
  fi

  package="${packages[0]}"
  name="$(basename "$package")"
  entries="$(unzip -Z1 "$package")"
  nuspec="$(unzip -p "$package" "$id.nuspec")"
  findings=()
}

# A finding unless the package carries the entry, worded "<entry> is missing, so <consequence>".
require() {
  grep -Fxq "$1" <<<"$entries" ||
    findings+=("$1 is missing, so $2")
}

# Every assembly named has to sit under analyzers/dotnet/cs, and nothing else may sit under analyzers/. One
# list says both, so the two checks cannot disagree about what the closure is. The format is the consequence
# of a missing assembly, with %s standing for its file name wherever the sentence names it.
require_closure() {
  local format="$1" assembly consequence stray
  shift
  for assembly in "$@"; do
    printf -v consequence "$format" "$assembly"
    require "analyzers/dotnet/cs/$assembly" "$consequence"
  done

  stray="$(grep '^analyzers/' <<<"$entries" | grep -vFx -f <(printf 'analyzers/dotnet/cs/%s\n' "$@") || true)"
  [ -z "$stray" ] ||
    findings+=("analyzers/ carries entries this package does not ship: $(paste -sd' ' <<<"$stray")")
}

# The package's MSBuild file under both paths it is packed to, for the id the calling script set.
require_build_targets() {
  require "buildTransitive/$id.targets" "$1"
  require "build/$id.targets" "$1"
}

# What makes a package one the compiler loads and nobody compiles against. No lib/: an assembly there is a
# reference every consumer compiles against by accident, and an accidental public surface cannot be withdrawn
# from a version once pushed. No <dependencies>, since what the analyzer needs travels inside the package; $1
# is what one would do instead, worded to follow "which". A development dependency, so a consumer's own
# package never depends on this one. And no symbol package for the id the calling script set, among the
# artifacts it named: symbols are collected with the build output, which this package does not ship, so a
# .snupkg is an empty one on its way to nuget.org.
require_analyzer_only() {
  local libraries symbols
  libraries="$(grep '^lib/' <<<"$entries" || true)"
  [ -z "$libraries" ] ||
    findings+=("lib/ is present, which makes this package something consumers compile against: $(paste -sd' ' <<<"$libraries")")

  ! grep -q '<dependencies' <<<"$nuspec" ||
    findings+=("the nuspec declares <dependencies>, which $1")

  grep -q '<developmentDependency>true</developmentDependency>' <<<"$nuspec" ||
    findings+=("the nuspec is not a development dependency, so a consumer's own package would depend on this one")

  shopt -s nullglob
  symbols=("$artifacts"/$id.[0-9]*.snupkg)
  shopt -u nullglob
  [ "${#symbols[@]}" -eq 0 ] ||
    findings+=("${#symbols[@]} $id symbol package(s) were packed, and an analyzer-only package has no symbols to carry")
}

# Prints each finding as a workflow error and fails, or says the package carries the shape it was checked
# for.
report() {
  local finding
  if [ "${#findings[@]}" -ne 0 ]; then
    for finding in "${findings[@]}"; do
      echo "::error::$name: $finding"
    done
    exit 1
  fi

  echo "$name carries the $1 package shape"
}
