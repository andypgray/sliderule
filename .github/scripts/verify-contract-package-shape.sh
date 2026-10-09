#!/usr/bin/env bash
# The contract package is the one every spec project references, and it now carries more than the library
# they compile against: the spec-literal analyzer and the completion provider ride inside it under
# analyzers/dotnet/cs, with a second copy of the contract assembly beside them, because a compiler and an
# editor both resolve an extension's dependencies from the directory it sits in and never from the
# consumer's package graph. Every part of that shape comes from a target in the project file rather than
# from anything the pack itself checks, so a hook lost in a merge produces a package that still builds,
# still pushes, and silently analyses nothing and completes nothing, at a version that can never be taken
# back.
#
# One script rather than a step in each workflow, for the same reason as verify-analyzer-package-shape.sh
# beside it: the shape is the thing a change has to find, and two copies of it is one copy that gets missed.
#
# Usage: verify-contract-package-shape.sh <artifacts-directory>
set -euo pipefail

artifacts="${1:?usage: verify-contract-package-shape.sh <artifacts-directory>}"

id='SlideRule'

. "$(dirname "${BASH_SOURCE[0]}")/package-shape.sh"
open_package "$artifacts" "$id"

# What a spec project compiles against, which is the half of this package that predates the analyzer.
require "lib/netstandard2.0/$id.dll" 'a spec project has nothing to compile against'

# The closure the compiler and the editor have to load, and nothing besides. A missing one of these does
# not fail the pack or the restore: it fails at load time, inside the consumer's build or inside their
# editor, where the only symptom is silence. The project file names the same three files in one place, so
# an assembly that joins the closure is added there and here.
require_closure "%s cannot load in a spec author's build or editor" "$id.SpecAnalyzers.dll" "$id.SpecCompletion.dll" "$id.dll"

# Both paths, from the one source file: buildTransitive/ is what an SDK-style consumer reads and what flows
# on to a project referencing theirs, and build/ is the only one an older client or a packages.config
# project looks in. Without the file a compiler below the analyzer's floor reports CS8032 on every build of
# every project that references this package, which is an error wherever warnings are errors. It takes the
# whole directory out by NuGetPackageId rather than by file name, so it covers the completion provider
# without naming it.
require_build_targets 'nothing takes the analyzer back out on a compiler that cannot load it'

require README.md "the package's page on nuget.org says nothing"

# Zero dependencies, read off the packed bytes rather than off the project file. A <dependency> element here
# would install a package into the graph of every repository that carries a spec, and "netstandard2.0 with
# zero dependencies" is a published claim of this package. The empty group is asserted with it, because
# suppressing the section altogether is the other way that claim goes false while everything else looks
# unchanged.
! grep -q '<dependency[ />]' <<<"$nuspec" ||
  findings+=("the nuspec declares a <dependency>, and zero dependencies is what this package's description and both READMEs tell a reader")

groups="$(grep -c '<group targetFramework=' <<<"$nuspec" || true)"
[ "$groups" = '1' ] ||
  findings+=("the nuspec carries $groups dependency groups, not the one empty group a package with no dependencies packs")

# The analyzer's copy of the contract is the same file lib/ is packed from, named a second time, so these
# two hashes are equal by construction rather than by luck. They come apart the moment someone repoints the
# packed path at a staged copy, and an analyzer deciding a literal against a different build of the contract
# than the spec compiled against gives a wrong answer rather than no answer. Compared only where both
# entries are there, since unzip writes nothing for an entry it cannot find and two hashes of nothing agree.
if grep -Fxq "lib/netstandard2.0/$id.dll" <<<"$entries" && grep -Fxq "analyzers/dotnet/cs/$id.dll" <<<"$entries"; then
  compiled="$(unzip -p "$package" "lib/netstandard2.0/$id.dll" | sha256sum | cut -d' ' -f1)"
  loaded="$(unzip -p "$package" "analyzers/dotnet/cs/$id.dll" | sha256sum | cut -d' ' -f1)"
  [ "$compiled" = "$loaded" ] ||
    findings+=("the contract assembly under analyzers/ ($loaded) is not the one under lib/ ($compiled), so the analyzer reads a spec against a different build of the contract than the spec compiled against")
fi

# Symbols are collected from the build output, so this package's symbol package holds the contract library's
# pdb and nothing else. A .dll inside it is pack having put package content into the symbol package, which
# nuget.org validates after the push, by email, on a version already published.
shopt -s nullglob
symbols=("$artifacts"/$id.[0-9]*.snupkg)
shopt -u nullglob
if [ "${#symbols[@]}" -ne 1 ]; then
  findings+=("${#symbols[@]} $id symbol packages were packed, not one")
else
  symbol_entries="$(unzip -Z1 "${symbols[0]}")"
  libraries="$(grep -E '\.dll$' <<<"$symbol_entries" || true)"
  [ -z "$libraries" ] ||
    findings+=("$(basename "${symbols[0]}") carries assemblies, which a symbol package does not ship: $(paste -sd' ' <<<"$libraries")")
fi

report contract
