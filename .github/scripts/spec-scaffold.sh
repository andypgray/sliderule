# Sourced by the pack-check steps that build the derive_spec recipe's spec project in a reader's tree, never run
# on its own. Source local-pack.sh first: scaffold writes its feed with local_pack_feed.
#
# spec_scaffold_cut <version> sets $csproj and $spec to the recipe's spec csproj and spec class, cut out of the
# recipe rather than written here, with the Version="..." substitution ArchPrompts applies when the server
# serves the prompt. So every leg executes the published recipe instead of a copy that could drift from it,
# and a recipe edit that breaks the scaffold reddens a leg rather than reaching a reader. An empty cut fails
# the step: a restructure must redden it, never silently scaffold nothing.
#
# scaffold <root> [feed] writes the reader's tree under <root>: the unchanged csproj and spec class, one rule in
# a file of its own, a product with one conforming interface, a solution holding both, and a nuget.config that
# the function named by [feed] writes, local_pack_feed by default, whose only source is the local pack. The rule is authored here because the recipe's Define() is empty by design,
# and a spec with no rules would prove loading and evaluate nothing. A second spec class is the seam that
# leaves the recipe's own file byte for byte what it publishes, since the model runs every IArchitectureSpec
# in the assembly over one shared Arch.
#
# product_tree <root> [feed] writes the same product, a solution holding only it, and the same nuget.config: the
# tree sliderule init starts from, with no spec project for init to find.
#
# nuget_org_feed <root> writes <root>/nuget.config with nuget.org as its only source, for a leg that runs a
# released version rather than this job's pack.
#
# checked_rule <dir> writes that one rule into the spec project folder <dir>.
#
# write_violation <root> writes <root>/src/MyApp/Broken.cs, an interface without the prefix checked_rule's rule
# requires: the violation a leg plants to see the rule go red.
#
# dead_pattern_rule <dir> writes a second rule into the spec project folder <dir>, in DeadPattern.cs, for the
# spec-literal analyzer to report as SR1017. 'MyApp.*.Controllers.*' is a subtree glob whose literal prefix
# itself carries a wildcard, so it can never match anything — a deduction from the matcher's own semantics,
# which is the line the catalog draws and the analyzer holds to.
#
# build_fails_with <target> <log> <pattern> <message> builds <target> into <log>, and fails the step with
# <message> and the log unless the build failed with a line matching the extended regex <pattern>.
#
# It sets no shell options: a sourced file runs in the caller's shell, and the callers run under the runner's
# default bash -e.
. .github/scripts/fence.sh

spec_scaffold_cut() {
  local recipe="src/SlideRule.Cli/Mcp/Prompts/derive-spec.md"
  csproj="$(fence "$recipe" xml 'PackageReference Include="SlideRule"')"
  spec="$(fence "$recipe" csharp 'IArchitectureSpec')"
  [ -n "$csproj" ] && [ -n "$spec" ] || {
    echo "::error::Could not cut the spec scaffold out of $recipe — the xml fence carrying the PackageReference, or the csharp fence carrying the IArchitectureSpec class, has moved or changed shape. That recipe is what adopters follow, so fix it or fix this cut; do not delete the assertion."
    exit 1
  }
  csproj="$(printf '%s\n' "$csproj" | sed 's|Version="\.\.\."|Version="'"$1"'"|')"
  scaffold_version="$1"
}

scaffold() {
mkdir -p "$1/src/MyApp" "$1/arch/MyApp.Architecture"
printf '%s\n' "$csproj" > "$1/arch/MyApp.Architecture/MyApp.Architecture.csproj"
printf '%s\n' "$spec" > "$1/arch/MyApp.Architecture/ArchitectureSpec.cs"

# The same line DeriveSpecPromptTests pins the served prompt to, asserted from the other side: the csproj
# under test is the one an adopter is handed, pinned to the pack.
grep -Fq "<PackageReference Include=\"SlideRule\" Version=\"$scaffold_version\" />" \
  "$1/arch/MyApp.Architecture/MyApp.Architecture.csproj" || {
  echo "::error::The scaffold csproj did not come out pinned to $scaffold_version — the Version=\"...\" placeholder ArchPrompts substitutes is no longer where the recipe spells it"
  cat "$1/arch/MyApp.Architecture/MyApp.Architecture.csproj"
  exit 1
}

checked_rule "$1/arch/MyApp.Architecture"
write_product "$1"

cat > "$1/MyApp.slnx" <<'SLNX'
<Solution>
  <Folder Name="/src/">
    <Project Path="src/MyApp/MyApp.csproj" />
  </Folder>
  <Folder Name="/arch/">
    <Project Path="arch/MyApp.Architecture/MyApp.Architecture.csproj" />
  </Folder>
</Solution>
SLNX

${2:-local_pack_feed} "$1"
}

product_tree() {
write_product "$1"

cat > "$1/MyApp.slnx" <<'SLNX'
<Solution>
  <Folder Name="/src/">
    <Project Path="src/MyApp/MyApp.csproj" />
  </Folder>
</Solution>
SLNX

${2:-local_pack_feed} "$1"
}

nuget_org_feed() {
  cat > "$1/nuget.config" <<'CONFIG'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <clear />
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
</configuration>
CONFIG
}

checked_rule() {
cat > "$1/CheckedRule.cs" <<'CS'
using SlideRule;

namespace MyApp.Architecture;

public sealed class PackageReferenceProof : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("naming/interfaces")
            .Enforce(arch.Project("MyApp").OfKind(TypeKind.Interface).MustHavePrefix("I"))
            .Because("The scaffolded spec has to evaluate over real symbols for a green run to mean anything, so it states the one convention the product below obeys and the violation below breaks.");
    }
}
CS
}

write_product() {
mkdir -p "$1/src/MyApp"
cat > "$1/src/MyApp/MyApp.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>
</Project>
CSPROJ

cat > "$1/src/MyApp/Shipments.cs" <<'CS'
namespace MyApp;

public interface IShipmentPort
{
    string Describe();
}

public sealed class ShipmentPort : IShipmentPort
{
    public string Describe() => "shipment";
}
CS
}

write_violation() {
cat > "$1/src/MyApp/Broken.cs" <<'CS'
namespace MyApp;

public interface ShipmentPortWithoutThePrefix
{
}
CS
}

dead_pattern_rule() {
cat > "$1/DeadPattern.cs" <<'CS'
using SlideRule;

namespace MyApp.Architecture;

public sealed class DeadPatternProof : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        arch.Rule("naming/dead-pattern")
            .Enforce(arch.Namespace("MyApp.*.Controllers.*").MustHavePrefix("I"))
            .Because("The pattern this rule selects on can never match a namespace, so the rule holds nothing to anything.")
            .Fix("Anchor the subtree on a literal prefix, so the pattern names the namespaces it means.");
    }
}
CS
}

build_fails_with() {
  local rc=0
  dotnet build "$1" > "$2" 2>&1 || rc=$?
  if [ "$rc" -eq 0 ] || ! grep -Eq "$3" "$2"; then
    echo "::error::$4 (build exited $rc)"
    cat "$2"
    exit 1
  fi
}
