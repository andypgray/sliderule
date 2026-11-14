# Contributing to SlideRule

SlideRule is a .NET architecture checker that enforces one C# spec and renders the same rules for coding agents. This guide covers building, testing and changing SlideRule. For product use and installation, see the [README](README.md).

## Development setup

- Install the .NET 10 SDK to build `SlideRule.slnx`, which lists 25 projects. The CLI, workspace assembly, xUnit adapter, rule pack and test project target `net10.0`; the other projects under `src/` target `netstandard2.0`.
- Keep an SDK installed when running tests. The workspace assembly loads solutions through `MSBuildWorkspace`, which needs the SDK's MSBuild. A runtime alone cannot run the tests that load fixture solutions.

```bash
dotnet build SlideRule.slnx
dotnet test SlideRule.slnx

# One test by fully-qualified name
dotnet test SlideRule.slnx --filter "FullyQualifiedName~SpecValidationTests"
```

One test reaches the network. Set `SLIDERULE_LINK_CHECK` to a non-blank value to enable it; unset or blank skips it before any network access. The skip reason is `Cited-page link checking is opt-in and needs the network: set SLIDERULE_LINK_CHECK to a non-blank value to run it.` An offline clone can therefore pass the suite. The test probes citations in the root and CLI `AGENTS.md` files and in the root `AGENTS.md` files of the Meridian, Meridian.Quoting and Meridian.Interchange examples. It probes each distinct page once, up to four at a time, using HEAD and retrying with GET after 405 or 501. A 404 or 410 fails with each citation's file, line and rule ID. Other non-success responses and unreachable pages produce a skip naming those pages. CI enables it only on `ubuntu-latest` among its four images (`ubuntu-latest`, `windows-latest`, `windows-2022`, `macos-latest`). The release workflow also enables it for the run that publishes.

The suite runs on Microsoft.Testing.Platform. The root `global.json` contains only the `test.runner` setting selecting it. The test project is an executable referencing `xunit.v3` 4.0.2 and Shouldly, with neither `Microsoft.NET.Test.Sdk` nor `xunit.runner.visualstudio`. xUnit v3 4.x ships Microsoft.Testing.Platform 2.x, which the .NET 10 SDK does not drive through the VSTest target. VSTest filter expressions still work, including alternatives such as `--filter "FullyQualifiedName~A|FullyQualifiedName~B"`. CI uses `--output Detailed` for per-test lines.

## Test culture: pinned tests are the spec

Tests use xUnit v3 and Shouldly assertions, with `FsCheck.Xunit.v3` for property tests.

- String-equality tests pin the fluent API's shape, rendered rule fragments, violation messages, and CLI and JSON outputs. The goldens are committed under `tests/SlideRule.Tests/Cli/Golden/`. Comparisons ignore line endings and surrounding whitespace. A failure names the golden's path with `Update it deliberately`. Review the changed text before updating the pin, and explain the change in the PR.
- Extraction tests have two tiers. One builds a `CSharpCompilation` from source in memory. The other loads the checked-in `tests/SlideRule.Tests/Fixtures/TestSolutions/MyApp` solution through `MSBuildWorkspace`. Use the in-memory tier for extraction behavior and the fixture when the workspace load is the subject.
- The public-surface pins are `tests/SlideRule.Tests/DocHygiene/PublicSurface.SlideRule.txt` and `PublicSurface.SlideRule.Xunit.txt` in the same directory. They record every exported type and every public enum constant with its value in the two packages consumers compile against. A surface change fails the suite without rewriting the pin. The regenerated `<pin file name>.actual` is written next to the test assembly; the failure prints `cp '<regenerated>' '<committed>'`. Both packages also enable package validation during `dotnet pack`. It compares against the last published version, currently `PackageValidationBaselineVersion` 0.3.0, in strict mode, reporting additions as well as removals. Acknowledged differences go in `CompatibilitySuppressions.xml` next to the affected csproj; none is committed today.
- Table tests compare MCP results with their CLI counterparts after newline normalization. The tools capture output from the same runners; `arch_context` is compared with rendered card text. The xUnit adapter also fails a red rule with `HumanReportRenderer.RuleBlock`, the rule's block as `check` writes it in its human report. Review these outputs together when changing a runner or shared renderer.

## The dogfood invariant

This repository uses [`arch/SlideRule.Architecture`](arch/SlideRule.Architecture) as its own spec. The root settings name its `SlideRule.Architecture.csproj` in `spec`. Its render produces 16 committed artifacts. The root [`AGENTS.md`](AGENTS.md) contains the rules, and [`ARCHITECTURE.md`](ARCHITECTURE.md) contains two Mermaid drawings, the codebase survey and the architecture law. Another 13 directories under `src/` each contain an `AGENTS.md` card. These Markdown blocks use `<!-- sliderule:begin -->` and `<!-- sliderule:end -->` markers. The remaining artifact, [`sliderule.model.json`](sliderule.model.json), is the built model as data; its whole text is generated, without markers. Self-spec tests compose every artifact in process and compare it with the committed file. CI also runs `render --check` through this repository's action. It writes no files, names those a render would change and exits 1 if any differ.

- Regenerate changes to the rules or their rendering with `sliderule render` at the repository root, then commit the result. It reads the solution, spec and render targets from [`sliderule.json`](sliderule.json). Here, `render.model` is `true` and `render.diagram` targets `ARCHITECTURE.md`. Edit the spec or renderer instead of generated text.
- Review card placement when rules move. A layer with rules of its own and each scope get cards where their code is located; placements in one directory merge. The current directories are `src/SlideRule/` and its `Checking/`, `Model/` and `Rendering/` subdirectories; `src/SlideRule.Analyzers/`, `src/SlideRule.Cli/`, `src/SlideRule.Extraction/`, `src/SlideRule.Guidance.DotNet/`, `src/SlideRule.Packs.DotNet/`, `src/SlideRule.Roslyn/MsBuild/`, `src/SlideRule.SpecAnalyzers/`, `src/SlideRule.SpecCompletion/` and `src/SlideRule.Xunit/`. Render deletes no files. Delete a card whose placement disappeared, or restore the rule that placed it; the self-spec tests reject committed orphan cards.
- Edit handwritten text outside the markers as needed. Render replaces only the managed block and preserves the surrounding text; the model file is generated throughout.

[Operating SlideRule](OPERATING.md#changing-the-spec) describes the same contract for any repository that commits rendered files.

## Editing the reader pages

Documentation changes run through the suite like code changes. Tests in `tests/SlideRule.Tests/DocHygiene/` cover the root pages, package readmes under `src/`, `hooks/README.md` and the example pages. The Dogfood tests replay the graph survey, `explain --type` claims and hook report on [SlideRule on itself](SELF-CHECK.md). The Hooks tests compare the hooks page and settings snippets with the command's exit codes, options and environment variables. Quote gates compare excerpts with their committed sources and captured output with fresh runs; each captured fence's registration identifies its replay test. Every fenced `sliderule` command in tracked Markdown must parse against the shipped command tree. The changelog is excluded because it records commands as released; Usage lines in CLI help fences are treated as synopses. Count gates compare registered numbers with the solution file, tracked set and self-spec, rule bullets in the committed root `AGENTS.md`, committed render, and baseline `entries`. These include rule, artifact and grandfathered counts. The anchor gate resolves Markdown `#fragment` links and absolute links into this repository from other tracked non-test files using GitHub heading anchors. Hygiene tests reject internal working references and private environment details, including machine paths, in tracked text files.

A count failure names the line and expected number; a captured-output failure prints replacement text. Run these groups with `dotnet test SlideRule.slnx --filter "FullyQualifiedName~SlideRule.Tests.DocHygiene|FullyQualifiedName~SlideRule.Tests.Dogfood|FullyQualifiedName~SlideRule.Tests.Hooks"`. Example walkthrough replays skip until their example is built. So do the two Meridian captures on the self-check page, a SARIF result and the matching check-document rule. CI's examples job builds the examples and runs those tests. Several gates keep lists of exempted or registered sentences. A rewrite that removes one can fail a gate whose subject is not the wording you changed. When rewriting a registered sentence, update its registration too. A count registration must match exactly once, and an exemption must still match a line with a relevant hit. Remove exemptions when their sentences are gone.

## Pull request expectations

- Keep each PR focused on one change.
- Pass `dotnet test SlideRule.slnx` before submitting.
- Document public members. Every project under `src/` treats missing XML documentation (CS1591) as a build error. Malformed comments and unresolved `cref` references are errors under `src/`, `arch/` and in the test project. Packages include the XML documentation.
- Include the corresponding pinned-test changes with a fluent API change, and explain why the contract changed.
- Check portability across Linux, Windows and macOS. CI builds and tests on `ubuntu-latest`, `windows-latest`, `windows-2022` and `macos-latest`. Golden comparisons ignore line endings and surrounding whitespace; product output uses forward slashes for paths on every OS.

## Dependency updates

Commit regenerated `packages.lock.json` files with dependency changes. There are 11: one for each of the five packages, plus the rule pack, test project, spec analyzer, completion provider, extraction assembly and workspace assembly. The spec analyzer and completion provider ship inside `SlideRule`, under `analyzers/dotnet/cs`. Extraction and workspace assemblies ship inside `SlideRule.Cli` and `SlideRule.Xunit`; extraction also ships inside `SlideRule.Analyzers`. Each lock-file project enables `RestorePackagesWithLockFile`. `RestoreLockedMode` applies when `CI` is `true`, so local restores are unlocked and a build's restore updates affected lock files after a package change.

Dependabot updates a project's `PackageReference` and its own lock file, leaving downstream lock files stale. For `dependabot/nuget/*` branches, the build-and-test, pack-check, self-check, examples and showcase jobs run `.github/scripts/relock.sh`. It runs `dotnet restore SlideRule.slnx --force-evaluate` and commits the regenerated files locally so those jobs test the update. The script does no work on other branches, and CI never pushes its commit. The `lock-files` job runs on every PR and push to main. It force-evaluates the restore and fails on changed committed lock files or newly generated uncommitted ones. Its job summary prints the repair commands. Commit the relock on the branch to pass it.

To finish one:

```bash
gh pr checkout <number>
dotnet restore SlideRule.slnx --force-evaluate
git add -- "*packages.lock.json"
git commit -m "Regenerate NuGet lock files"
git push
```

Restore the whole solution because a version change can rewrite the lock file of every project that references the changed project transitively. Restoring only the project Dependabot edited leaves those downstream files out of date.

## Versioning and releases

The five packages (`SlideRule`, `SlideRule.Analyzers`, `SlideRule.Xunit`, `SlideRule.Cli`, `SlideRule.Guidance.DotNet`) are the only packable projects. They share the root `Directory.Build.props` `<Version>`, currently 0.3.0. Pushing a `v*` tag starts the [release workflow](.github/workflows/release.yml). After removing the leading `v`, the tag must match `<Version>`, `.mcp/server.json`'s `.version` and `.packages[0].version`. The workflow builds, tests and packs, then verifies that the tool package contains an entry at exactly `.mcp/server.json`. It attests packages with signed build provenance through `actions/attest` and publishes to nuget.org using a short-lived key from OIDC trusted publishing. The GitHub release has generated notes, the packages and the attestation bundle. A second job waits up to 45 minutes for nuget.org's tool readme to contain `mcp-name: io.github.andypgray/sliderule`, which the MCP registry validates. It then runs `mcp-publisher login github-oidc` and `mcp-publisher publish .mcp/server.json`. [CHANGELOG.md](CHANGELOG.md) records the release history.

Under 0.x, a minor version may change the spec API, with a changelog entry and no shim, and a patch version never does. The check report, status burndown, graph survey, baseline file and model file each have a `schemaVersion`, which moves when a key is removed, renamed or given a new meaning. The hook document has none. Use the [command-line reference](CLI.md#documents-and-versions) for the document versions and [Operating SlideRule](OPERATING.md#upgrading) when upgrading a spec across a release.

## License

By contributing, you agree that your contributions are licensed under the project's [MIT License](LICENSE).
