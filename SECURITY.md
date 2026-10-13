# Security

This document covers reporting a vulnerability in SlideRule, the tool's security model, and verifying the packages you install.

## Supported versions

Only the latest release on nuget.org receives security fixes.

## Reporting a vulnerability

Report privately through [GitHub security advisories](https://github.com/andypgray/sliderule/security/advisories/new). Do not open a public issue for a security report. You can expect an acknowledgment within 7 days.

## Security model

SlideRule is a local analysis tool, and its threat model follows from that.

- It runs with your user privileges: as the `sliderule` CLI, as a stdio MCP server your MCP client starts (`sliderule mcp`), or as the hook your agent runs (`sliderule hook`). It opens no network listener, makes no outbound calls and sends no telemetry. The xUnit adapter, `SlideRule.Xunit`, runs the same solution load inside your test process.
- Apart from the build files a solution load can rewrite under `obj`, it writes to your repository only when you run a command for that. `render` writes the managed `AGENTS.md` block, the per-directory `AGENTS.md` cards, and the diagram file that `--diagram` or the settings file names. It writes the model file `sliderule.model.json` when `--model` is passed or the settings file sets `render.model`, and any `render` rewrites that file once it exists. `baseline` writes the baseline files, and `check --sarif <path>` writes its report to that path. The MCP server has no tool that writes any of them, and the [hook settings](https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json) deny a coding agent the `baseline` command. The hook and `check --diff-base` run read-only `git` commands in your repository (`rev-parse`, `diff --name-only` and `status`), and so does `arch_check` given a `diffBase`.
- Outside the repository it keeps its own state. Most commands that load a solution keep an extraction cache under `%LOCALAPPDATA%\SlideRule\cache`, as the [command-line reference](https://github.com/andypgray/sliderule/blob/main/CLI.md#caching) describes, and `SLIDERULE_CACHE_DIR` moves it. A `--binlog` you pass is copied into that cache, unless `--no-cache` is passed too. A binlog can contain whatever the build recorded, environment variables included, so treat the cache with the care you give the binlog. A run that the extraction cache serves only in part writes a solution filter to the temporary directory and deletes it after the load. The hook keeps per-session state in the temporary directory, which `SLIDERULE_HOOK_STATE_DIR` moves. In a clone of this repository, the MCP server's [launcher](https://github.com/andypgray/sliderule/blob/main/hooks/README.md#why-a-source-checkout-needs-the-launcher) copies the CLI build to `~/.sliderule/mcp-run`, which `SLIDERULE_MCP_RUN_ROOT` moves.
- The MCP server writes a daily log to `%LOCALAPPDATA%\SlideRule\logs` (the local application data folder on other platforms) and keeps the 7 newest files. A log can contain absolute paths and rule and violation text read from your solution, and it stays on the machine. The extraction cache contains names and absolute file paths read from your code. The hook's state files contain the paths of the changed code files in your working tree and the last warnings the hook reported. Outside Windows, the temporary directory can be shared with other local users, so set `SLIDERULE_HOOK_STATE_DIR` to a private directory there.
- SlideRule never compiles your projects into assemblies, but loading a solution runs code from it. Give a solution the trust you give an IDE that opens it. MSBuildWorkspace evaluates the project files and runs a design-time build of each project. A design-time build runs the targets that lead up to compiling, and skips the compiler. Binding the compilations runs the solution's source generators in the SlideRule process. A `--binlog` replay loads the analyzers and generators the recorded build used, from their paths on disk. Each later run that replays the copy in the cache loads them too.
- The spec is a built assembly that SlideRule loads in a collectible `AssemblyLoadContext` and runs, either the one you name or the one it finds by convention. A spec runs with your privileges, so treat a spec from an untrusted source as you would any code you run.
- Two packages run code inside the compiler and the IDE. The `SlideRule` package adds a spec analyzer and a completion provider to the spec project, and `SlideRule.Analyzers` checks each project that references it against the model file. Each reads the compilation it runs in, and `SlideRule.Analyzers` also reads the model file and the baseline files in `arch/baselines` next to it. Neither package writes a file.

## Verify what you install

Each release ships a signed build provenance attestation covering all four packages. Download a `.nupkg` from the GitHub release and verify it as built:

```bash
gh attestation verify SlideRule.Cli.<version>.nupkg --repo andypgray/sliderule
```

The release also includes the Sigstore bundle (`attestation.intoto.jsonl`) for offline verification with `gh attestation verify --bundle`.

The nuget.org copy differs, because nuget.org adds a repository signature (`.signature.p7s`) after upload, and that changes the file's hash. The attestation therefore matches only the copy on the GitHub release. Use the GitHub release asset for digest verification, and `dotnet nuget verify <file>` for the nuget.org repository signature.

`dotnet tool install -g SlideRule.Cli` and, on a machine with a .NET 10 SDK installed, `dotnet dnx SlideRule.Cli` fetch the same nuget.org package. `sliderule --version` prints `<version>+<commit>`, and that commit matches the release tag on andypgray/sliderule, a source cross-check that needs no tooling.

## Supply chain

Publishing uses NuGet trusted publishing (OIDC), so there is no long-lived API key to store or leak. The release workflow builds, tests, packs and attests every package, and lists the release in the MCP registry with the same kind of OIDC token, with no stored registry credential. Builds use SourceLink and a deterministic CI configuration. Every GitHub Actions dependency is pinned to a commit SHA. Every NuGet dependency of the shipped projects is locked to a content hash in a committed `packages.lock.json`, which CI restores in locked mode. Dependabot proposes updates to both kinds of pin, and a NuGet update passes CI once its lock files are regenerated, as the [contributing guide](https://github.com/andypgray/sliderule/blob/main/CONTRIBUTING.md#dependency-updates) explains.
