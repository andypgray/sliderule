#!/usr/bin/env bash
# The guidance package is one analyzer assembly with its rules compiled in: the contract, the extractor and
# the build analyzer's engine sit inside it under its own assembly name, so it shares no file with the
# contract package or the analyzer package and depends on neither. Every part of that shape comes from a
# property in the project file, so a knob lost in a merge produces a package that still builds and pushes
# and then either loads a second SlideRule.dll beside the contract's or puts an assembly in lib/ for
# consumers to bind to.
#
# Usage: verify-guidance-package-shape.sh <artifacts-directory>
set -euo pipefail

artifacts="${1:?usage: verify-guidance-package-shape.sh <artifacts-directory>}"

id='SlideRule.Guidance.DotNet'

. "$(dirname "${BASH_SOURCE[0]}")/package-shape.sh"
open_package "$artifacts" "$id"

# The one assembly, and nothing beside it. A SlideRule.dll or SlideRule.Extraction.dll here would be the
# same assembly identity the other two packages ship, at whatever version each package is.
require_closure "the analyzer cannot load in a consumer's build" "$id.dll"

require_build_targets "a compiler below the Roslyn floor fails every build with CS8032"

require README.md "the package's page on nuget.org says nothing"

# The package needs neither the contract nor the analyzer package, at any version.
require_analyzer_only "ties this package to another package's version"

report guidance
