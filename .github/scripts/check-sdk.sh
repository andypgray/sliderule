#!/usr/bin/env bash
# Fails a job whose dotnet is not the SDK band it asked setup-dotnet for, or that can see an SDK outside the
# bands it asked for.
# Usage: check-sdk.sh <band in use> [<band beside it>...], for example check-sdk.sh 10.0, or check-sdk.sh 11.0 10.0
# for a job that installs SDK 10 beside the SDK 11 it runs.
#
# setup-dotnet lets the newest SDK a runner image preinstalls win over the version a job asks for, so each job
# installs into a directory of its own and runs this right after setup-dotnet. An SDK outside the named bands
# means that directory is not the only one dotnet reads, and an image update could change the SDK again.
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "::error::check-sdk.sh takes the SDK band in use, such as 10.0, and any bands beside it, not '$*'." >&2
  exit 2
fi
for band in "$@"; do
  if [[ ! "$band" =~ ^[0-9]+\.[0-9]+$ ]]; then
    if [[ $# -eq 1 ]]; then
      echo "::error::check-sdk.sh takes one SDK band, such as 10.0, not '$*'." >&2
    else
      echo "::error::check-sdk.sh takes SDK bands such as 10.0, and '$band' is not one." >&2
    fi
    exit 2
  fi
done

band="$1"
version="$(dotnet --version | tr -d '\r')"
sdks="$(dotnet --list-sdks | tr -d '\r')"
echo "SDK $version from $(command -v dotnet)"

if [[ "$version" != "$band".* ]]; then
  echo "::error::This job asks for SDK $band but runs $version." >&2
  exit 1
fi

others="$sdks"
for allowed in "$@"; do
  others="$(grep -v "^${allowed//./\\.}\." <<< "$others" || true)"
done
if [[ -n "$others" ]]; then
  if [[ $# -eq 1 ]]; then
    asked="SDK $band only,"
  else
    asked="SDK $band, and ${*:2} beside it,"
  fi
  echo "::error::This job asks for $asked and dotnet also lists: ${others//$'\n'/; }" >&2
  exit 1
fi
