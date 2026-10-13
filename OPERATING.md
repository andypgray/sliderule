# Operating SlideRule

This page is for a team whose spec is committed and green. It covers where the check runs, wiring it into CI, working a ratchet down to `Enforce`, rolling out the build analyzer, changing the spec, upgrading, and what to do when a run fails. The [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md) has every option, and [`hooks/`](https://github.com/andypgray/sliderule/tree/main/hooks) has the agent hook.

## Where the check runs

| Surface | Runs | A red rule |
|---|---|---|
| `sliderule check` | when you run it | exits 1 |
| the agent hook | when an agent's turn ends | blocks the stop |
| CI | on each push and pull request | fails the job |
| the build analyzer | in the editor and in `dotnet build` | a compiler warning |
| the xUnit adapter | in your test run | a failed test |

Every surface reads the same rules and the same baselines. The build analyzer reads the rules from the model file `render` writes, so it agrees with the others while that file is current. The adapter's failure text is the CLI's, and the build analyzer never reports a violation `check` would not, though it leaves some rules to `check`, as [its README](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md#what-the-build-checks-and-what-it-leaves-to-check) explains. `check` in CI is the gate, and the other surfaces report the same findings earlier.

A check reads your code from source, so an edit counts as soon as it is saved, built or not. It needs the solution restored, and it runs the spec from the spec project's last build, so rebuild the spec project after each change to the spec.

## CI

A pull-request job installs the tool, builds the solution, checks it, and fails on any stale rendered file. The check takes the pull request's base ref, from which the scope tripwires read the change:

```bash
dotnet tool install -g SlideRule.Cli --version <version>
dotnet build MyApp.sln
sliderule check --diff-base origin/main --sarif sliderule.sarif
sliderule render --check
```

Install the version the spec's `SlideRule` package reference names, so CI checks with the tool the spec was written against. The commands find the solution and the spec through a committed [settings file](https://github.com/andypgray/sliderule/blob/main/CLI.md#the-settings-file), or take them as arguments.

`check` exits 1 on a red rule, and 2 on SlideRule's own error, such as a spec error or a project that failed to load. `render --check` exits 1 when a rendered file is stale, and a card the spec now places where none is committed counts as stale. A card the spec no longer places is not compared, so delete it by hand as step 4 of [Changing the spec](#changing-the-spec) explains. `--diff-base` needs the base ref in the checkout, and on GitHub Actions a checkout with `fetch-depth: 0` provides it. A ref the checkout lacks fails the run with exit 2, and without `--diff-base` every scope's tripwire reports skipped.

Upload `sliderule.sarif` to code scanning to see each violation on the line that caused it. On GitHub, `github/codeql-action/upload-sarif` uploads it, with `security-events: write` on the job and `if: always()` on the step, so the alerts still upload when the check fails. A red rule becomes an error-level alert, and a tripwire touch a warning. Each baselined site becomes a note that quotes its baseline entry's reason, or names the baseline file when the entry has none. The xUnit adapter can run the same rules as named tests in the test step, for teams that want them in the test report.

This repository's own [CI workflow](https://github.com/andypgray/sliderule/blob/main/.github/workflows/ci.yml) runs these steps on every push to `main` and every pull request, with the tool built from source.

## Working a ratchet down

A `Migrate` rule moves toward `Enforce` in five steps:

1. Capture. `sliderule baseline init` writes a baseline for every ratcheted rule that has none yet. Commit it with the spec.
2. Hold. A new violation fails `check` and the adapter, blocks the hook's stop, and shows as a warning in builds that run the analyzer. The baselined sites stay quiet, and SARIF reports them as notes.
3. Watch. `sliderule status` prints each ratchet's burndown: the violations still baselined, the entries already fixed, and the rules ready for promotion. A ratchet's line reports its counts as `N grandfathered remaining, N new, N fixed awaiting acceptance`, with the site total after the first count when it is larger.
4. Shrink. `sliderule baseline accept-reductions` removes the fixed entries. Commit the smaller files.
5. Promote. Once a baseline is empty, `status` suggests `Enforce` by printing `0 remaining; promotable to Enforce (baseline is empty)`, and you change the rule's `.Migrate(from:, to:)` to `.Enforce(...)` with the same constraint. Drop any `.Baseline(...)` or `.WhileYoureThere(...)` too, which an `Enforce` rule does not take. Then delete the rule's baseline file.

The first two commands below are steps 3 and 4, and the next two paragraphs explain the other two:

```bash
sliderule status
sliderule baseline accept-reductions
sliderule baseline add --rule time/inject-clock --because "The report names its file by the wall clock" --source MyApp.Web.ReportsController --target System.DateTime.Now
sliderule baseline rename --from time/no-datetime-now --to time/inject-clock
```

`sliderule baseline add` records one violation on purpose, with the reason given in `--because`, for a site you accept knowingly. SARIF repeats that reason on the site's alert.

When a ratcheted rule's ID changes, `sliderule baseline rename --from <old> --to <new>` moves its baseline to the new ID. Until then, `status` lists the baseline under its old ID, and `check` names the rename.

Each baseline entry is sealed, so an edit by hand fails the next run. Restore the line from version control, and resolve a merge conflict in a baseline file by keeping whole entry lines from either side.

`sliderule baseline` is yours to run. A baseline turns a red rule green without fixing anything, so the hook settings deny the command to an agent, and the MCP server has no tool that writes one. Review a baseline change in a pull request as you would a code change.

## Rolling out the build analyzer

1. Turn on the model file with `sliderule render --model`, or with `"render": { "model": true }` in the settings file, and commit `sliderule.model.json`.
2. Add `SlideRule.Analyzers` to the projects the spec governs, from a `Directory.Build.props`, or with a `GlobalPackageReference` under central package management, at the tool's version.
3. Decide what a warning does. The analyzer reports warnings, so builds keep passing. A build that treats warnings as errors needs the IDs in `WarningsNotAsErrors`, and an `.editorconfig` can make `SR2001` an error one folder at a time. In CI, promote the analyzer's six IDs to errors by passing `-warnaserror:SR2001,SR2002,SR2090,SR2091,SR2092,SR2093` to the solution build. A broken rule or a model file the build could not read then fails the build.
4. Keep the default tier, `live`, in the builds a coding agent runs, which is where an agent reads the warning. `-p:SlideRuleTier=off` skips the analyzer in an inner-loop build.

The [analyzer's README](https://github.com/andypgray/sliderule/blob/main/src/SlideRule.Analyzers/README.md) has the details, including `packages.config` projects and what each editor shows.

## Changing the spec

A change to the rules follows one loop:

1. Edit the spec and build the spec project.
2. Run `sliderule check`, and `sliderule baseline init` for a new ratcheted rule.
3. Run `sliderule render`, and read its diff: the `AGENTS.md` block, the cards and the model file.
4. Delete any card in a directory the spec no longer covers. `render` never deletes one, so look in the directories a moved rule or scope left.
5. Commit the spec, its baselines and the rendered files as one change.

A merge conflict inside a managed block, or in the model file, resolves by rendering again from the merged spec. Resolve a conflict outside a block's markers by hand first. `render --check` in CI catches a rendered file that someone forgot to commit.

## Upgrading

The four packages ship one version together, so upgrade them in one change. Update the tool first:

```bash
dotnet tool update -g SlideRule.Cli --version <version>
```

Then set the same version on the spec project's `SlideRule` reference, and on `SlideRule.Analyzers` and `SlideRule.Xunit` wherever you use them. Before 1.0, a minor version can change the spec API with no shim, so read the release's Breaking entries in the [changelog](https://github.com/andypgray/sliderule/blob/main/CHANGELOG.md) first. Build the spec, run `sliderule render` so the model file comes from the new tool, and run `check`.

A half-finished upgrade shows in the build: a model file in a newer schema than the analyzer reads stops it with `SR2091`, and packages at two versions report `SR1093` or `SR2093`. A model file in an older schema is reported as `SR2091` too, until `render --model` rewrites it. The extraction cache starts over by itself after an upgrade, because a run never reuses a cache another SlideRule version wrote, and a baseline file in the previous format is read as before and upgraded on its next write.

## When a run fails

| Symptom | Cause | Fix |
|---|---|---|
| exit 1 | a rule is red | fix the code the report names |
| exit 2 | SlideRule's own error | read the message on stderr |
| `No spec project found` | no spec project, or a failed load or restore | add the project or pass `--spec`; after a failed load or restore, restore and build |
| `has no built output` | the spec project is not built | build it |
| `modelIncomplete`, failed projects | a project failed to load | build the solution, and read the load errors the run lists |
| a refusal naming `dotnet restore` | packages did not resolve | restore the solution |
| `failed its integrity check` | a baseline line edited by hand | restore the line |
| `SR2090` | no model file above the project | run `sliderule render --model` |
| `SR2091` | a model or baseline file the analyzer cannot read, or a model file in another schema | fix the file the warning points at; re-render an older model file with `sliderule render --model`, or update the package for a newer one |
| `SR1093`, `SR2093` | packages at two versions | align the versions |
| a stop passes unchecked | the hook timed out | raise the hook's timeout |

`--allow-workspace-diagnostics` checks the projects that did load. The check and status documents set `modelIncomplete: true` with or without it, so a partial green cannot pass for a whole one. Next to any load failure, `check` names the MSBuild install it used, and `SLIDERULE_VS_INSTALL_PATH` chooses another for non-SDK-style projects. The MCP server writes a daily log to the `logs` directory next to the default cache root, such as `%LOCALAPPDATA%\SlideRule\logs` on Windows.
