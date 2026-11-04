#!/usr/bin/env bash
# Regenerates the NuGet lock files on a Dependabot NuGet branch and commits them locally, so the job that
# runs it tests what Dependabot proposed. On any other ref it does nothing.
#
# Dependabot bumps a PackageReference and that project's own packages.lock.json, but leaves the lock file
# of every project downstream of it stale, so a NuGet update PR arrives failing NU1004 at Restore, before
# a single test runs: a red check that says nothing about the bump it was raised for.
#
# Solution-wide, because the staleness ripples: a version change in one project rewrites the lock file of
# every project that references it transitively, and Dependabot updates one directory at a time, so
# nothing scoped to the bumped project could be right. Three details carry weight:
#
#   - CI is emptied for the restore alone, so only it leaves locked mode; every build, test and pack step
#     after it still sees CI=true. RestorePackagesWithLockFile is unconditional, so the lock files are
#     rewritten rather than ignored.
#   - The commit is local and never pushed. A GITHUB_TOKEN push could not re-trigger the PR's checks
#     anyway, and a standing write credential is a poor trade for a weekly convenience PR. It exists so
#     pack-check's `<version>+<commit>` assertion still names the commit SourceLink embedded, and so
#     nothing packs from a dirty tree.
#   - The head ref, not the actor: GITHUB_HEAD_REF is empty on push, so main is untouched; a maintainer
#     re-running the workflow gets the same answer; and a github-actions bump does not pay for a restore
#     it has no use for.
#
# The PR still cannot reach all-green by itself. The lock-files job stays red until a human puts the
# relock commit on the branch, which is the one signal worth acting on.
set -euo pipefail

case "${GITHUB_HEAD_REF:-}" in
  dependabot/nuget/*) ;;
  *)
    echo "Not a Dependabot NuGet branch; the lock files stay as committed."
    exit 0
    ;;
esac

CI='' dotnet restore SlideRule.slnx --force-evaluate
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add -- '*packages.lock.json'
git diff --cached --quiet || git commit -q -m 'Regenerate NuGet lock files'
