# Contributing to SlideRule

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents. Thanks for your interest in working on it. For what it does and how to install it, see the [README](README.md); this document is about working on the code.

## Development setup

- The .NET 10 SDK is required. The solution file is `SlideRule.slnx` (`.slnx`, not `.sln`), which needs a current SDK to load.
- The checker loads solutions through MSBuildWorkspace (via MSBuildLocator), so the machine running the tests needs a .NET SDK. Plain runtime-only environments cannot run the workspace-tier tests.

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx

# One test by fully-qualified name
dotnet test SlideRule.slnx --filter "FullyQualifiedName~SpecValidationTests"
```

One gate reaches the network and is off by default. Set `SLIDERULE_LINK_CHECK=1` and the suite HEADs every page the committed context blocks cite, failing on a 404 or 410. Left unset, the gate reports itself skipped with that reason, so a clone with no network still goes green. CI opts in on its Linux leg, and the release workflow opts in for the run that publishes.

The suite runs on Microsoft.Testing.Platform rather than VSTest. The reason is `xunit.v3` 4.0.0, which ships a version of that platform the .NET 10 SDK declines to drive through the VSTest target. `global.json` selects the runner, and the test project carries neither `Microsoft.NET.Test.Sdk` nor `xunit.runner.visualstudio`. Filter expressions are unchanged, since `xunit.v3` accepts the VSTest filter syntax. VSTest's other options are rejected. `--logger "console;verbosity=normal"` stops the run before a test is discovered, and `--output Detailed` prints the per-test line it used to. A failing run exits 2 where VSTest exited 1, and a filter that matches nothing exits 8 where VSTest reported success.

## Test culture: pinned tests are the spec

The house stack is xUnit v3 + Shouldly. Beyond that, one convention carries this repo:

- **Pinned strings are the spec.** String-equality tests pin the fluent-API shape, the prose fragments every rule renders, the violation-message text, and the CLI/JSON golden outputs. They are not incidental assertions; they are the contract. When a pin must move, move it deliberately as part of the change, with the reason in the PR. A diff that silently rewrites golden text to make a test pass will be asked to justify every changed byte.
- Extraction tests run in two tiers: fast `CSharpCompilation`-from-source unit tests, and an MSBuildWorkspace tier over the checked-in fixture solution (`tests/SlideRule.Tests/Fixtures/TestSolutions/MyApp`). Prefer adding coverage at the fast tier; touch the fixture solution only when the workspace path itself is the subject.
- **The shipped public surface is pinned, and watched again at pack time.** `tests/SlideRule.Tests/DocHygiene/PublicSurface.*.txt` records every exported type and every public enum constant of the two packages a consumer compiles against. The suite reds the moment one moves. `dotnet pack` runs package validation on both, and once a version under their package IDs is published it also compares each against its published predecessor and fails on any difference the repository has not acknowledged in a `CompatibilitySuppressions.xml`. A NuGet version can never be replaced once pushed, so an accidental `public` is permanent. If your change moves either, read what moved before accepting it. The failure names the regenerated file and the command that copies it into place.
- CLI↔MCP output parity is pinned by table tests. If you change a runner's output, the CLI text, the MCP tool result, and (for check failures) the xUnit adapter's failure text move together. They are the same string by construction.

## The dogfood invariant

This repo governs itself. The project at [`arch/SlideRule.Architecture`](arch/SlideRule.Architecture) is a real spec for this codebase, and `sliderule render` generates fifteen committed artifacts from it. The root [`AGENTS.md`](AGENTS.md) carries the rule block, [`ARCHITECTURE.md`](ARCHITECTURE.md) carries the project diagram, and twelve directories under `src/` carry a scoped rule card each. Each of those is a block between `<!-- sliderule:begin -->` / `<!-- sliderule:end -->` markers. The last is [`sliderule.model.json`](sliderule.model.json), the built model as data, and `render` owns that whole file. Self-spec tests keep the whole set current, and CI's `render --check` fails on any file a render would change, so:

- Never hand-edit inside the markers in any of those files, or anywhere in the model file. If your change alters what the spec renders, regenerate (`sliderule render` at the repository root, which reads the solution, the spec and every render target from [`sliderule.json`](sliderule.json)) and commit the result with your change, or the suite goes red.
- Expect that set to move. A rule anchored to a new directory makes `render` write a card there, and a card whose rules have moved away has to be deleted. A test fails on a committed card that no rule placement produced, so an orphan cannot stay behind being read.
- Content outside the markers is ordinary hand-written documentation; edit it freely.

## Pull request expectations

- Small and focused: one change per PR.
- A green `dotnet test SlideRule.slnx` on your machine.
- XML doc comments on public members. The packages ship them, so in the shipping projects a missing one, or a `cref` that does not resolve, is a build error. Comments explain *why*, not *what*.
- Fluent-surface changes ship together with their pinned-test moves in the same PR.
- Cross-platform portability matters. Goldens are forward-slash and CRLF-tolerant by design, and a change that only passes on one OS is not done.

## Dependency updates

Every NuGet dependency of the shipped projects is pinned to a content hash in a committed `packages.lock.json`. There are ten: one for each of the four packages, plus one each for the rule pack, test project, spec analyzer, completion provider, extraction assembly and workspace assembly. The spec analyzer and the completion provider ship inside the contract package, and the extraction and workspace assemblies inside the tool and the xUnit adapter. Locked mode is CI-only, so your local restores stay unlocked. Adding or bumping a package then updates the affected lock files as a side effect of building. Commit them with the change and your PR arrives consistent.

Dependabot cannot do that. It edits the `PackageReference` and leaves the lock files alone, so its NuGet PRs arrive with the two out of step. CI relocks them in place on those branches. This lets the bump be built, tested on three operating systems, packed, self-checked, and run against the examples. The relock is never pushed, so one check stays red on purpose. That check, `lock-files`, regenerates the lock files and requires that none of them move. It goes green once the relock is a commit on the branch.

To finish one:

```bash
gh pr checkout <number>
dotnet restore SlideRule.slnx --force-evaluate
git add -- "*packages.lock.json"
git commit -m "Regenerate NuGet lock files"
git push
```

The restore is solution-wide because the staleness is. Bumping a package that one project references rewrites the lock file of every project downstream of it. That is more than the single directory Dependabot edited.

## Versioning and releases

The four packages (`SlideRule`, `.Analyzers`, `.Xunit`, `.Cli`) version in lockstep from a single `<Version>` in the root `Directory.Build.props`. Releases are cut by tag, and the workflow runs the version-consistency checks, trusted publishing (OIDC), attestation, and a check that `.mcp/server.json` is packed. The MCP registry picks the release up from nuget.org. Release notes live on GitHub Releases and in [CHANGELOG.md](CHANGELOG.md).

Under 0.x, a minor version may change the spec API. Every such change gets a Breaking entry in the changelog and ships without a shim. A patch version makes none. The JSON documents carry a `schemaVersion` that moves when their shape does. The surface pins above make any move deliberate.

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE), the same license as the project.
