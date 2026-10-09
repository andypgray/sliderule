#!/usr/bin/env bash
# The analyzer package is the one package here that ships no lib/ folder. What it carries is three
# assemblies the compiler loads together, the MSBuild file that hands them the rules, and nothing anybody
# compiles against. Every part of that shape comes from a property in the project file rather than from
# anything the pack itself checks, so a knob lost in a merge produces a package that still builds, still
# pushes, and silently analyses nothing — or one that puts an assembly in lib/ for consumers to bind to,
# which is a public surface nobody reviewed and a version that can never be taken back.
#
# One script rather than a step in each workflow, for the same reason as verify-xml-docs-packed.sh beside
# it: the shape is the thing a change has to find, and two copies of it is one copy that gets missed.
#
# Usage: verify-analyzer-package-shape.sh <artifacts-directory>
# The directory holds the contract package of the same build as well, which the last check compares with.
set -euo pipefail

artifacts="${1:?usage: verify-analyzer-package-shape.sh <artifacts-directory>}"

id='SlideRule.Analyzers'

. "$(dirname "${BASH_SOURCE[0]}")/package-shape.sh"
open_package "$artifacts" "$id"

# The closure the compiler has to load, and nothing besides. An analyzer's dependencies are resolved from
# the folder it sits in, so a missing one of these three does not fail the pack or the restore: it fails at
# analyzer load time, inside the consumer's build.
require_closure "the analyzer cannot load in a consumer's build" "$id.dll" 'SlideRule.Extraction.dll' 'SlideRule.dll'

# Both paths, from the one source file: buildTransitive/ is what an SDK-style consumer reads and what flows
# on to a project referencing theirs, and build/ is the only one an older client or a packages.config
# project looks in. Without the file the package restores and analyses nothing, because nothing has told
# the analyzer where the rules are.
require_build_targets 'nothing hands the analyzer the model file and the baselines'

# No lib/. A copy of the assembly there is a reference every consumer compiles against by accident, and an
# accidental public surface cannot be withdrawn from a version once pushed.
libraries="$(grep '^lib/' <<<"$entries" || true)"
[ -z "$libraries" ] ||
  findings+=("lib/ is present, which makes this package something consumers compile against: $(paste -sd' ' <<<"$libraries")")

require README.md "the package's page on nuget.org says nothing"

# No <dependencies>. The closure travels inside the package, so a dependency here would install a second
# copy of Core or Extraction into the consumer's own graph at whatever version resolution picked.
! grep -q '<dependencies' <<<"$nuspec" ||
  findings+=("the nuspec declares <dependencies>, which puts this package's own closure into the consumer's package graph")

# No symbol package. Symbols are collected with the build output, which this package does not ship, so any
# .snupkg here is an empty one on its way to nuget.org.
shopt -s nullglob
symbols=("$artifacts"/$id.*.snupkg)
shopt -u nullglob
[ "${#symbols[@]}" -eq 0 ] ||
  findings+=("${#symbols[@]} $id symbol package(s) were packed, and an analyzer-only package has no symbols to carry")

# The same contract assembly, byte for byte, as the contract package of the same build. A project carrying
# both packages hands the compiler two copies of SlideRule.dll under one assembly identity. A .NET
# Framework compiler holds one copy per identity for its whole process, and its compiler server refuses a
# compilation whose copies differ and shuts itself down, so the build compiles again without it. At one
# version the two copies therefore have to be one file. They are because one pack builds both, and nothing
# else holds that, which is why it is compared here. Compared only where both entries are there, since unzip
# writes nothing for an entry it cannot find and two hashes of nothing agree.
shopt -s nullglob
contracts=("$artifacts"/SlideRule.[0-9]*.nupkg)
shopt -u nullglob
core='analyzers/dotnet/cs/SlideRule.dll'
compiled_core='lib/netstandard2.0/SlideRule.dll'
if [ "${#contracts[@]}" -ne 1 ]; then
  findings+=("$artifacts holds ${#contracts[@]} SlideRule.<version>.nupkg files, not the one this package's copy of the contract assembly is compared with")
else
  contract_entries="$(unzip -Z1 "${contracts[0]}")"
  if grep -Fxq "$core" <<<"$entries" && grep -Fxq "$compiled_core" <<<"$contract_entries"; then
    carried="$(unzip -p "$package" "$core" | sha256sum | cut -d' ' -f1)"
    published="$(unzip -p "${contracts[0]}" "$compiled_core" | sha256sum | cut -d' ' -f1)"
    [ "$carried" = "$published" ] ||
      findings+=("the contract assembly this package carries ($carried) is not the one $(basename "${contracts[0]}") ships ($published), so a .NET Framework compiler server refuses every compilation of a project that carries both")
  fi
fi

report analyzer
