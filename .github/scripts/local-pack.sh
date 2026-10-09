# Sourced by the pack-check steps that install or restore from this job's own pack, never run on its own.
# What they share is the version they pin, a feed that serves the pack and nothing else, and the check that
# a restore took the contract library from it.
#
# pack_version prints the version Directory.Build.props declares. The path is relative, so a caller runs it
# before any cd.
#
# local_pack_feed <dir> writes <dir>/nuget.config with the local pack as its only source, so no feed but
# this job's pack can answer a restore under <dir>.
#
# require_contract_from_pack <version> <subject> fails the step unless $NUGET_PACKAGES holds the contract
# library at <version>, and its error names <subject> as what did not restore it. Restored from the pack,
# not resolved from a project: the path is the assertion.
#
# It sets no shell options: a sourced file runs in the caller's shell, and the callers run under the
# runner's default bash -e.
pack_version() {
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -n 1
}

local_pack_feed() {
  cat > "$1/nuget.config" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="local-pack" value="$GITHUB_WORKSPACE/artifacts" />
    </packageSources>
</configuration>
CONFIG
}

require_contract_from_pack() {
  [ -f "$NUGET_PACKAGES/sliderule/$1/lib/netstandard2.0/SlideRule.dll" ] || {
    echo "::error::$2 did not restore the contract library from the local pack (expected sliderule/$1/lib/netstandard2.0/SlideRule.dll)"
    find "$NUGET_PACKAGES" -name 'SlideRule.dll' || true
    exit 1
  }
}
