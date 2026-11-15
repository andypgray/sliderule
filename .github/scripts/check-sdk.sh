#!/usr/bin/env bash
# Fails a job whose dotnet is not the SDK band it asked setup-dotnet for, or that can see an SDK outside it.
# Usage: check-sdk.sh <band>, for example check-sdk.sh 10.0.
#
# setup-dotnet lets the newest SDK a runner image preinstalls win over the version a job asks for, so each job
# installs into a directory of its own and runs this right after setup-dotnet. A second SDK in the list means
# that directory is not the only one dotnet reads, and an image update could change the SDK again.
set -euo pipefail

if [[ $# -ne 1 || ! "$1" =~ ^[0-9]+\.[0-9]+$ ]]; then
  echo "::error::check-sdk.sh takes one SDK band, such as 10.0, not '$*'." >&2
  exit 2
fi

band="$1"
version="$(dotnet --version | tr -d '\r')"
sdks="$(dotnet --list-sdks | tr -d '\r')"
echo "SDK $version from $(command -v dotnet)"

if [[ "$version" != "$band".* ]]; then
  echo "::error::This job asks for SDK $band but runs $version." >&2
  exit 1
fi

others="$(grep -v "^${band//./\\.}\." <<< "$sdks" || true)"
if [[ -n "$others" ]]; then
  echo "::error::This job asks for SDK $band only, and dotnet also lists: ${others//$'\n'/; }" >&2
  exit 1
fi
