# Examples

Six worked examples demonstrate one spec model with two render targets (deterministic enforcement
and generated agent context). Four are solutions, and CI builds each one with the
[build analyzer](../src/SlideRule.Analyzers/README.md) attached and runs `check` against its
committed baselines. CI also re-runs `render` to prove that the committed agent context and the
committed architecture drawings both still match the code. The other two walk a flow with captured
output. Three of the four postures (`Enforce` law, ratcheted `Migrate` debt, and `Quarantine`
containment) are spread across the set. The fourth, `Caution`, is
in [SlideRule's own spec](../arch/SlideRule.Architecture/SlideRuleArchitectureSpec.cs) instead.

Each of the four solutions ships an `ARCHITECTURE.md` with a pair of drawings that
`render --diagram` writes. One is a codebase survey of the projects and the references between them,
drawn from what the code does. The other is a law fence of the places the spec names and the
references it forbids, drawn from the spec.

All six share one fiction (Meridian, a freight-forwarding company), so the examples can
cross-reference one another as parts of one business.

- [Three of the four postures on one codebase](Meridian/): the `Meridian` monolith mid-migration.
  It has the law it keeps, three ratcheted migrations with their burndown, and a quarantined scope
  with dragons. Its [drawings](Meridian/ARCHITECTURE.md) put those three postures on one page as
  shapes. The shapes are a solid ban, two dotted arrows for the inline-SQL debt, and a box you can
  only enter through its facades.

- [Enforce-only clean architecture](Meridian.Quoting/): the greenfield `Meridian.Quoting`
  subsystem. It shows the generated `AGENTS.md` block next to the spec that produced it, and every
  rule runs as an individually named xUnit test. Its [drawings](Meridian.Quoting/ARCHITECTURE.md)
  are the tidiest law in the set, and only two of its nine rules have an arrow to draw. The other
  seven are about names and shapes, which have no direction.

- [Module isolation as law](Meridian.Operations/): the `Meridian.Operations` modular
  monolith. A scoped rules card is rendered into every module directory, and one module is
  quarantined behind its facade. Its [drawings](Meridian.Operations/ARCHITECTURE.md) are a
  two-node survey next to a ten-node law, because the modules are namespaces and only the spec
  names them.

- [Microsoft guidance, enforced and cited](Meridian.Interchange/): the `Meridian.Interchange`
  outbound worker. Every rule cites the learn.microsoft.com page it enforces. The example shows the
  canon sentence, spec excerpt, and real violation, rule by rule. Its
  [drawings](Meridian.Interchange/ARCHITECTURE.md) show the limits of a drawing. Canonical guidance
  is almost entirely non-spatial, so 11 of the 12 rules have no arrow to draw and are listed
  under the fence.

- [Day-one adoption on an existing codebase](Meridian/ADOPTING.md): the `Meridian/ADOPTING.md`
  walkthrough. It covers the full derive flow from real code to a first spec, and every step is a
  real command with real output.

- [The agent loop, closed by a hook](Meridian/hooks/): the `Meridian/hooks` showcase. A Claude
  Code hook runs `check` when the agent's turn ends, so new code in a retired pattern goes red at the
  moment of creation. The storyboard walks one task from the old pattern to self-correction.

Read them in list order. Later pages build on earlier ones, and each ends with cross-links to
the examples it connects to.
