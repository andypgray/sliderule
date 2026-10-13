# Contributing to SlideRule

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents. Thanks for your interest in working on it. For what it does and how to install it, see the [README](README.md); this document is about working on the code.

## Development setup

- The .NET 10 SDK is required. The solution file is `SlideRule.slnx`, which needs a current SDK to load.
- The checker loads solutions through MSBuildWorkspace, so the machine running the tests needs a .NET SDK. A machine with only the .NET runtime cannot run the workspace-tier tests.

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx

# One test by fully-qualified name
dotnet test SlideRule.slnx --filter "FullyQualifiedName~SpecValidationTests"
```

One test reaches the network, and it is off by default. Set `SLIDERULE_LINK_CHECK` to any non-blank value to turn it on. The suite then sends a HEAD request to every page the rules cite in the committed `AGENTS.md` files, examples included, and fails on a 404 or 410. Left unset, the test reports itself skipped with that reason, so a clone without network access still passes. CI turns it on for its Linux leg, and the release workflow for the run that publishes.

The suite runs on Microsoft.Testing.Platform, because `xunit.v3` 4.0.0 ships a version of that platform the .NET 10 SDK does not drive through the VSTest target. `global.json` selects the runner, and the test project references neither `Microsoft.NET.Test.Sdk` nor `xunit.runner.visualstudio`. Filter expressions work as before, since `xunit.v3` accepts the VSTest filter syntax, but VSTest's other options are rejected. `--logger "console;verbosity=normal"` stops the run before any test is discovered, and `--output Detailed` prints the per-test line that option printed under VSTest. A failing run exits 2 where VSTest exited 1, and a filter that matches no test exits 8 where VSTest reported success.

## Test culture: pinned tests are the spec

The test stack is xUnit v3 with Shouldly, and the suite follows these conventions:

- **Pinned strings are the spec.** String-equality tests pin the fluent-API shape, the prose fragments every rule renders, the violation-message text, and the CLI and JSON golden outputs. Those pins are the contract. When a pin must move, move it as part of the change and give the reason in the PR. A diff that rewrites golden text only to make a test pass will be asked to justify every changed byte.
- Extraction tests run in two tiers: fast `CSharpCompilation`-from-source unit tests, and an MSBuildWorkspace tier over the checked-in fixture solution (`tests/SlideRule.Tests/Fixtures/TestSolutions/MyApp`). Prefer adding coverage at the fast tier, and touch the fixture solution only when the workspace path itself is the subject.
- **The shipped public surface is pinned, and checked again at pack time.** `tests/SlideRule.Tests/DocHygiene/PublicSurface.*.txt` records every exported type and every public enum constant of the two packages a consumer compiles against, `SlideRule` and `SlideRule.Xunit`, and the suite fails as soon as one moves. `dotnet pack` runs package validation on both. Once a version under their package IDs is published, it also compares each package with its published predecessor, and fails on any difference the repository has not acknowledged in a `CompatibilitySuppressions.xml`. A NuGet version can never be replaced once pushed, so an accidental `public` is permanent. If your change moves either surface, read what moved before accepting it. The failure names the regenerated file and the command that copies it into place.
- CLI↔MCP output parity is pinned by table tests. If you change a runner's output, the CLI text, the MCP tool result, and (for check failures) the xUnit adapter's failure text move together. They are the same string by construction.

## The dogfood invariant

This repository is checked by its own spec. The project at [`arch/SlideRule.Architecture`](arch/SlideRule.Architecture) is a real spec for this codebase, and `sliderule render` generates 15 committed artifacts from it. The root [`AGENTS.md`](AGENTS.md) has the rule block, [`ARCHITECTURE.md`](ARCHITECTURE.md) has the two drawings, and 12 directories under `src/` have an `AGENTS.md` card each. Each of those is a block between `<!-- sliderule:begin -->` and `<!-- sliderule:end -->` markers. The last artifact is [`sliderule.model.json`](sliderule.model.json), the built model as data, which `render` writes whole. Self-spec tests keep the whole set current, and CI's `render --check` fails on any file a render would change, so:

- Never hand-edit inside the markers in any of those files, or anywhere in the model file. If your change alters what the spec renders, regenerate (`sliderule render` at the repository root, which reads the solution, the spec and every render target from [`sliderule.json`](sliderule.json)) and commit the result with your change, or the suite goes red.
- Expect that set to move. A layer with rules of its own, or a scope, whose code is in a new directory makes `render` write a card there. `render` never deletes a card, so when a rule moves away, delete the card it left behind. A self-spec test fails on a committed card that no rule placement produced.
- Content outside the markers is ordinary hand-written documentation, so edit it freely.

The same contract applies to any repository that commits rendered files, as [Operating SlideRule](OPERATING.md#changing-the-spec) describes.

## Editing the reader pages

The tests in `tests/SlideRule.Tests/DocHygiene/` check the root pages, the package readmes, the hooks page and every page under `examples/`. Tests in `tests/SlideRule.Tests/Dogfood/` re-run some of the captured fences on [SlideRule on itself](SELF-CHECK.md), and tests in `tests/SlideRule.Tests/Hooks/` check the hooks page against the `hook` command. The suite runs them on a prose edit as on any other change. The quote gates compare each fenced excerpt with the committed file it was cut from, and each captured run with a fresh one. Every `sliderule` command in a fence is parsed against the command tree the tool ships. The changelog is the exception, because its fences record each release's commands as they shipped. The count gates compare each stated number with its source, such as the solution file, the spec, the committed render and the baseline files. The rule count, the artifact count and the grandfathered counts are among them. The anchor gate resolves every `#fragment` link to a heading of its target. The hygiene gates reject internal working references and machine paths.

When a count or a whole fence drifts, the failure prints what belongs in its place. To run the tests in those three folders alone, use `dotnet test SlideRule.slnx --filter "FullyQualifiedName~SlideRule.Tests.DocHygiene|FullyQualifiedName~SlideRule.Tests.Dogfood|FullyQualifiedName~SlideRule.Tests.Hooks"`. The replayed runs on the example pages skip unless their example is built, as do the two Meridian fences on SlideRule on itself. Several gates keep lists of exempted or registered sentences that must still match live text. A rewrite that removes such a sentence can therefore fail a gate whose subject is not the wording you changed. Update the entry to the new wording, or delete it when the sentence is gone.

## Pull request expectations

- Small and focused: one change per PR.
- A green `dotnet test SlideRule.slnx` on your machine.
- XML doc comments on public members. The packages ship them, so in the shipping projects a missing one, or a `cref` that does not resolve, is a build error. Comments explain *why*, not *what*.
- Fluent-surface changes ship together with their pinned-test moves in the same PR.
- Cross-platform portability matters. Goldens are forward-slash and CRLF-tolerant by design, and a change that only passes on one OS is not done.

## Dependency updates

Every NuGet dependency of the shipped projects is pinned to a content hash in a committed `packages.lock.json`. There are ten: one for each of the four packages, plus one each for the rule pack, test project, spec analyzer, completion provider, extraction assembly and workspace assembly. The spec analyzer and the completion provider ship inside the contract package, and the extraction and workspace assemblies inside the tool and the xUnit adapter. Locked mode applies on CI only, so your local restores stay unlocked. Adding or bumping a package then updates the affected lock files as a side effect of building. Commit them with the change and your PR arrives consistent.

Dependabot edits a project's `PackageReference` and that project's own lock file. Every lock file downstream of that project stays as it was, so Dependabot's NuGet PRs arrive with those files out of step. On those branches CI relocks them in place, so the bump can be built, tested on three operating systems, packed, self-checked and run against the examples. CI never pushes the relock, so one check, `lock-files`, stays red until you do. That check regenerates the lock files and fails if any of them changes, and it passes once the relock is a commit on the branch.

To finish one:

```bash
gh pr checkout <number>
dotnet restore SlideRule.slnx --force-evaluate
git add -- "*packages.lock.json"
git commit -m "Regenerate NuGet lock files"
git push
```

The restore covers the whole solution because the staleness does. Bumping a package that one project references rewrites the lock file of every project downstream of it, which is more than the single directory Dependabot edited.

## Versioning and releases

The four packages (`SlideRule`, `.Analyzers`, `.Xunit`, `.Cli`) version in lockstep from a single `<Version>` in the root `Directory.Build.props`. A release is cut by pushing a `v*` tag. The [release workflow](.github/workflows/release.yml) checks that the tag matches that `<Version>` and both versions in `.mcp/server.json`, and that the tool package contains `.mcp/server.json`. It publishes the packages to nuget.org through trusted publishing (OIDC) and attests them. A second job then lists the release in the MCP registry. It waits until nuget.org serves the tool's readme with its `mcp-name` marker, which the registry checks, and publishes `.mcp/server.json` with `mcp-publisher`, signed in with the job's GitHub OIDC token. Release notes are on GitHub Releases and in [CHANGELOG.md](CHANGELOG.md).

Under 0.x, a minor version may change the spec API, with a Breaking entry in the changelog and no shim, and a patch version never does. Each JSON document except the hook's has a `schemaVersion`, as the [command-line reference](CLI.md#documents-and-versions) lists. The version moves when a key is removed, renamed or given a new meaning. The surface pins above make every such move deliberate. [Operating SlideRule](OPERATING.md#upgrading) covers upgrading a spec across a release.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE), the same license as the project.
