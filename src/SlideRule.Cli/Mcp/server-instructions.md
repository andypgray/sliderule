SlideRule exposes this codebase's architecture spec, one reified model enforced and explained, as five read-only tools.

- `arch_check` — the spec's verdict as a JSON report. Call before finishing work that touched architecture-relevant code; narrow with `rules` globs.
- `arch_status` — the migration burndown: per-rule grandfathered/stale counts and promotion suggestions.
- `arch_explain` — a rule's because / fix / posture; or, given `type` or `path`, who claims a type and why.
- `arch_context` — the scope card(s) covering a path. Call before editing an unfamiliar directory to learn its rules or whether it is dragon territory.
- `arch_graph` — the codebase survey: projects, namespace inventories, reference edges, who declares what. Needs no spec — orient on an unfamiliar solution or plan new rules; narrow with `grain` or `projects`.

One prompt, `derive_spec` — the onboarding recipe to run when spec resolution reports no spec project yet.

- Documents are the CLI's `--json` output byte for byte: camelCase, optional fields absent not null, check entries keyed by `id` (`ruleId` is SARIF-only).
- The server is bound to one solution + one spec at start; the tools take no solution argument.
- The first call loads the workspace; later calls reconcile it. Code is read from source, built or not; the spec from its project's last build, so rebuild that after a spec edit.
- Drill down with `arch_explain <rule-id>`; the always-on summary lives in the root `AGENTS.md` managed block.
