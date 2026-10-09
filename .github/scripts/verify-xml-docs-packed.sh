#!/usr/bin/env bash
# The two library packages carry their XML documentation beside the assembly, which is what a
# consumer's editor reads for the doc comments. It is a build property rather than a pack item, so
# nothing else in the pack would notice it going missing. The Core glob pins a digit after the id,
# because the bare prefix also matches the other three packages.
#
# One script rather than a step in each workflow: the package-to-TFM table below is the thing a
# framework bump has to find, and two copies of it is one copy that gets missed — in release.yml,
# which is the workflow that actually pushes to nuget.org.
#
# Usage: verify-xml-docs-packed.sh <artifacts-directory>
set -euo pipefail

artifacts="${1:?usage: verify-xml-docs-packed.sh <artifacts-directory>}"

while read -r package entry; do
  entries="$(unzip -Z1 "$artifacts"/$package)"
  grep -Fxq "$entry" <<<"$entries" || {
    echo "::error::$entry is missing from $artifacts/$package: the package ships no XML documentation"
    exit 1
  }
done <<'EOF'
SlideRule.[0-9]*.nupkg lib/netstandard2.0/SlideRule.xml
SlideRule.Xunit.*.nupkg lib/net10.0/SlideRule.Xunit.xml
EOF
