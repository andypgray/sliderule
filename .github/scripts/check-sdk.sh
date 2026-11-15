#!/usr/bin/env bash
# Fails a job whose dotnet is not the SDK band it asked setup-dotnet for, or that can see an SDK outside the
# bands it asked for.
# Usage: check-sdk.sh <band in use> [<band beside it>...], for example check-sdk.sh 10.0, or check-sdk.sh 11.0 10.0
# for a job that installs SDK 10 beside the SDK 11 it runs.
#
# setup-dotnet lets the newest SDK a runner image preinstalls win over the version a job asks for, so each job
# installs into a directory of its own and runs this right after setup-dotnet. An SDK outside the named bands
# means that directory is not the only one dotnet reads, and an image update could change the SDK again.
# DOTNET_INSTALL_DIR names that directory, and every SDK dotnet lists must sit under it, so a job whose image
# preinstalls only the bands it asks for still fails when setup-dotnet installed somewhere else.
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
if [[ -z "${DOTNET_INSTALL_DIR:-}" ]]; then
  echo "::error::check-sdk.sh needs DOTNET_INSTALL_DIR, the directory setup-dotnet was told to install into." >&2
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

# dotnet writes the directory with backslashes on Windows, and DOTNET_INSTALL_DIR arrives there as RUNNER_TEMP's
# backslashed spelling with /.dotnet appended. Neither side fixes the drive letter's case, so both are compared with
# slashes and case folded.
fold() {
  printf '%s' "${1//\\//}" | tr '[:upper:]' '[:lower:]'
}
expected="$(fold "$DOTNET_INSTALL_DIR/sdk")"
elsewhere=""
while IFS= read -r row; do
  [[ -n "$row" ]] || continue
  directory="${row#*\[}"
  if [[ "$(fold "${directory%]}")" != "$expected" ]]; then
    elsewhere+="${elsewhere:+; }$row"
  fi
done <<< "$sdks"
if [[ -n "$elsewhere" ]]; then
  echo "::error::This job installs into $DOTNET_INSTALL_DIR, and dotnet lists SDKs outside it: $elsewhere" >&2
  exit 1
fi
