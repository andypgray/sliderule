# SlideRule — the spec language (grammar)

## 0. Authority and how to read this document

This document is the **canonical specification of the fluent surface** — the "language" spec
authors write. It pins what a spec sentence may say, what it reifies to, and what prose it
renders. The four dialect decisions in §1 are ratified; §9 records the deliberate divergences
from the prior art.

The pinned tests are the enforcement of this document: **the fluent surface may only change
together with this document and its pinned tests.** When a pin must move, move it deliberately
as part of the change (house test culture).

## 1. Ratified dialect decisions

1. **Modal `Must*` constraint verbs** with lexical polarity — not ArchUnit-literal
   `That()/Should()`.
2. **Split escape hatches**: `.Where(pred, description:)` in selector position, `.Must(pred,
   description:)` in constraint position; the mandatory-description semantics are locked.
3. **`Because` is required** (spec-build error when missing); `Fix` is optional.
4. **Posture granularity: dual surface, single model** —
   `Rule().Enforce/Migrate(...)` and `Scope().Quarantine/Caution(...)` on the surface; a scope desugars
   to ordinary posture-bearing rule nodes (§7); checker/renderer/baseline walk ONE rule model.

## 2. Design principles

1. **Rules are data.** The fluent API builds a walkable model; nothing on a rule executes.
   There is no `Check()`/`GetResult()` terminal anywhere in the grammar.
2. **Every vocabulary member renders.** Each combinator carries a deterministic prose
   fragment, pinned by string-equality tests. The admission rule (§10) forbids members that
   cannot self-describe.
3. **Polarity is lexical.** Negation lives in the verb name (`MustNotReference`); there is no
   `Not()` combinator, no `ShouldNot()` gate, no `noTypes()` prefix. Double negation is
   inexpressible by construction.
4. **Fragments compose without conjugation.** Modal "must" is number-invariant in English, so
   singular subjects ("the Domain layer") and plural subjects ("types implementing X") take
   the same verb fragments. Adjectives are participial/prepositional phrases that append as
   reduced relative clauses.
5. **Selections and constraints are first-class values** — assignable to variables, returnable
   from helpers, buildable in loops. The grammar never requires a chain to complete in one
   expression. An operand list is buildable the same way: the calls a helper's list feeds take it
   as one sequence (§3.3), and an empty one is reported at spec build (§8 item 36).
6. **Compile-enforce the big structure; validate the fine rules; throw on what is neither.**
   Stage types make illegal sentence *shapes* uncompilable; spec-build validation reports every
   remaining error at once (§8) so an agent fixing a spec sees all problems in one pass. Between
   those two sits a third tier for arguments nobody could have written on purpose — a `null` where
   a string, a type, a selection or a lambda belongs, a `null` `params` array or sequence, or a
   `null` inside either, an enum value outside the ones declared. Those throw the ordinary
   `ArgumentNullException` / `ArgumentOutOfRangeException` at the call, naming the parameter the
   caller wrote. The split is
   by kind, not by slot: `arch.Namespace(null!)` throws and `arch.Namespace("")` is collected as
   item 15, because a blank is a claim about the codebase that belongs in the one report with a
   `file:line` beside every other, while a null is a defect in whatever code built the spec and has
   no spec location to point at. An undefined `Lifetime` is the one deliberate exception, left in
   the catalog (item 19) because its message also names the rule that used it, which a throw at
   `arch.Registered(...)` could not.
7. **Honesty is pinned.** Where a verb's semantics have a boundary (the `MustOnly*` reference
   universe, §4.1), the rendered fragment states the boundary. Escape hatches without
   descriptions do not compile. The rendered block carries two kinds of prose, and the claim is
   exact for one of them: a generated sentence (the law, a module-map row's name and globs, a
   sanctioned surface) is *true of the code* whenever the checker is green; authored prose
   (`Because`, `Fix`, a Migrate's `from:`, `Dragons`, a layer's `Purpose`, a rule's `Citation`)
   is *current with the spec*, rendered verbatim beside the sentence or definition it glosses,
   re-rendered with it, and checked by nothing. A `Citation` is the one authored field the spec
   build looks inside, and only far enough to know it is a URL (§8 item 30); whether the page
   still says what the rule claims is not a thing this grammar can know. An `Exemplar` is the one
   authored pointer `check` verifies. It is a selection rather than prose, so it resolves against
   the solution as a subject does, and a part of it that matches no declared type raises a warning
   on the rule (§5.5).

## 3. The grammar

### 3.1 Statement forms

A spec is a class implementing `IArchitectureSpec` with one method, `Define(Arch arch)`.
Inside it there are exactly three statement forms:

```
definition :=  var x = arch.Layer(name, glob, globs...)[.Purpose(prose)]
             | arch.Layer(name, selection)[.Purpose(prose)] | arch.Namespace(glob)
             | arch.Project(name) | arch.Type(typeof(X)) | arch.Type<X>()
             | arch.Registered(lifetime) | arch.Registered()
             | arch.AnyOf(selection, selections...) | arch.AnyOf(typeof(X), types...)
             | arch.AnyOf(selection-list) | arch.AnyOf(type-list)
             | arch.Each(layer, layers...) | arch.Each(project-selection)
             | arch.Member(typeof(X), nameof(X.M))
             | arch.Member<T>(x => x.M) | arch.Member(() => X.M)
rule       :=  arch.Rule(id) . posture-verb . trailer*
scope      :=  arch.Scope(id) . Quarantine(selection) . quarantine-clause* . trailer*
             | arch.Scope(id) . Caution(selection) . caution-clause* . trailer*

posture-verb :=  Enforce(constraint)
              |  Migrate(from: prose, to: constraint) [.Baseline(path)] [.WhileYoureThere(policy)]
constraint   :=  selection . modal-verb(target...)          — a complete, reified sentence
             |   member-selection . member-modal-verb(...)  — a member-shape sentence (§4.6)
selection    :=  noun [. adjective]*                        — an immutable, reusable value
member-selection := selection . projection [. member-adjective]*   — a member subject (§4.6)
projection   :=  Members | Methods | Properties | Fields | Events
member       :=  arch.Member(typeof(X), nameof(X.M))        — a leaf value, NOT a selection (§4.5)
             |   arch.Member<T>(x => x.M)                   — a typed instance-member anchor (§4.5)
             |   arch.Member(() => X.M)                     — a static-member anchor (§4.5)
quarantine-clause :=  BoundaryOnlyVia(selections...) | BoundaryOnlyVia(types...)
             |   Dragons(prose) | DragonsDoc(path) | Baseline(path)
caution-clause :=  Dragons(prose) | DragonsDoc(path)
trailer      :=  Because(prose) | Fix(prose) | Citation(uri) | Exemplar(selection)
```

Modal-verb targets are selections for the dependency verbs (§3.3) and members for the
member-access verb `MustNotUse` (§4.5). Selection-valued *operands* are a different position:
`MustBelongTo`'s memberships and `MustHaveExactlyOneCounterpart`'s `among:` are selections
resolved in subject position, naming where a subject may live or where its counterpart may
stand — never the far end of an edge. A *family* — `arch.Each` over declared layers or over a
project selection (§5.1) — is a selection whose noun carries a partition into cells; it may
stand only as a rule subject, directly or as the source of a member projection, never in an
operand position (§8 item 27). The member-modal verbs (§4.6) take no target — they
are shape/naming assertions over the projected member set, so `member-selection` is itself the
whole subject side of a member-shape sentence. The one exception is the methods-only
`.MustAcceptParameter` (§5.7), whose type anchor renders on the verb side of the
sentence.

### 3.2 Stage machine

```
Arch
 ├─ .Layer(name, string glob, params string[] more) → Layer (: Selection; .Purpose(prose) → Layer)
 ├─ .Layer(name, Selection definition)             → Layer (the layer names what the definition
 │                                                    names; ProjectSelection is not a Selection,
 │                                                    so the artifact stratum cannot be a layer)
 ├─ .Namespace(glob)  → Selection
 ├─ .Project(name)    → Selection
 ├─ .Type(Type)       → Selection      (single type; there is deliberately no
 │                                      arch.Types(params Type[]) — it cannot coexist with
 │                                      the arch.Types property (CS0102); the multi-type noun
 │                                      is .AnyOf(Type, params Type[]) below)
 ├─ .Type<T>()        → Selection      (generic sugar: ≡ .Type(typeof(T)); §5.2 note)
 ├─ .AnyOf(Selection first, params Selection[] more) → Selection
 │                                     (the union of its operands; §5.1)
 ├─ .AnyOf(Type first, params Type[] more) → Selection
 │                                     (sugar: ≡ .AnyOf(.Type(a), .Type(b), …) — the
 │                                      multi-type noun)
 ├─ .AnyOf(IEnumerable<Selection>) / .AnyOf(IEnumerable<Type>) → Selection
 │                                     (the same unions over a computed list; §3.3)
 ├─ .Registered(Lifetime) → Selection  (types named in a source-visible container
 │                                      registration with that lifetime — service and
 │                                      implementation alike; §4.7)
 ├─ .Registered()     → Selection      (same, any lifetime; §4.7)
 ├─ .Member(Type, string) → Member     (member-access leaf, target-only — NOT a Selection:
 │                                      adjectives and modal verbs must not apply; §4.5)
 ├─ .Member<T>(Expression<Func<T, object?>>)  → Member  (typed instance value-member anchor; §4.5)
 ├─ .Member<T>(Expression<Action<T>>)         → Member  (typed instance void-method anchor; §4.5)
 ├─ .Member(Expression<Func<object?>>)        → Member  (static value-member anchor; §4.5)
 ├─ .Member(Expression<Action>)               → Member  (static void-method anchor; §4.5)
 ├─ .Types            → Selection      (all solution-declared types)
 ├─ .Projects         → ProjectSelection (all solution-declared projects, as build
 │                                      artifacts — the third subject stratum; §4.10)
 ├─ .Rule(id)         → IRuleBuilder   (registers the node immediately)
 └─ .Scope(id)        → IScopeBuilder  (registers the node immediately)

Selection    — adjectives → Selection; modal verbs → Constraint (terminal);
               projections (.Members / .Methods / .Properties / .Fields / .Events)
               → MemberSelection (§4.6)
MemberSelection — member adjectives (.WithSuffix / .WithPrefix / .WithNameMatching /
               .Named / .AttributedWith / .ThatAreStatic / .ThatArePublic and the
               other accessibility adjectives / .Except / .Where) → the SAME concrete
               member-selection type, .Named and .Except also over a computed list
               (§3.3); member modal verbs → Constraint (terminal)
MethodSelection — a MemberSelection minted by .Methods that additionally offers
               .Returning(Type first, params Type[] more) /
               .Returning(string first, params string[] more) → MethodSelection (§4.6)
               and .MustAcceptParameter(Type) / (string) / <T>() → Constraint
               (terminal; §5.7)
PropertySelection — a MemberSelection minted by .Properties that additionally offers
               .MustBeGetOnly() → Constraint (terminal; §5.7)
FieldSelection — a MemberSelection minted by .Fields that additionally offers
               .MustBeReadonly() → Constraint (terminal; §5.7)
ProjectSelection — project adjectives (.Named / .Matching / .Packable / .Except / .Where)
               → ProjectSelection, .Named and .Matching also over a computed list (§3.3);
               project modal verbs (MustOnlyTarget / MustTarget /
               MustReferenceNoPackages / MustOnlyReferencePackages / MustNotReferencePackages /
               MustSetProperty / MustLockPackages / MustNotBePackable / .Must) → Constraint
               (terminal; §4.10, §5.8)
IRuleBuilder — ONLY .Enforce(Constraint) → IEnforceRule | .Migrate(from:, to:) → IMigrateRule
IEnforceRule — .Because / .Fix / .Citation / .Exemplar(Selection)
IMigrateRule — .Because / .Fix / .Citation / .Exemplar(Selection) / .Baseline(path)
               / .WhileYoureThere(MigrationPolicy)
IScopeBuilder — ONLY .Quarantine(Selection) → IQuarantinedScope | .Caution(Selection) → ICautionedScope
IQuarantinedScope — .BoundaryOnlyVia(Selection first, params Selection[]) / .BoundaryOnlyVia(params Type[])
               / .Dragons(prose) / .DragonsDoc(path) / .Baseline(path) / .Because
ICautionedScope — .Dragons(prose) / .DragonsDoc(path) / .Because
```

Structural consequences, all deliberate:

- A rule cannot exist without an ID (it is the anchor argument) or without a posture (the
  only methods on `IRuleBuilder` are posture verbs). Rule anatomy is enforced by the grammar.
  A scope likewise: `IScopeBuilder` carries only its two posture verbs, and `ICautionedScope`
  carries no `BoundaryOnlyVia`, `Baseline` or `Fix` — a caution has no boundary and nothing to
  grandfather, so those clauses are uncompilable on it by construction, not validated away.
- Trailers exist only on posture stage types and on the layer definition —
  `arch.Rule(id).Because(...)` still does not compile. `.Purpose(prose)` is the one trailer a
  definition carries; it returns the same `Layer`, so a described layer is the selection it was.
- Register-on-anchor: a dangling `arch.Rule("x");` is caught by validation (§8 item 2), not
  silently dropped.
- **`Selection` and `Constraint` are closed class hierarchies** — abstract classes with
  `private protected` constructors. Foreign nodes cannot enter the model, so every node is
  walkable and renderable by construction. Stage interfaces stay thin; vocabulary ships as
  extension methods where growth is expected (the FluentValidation shape — evolvable and
  netstandard2.0-safe).
- Fresh-instance contract: one model build = one fresh spec instance + one fresh `Arch`.
  Reusing a `Selection` minted on another `Arch` is a validation error (§8 item 10).
- **`Member` is a leaf, not a `Selection`.** It never enters the selection hierarchy, so
  adjectives and modal verbs are uncompilable on it by construction — a member names a thing
  to ban, not a set of types to constrain. It is Owner-stamped like a selection, and the
  fresh-instance contract covers it (§8 item 13).
- **`MemberSelection` is its own closed hierarchy, disjoint from `Selection`.** A projection
  turns a type selection into a member selection; the two hierarchies share no base, so the
  member adjectives/verbs and the type adjectives/verbs never collide on overload resolution
  (a `.WithSuffix` call binds to the type-side or member-side vocabulary purely by receiver
  type). Member adjectives are generic self-type extensions, so a chain preserves its concrete
  type: `.Methods.Returning(...).WithSuffix(...)` is still a `MethodSelection`, and
  `.Returning` stays reachable in any adjective order. Each kind-only member of the vocabulary
  lives on the projection type that mints it — `.Returning` and `MustAcceptParameter` on
  `MethodSelection`, `MustBeGetOnly` on `PropertySelection`, `MustBeReadonly` on
  `FieldSelection` — so calling one off any other projection is uncompilable by
  construction — a structural consequence, not a validated one.
- **`ProjectSelection` is the third closed hierarchy, disjoint from both.** `arch.Projects`
  names build artifacts where `arch.Project(name)` keeps naming the types a project declares
  (§5.1) — the `Types`-property/`Type`-method coexistence, one stratum up. Shared vocabulary
  names (`.Except`, `.Where`, `.Must`) bind purely by receiver type, and a project constraint
  has no type-side subject to offer: its subject slot is null exactly there, so every
  type-side reader dispatches on the constraint's stratum before reading one (§4.10).

### 3.3 Dependency-verb overloads (pinned)

```csharp
Constraint MustNotReference(Selection first, params Selection[] more)
Constraint MustNotReference(Type first, params Type[] more)      // sugar for arch.Type(...)
```

Same shape for `MustOnlyReference`, `MustNotBeReferencedBy`, `MustOnlyBeReferencedBy`, the
constructor-ban verb `MustNotConstruct`, the injection-ban verb `MustNotInject`, the
exception verbs `MustNotCatch`, `MustNotCatchUnfiltered`, `MustNotSwallow`, `MustNotThrow` and
`MustOnlyThrow`, and the signature-exposure verb `MustNotExpose`: each carries the identical
pair — a `Selection`
list and the `Type` sugar — and deliberately **no expression overload**. Constructor-ness lives in
the verb, not the anchor, so ordinary selections name what may not be `new`ed
(`arch.Types.Implementing(typeof(IHandler<>))`), which scales to "all registered services" where a
per-constructor anchor would be dummy-argument noise. Injection-ness lives in the verb the same
way, and its natural operands are the registration-fact selections
(`arch.Registered(Lifetime.Scoped)`, §4.7), though any selection or bare type works.
Catch-ness and throw-ness live in the verb the same way again: the operands name exception
types, most often through the bare `typeof` sugar (`MustNotCatch(typeof(Exception))`); their
edge semantics are §4.8. The `when`-filter condition on `MustNotCatchUnfiltered` rides the verb
for the same reason and a sharper one — a filter is not a type, so no operand could name it.
The same holds for the rethrow condition on `MustNotSwallow`: a terminal `throw` is not a type
either, and the verb reads both conditions together.
Exposure-ness lives in the verb the same way: the operands name the types that may not surface
in a public signature position, most often through the bare `typeof` sugar
(`MustNotExpose(typeof(DataTable))`); its edge semantics are §4.9. The
`(first, more)` shape makes zero-argument calls **uncompilable** and keeps single-argument
overload resolution unambiguous. A list computed at run time goes through the sequence forms
instead (**Computed lists**, below), where an empty list is §8 item 36. Mixing selections and
types in one call = wrap the type: `MustNotReference(web, arch.Type(typeof(SqlConnection)))`. The same `(first, more)` shape
applies to `Layer(name, glob, more)` — a layer with zero globs is uncompilable — and to
`Each(Layer first, params Layer[] more)` — a family with zero cells is uncompilable, and its cells
are typed to `Layer`, so an adjective-bearing cell, a union cell or a nested family is a compile
error rather than a §8 item; the project form takes one `ProjectSelection`, whose `.Named` and
`.Matching` already say "these".

The member-access verb (§4.5) carries the same shape over `Member` targets — a zero-member
call is uncompilable:

```csharp
Constraint MustNotUse(Member first, params Member[] more)
```

and, as pure authoring sugar for the static anchor forms, the same shape over static-member
lambdas (each desugars at mint through `arch.Member(() => …)` to the identical leaf):

```csharp
Constraint MustNotUse(Expression<Func<object?>> first, params Expression<Func<object?>>[] more)  // () => Type.M  /  () => Type.M()
Constraint MustNotUse(Expression<Action>      first, params Expression<Action>[] more)           // () => Type.M()  (void)
```

Member targets are `arch.Member`-anchored — `typeof` + `nameof`, or the expression forms
`arch.Member<T>(x => x.M)` / `arch.Member(() => X.M)` (§4.5) — so a
banned member participates in compilation and refactoring like every other spec reference; the
expression forms additionally let the compiler check the type↔member pairing that `typeof` +
`nameof` leaves unverified. There is no string-FQN member overload (growth, §11). The
anchoring policy every string form follows (§5.2): `typeof` whenever the spec project can
compile against the anchored type — a `typeof` participates in compilation and refactoring,
and nothing checks a string — and the string only when it cannot.

The dependency verbs carry **no** generic twin: the pinned `(first, params more)` shape has no
generic form (a variadic type-argument list is inexpressible), and mixing a bare type into a
selection list is served by wrapping it — `MustNotReference(web, arch.Type<SqlConnection>())`.
The generic sugar exists only where a single type is the whole argument: `arch.Type<X>()` and
the `Implementing` / `DerivedFrom` / `AttributedWith` hierarchy adjectives and their `Must*`
twins (§5.2, §5.3).

The member-access verb has **no instance-form verb twin and no cross-form mixing**: a
`MustNotUse<T>(x => x.M)` twin would drag the type argument onto the verb, and one call cannot
name two declaring types — instance members stay `arch.Member<T>(x => x.M)`. A static value
and a static void target are disjoint delegate shapes (`Func<object?>` vs `Action`) that no
single overload spans, so one call mixes neither the two static forms nor a static with an
instance — wrap the odd target with `arch.Member` and pass the uniform `Member` list, exactly
as a mixed selection/type list wraps the type (above). The sugar applies only where the whole
list is static and one form: `web.MustNotUse(() => DateTime.Now, () => DateTime.UtcNow)`.

**Computed lists.** Seven calls also take their operands as one sequence, for a spec that builds
the list in a helper or a loop: `arch.AnyOf(IEnumerable<Selection>)` and
`arch.AnyOf(IEnumerable<Type>)`, project `.Named` and `.Matching(IEnumerable<string>)`, member
`.Named(IEnumerable<string>)` and `.Except(IEnumerable<MemberSelection>)`, and
`MustNotUse(IEnumerable<Member>)`. Each builds the node its `(first, more)` sibling builds from the
same operands, so the model and the sentence are identical. A selection position takes a computed
list as one operand, `arch.AnyOf(list)`. The sequence is read once, at the call, so a lazy query
runs once and a list changed afterwards does not change the spec. A null sequence or a null entry
throws at the call (§2 principle 6). An empty sequence compiles where the `(first, more)` shape
would not, and is reported at spec build as §8 item 36. `IEnumerable<T>` is covariant, so a
`List<Layer>` binds `AnyOf` and a `List<MethodSelection>` binds member `.Except` with no cast. A
single operand still binds the `params` form. Where C# has collection expressions (12 and later),
`arch.AnyOf([])` is ambiguous between the two union forms (CS0121), and `arch.Projects.Named([])`
binds the sequence form and reaches item 36. The types such a helper takes or returns (`Selection`,
`Layer`, `MemberSelection`, `MethodSelection`, `PropertySelection`, `FieldSelection`,
`ProjectSelection`) are in the root namespace, so it compiles under `using SlideRule;` alone. The
other `(first, more)` lists have no sequence form yet (§11).

## 4. Pinned semantics

### 4.1 Reference universe (per position)

- **Subjects** range over **solution-declared types** — the set extraction walks. That walk applies
  a **type-side screen**, the twin of the member inventory's (§4.6): out go the implicitly declared,
  the types no C# source can name, and anything outside the v1 `TypeKind` set. Generator output
  stays in. A generated partial is explicitly declared and its name is writable, so
  `arch.Project("MyApp.Cli")` names the regex types a `[GeneratedRegex]` method puts in that
  assembly, and a top-level-statements `Program` is a subject like any other (its name is writable
  and it carries real reference, catch and throw edges). **A project noun names what the assembly
  declares, generated types included** — a pinned boundary, not an oversight. Screening them here
  would also strip them from target position and from the codebase graph, and they would come back
  as *external* nodes the moment anything referenced them. `.Authored()` (§5.2) is the opt-in
  narrowing for a law a generator's output cannot be expected to satisfy. Because the boundary is
  silent by construction, both surfaces state it: the codebase survey counts generated types per
  project and per namespace, and `check` reports how many of a rule's subject were generated
  whenever any of it was.
- **Targets** range over **all referenced types, including metadata references** —
  `MustNotReference(typeof(SqlConnection))` and `arch.Namespace("System.Web.*")` as a target
  both work against BCL/NuGet types.
- **One fully-qualified name can mean two types.** A project declares a name that a referenced
  assembly also supplies: a stand-in written under a package's own namespace, a polyfill under
  a BCL one. The model carries both — the source declaration, and a shallow external attributed
  to the supplying assembly — and every reference resolves to whichever the referencing
  compilation actually bound. So a project that references the package reaches the package's
  type and never the declaring project's, on the edge axes and in the hierarchy alike. A rule
  naming the type reaches both; `arch.Project` is what tells them apart, because the external
  one carries the supplying **assembly**'s name, so a project selection over the declaring
  project reaches the declaration alone. Nothing else splits: a type several projects declare
  still means one node (the next bullet states its membership), and a name no project declares
  is one node as before. `check` reports the split
  as an advisory note, and the codebase survey states it as a coverage key.
- **One type can be declared by several projects.** A linked `<Compile Include>`, shared source
  or a source-only package compiles one file into each of them, and a copied helper or a
  hand-written polyfill gives each project a file of its own under the same name. Either way the
  model carries **one node** for the type, and its member inventory and hierarchy follow the
  first declarer in ordinal project order. Membership is N-way: `arch.Project` naming **any**
  declarer contains the type — as a subject, as a target operand, in a member selection, inside
  a union. Edges add attribution on top. One model edge stands for one instance per project
  whose compilation makes the reference, and extraction records each instance as two projects.
  The first is the project that made the reference. The second is the project whose copy of the
  target it reached: its own copy where it compiles one (intra-project), and otherwise the copy
  of the declarer whose assembly it bound. Copies that have drifted apart therefore answer for
  their own references only, and a project that references one declarer reaches that declarer's
  copy. One rule then covers every reference verb at either end. The **subject** bounds which of
  those instances the rule owns, at whichever end it sits; the **operand** is the predicate at
  the far end, asked at the project that instance reached. A `MustNot*` verb is violated by an
  owned instance whose predicate holds, a `MustOnly*` verb by an owned instance whose predicate
  fails. So a declarer's reference to its own compiled-in copy never satisfies a ban aimed at
  another declarer, and a shared type's outward edge answers to each declarer whose copy makes
  it, separately. A selection names a node **at** a project when it matches the node and either
  one project declares it, or a head that is not a project admitted it, or that project is among
  the ones its project-headed selections spelled. Heads that are not projects therefore stay
  attribution-insensitive: `typeof`, `arch.Namespace`, `arch.Types`, `arch.Registered`, and
  `MustNotUse`'s string-keyed member anchors, so `MustNotReference(typeof(T))` reds even on a
  project's own copy. A layer takes its definition's stance: the glob form is
  attribution-insensitive like the globs it is made of, and a layer defined as a project noun
  names its nodes at that project, exactly as the bare noun would. A family (`arch.Each`, §5.1)
  takes each cell's stance: a family of layers names a node wherever the cells holding it do,
  and a family of projects names its nodes at each project, exactly as the bare project nouns
  would — which is what lets a type compiled into two cells, project cells or layers defined by
  those projects, be judged at each (§5.1). `MustOnly*` allows its own subject (below), and the
  subject is an allow entry like any other here: an intra-copy edge is
  satisfied by an entry naming the compiling project, by a non-project entry containing the
  type, or by the subject at the projects it names the node at. Where the edge runs between two
  shared types that leaves an entry naming the compiling project, the subject included when it
  names one, and nothing else. `Except` subtracts by node identity:
  `arch.Project("A").Except(arch.Project("B"))` removes a co-declared node from A's selection
  too. Read as "A's copy but not B's" it is a trap — it deletes every shared type from A,
  and the way to speak about one declarer's instances is to anchor the subject on that
  project. A violation cites the sites of the owned instances that violate the rule: anchored
  on one declarer it prints the lines that declarer's copy makes, under a subject naming the
  node at every declarer it prints the lines of every copy that violates, and a linked file's
  copies share every line, so its violations print the same whichever declarer owns them. Over
  a family the pair is minted once (below), citing the lines of every cell whose owned instance
  violates. Where nothing is multiply declared this all degenerates to the single-attribution
  reading, byte for byte. One documented approximation remains: where the assembly a reference
  bound does not identify a single declarer, as when two projects build under one assembly
  name, the reference reaches the first declarer's copy. The attribution is disclosed
  three ways: `check`'s advisory merge note, the survey's `multiplyDeclaredTypes` coverage
  statement, and the winning node's `AlsoDeclaredBy` roster. The survey's `projectEdges` reads
  the same instances the verdict does, so a pair it does not render is a pair no rule will red,
  and its `externalEdges` count each external reference at every project that made it. The
  node also keeps which project compiled each declaring file, and scoped placement (§6) counts
  the files of the projects a selection names the node at.
- **One project file can mean several compilations.** A multi-targeting project compiles once
  per framework; extraction walks them all, and the model unions them under the one project
  name. The union is lossless — types, references and solution membership all survive — except
  for the member inventory and hierarchy of a type more than one framework declares: those come
  from the first framework extracted that declares it, and extraction consumes frameworks in
  ordinal order of the framework name, never the csproj's declaration order. Its references are
  the union of every framework's compilation, like every edge in the model. So on
  `net48;net6.0;net8.0;netstandard2.0` a type `net48` declares takes its members and hierarchy
  from `net48`, while a type under `#if NET`, which only `net6.0` and `net8.0` declare, takes
  them from `net6.0`: a member, base type or interface only a later framework's `#if` branch
  declares is not in the model, and a rule about such a type's members or hierarchy is checked
  against the winning framework's compilation alone, while a reference only that branch makes is
  in the model and a ban on it reds. A framework-exclusive type (one only a single framework
  declares) keeps that framework's facts. The codebase survey lists each multi-targeted project's
  frameworks in extraction order; for a project targeting two it names the framework the shared
  types follow, and past two it states the rule over the list it prints, that each shared type
  follows the first of them that declares it. `check` stamps each project's frameworks and the
  first framework a shared type follows, beside an advisory note naming every framework a shared
  type follows wherever two frameworks actually collapsed a type.
- **`MustOnly*` complement universe = solution-declared types — for the *reference* verbs.**
  BCL/NuGet references are
  exempt, and the fragment states it: *"must reference only {list} (external packages are not
  constrained by this rule)"*. Without this pin every `MustOnlyReference` rule is either 100%
  violated (`System.String`) or renders a false sentence — both fatal to "the prose is
  provably true". `MustOnlyThrow` diverges deliberately: no type must *throw* a BCL
  exception, so the exemption's rationale never applies — the verb is strict and its
  fragment carries no caveat (§4.8).
- **`MustOnly*` allows the subject implicitly.** A subject's reference to another member of its
  own selection is permitted. "Self" is the **refined** subject — the membership the rule ranges
  over, after `Except` — evaluated as one more allow entry, so it narrows with the subject and
  carries the at-project naming above.
  `arch.Types.WithSuffix("Controller").MustOnlyReference(domain)` is therefore a law and not a
  tautology: a Controller may reach the Domain layer or another Controller, and nothing else.
  Listing the subject explicitly stays legal and renders as written, which is how the Quarantine
  `{id}/containment` desugaring spells its allowed set (§7), its hermetic branch needing an
  operand to name. The subject-only shape has a verb of its own, `MustOnlyReferenceItself()`
  (§5.3). (Self-edges never arise — extraction drops them.)
- **On a family, self is the cell as declared.** Over an `arch.Each` subject (§5.1) the four
  `MustOnly*` reference verbs — `MustOnlyReference`, `MustOnlyReferenceItself`,
  `MustOnlyBeReferencedBy`, `MustOnlyBeReferencedByItself` — read "self" per cell: the cell the
  owned instance sits in, as declared — the whole layer or the whole project, not the family's
  refined membership. A plain selection declares no unit, so its self can only be what it
  resolved to; a family declares its units, and the unit is what "own" means — a module's
  `Contracts` cone, excluded from the subject by `Except`, still counts as that module's self.
  Evaluation is the per-cell desugar in one rule: for each cell C, the subject is C ∩ the
  family's membership, the allow-set is the operands ∪ C as declared, and a violation is minted
  once per (source, target) pair whichever cell says so, citing the lines of every cell's owned
  instance, so the baseline keys exactly as it always has. `MustNotReferenceEachOther()` (§5.3)
  reads the same cells with the other polarity:
  an owned instance whose far end lies in any *other* cell, as declared, is a violation.
  `MustNotHaveCircularReferences()` (§5.3) reads the cells as a graph: an arrow from cell A to
  cell B for every owned instance whose far end lies in B, and a violation for every pair on an
  arrow inside a strongly connected component of that graph (§4.3) — the cells may reference
  each other, but not in a circle.
- `MustOnlyBeReferencedBy` needs no caveat: only solution types can be observed referencing.
- Checker behavior: an empty *subject* selection **fails** the rule by default
  (ArchUnit and ArchUnitNET precedent, with a pinned message). For a **union** subject the
  default sharpens per operand: every operand must match at least one type, and each empty one
  fails the rule in its own right with the operand named, so a typo'd operand is never masked by
  its siblings (§5.1). A **`Named` list** on the subject sharpens the same way per name,
  because `X.Named("A", "B")` names exactly what `arch.AnyOf(X.Named("A"), X.Named("B"))` names:
  where the subject matched something, each listed name no type of it carries fails the rule
  with the same message as an empty union operand, quoted as the subject would read with that
  name alone ("types named `B`"), so the two spellings fail alike. This reads the subject's own
  lists and those of each union operand; a list inside an `Except` exempts and is not read, and
  a subject that matched nothing fails once, whole, with no per-name failures beside it. A run
  over one project's compilation drops from these lists the names that project does not
  declare, as it drops a union operand that is empty there, since another project may declare
  them. An **`Under` list** (§5.2) is read per glob on all of these terms, since
  `X.Under("a", "b")` names what `arch.AnyOf(X.Under("a"), X.Under("b"))` names: a glob no type of
  the subject is declared under fails the rule as "types under `b`". A **family** subject sharpens the same way per cell: a layer cell
  matching nothing, or a project cell declaring no solution type, fails the rule naming that
  cell; a project form naming no project at all is the plain empty-subject failure, there being
  no cell to name. A run a solution filter (§4.4) narrowed skips instead: a rule *all* of
  whose violations are these empty-selection failures reports **skipped**, one line naming the
  filter and the unchecked-project count, because under a narrowing filter an empty selection is
  the expected consequence of the narrowing rather than evidence of a typo'd spec. The cost is
  deliberate: a typo'd selection goes quiet under a filter until an unfiltered run reds
  it. Unfiltered runs keep the fail-closed default untouched. An empty resolved *operand* set
  warns **"rule is inert"** only on a forbidden-set dependency verb (`MustNotReference` /
  `MustNotBeReferencedBy` / `MustNotConstruct` / `MustNotCatch` / `MustNotCatchUnfiltered` /
  `MustNotSwallow` / `MustNotThrow` / `MustNotExpose`) whose operand is a
  **pattern selection**
  (Layer / Namespace / Project
  or a refined `Types`); a bare `typeof(...)` target absent from the codebase is the *win
  condition* and stays silent, the `MustOnly*` verbs never raise it (an empty allow-set is loud
  on its own), and `MustNotUse` / `MustNotInject` never warn at all (§4.5, §4.7).
  The same test runs **per operand**: on those verbs, an operand that is a pattern selection and
  matched nothing warns **"rule is partly inert"** naming that operand, even where the operands
  beside it matched and the rule is enforcing them (§5.1). The warning changes no verdict: the
  rule passes or fails on what those operands find. Without it the fold hides a dead operand
  entirely — the united set is non-empty, so the whole-target test above can never see one, and a
  ban that has lost a clause reads exactly like one that has not. Same silence for a `typeof`
  operand, at this grain too. A `Named` list is read **per name** at this grain: every type
  adjective filters one type at a time, so `X.Named("A", "B")` names exactly what
  `arch.AnyOf(X.Named("A"), X.Named("B"))` names, and a name that no type the operand matched
  carries is a dead operand of that union. It warns as one, quoted as the operand would read with
  that name alone ("types named `B`"), beside the names that matched, and an `Under` list's globs
  are read the same way ("types under `b`"). A live operand's parts are
  read the same way. Each part of an `arch.AnyOf(...)` passed as one operand, and the definition of
  a layer used as a target, is tested as it is written, before the union's or the layer's own
  adjectives: a part that matched nothing warns as itself, and a dead name in its list warns as
  above. A layer used as a subject is not opened. Only the operand's own
  `Named` adjectives are read this way: a list inside an `Except` exempts, and a name there that
  matches nothing exempts nothing, which leaves the ban stricter rather than quieter. The
  `MustOnly*` verbs run the per-operand test too, on every pattern in the allow-set whether or not
  the rest of it matched, and warn **"rule is stricter than written"** naming each dead allowed
  pattern, part or name. What that pattern was meant to allow is then reported as violations under
  the callers' names, and the warning is what names the stale pattern. It is the partly inert
  warning's kind, so the rule is not measured in full, but it errs the other way: a dead allowed
  pattern only adds violations and never leaves a baseline entry unmatched. Its rule's unmatched
  entries therefore stay fixed debt that `baseline accept-reductions` retires, while
  `baseline init` skips the rule (§4.4) rather than capturing those violations as debt, and the
  rule is never suggested for promotion. A `typeof` operand stays silent here as well. The two
  subject-position lists are read the same way: each dead pattern, part or name of a
  `MustBelongTo` membership — the reside pair's glob or project name included — warns that the
  rule is stricter than written, in the allow-list's direction, and each of
  `MustHaveExactlyOneCounterpart`'s `among:` warns that the rule is not measured as written, in
  the partly inert direction, since a dead counterpart pattern can both add a violation and
  remove one. Both stay silent on any narrowed run, where a pattern's types may sit in a project
  the run never loaded. The negative
  **anchor** verbs (`MustNotImplement` /
  `MustNotDeriveFrom` / `MustNotBeAttributedWith`, type and member side) report nothing and cannot:
  they resolve no target set, so "no subject matched this anchor" is what a *satisfied* ban looks
  like, and resolving the name instead does not separate a typo from a ban on something this
  codebase has never held (§4.5, §4.6).
- **Edges are definition-level (v1).** A source-level reference to `IHandler<Order>` is a
  reference to *both* the open definition `IHandler<T>` and the argument `Order`; the checker
  mints no edge to the closed construction. A closed generic in an `arch.Type(...)` /
  dependency-verb position (a reference *target* or *subject*) therefore has no node to resolve
  to and is refused as a rule-level error with guidance ("ban the open definition and/or the
  argument type"). The hierarchy adjectives (§5.2), which match against *preserved*
  constructions, are where a closed generic is meaningful.
- Rendered glossary line, once per managed block: *"reference = a source-level type
  reference."* A collective layer sentence is exactly the universally-quantified types
  sentence at v1 edge granularity.

### 4.2 Namespace patterns

Namespace patterns are dot-segment aware and case-sensitive; path globs, the one pattern over
file paths, have their own matcher (below):

- **Trailing `.*` is the subtree operator and is self-inclusive**: `MyApp.Domain.*` matches
  the namespace `MyApp.Domain` itself and all descendants. (Otherwise a facade declared
  directly in `MyApp.Legacy.Billing` falls outside its own quarantine — the silent-hole bug.)
- An interior standalone `*` segment matches exactly one segment.
- A partial-segment `*` (e.g. `Legacy*`) matches within the segment and never crosses a dot.
- A lone `*` matches everything.

Edge cases, pinned by tests:

| Pattern | Matches | Does not match |
|---|---|---|
| `MyApp.Domain.*` | `MyApp.Domain`, `MyApp.Domain.Orders`, `MyApp.Domain.Orders.Internal` | `MyApp.DomainX`, `MyApp` |
| `MyApp.Domain` | `MyApp.Domain` only | `MyApp.Domain.Orders` |
| `MyApp.*.Orders` | `MyApp.Sales.Orders` | `MyApp.Orders`, `MyApp.A.B.Orders` |
| `MyApp.Legacy*` | `MyApp.Legacy`, `MyApp.LegacyBilling` | `MyApp.Legacy.Billing` |
| `*` | everything | — |

**Type- and member-name globs** (`WithNameMatching` and `MustHaveNameMatching`, both with their
member twins — §5.2/§5.3/§5.7) share one matcher, distinct from namespace patterns and pinned by
its own edge-case tests: matching is case-sensitive ordinal; `*` matches any run of characters
including the empty run (`*Async` matches `Async`); a pattern with no `*` is an exact name match;
a lone `*` matches every name.

**Affixes are literal text**, not globs (§5.2/§5.3/§5.7): `WithSuffix`/`WithPrefix`,
`MustHaveSuffix`/`MustHavePrefix` and their member twins compare the whole argument against the
end or the start of the simple name, ordinal and case-sensitive, so a `*` in it is a literal.
Over a codebase declaring `OrderController`, `WithSuffix("*Controller")` selects nothing while
`WithNameMatching("*Controller")` selects it. The rendered fragment does not show the difference,
because it synthesizes the glob spelling out of the affix — `MustHaveSuffix("Handler")` renders
"must be named `*Handler`" (§5.3) — so an affix carrying a `*` of its own renders as a sentence
that reads like a pattern while matching literally.

Neither kind has dot-segment structure or a subtree operator, so §8 item 16 never applies to
them; only the blank check (item 15) does, and it covers globs and affixes alike.

**`Named` is the exact form** (§5.2): a name, not a pattern. It is matched ordinal and
case-sensitive against the whole simple name (a `*` in it is a literal), so the glob semantics
above are untouched and a wildcard-free glob still means what it meant; `Named` is the spelling
that says the intent. The simple name is the one a report prints without its namespace, generic
arity or containing type: `Named("Line")` reaches a nested `Order.Line`, `Named("Order.Line")`
names nothing, and `Named("Repository")` reaches `Repository<T>`. A name reaches every type that
carries it, in every namespace and project, which is why the fragment says "types named `X`"
rather than rendering the bare backticked name a single `arch.Type` does (§5.2, §6). Each name is
checked on its own, one register softer in target position than in subject position, as for
union operands (§4.1). As the target of a forbidden-set verb, a name that matches nothing while
another name in the list matches warns that the rule is partly inert. As the subject, it fails
the rule naming that name (§4.1).

**Path globs** (`Under`, §5.2) read a different string: a declaring file's path relative to the
solution directory. Segments are separated by `/`; `*` matches within one segment and never
crosses a `/`; a `**` segment matches any number of segments, none included; and matching ignores
case on every operating system, because one spec is checked on Windows and on Linux and the case
of a folder is not a fact a law should turn on. A glob reads as a prefix: it matches a file when it
matches the file's path or any directory leading to it, so `src/Widgets` means what
`src/Widgets/**` means, and a glob may name one file (`vendor/Compiler.cs`). A rooted glob, a
backslash, a `.` or `..` segment, an empty segment and a `**` written inside a longer segment are
refused at spec build (§8 item 35) rather than normalized, so what the model carries is what the
author wrote. A path is made relative by text alone: both separators split, `.` segments drop,
`..` folds, the solution directory is compared segment by segment ignoring case, and a file
outside it is under no glob.

| Glob | Matches | Does not match |
|---|---|---|
| `src/Widgets` | `src/Widgets/Button.cs`, `src/Widgets/Deep/Panel.cs` | `src/WidgetsExtra/Button.cs` |
| `src/*/Internal` | `src/Console/Internal/Ansi.cs` | `src/Console/Deep/Internal/Ansi.cs` |
| `src/**/Internal` | `src/Internal/Ansi.cs`, `src/Console/Deep/Internal/Ansi.cs` | `lib/Internal/Ansi.cs` |
| `**/*.Designer.cs` | `src/Forms/Main.Designer.cs` | `src/Forms/Main.cs` |
| `SRC/widgets` | `src/Widgets/Button.cs` | — |

### 4.3 Violation identity (what a baseline entry keys)

Per verb class — this is grammar-level semantics, not baseline file format:

- **Dependency verbs**: `(ruleId, source symbol ID, target symbol ID)`. A second forbidden
  reference from a grandfathered type — or the same type referencing a *different* forbidden
  target — is NEW and red, preserving the ratchet ("new code in the old pattern must be
  red"). Multiple reference sites within one (source, target) pair ride together under that
  one identity, and the entry records how many it grandfathers: more sites than it recorded is
  NEW and red, fewer is a reduction (the site-count paragraph below).
- **Circular-references verb** (`MustNotHaveCircularReferences`, §5.1/§5.3): the same edge key,
  `(ruleId, source symbol ID, target symbol ID)`, minted for every type pair on every arrow inside
  a strongly connected component of the cell graph — an arrow being every owned instance whose
  source sits in one cell of the family and whose target sits in another (externals exempt, as on
  every reference verb; a target outside every cell is no node at all). Keying on cycles was
  declined: a baseline of cycles churns by dozens of entries on one new arrow, enumerating cycles
  is exponential in the component (ArchUnit caps it), and blaming the one edge that closed the
  circle depends on discovery order. The cost is stated rather than hidden: the intended direction
  is blamed beside the stray back-reference, and the author who knows the direction writes the
  ordering rule (§11). The ratchet falls out of the key: a new pair on a circle's arrow is red, a
  new site inside a grandfathered pair is red under the site count, a cell joining the component
  mints new pairs, and a broken circle leaves every entry on it stale for `status` to surface and
  `baseline accept-reductions` to retire. The circle itself rides the JSON channel's `detail`
  only — "circular references among the A and B layers", the component's cells in declaration
  order — and no other renderer changes shape for it.
- **Construction verb** (`MustNotConstruct`, §4.5/§5.3): `(ruleId, source symbol ID,
  constructed symbol ID)` — the same edge-key shape as a dependency reference, the constructed
  type keying the target slot. Overload-indifferent: every constructor overload of the constructed
  type collapses to the one type-pair identity, and the `new` sites are evidence, not identity.
  A grandfathered `new Foo()` plus a *different* forbidden constructed target from the same
  source is NEW and red, exactly like the reference ratchet; multiple `new` sites within one
  (source, constructed) pair ride together, and are counted.
- **Injection verb** (`MustNotInject`, §4.7/§5.3): `(ruleId, source symbol ID, injected
  symbol ID)` — the same edge-key shape, the injected parameter type keying the target
  slot. Constructor-overload- and parameter-name-indifferent: every constructor parameter
  typed on the injected type collapses to the one type-pair identity, and the parameter sites
  are evidence, not identity. A grandfathered captive injection plus a *different* forbidden
  injected target from the same source is NEW and red; multiple injecting parameters within
  one (source, injected) pair ride together, and are counted.
- **Catch verbs** (`MustNotCatch`, `MustNotCatchUnfiltered` and `MustNotSwallow`, §4.8/§5.3):
  `(ruleId, source symbol ID, caught symbol ID)` — the same edge-key shape, the caught
  exception type keying the target slot.
  Catch sites are evidence, not identity: multiple `catch` clauses within one (source, caught)
  pair ride together and are counted, and a grandfathered catch plus a *different* forbidden
  caught type from the same source is NEW and red. **All three catch verbs key that same edge.**
  The *unfiltered* sites `MustNotCatchUnfiltered` reports and the *swallowing* sites
  `MustNotSwallow` reports are evidence too — narrowing which sites a violation prints never
  narrows what it keys — so an entry written under one catch verb keys the identical edge under
  the others, and adding a `when` filter or a terminal `throw` to any clause of a grandfathered
  pair moves the evidence, not the baseline.
- **Throw verbs** (`MustOnlyThrow` and `MustNotThrow`, §4.8/§5.3): `(ruleId, source symbol ID,
  thrown symbol ID)` — the same shape, the thrown type keying the target slot. Throw sites
  are evidence, not identity, and are counted; a grandfathered thrown type plus a *different*
  thrown type the rule objects to, from the same source, is NEW and red. The allow-list and
  the ban key edges identically, so which polarity a spec chose is invisible to its baselines.
- **Exposure verb** (`MustNotExpose`, §4.9/§5.3): `(ruleId, source symbol ID, exposed symbol
  ID)` — the same edge-key shape, the exposed type keying the target slot. Signature
  positions are evidence, not identity: every signature position of one exposed type within a
  source rides together and is counted, and a grandfathered exposure plus a *different*
  forbidden exposed type from the same source is NEW and red.
- **Project item verbs** (the package verbs `MustReferenceNoPackages`,
  `MustOnlyReferencePackages` and `MustNotReferencePackages`, `MustOnlyTarget` and `MustTarget`,
  and `MustSetProperty`, §4.10/§5.8): `(ruleId, project:{name}, package:{id})`,
  `(ruleId, project:{name}, framework:{moniker})` and
  `(ruleId, project:{name}, property:{name}@{moniker})`. This is the edge-key shape, the project
  in the source slot and the item it names in the target slot. A grandfathered package plus a
  *different* package the same project declares later is NEW and red, and so is a framework the
  project starts targeting. `MustTarget` keys the framework a project lacks the same way, so a
  second project that drops it is NEW and red. `MustSetProperty` keys the property with the
  framework of the build it judged, the property spelled as the rule names it, and `@{moniker}`
  dropped for a build no framework was told apart in. The framework is identity rather than
  evidence because two frameworks that both lack a property share one site, the project file, so
  a key without it would bless the next framework that loses the property under the site count.
  The package identifier is spelled as the project declares it and compared ordinally, though a
  package pattern matches it ignoring case (§4.10); the moniker is the normalized short form. A
  baseline file written before items had identities of their own holds one subject entry,
  `project:{name}`, for the project. That entry still grandfathers every item of its project,
  and `status` counts it as uncounted until `baseline accept-reductions` splits it into one
  counted entry per item it still covers, each keeping its reason.
- **Shape/naming/inheritance/attribute/membership verbs and escape hatches**:
  `(ruleId, subject symbol ID)`. A membership verb's operands (`MustBelongTo`'s memberships,
  §5.3) are part of the rule, never of the identity: widening or narrowing the membership list
  changes which subjects red, and every existing entry keeps keying the subject it
  grandfathered. The correspondence verb (`MustHaveExactlyOneCounterpart`, §5.3) rides the same
  key with two red arms — a subject reds with zero counterparts and with several — and both arms
  key the subject alone: counterparts are evidence, never identity, so a grandfathered subject
  stays blessed on the day its arm flips, and widening `among:` or editing `named:` changes
  which subjects red while every entry keeps its subject. Where the arms differ is where they
  point — a missing counterpart sites at the subject's own declaration, an ambiguity at the
  counterparts that collide — which is the catch bullet's "narrowing which sites a violation
  prints never narrows what it keys" read in the other direction.
- Symbol IDs are Roslyn `DocumentationCommentId` strings — stable across file moves and
  formatting.
- A symbol ID names a name, not a node. Where a fully-qualified name means several types — see the
  reference universe above — they share an ID, so a baseline entry written for either grandfathers
  the other. That is deliberate: an ID that forked on which assembly supplied the name would not
  survive the package upgrade it exists to survive. It is also a widening, so prefer fixing such a
  pair to baselining it; the survey's shadowed-name key is where the split shows before a baseline
  is written.

**The site count is a measure, not identity.** An edge entry — every shape above that keys a
(source, target) pair — records how many distinct `file:line` sites it grandfathered when it was
written: the same number `check --json` reports as `siteCount`. The ratchet compares the count
it observes with the count the entry recorded. More is growth, and the violation is red with
every site of the pair listed; the same or fewer passes, a shortfall being a reduction that
`baseline accept-reductions` records. An entry with no count — every file written before the
count existed — grandfathers the whole pair, as it always did, and `status` names it as
uncounted until `baseline accept-reductions` records one. Subject entries carry no count: their sites
are declarations, and a verb like `MustHaveExactlyOneCounterpart` moves its whole site set when
its arm flips. The count is neither identity nor annotation. It is excluded from entry equality,
so a grown pair is one entry that grew rather than a stale entry beside a new violation, and it
is included in the entry's own seal (§4.4), so a hand-edited count is refused like any other
edit. Three consequences follow from measuring evidence by line. A reformat that splits or joins
two mentions of one target on one line moves the count; identity is unaffected, and `baseline add` on
the entry re-records it. Where one symbol ID names several types (above), the recorded count covers
the larger of the violations sharing it. And the catch and throw verbs, which key one edge but
print different evidence, record the count *as measured by the verb that wrote it*: a rule
switched from `MustNotCatchUnfiltered` to `MustNotCatch` sees the filtered clauses it never
counted as growth, and the reverse switch as a reduction. Growth is accepted in one way only —
`baseline add` on the existing entry, which re-records the observed count under its mandatory
`--because`.

### 4.4 Migrate defaults

- `.Baseline(path)` omitted ⇒ a deterministic conventional path derived from the rule ID:
  `arch/baselines/<rule-id>.json`,
  where the rule ID's `/` separators become subdirectories (IDs match §8's
  `^[a-z0-9-]+(/[a-z0-9-]+)*$`, so the result is filesystem-safe). The path is filled into the model
  at build time — `MigrateData.BaselinePath` is never null post-build — stored forward-slash, and
  resolved by the CLI against the solution directory (an absolute spec path wins). A run through a
  solution filter (`.slnf`) resolves against the directory of the solution the filter references,
  so the filter and its solution find the same committed file.
- One file may carry many rules' sections, one entry per line, and every entry carries its own
  `seal`: a hash over the rule ID and the entry's own fields. An edited entry, an edited count, and
  an entry moved into another rule's section are all refused; a reformat or a CRLF checkout is not.
  There is no whole-file digest, and that is what lets two branches burn the same file down at once.
  Payoffs of different rules merge with nothing in common to conflict on. Payoffs of one rule
  conflict only where their lines touch, and there the resolution is to keep whole entry lines from
  either side: every line either branch committed is a complete entry, so nothing has to be
  recomputed and no valve has to be re-run. What a seal cannot see is a line taken away — deleting
  an entry is accepted, and the violation it grandfathered goes red on the next `check`. A renamed
  rule finds no section under its new ID, and editing the key by hand fails the seal, which binds
  each entry to the old ID. `sliderule baseline rename --from <old> --to <new>` moves the
  section and re-seals every entry unchanged.
- `.WhileYoureThere(policy)` omitted ⇒ `MigrationPolicy.MigrateIfSmall`, and the default
  renders in the counter-prior prose (the boy-scout sentence always has content).

### 4.5 Member-use edges (member-access targets)

The first rules a long-lived .NET estate asks for are member-level bans — `DateTime.Now`,
`.Result`/`.Wait()`, `ConfigurationManager.AppSettings`, `HttpContext.Current` — all invisible
to type-level edges (`DateTime` the *type* cannot be banned). `arch.Member` + `MustNotUse`
express them: members as constraint **targets**. Member *subjects* are §4.6.

- **A member-use edge** is `(source type, target member, file:line sites)`, recorded beside
  the type-level edge wherever the walk binds a member: property/field/event accesses
  (including `?.`, compound assignment, and `+=`/`-=` subscription), method invocations,
  method-group references, and `using static` bare names.
- **Not recorded, pinned**: `nameof` operands — a deliberate asymmetry with type edges
  (`nameof(Target)` DOES mint a type edge; `nameof(DateTime.Now)` never reads the clock, so
  it is not a *use*); compiler-pattern consumption the syntax walk never sees as a member
  access (`await`'s `GetAwaiter`, `using`'s `Dispose`, `foreach`'s enumerator pattern, query
  syntax) — the documented syntax-walk boundary; indexers (growth, §11). Constructors are not a
  member *use* either, but they ARE recorded as their own **construction edge** (below), a
  third axis beside reference and use.
- **A construction edge** is `(source type, constructed type,
  file:line sites)`, recorded beside the type-level edge at every object-creation expression —
  explicit `new Foo()` **and** target-typed `new()` (the modern-codebase form).
  Constructed generics normalize to their open definition (§4.1); self-construction is dropped like
  the type-edge self-reference. **Excluded, each with a pinned must-NOT-mint row**: attribute
  applications, `: base(…)`/`: this(…)` initializers, delegate creation (`new Action(M)` and its
  target-typed form, keyed on the constructed symbol's delegate kind so both spellings skip), `with`
  expressions, and array creation. Reflection/container construction is the documented **honesty
  boundary**, invisible either way — a container *registration* (`AddScoped<IFoo, Foo>()`) mints only
  type references, so pure-DI composition roots need no exemption, while a factory lambda whose
  body `new`s (`AddScoped(sp => new Foo(…))`) IS caught and wants `.Except(root)` or a baseline, so
  the boundary is one sentence to teach. The verb (`MustNotConstruct`) is §5.3; the glossary clause is §10.
- **Normalizations**: an accessor resolves to its property/event; a reduced extension-method
  call resolves through `ReducedFrom` to the declaring static class's method; constructed
  generic members resolve to their `OriginalDefinition` — member edges are definition-level
  exactly like type edges (§4.1); same-type accesses are dropped (matches the type-edge
  self-drop).
- **Matching** is by `(declaring type, member name)` — one ban covers every overload; there is
  no signature form. **Violation identity** is `(ruleId, source type ID, member
  DocumentationCommentId)` — the *specific* member's ID (`M:`/`P:`/`F:`/`E:` forms), so a
  grandfathered `Wait()` rewritten as `Wait(timeout)` is a NEW red (the ratchet blesses the
  observed violation, not the ban).
- **Dispatch boundary, pinned**: an edge records the symbol Roslyn resolves at the call site.
  A ban on a concrete member does not catch calls through an interface-typed receiver, and an
  interface-member ban does not catch direct concrete calls — member bans are
  source-visibility bans, not runtime-dispatch bans.
- **Attribution** (§4.1): in a type several projects declare, a member use is one instance per
  copy that makes it, exactly as an edge is, and the subject owns the instances of the copies it
  names the type at. `arch.Project("A").MustNotUse(() => DateTime.Now)` therefore reds only on
  uses A's copy makes, and cites only their lines; a subject whose head is not a project owns
  every copy's. The member side stays attribution-insensitive: a `(declaring type, name)`
  anchor names no project, so the ban reaches the member in every copy of its declaring type.
- **Anchoring**:
  `arch.Member(typeof(X), nameof(X.M))` and, as pure authoring sugar desugaring at mint to the
  identical leaf, the expression anchors `arch.Member<T>(x => x.M)` (instance; value member via
  `Func<T, object?>`, void method via `Action<T>`) and `arch.Member(() => X.M)` (static). The
  anchor is always the tree's **resolved** member: the declaring type from its `MemberInfo`
  (`DeclaringType`, **never** `ReflectedType` — an inherited `Task<int>.Wait()` anchors `Task`),
  with a constructed generic normalized to its definition at mint (`Member<Task<int>>(t => t.Result)`
  → `typeof(Task<>)`; `t.Wait()` → `typeof(Task)`). A closed-generic anchor type is refused at
  check time with the §4.1 guidance (ban the open definition); because expression anchors
  auto-normalize, that refusal — and the §8 item 12 "declared on the anchored type" guard — are
  **unreachable** from an expression-minted member (the member is real and generic-normalized by
  construction), and both remain live for the `typeof` form. An unresolvable anchor expression is
  reported at spec build (§8, one code / eight messages). The receiver rules: the instance form
  wants the lambda parameter itself, reached only through identity-preserving casts (an interface
  or reference `Convert` with no operator method, or an `as`-cast, which anchors the cast-to member
  per the dispatch boundary above); a user-defined conversion instead stops the peel and is
  reported, since following it would silently anchor the post-conversion type. A reduced extension
  call `x.Ext()` in the instance form anchors the declaring static class (the `ReducedFrom` parity
  above); in the static form an extension is anchored like any other static call, so `() => Ext.M(arg)`
  and `() => captured.M()` both name `Ext.M`. What the **compiler** already refuses
  needs no runtime handling: events (CS0070), inaccessible members (CS0122), `ref`-returns
  (CS8153), `ref struct` receivers (CS8640), and statics accessed through an instance (CS0176); a
  method-group body (`x => x.M` or `() => T.M`) warns CS8974 and is steered to the invocation form
  (`x => x.M()` or `() => T.M()`) at spec build.

  The `MustNotUse` verb also carries the two **static** anchor forms directly (§3.3):
  `web.MustNotUse(() => DateTime.Now, () => Type.M())` desugars target-by-target through
  `arch.Member(() => …)` to the identical leaves, so the common all-static ban needs no
  `arch.Member` ceremony. Six of the eight unresolvable-anchor messages are reachable from the
  verb's static forms (the two instance-form steers — a static member in the typed form, a
  receiver that is not the parameter — have no static spelling); they fire from the verb position
  in the same §8 pass. Instance and mixed-form targets keep the explicit `arch.Member` wrap.
- **Inert semantics**: a banned member absent from the codebase is the win condition and
  stays silent. Member targets are concrete `(type, name)` anchors — no pattern form
  exists — so `MustNotUse` never warns, exactly like a bare `typeof` target (§4.1). An
  empty *subject* fails the rule (shared checker default).
- Rendered glossary line, beside the reference entry and **only when the spec carries a
  member-target rule**: *"use = a source-level member access."*

### 4.6 Member subjects (member selections)

Where §4.5 admits members as constraint *targets* (`arch.Member` + `MustNotUse`), this section
admits them as *subjects*: a projection turns a type selection into a **member selection** over
the declared members of the selected types, refinable with member adjectives and asserted on
with member modal verbs. The flagship is `naming/async-suffix` — *"Methods returning `Task`
declared in types in `MyApp.Web.*` must be named `*Async`."* A member constraint's subject, as
every walk through `Constraint.Subject` sees it — foreign-`Arch` detection (§8 item 10) and
Quarantine desugaring (§7) — is the underlying **type** selection.

- **The inventory universe** is the **declared members of solution-declared types** — the same
  subject universe as type selections (§4.1), read one level down. Extraction inventories, per
  declared type, its declared methods, properties, fields, and events. **Excluded**:
  property/event accessors (they fold into the property/event, matching the §4.5 member-use
  normalization), constructors including static constructors, operators and conversions,
  finalizers, **explicit interface implementations of every kind** — method (`void IFoo.Bar()`),
  property (`int IFoo.P`), and event (`event Action IFoo.E`) alike — indexers, any
  compiler-generated or implicitly-declared member, and **any member with no source-writable
  name** — the member-side twin of the type-side screen, which is what keeps the synthesized
  top-level-statements entry point `<Main>$` out of a naming law's reach while leaving its
  enclosing `Program` type (whose name *is* writable, and which carries real reference, catch and
  throw edges) fully inventoried. An explicit interface implementation is
  interface plumbing, not authored surface: its accessibility is `Private` and its name is fixed
  by the interface, so a naming or shape verb could never sensibly apply. The *method* impl is
  dropped by the non-`Ordinary` `MethodKind` screen, the *property* and *event* impls by their
  non-empty `ExplicitInterfaceImplementations`. A member subject and `MustAcceptParameter` (§5.7)
  therefore never see an explicit implementation of any kind, and none mints an exposure edge
  (they carry no inventory, and §4.9 gates on public besides). **Enum and
  delegate types contribute no inventory** — an enum's fields are its values (an enum-value read
  stays a recorded field *use*, §4.5, untouched by this section) and a delegate's `Invoke`/
  `BeginInvoke` are runtime plumbing, not authored surface. **External (metadata) types carry no
  inventory** — the member axis is solution-declared-only, exactly as external types carry a
  shallow type hierarchy (§5.2).
- **An empty member subject fails the rule** by default, the member analog of the empty-type
  subject (§4.1), with its own pinned message *"The subject selection matched no
  solution-declared members."* (a selection that matches types none of whose members survive the
  kind filter is the ordinary way to hit it). Under a narrowed run it skips exactly as the type
  analog does (§4.1). A member subject's own **`Named` list** sharpens per name on the type
  side's terms (§4.1): where the member subject matched something, each listed name no member of
  it carries fails the rule with *"The subject selection operand "…" matched no
  solution-declared members."*, quoted as the subject would read with that name alone ("`Ghost`
  methods of types named `Program`"). A list inside an `Except` payload exempts and is not read,
  a subject that matched nothing fails once, whole, and a run over one project's compilation
  reads no member name this way, since another project's members may carry it.
- **Member exclusions.** `.Except(memberSelection, …)` removes from a member subject every member
  any payload names (§5.7). Each payload is a member selection over a type selection of its own,
  resolved where an exclusion stands, over the whole solution in target position (§4.1), and then
  narrowed by its own member adjectives, nested `Except`s included. Subtraction is by member, so a
  payload drawn from other types removes only what it names. A payload of another single kind than
  the selection it subtracts from would remove nothing and is refused at spec build (§8 item 34);
  a `.Members` payload holds every kind. There is no member union: several payloads are the union,
  and the sentence or-joins them.
- **Violation identity is `(ruleId, member DocumentationCommentId)`** — the member's own
  `M:`/`P:`/`F:`/`E:` DocId, so the ratchet blesses the *specific* member and a renamed or
  newly-added member is a NEW red: the shape-verb identity (§4.3) with a member ID in the
  subject slot.
- **`.Returning(Type first, params Type[] more)` / `.Returning(string first, params string[] more)`**
  matches a method's return type at the **definition level**, mirroring `Implementing` (§5.2): a
  **non-generic** anchor (`typeof(Task)`) matches exactly; an **open-generic** anchor
  (`typeof(Task<>)`) matches *any* construction (`Task<int>`, `Task<Order>`, …) on the definition
  name. A **closed-generic** anchor (`typeof(Task<int>)`) is refused at spec build (§8 item 14)
  with guidance to use the open definition, and a check-time backstop guards the same class of
  mistake. The string form is §5.2's escape hatch on §5.2's terms: each name is the return type
  definition's FQN as a report prints it, an open generic spelled with its declared
  type-parameter names (`"System.Threading.Tasks.Task<TResult>"`), matching any construction
  of that definition and rendering byte-identically to the `typeof` twin. The homogeneity rule
  bites harder here than anywhere: a return-type list routinely pairs a BCL wrapper with a
  framework type, and one call takes `typeof` anchors or strings, never a mix — so a list
  reaching for a type the spec cannot reference is spelled all-string,
  `"System.Threading.Tasks.Task<TResult>"` beside `"Microsoft.AspNetCore.Mvc.IActionResult"`.
  Item 14 is a `typeof`-arm fact: a constructed string names no definition and never matches,
  and only blankness is validated (§8 item 15, "return type name"). The honesty cost: a typo'd
  or constructed string as the sole anchor empties the member subject and the rule reds on the
  empty-subject gate; beside an anchor that still matches it is inert, and the rule stays green.
  There is no derived-from / assignability matching — the return type is compared to the
  anchor's definition FQN, nothing wider. `.Returning` is **methods-only**: it lives on the
  `.Methods` projection's `MethodSelection` and is uncompilable elsewhere (§3.2), so a
  return-type filter on a field or property never type-checks. There is **no `.Returning<T>()`
  generic twin**: the open-generic anchor is the main use and is inexpressible as a type
  argument, and a closed-generic type argument would only reproduce the §8 item 14 refusal —
  the one anchor position the §10 triple discipline exempts from its `<T>` leg.
- **Parameter facts.** Extraction inventories, per declared *method*, its parameters in
  declaration order — each `(Name, TypeFullName)` with the type definition-normalized exactly
  like the return type (§5.6); properties, fields, and events carry an empty list (accessors,
  constructors, operators, and indexers are already outside the inventory).
  `MustAcceptParameter` (§5.7) evaluates against these facts: a subject method passes iff any
  declared parameter's type matches the anchor's definition FQN — a non-generic anchor
  exactly, an open-generic anchor on any construction, the `.Returning` matching discipline
  verbatim. The anchor is a `typeof`, a definition-FQN string on §5.2's terms
  (`"System.Threading.CancellationToken"`, `"System.IProgress<T>"`; validated for blankness
  alone, §8 item 15, "parameter type name"), or the `<T>` sugar
  (`MustAcceptParameter<CancellationToken>()`, §10). A typo'd or constructed string here is
  always red: the verb asks every subject method for a parameter no declaration can have, and
  an empty subject reds on its own gate. Declaration semantics, pinned in the extraction
  matrix: a default-valued parameter counts (`CancellationToken cancellationToken = default` — the most common
  compliant signature); the extension-method `this` parameter is included (the declared
  static method's list, never the reduced form); `ref`/`in`/`out` do not change the recorded
  type; `params CancellationToken[]` is the array type and does not match a
  `CancellationToken` anchor; `CancellationToken?` is `Nullable<>`'s definition and does not
  match either — the definition-level-exact honesty boundary `.Returning` and `MustNotCatch`
  carry; a type-parameter-typed parameter records its
  declared name (`T`) — the same rendering that pins `Echo<T>(T value)`'s return type — so it
  never matches a `typeof` anchor; a record's positional list surfaces as the generated
  property, never as parameter facts (the primary constructor is outside the inventory);
  partial-method parameters are read once and ride the partial-union merge.
- **Attribute facts.** Extraction inventories, per declared member, its **declared
  attributes** — each a definition/constructed name pair (`IAttributeInfo`, §5.6), the
  definition open-reduced so a C# 11 generic attribute keeps both its definition
  (`N.MarkAttribute<T>`) and its substituted construction (`N.MarkAttribute<System.Int32>`),
  ordinal-sorted by constructed name. Name pairs, not nodes: unlike the type side the fact
  mints nothing — no reference edge, no graph entry — keeping the member model's
  by-FQN-string discipline. **Declared-only is the boundary**: an attribute on a property's
  `get`/`set` accessor hangs off the accessor method symbol (accessors fold into their
  property and are not inventoried in their own right), and a `[return:]` attribute hangs off
  the return value — both are outside the fact. A **partial method** reports the **union** of
  both parts' attributes, because the merged symbol does — pinned empirically in the
  extraction matrix. The member `AttributedWith` adjective and the member attribute verbs
  (§5.7) match against these facts on the type side's discipline (§5.2): declared attributes
  only, a non-generic or open-definition anchor matching on the definition name, a closed
  `typeof` construction on the constructed name.
- **Mutability facts.** Extraction records, per declared member, a property's **setter shape** —
  `HasSetter` and `HasInitOnlySetter`, read off `IPropertySymbol.SetMethod` — and a field's
  **writability**, `IsReadOnly` and `IsConst`, read off the field symbol's own flags. Each is
  kind-scoped and simply false elsewhere, the `IsAsync` form: a method carries none of the four,
  and a property carries neither field flag. `HasInitOnlySetter` implies `HasSetter`, and
  `IsReadOnly` and `IsConst` are disjoint — a `const` field is not `readonly` in the symbol API,
  though it is more constrained than one. Three honesty boundaries, each stated because a reader
  will assume otherwise: **declaration shape is not deep immutability** — a get-only property
  whose type is itself mutable still hands the caller something it can write through, the same
  static-declaration boundary §4.9 carries for exposure; **setter kind is recorded distinctly**,
  so an init-only setter is a fact the model carries even where no verb reads it (§11);
  and **`HasSetter` is accessibility-blind** — a `private set` is a setter, because the
  declaration has one.
- **Declaration-semantics flags are pinned to C#, not IL.** `IsVirtual` is true for a member
  declared `virtual` and false for an `override` or `abstract` one (an override is not itself
  "virtual" in the authored sense); `IsOverride` is true for a member declared `override`, sealed or
  not, and `IsSealed` for a `sealed override` alone — the keywords the member itself writes, so a
  member of a sealed class reports neither by inheritance, and `sealed` on an interface member makes
  it non-virtual rather than sealed; `IsAbstract` is true for an `abstract` member and for every interface
  member without a body (interface members are abstract; a default implementation is virtual
  instead), and `MustBeOverridable` (§5.7) reads `IsVirtual`, `IsAbstract`, `IsOverride` and
  `IsSealed` together; `IsAsync` reflects the `async` keyword; and
  the four mutability flags read the declaration the same way — an expression-bodied property has
  no setter, a manually written `set` accessor is one, and an `abstract` or interface property's
  `set` counts because the declaration writes it. These
  carry the same declaration-semantics discipline as the type flags (§5.6) and are pinned in the
  extraction matrix.
- **The violation line** names the member as `{DeclaringType.FullName}.{Name}` (with `()`
  appended iff the member is a method — the shared member-display convention of §6/§4.5), located
  at the member's **declaration** `file:line`.
- **No glossary line.** Member subjects never mint or read an edge, so they never trip the
  `use`-glossary gate (§4.5): a spec whose member rules are all member-subject rules renders
  no `use` clause.

### 4.7 Registration facts and injection edges (the DI axis)

The captive-dependency rule — a singleton must not depend on a scoped or transient service —
needs two facts no other section provides: who is *registered* with what lifetime, and who
*injects* whom. Extraction records both; the `arch.Registered` noun (§3.2, §5.1) and the
`MustNotInject` verb (§3.3, §5.3) consume them.

- **An injection edge** is `(source type, injected parameter type, parameter file:line
  sites)`, read from the **declared instance constructors** of every solution-declared type —
  a declaration-side pass, not a body walk: the edge exists because the parameter is declared,
  whether or not any body dereferences it. **Primary constructors are included** (their
  parameters are the modern injection surface); implicitly-declared constructors mint nothing
  (the compiler's parameterless default, a record's copy constructor, static constructors).
  Parameter types decompose **definition-level like type edges** (§4.1): `IEnumerable<IFoo>`
  contributes `IEnumerable<>` and `IFoo`; arrays contribute their element type; constructed
  generics contribute the definition and every argument. Self-injection is dropped (the
  type-edge self-drop); enum and delegate types declare no walkable constructors and
  contribute none. External parameter types get nodes like any other external endpoint, so
  targets can match them (§4.1).
- **A registration fact** is `(lifetime, service type, implementation type?, file:line
  sites)`, read from a **whole-compilation pass** over every syntax tree — not a per-declared-
  type walk, because the most common composition root is a top-level-statements `Program`.
  Recognition is **symbol-first** (never name-only): the invoked method must resolve
  (`ReducedFrom`-normalized) to a method whose containing namespace is
  `Microsoft.Extensions.DependencyInjection` (or its `.Extensions` sub-namespace, the
  `TryAdd*` family's home), whose name is in the recognized-call table, and whose first
  parameter is `IServiceCollection`. A look-alike extension in a user namespace is not
  recognized — but an **in-solution wrapper whose body calls a recognized registration IS seen** (the
  pass walks the wrapper's body like any other tree).

  | Recognized call | Lifetime | service / implementation |
  |---|---|---|
  | `AddSingleton` / `AddScoped` / `AddTransient` and their `TryAdd*` twins | by name | two type-args → (service, impl); one type-arg → (T, T) iff the call has receiver-only arguments, else (T, —) — a factory or instance registration names no implementation type; the `typeof` overloads mirror the same split by `Type`-parameter count |
  | `AddHostedService<T>` | Singleton | service = `Microsoft.Extensions.Hosting.IHostedService`, synthesized — no syntactic mention exists (implementation-only if unresolvable); impl = `T` |
  | `AddDbContext<T>` / `AddDbContextPool<T>` | Scoped; an explicit **literal** `ServiceLifetime.X` argument is honored; a non-literal lifetime argument → no fact recorded (never guess) | (T, T) |
  | `AddHttpClient<TClient>` / `AddHttpClient<TClient, TImpl>` | Transient | (TClient, TClient) / (TClient, TImpl); the named-only string form registers no user type → no fact |

  Open-generic `typeof` registrations (`AddSingleton(typeof(IRepo<>), typeof(Repo<>))`)
  record definition-level, like every other fact (§4.1).
- **The honesty boundary** (rendered into the glossary whenever a `Registered` noun is in
  play, §10): registrations the source does not spell with a recognized call are invisible —
  `Configure`/`AddOptions` (the options interfaces are framework-registered), keyed-service
  overloads, raw `ServiceDescriptor`/`TryAddEnumerable`, assembly-scanning registrars,
  reflection, framework defaults, and wrapper extensions compiled into packages (no syntax
  tree to walk). The recognized-call table is the fence; growth beyond it is named in §11.
- **`arch.Registered(lifetime)` membership** is the union of **service and implementation
  type FQNs** of the recognized registrations at that lifetime (`arch.Registered()` — any
  lifetime). Registration is many-to-many, so membership is resolved at evaluation against
  these facts, never denormalized onto the type model. In **subject** position the selection
  intersects solution-declared types (§4.1, as always); in **target** position it also
  matches externals (`AddSingleton<IClock, SystemClock>()` makes an external `IClock` a
  matchable operand). An empty `Registered` **subject** fails the rule (the shared §4.1
  default) — the loud failure is how an author discovers the visibility boundary above. An
  empty `Registered` **operand** on `MustNotInject` means no such registrations exist — the
  win condition — so `MustNotInject` **never warns** (§5.3, the bare-`typeof` precedent).
- **`MustBeRegistered()` membership** (§5.3) is `arch.Registered()`'s exactly — any lifetime,
  resolved through the same noun at evaluation, so the verb and the noun cannot drift. It is
  the completeness half of the axis beside `MustNotInject`'s shape half: the injection verb
  constrains what the registered may depend on; this verb demands the registration itself. It
  also inverts the polarity of the honesty boundary above: under the nouns an invisible
  registration hides a violation, while under this verb it reds a correctly registered type —
  a false red, not a miss. An estate that registers through an invisible route — assembly
  scanning, keyed overloads, raw `ServiceDescriptor`, a wrapper compiled into a package —
  should not use the verb; the recognized-call table is the fence either way.

### 4.8 Exception edges (catch and throw)

The scoped exception-catch policy — only top-level handlers may catch base `Exception` — and
the custom-exceptions rule — scoped code throws its own domain exceptions, not bare BCL ones —
need facts no other section records: who *catches* what, and who *throws* what. Extraction
records both; the catch verbs `MustNotCatch`, `MustNotCatchUnfiltered` and `MustNotSwallow` and
the throw verbs `MustOnlyThrow` and `MustNotThrow` (§3.3, §5.3) consume them.

- **A catch edge** is `(source type, caught type, file:line sites)`, recorded at every
  source-level `catch` clause. A typed catch mints the catch edge **only** — its type-name
  syntax already mints the ordinary reference edge, so the catch channel never double-mints
  (the explicit-`new` precedent, §4.5). A **bare `catch`** records `System.Exception` — the
  language's own semantics — as a catch edge only: nothing in source names the type, so no
  reference edge exists (and if the compilation cannot resolve `System.Exception`, nothing is
  minted). `when` filters never suppress the edge, and filter contents mint their ordinary
  type/member edges. **A rethrowing catch still mints**: `catch (Exception) { throw; }` is a
  catch of `Exception` — edges are facts, and a sanctioned log-and-rethrow site is excepted or
  grandfathered deliberately, never silently exempted. Type-parameter catches mint nothing;
  constructed generic caught types normalize to their definition (§4.1); self-catches drop;
  lambda and local-function catches attribute to the enclosing type; top-level-statements
  catches attribute to `Program`.
- **A catch edge additionally records its unfiltered sites** — a separate fact beside the
  edge, not a narrowing of the rules above: each edge carries the **subset of its sites whose
  `catch` clause spells no `when` filter**, under the same keying and minting rules. The
  recorded subset is the *unfiltered* one, and the polarity is
  essential: sites dedupe by `(file, line)`, so a filtered and an unfiltered catch of one
  type on one physical line collapse to a single site, and a filter that a conditional
  compilation applies under one target framework but not another reaches the merge as one site
  reported both ways. Recording which sites are unfiltered answers both cases the way a ban
  needs — the site counts as unfiltered, and a rule reads the recorded list as its evidence
  rather than subtracting one set from another. A **bare `catch`** is unfiltered; a bare
  **`catch when (…)`** is filtered. **Filter presence is syntactic**, and that is the honesty
  boundary: `when (true)` counts as filtered, because this axis records what the source spells
  and never judges what a filter tests. A filter that admits everything is a code-review
  matter, not an extraction one.
- **A catch edge additionally records its swallowing sites** — a second named fact recorded the
  same way, and a subset of the unfiltered subset. A site is **swallowing** when its clause
  carries no `when` filter **and** its block's last statement is not a `throw` statement, bare
  `throw;` or expression `throw new X(…)` alike. That composite is what a handler needs to hold
  a failure and continue: a filtered catch named its expectations, and a clause ending in a
  throw suppresses nothing, so neither is swallowing. The polarity is essential for the same
  reason as the unfiltered subset — a rethrowing and a swallowing catch of one type on one
  physical line collapse to a single site, and a `#if`-divergent rethrow reaches the merge as
  one site reported both ways; recording the *swallowing* sites answers both the way a ban needs.
  **The rethrow fact is syntactic**, and that is its honesty boundary: it reads the block's
  **last statement** and never analyses whether every path
  reaches it. `catch { if (…) return; throw; }` counts as throwing even though one path leaves
  having suppressed the failure, and `catch { if (…) throw; Cleanup(); }` counts as swallowing
  even though one path rethrows. The throw's operand is never judged either — a
  translate-and-throw and a bare rethrow read identically.
- **A throw edge** is `(source type, thrown expression's static type, file:line sites)`,
  recorded at throw statements and throw expressions alike (`?? throw`, conditional and
  switch-expression arms, and the expression-bodied `=> throw new X()`). A bare rethrow
  `throw;` mints nothing — it introduces no thrown expression — while `throw ex` mints the
  variable's **static** type (a pinned asymmetry): under a strict `MustOnlyThrow`, a
  `catch (Exception ex) { …; throw ex; }` in scoped code is red, and the cure is `throw;` —
  which also preserves the stack trace. `throw null` and type-parameter throws mint nothing;
  `throw new T()` coexists with its construction edge (§4.5) — one site, two facts;
  constructed generics normalize; self-throws drop; attribution follows the catch rules.
- **Throw helpers are the honesty boundary**: `ArgumentNullException.ThrowIfNull(x)` is an
  invocation, not a throw — it mints a member-use edge (§4.5) and no throw edge. A
  helper-mediated throw is invisible to this axis, exactly as reflection construction is
  invisible to the construction axis; the member-use axis is how helper calls are governed.
- **Matching is exact, definition-level FQN**: `MustNotCatch(typeof(Exception))` does not
  flag `catch (IOException)` — the narrow catch is the good state the rule steers toward,
  and widening the match would punish it. There is no hierarchy-aware operand matching
  (growth, §11); hierarchy adjectives as operands (`arch.Types.DerivedFrom<X>()`) match
  solution-declared types only, because external types carry a shallow hierarchy (§5.2) — a
  `DerivedFrom` operand never matches an external exception type.
- **`MustOnlyThrow` is strict.** The §4.1 "`MustOnly*` complement universe =
  solution-declared" exemption is scoped to the *reference* verbs: the unavoidable-BCL-noise
  argument does not hold for throw statements — no type must throw a BCL exception, guard
  throws are enumerable — and the custom-exceptions rule's teeth are exactly the
  `throw new InvalidOperationException(…)` in scoped code. External thrown types ARE
  constrained, and the fragment carries no exemption parenthetical (§5.3) because there is no
  exemption to state. A `Type`-sugar operand (`MustOnlyThrow(typeof(TimeoutException))`)
  resolves external allowed-set members by FQN as targets always have (§4.1); an allowed type
  absent from the model resolves empty and harmlessly allows nothing.
- **`MustNotThrow` is the ban polarity beside it.** `MustOnlyThrow` enumerates what a type may
  throw; `MustNotThrow` enumerates what it may not, for the ordinary case where the forbidden
  types are a handful and the permitted ones are the rest of the world. Matching is exact,
  definition-level FQN like every other operand on this axis, so a ban on `Exception` does not
  reach a `TimeoutException` throw — the narrow throw is the good state the rule steers toward,
  exactly as with the catch verbs. The two verbs are independent statements about one fact
  family: a spec may carry either, or both.
- Rendered glossary lines, beside the reference entry and gated per axis on the §4.5
  byte-identical-without-it terms: *"catch = a source-level `catch` clause (a bare `catch`
  counts as `System.Exception`)"* whenever the spec carries any catch-verb rule; *"throw =
  a source-level `throw` of the thrown expression's type (bare rethrows `throw;` are not
  recorded)"* whenever it carries any throw-verb rule. Each clause glosses the fact, not the
  verb, so swapping one verb on an axis for its sibling renders byte-identically.

### 4.9 Exposure edges (public signature positions)

The API-surface policy — a layer's public types must not leak another layer's types through
their own signatures — needs a fact no other section records: which type a public member
*names* in a signature position. Extraction records it; the `MustNotExpose` verb (§3.3, §5.3)
consumes it.

- **An exposure edge** is `(source type, exposed type, file:line sites)`, recorded at every
  public **signature position** of an effectively-public member: a method's return type and each
  parameter type, and a property/field/event's type. The sites are the exposing members'
  declaration lines. Where the signature **textually names** its type, the exposure edge is
  minted **beside** the ordinary reference edge that type-name syntax mints (the double-mint of
  §4.5's explicit-`new` precedent) — one site, two facts, the exposure channel never standing
  in for the type edge. A signature spelling that names no type mints no twin: the wrappers
  decomposition synthesizes from tuple and `?` syntax (nothing in `(Order, Widget)` or `int?`
  spells `ValueTuple` or `Nullable`) and a predefined keyword like `int` (keywords mint no type
  edge) reach the exposure channel alone — a twin-less endpoint is the designed record of a
  signature fact the reference axis never sees, not a gap.
- **The effective-visibility pin.** An edge is minted only from a member that is itself public
  **and** whose containing-type chain is public at every level. A `public` member nested in an
  `internal` type mints **nothing**: an internal type has no external contract, so nothing it
  declares is surface. This is the honesty boundary — internal members are not the API, and a
  rule about what a type exposes must not fire on what it keeps to itself. Explicit interface
  implementations are private, so they never mint; interface members are public, so a public
  interface's own signatures do.
- **Excluded**, because none is a member's public signature: constructors (their parameters are
  the injection axis's, §4.7), base-type and interface lists (inheritance, §5.2, not members),
  indexers/operators/conversions/accessors and every compiler-generated or record-synthesized
  member (the §4.6 inventory filter), `void` returns (a void return names no type), and
  non-public members. Type parameters, pointers, and `dynamic` mint nothing (the shared
  reference-universe gates); an enum's value fields are typed as the enum itself and self-drop.
- **Decomposition is definition-level and recursive**, exactly as a reference-edge target (§4.1)
  and an injected parameter type (§4.7): a constructed generic yields its open definition **and**
  every type argument (`Task<Order>` → `Task<>` and `Order`), an array yields its element type,
  a tuple yields the open `ValueTuple` definition **and** every element type (`(Order, Widget)`
  → `(T1, T2)`, `Order`, `Widget` — the open tuple definition is recorded under its display form
  `(T1, T2)`, not `System.ValueTuple<T1, T2>`), a nullable value type yields the open definition
  **and** its argument (`int?` → `System.Nullable<T>` and `System.Int32`), and a plain named
  type yields itself. Primitives and framework types **do** mint (factual and
  harmless — an operand selection never names them), so a `public string Name` exposes
  `System.String`; the noise stays invisible until a rule chooses to constrain it.
- **The honesty boundary is static signatures only.** Laundering an exposed type behind `object`
  (or `dynamic`, or an interface the caller downcasts) is invisible to this axis — the edge
  records the *declared* signature type, not what flows through it, exactly as the construction
  axis is blind to reflection. A signature that says `object` exposes `System.Object` and nothing
  more; the cure for a laundering signature is a named growth path (§11), not a wider match here.
- **Matching is exact, definition-level FQN**: `MustNotExpose(typeof(DataTable))` flags a
  `DataTable` return but not a `DataView` one, and there is no hierarchy-aware operand matching
  (growth, §11). Self-exposure (a type naming itself in its own signature) is dropped like the
  type-edge self-drop (§4.1).
- Rendered glossary line, beside the reference entry and gated on the §4.5
  byte-identical-without-it terms: *"expose = a public signature position (return, parameter, or
  property/field/event type) on a public member of an externally visible type"* whenever the
  spec carries a `MustNotExpose` rule.

### 4.10 Project subjects (artifact facts)

`arch.Projects` selects the solution's projects as build artifacts — the third subject
stratum (§3.2). `arch.Project(name)` keeps meaning the types the project declares; the
artifact stratum is where a rule speaks about the project itself: what it targets, what it
declares as package references, whether it locks restore, whether it packs, and which MSBuild
properties it sets.

- **Facts are evaluated, never parsed from the csproj XML.** What a verb reads is what
  MSBuild evaluation answers after SDK defaults and every import: the lock policy a
  repository declares once in a shared props file, and the `IsPackable` default nothing
  declares anywhere, are both facts the raw project file does not carry. The verb facts are
  the declared target frameworks (the list is total, and a classic project's framework
  normalizes to the short moniker, `net48`), the declared `PackageReference`s, each marked
  private where its `PrivateAssets` is `all` and carrying the frameworks whose evaluation
  declared it, `RestorePackagesWithLockFile`, `IsPackable`, and the properties a declaration in
  the solution sets, recorded once per evaluation with their evaluated values.
  `MustOnlyTarget` and `MustTarget` compare monikers ordinally against the normalized short
  forms. A package pattern is a glob, `*` matching any run of characters, dots included, and it
  is compared with the package identifier ignoring case, as NuGet compares identifiers.
- **Declared references only.** The package facts are the project's own declarations — the
  transitive graph is a different fact — and every package verb says so in its sentence (§5.8)
  rather than in a doc a reader has to know to look up.
- **Private references are not dependencies.** NuGet leaves a reference out of the package a
  project produces, and out of the restore of any project referencing it, when its
  `PrivateAssets` resolves to every asset — parsed as NuGet parses it: `;`-separated,
  case-insensitive, `buildtransitive` bringing `build` with it. That is how analyzers, source
  generators and build-time tools are declared, so such a reference is private, and
  `MustReferenceNoPackages` and the allow-list `MustOnlyReferencePackages` pass it whatever its
  name. The ban `MustNotReferencePackages` counts it: a banned package is banned however it is
  declared, which makes `MustNotReferencePackages("*")` the strict form that counts every
  reference. `IncludeAssets` and `ExcludeAssets` do not decide it:
  `ExcludeAssets="all"` alone keeps a package out of the package but still hands it to a
  referencing project, and `IncludeAssets="analyzers"` alone is still packed, so both stay
  non-private until `PrivateAssets="all"` joins them. The value is the evaluated one, so an
  `ItemDefinitionGroup` default or an `Update` counts while the site stays the `Include`, and a
  `GlobalPackageReference` under central package management is private, sited at the project
  file. A package declared more than once, in one evaluation or across a multi-targeted
  project's frameworks, is private only where every declaration is: a non-private declaration
  decides and brings its site, and between declarations that agree the ordinally-first site
  stays.
- **Facts are what evaluation answers on the machine that runs it.** A condition on
  `$([MSBuild]::IsOSPlatform(...))`, on `$(OS)` or on an environment variable resolves on the
  host running SlideRule, and a binlog replay is no exception: it takes the compilations from
  the log and evaluates the project files again, on the current host, reading none of the
  evaluations the log recorded. So a framework, package or property a project declares behind
  such a condition can give one verdict on Windows and another on Linux. A law about one
  platform's build is checked on that platform, in CI on that platform's runner.
- **A property is set when a declaration in the solution sets it.** An evaluation records each
  property whose winning definition sits in the project's cone (its own directory and those
  above it, where MSBuild discovers `Directory.Build.props`) or anywhere under the solution's
  directory, which reaches a props file a project imports from a sibling `build/` directory. A
  global, reserved or environment property has no definition and is never recorded, and neither
  is a value an SDK or a package import supplies, nor one from the restore output or a package
  folder inside the tree. So `MustSetProperty` asks whether somebody who can change the value
  declared it: the `IsPackable` the SDK defaults on is not set. The property name is matched
  ignoring case, as MSBuild matches it, and the value is compared ignoring case and surrounding
  whitespace. Each evaluation is judged apart, since a `$(TargetFramework)` condition can set a
  property for one framework, and a project with no recorded evaluation passes.
- **A package carries the frameworks that declare it.** A multi-targeted project is evaluated
  once per framework, so a `PackageReference` conditioned on `$(TargetFramework)` is declared by
  the evaluations whose condition holds, and `IPackageReferenceInfo.Frameworks` lists those,
  ordinally. An evaluation run with no framework chosen answers for the project's one framework
  where it declares exactly one; where it declares several, which is what a replay that cannot
  tell a project's compilations apart runs, it saw no framework's conditions, so its packages
  carry no framework and the list is empty, meaning unknown. No verb reads the list: a
  per-framework law is a `.Must` over it (§5.6), and the survey marks a package only some
  frameworks declare.
- **Unknown passes.** A load path that cannot carry a fact records absence, never a default,
  and every project verb passes on absence: a rule that reds on a project nothing evaluated
  is reporting the load's own gaps as architecture violations, which is `modelIncomplete`'s
  channel, not a verdict's. A verb that bounds a list passes an unevaluated project for free,
  finding nothing in the empty list to judge. `MustTarget` asks for a framework to be present,
  so it tests for the empty list instead of walking it, and passes the project the same way.
- **A package pattern that matches nothing is silence.** It draws no warning, unlike a type
  operand that resolves to nothing (§4.1): no reference to a permitted or a banned package is
  an ordinary state, and a ban on retired packages is in it from the day they retire.
- **Identity is `project:{name}`.** A project has no documentation-comment ID, so the
  identity wears the DocId shape and displays verbatim. It rides baseline entries unchanged,
  so every posture composes. The verbs over a list key each item apart, as an edge from the
  project to `package:{id}` or `framework:{moniker}` (§4.3), so a migrated rule still reds on the
  next package or framework the project takes, and a TFM migration is an ordinary ratcheted
  burndown, one framework at a time. `MustTarget` keys the framework it requires the same way.
- **The site is the declaration that won the evaluation** — regularly a props file above the
  project, not the csproj — and where the defect is an absence, the site falls back to the
  project file itself. `MustReferenceNoPackages` mints one violation per declared package that
  is not private, each sited at its own declaration; `MustOnlyReferencePackages` mints one per
  non-private package no pattern matches, and `MustNotReferencePackages` one per package a
  pattern matches, private or not, each sited the same way. `MustSetProperty` mints one per
  evaluation where the property is unset or holds another value, sited at the definition that
  won, or at the project file where nothing set it. `MustOnlyTarget` mints one violation per
  target framework the rule does not permit, each sited at the declaration of the project's
  framework list. `MustTarget` mints one per project whose list lacks the required framework,
  sited at the same declaration, and its message says the project does not target it.
- **A narrowed run skips a project rule that selects nothing**, exactly as it skips a type
  rule whose subject a filter erased.

## 5. Vocabulary v1 (closed; every member ships with pinned fragments)

The test suite generates the table below from the code and fails when this file differs from what
it generates. What each member means beyond its fragment is in §5.1–§5.3, §5.7 and §5.8. The
posture verbs, trailers and escape hatches of §5.4–§5.6 are written by hand.

<!-- sliderule:begin -->
*Generated from the shipped API by the test suite: each name and its overloads by reflection, each fragment by the sentence renderer over an example operand. Do not edit by hand.*

**Type subjects**

| Noun | Overloads | Fragment |
|---|---|---|
| `arch.Types` |  | Types |
| `arch.Layer` | `(string, Selection)` · `(string, string, …)` | The Domain layer |
| `arch.Namespace` | `(string)` | Types in `MyApp.Legacy.Billing.*` |
| `arch.Project` | `(string)` | Types in project `MyApp.Web` |
| `arch.Type` | `(Type)` · `<T>()` | `SqlConnection` |
| `arch.Registered` | `()` · `(Lifetime)` | Singleton-registered types |
| `arch.AnyOf` | `(IEnumerable<Selection>)` · `(IEnumerable<Type>)` · `(Selection, …)` · `(Type, …)` | Types in `MyApp.Tracking.*` or `MyApp.Billing.*` |
| `arch.Each` | `(Layer, …)` · `(ProjectSelection)` | Each of the Host, Adapter and Pack layers |
| `arch.Member` | `(() => …)` · `(Type, string)` · `(x => …)` | `DateTime.Now` |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.InNamespace` | `(string)` | Types in `MyApp.Web.*` |
| `.OfKind` | `(TypeKind)` | Interfaces |
| `.WithSuffix` | `(string)` | Types named `*Controller` |
| `.WithPrefix` | `(string)` | Types named `Legacy*` |
| `.WithNameMatching` | `(string)` | Types whose name matches `*Repo*` |
| `.Named` | `(string, …)` | Types named `OrdersRepository` |
| `.Under` | `(string, …)` | Types under `src/Widgets` |
| `.Implementing` | `(Type)` · `(string)` · `<T>()` | Types implementing `IHandler<T>` |
| `.DerivedFrom` | `(Type)` · `(string)` · `<T>()` | Types derived from `ControllerBase` |
| `.AttributedWith` | `(Type)` · `(string)` · `<T>()` | Types attributed with `[ApiController]` |
| `.Except` | `(Selection, …)` · `(Type, …)` | Types, except `HandlerRegistry` |
| `.Where` | `(Func<ITypeInfo, bool>, string)` | Types that are records |
| `.Authored` | `()` | Authored types |
| `.ThatArePublic` | `()` | Public types |
| `.ThatAreInternal` | `()` | Internal types |
| `.ThatArePrivate` | `()` | Private types |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustNotReference` | `(Selection, …)` · `(Type, …)` | must not reference the Web layer |
| `.MustOnlyReference` | `(Selection, …)` · `(Type, …)` | must reference only `SqlConnection` (external packages are not constrained by this rule) |
| `.MustOnlyReferenceItself` | `()` | must reference only itself (external packages are not constrained by this rule) |
| `.MustNotBeReferencedBy` | `(Selection, …)` · `(Type, …)` | must not be referenced by `SqlConnection` |
| `.MustOnlyBeReferencedBy` | `(Selection, …)` · `(Type, …)` | must be referenced only by `SqlConnection` |
| `.MustOnlyBeReferencedByItself` | `()` | must be referenced only by itself |
| `.MustNotReferenceEachOther` | `()` | must not reference the others |
| `.MustNotHaveCircularReferences` | `()` | must not have circular references with the others |
| `.MustNotUse` | `(() => …, …)` · `(IEnumerable<Member>)` · `(Member, …)` | must not use `DateTime.Now` |
| `.MustNotConstruct` | `(Selection, …)` · `(Type, …)` | must not construct `SqlConnection` |
| `.MustNotInject` | `(Selection, …)` · `(Type, …)` | must not inject `SqlConnection` |
| `.MustNotCatch` | `(Selection, …)` · `(Type, …)` | must not catch `InvalidOperationException` |
| `.MustNotCatchUnfiltered` | `(Selection, …)` · `(Type, …)` | must not catch `Exception` without a `when` filter |
| `.MustNotSwallow` | `(Selection, …)` · `(Type, …)` | must not swallow `Exception` |
| `.MustNotThrow` | `(Selection, …)` · `(Type, …)` | must not throw `InvalidOperationException` |
| `.MustOnlyThrow` | `(Selection, …)` · `(Type, …)` | must throw only `InvalidOperationException` |
| `.MustNotExpose` | `(Selection, …)` · `(Type, …)` | must not expose `SqlConnection` |
| `.MustResideInNamespace` | `(string)` | must reside in `MyApp.Web.*` |
| `.MustResideInProject` | `(string)` | must reside in project `MyApp.Web` |
| `.MustBelongTo` | `(Selection, …)` | must belong to the Domain layer or the Web layer |
| `.MustHaveExactlyOneCounterpart` | `(Selection, string)` | must have exactly one counterpart named `I{Name}` among interfaces |
| `.MustBeRegistered` | `()` | must be registered |
| `.MustHaveSuffix` | `(string)` | must be named `*Handler` |
| `.MustHavePrefix` | `(string)` | must be named `I*` |
| `.MustHaveNameMatching` | `(string)` | must have a name matching `*Repo*` |
| `.MustImplement` | `(Type)` · `(string)` · `<T>()` | must implement `IBillingFacade` |
| `.MustDeriveFrom` | `(Type)` · `(string)` · `<T>()` | must derive from `ControllerBase` |
| `.MustBeAttributedWith` | `(Type)` · `(string)` · `<T>()` | must be attributed with `[ApiController]` |
| `.MustNotImplement` | `(Type, …)` · `(string, …)` · `<T>()` | must not implement `IBillingFacade` or `IHandler<T>` |
| `.MustNotDeriveFrom` | `(Type, …)` · `(string, …)` · `<T>()` | must not derive from `ControllerBase` |
| `.MustNotBeAttributedWith` | `(Type, …)` · `(string, …)` · `<T>()` | must not be attributed with `[ApiController]` or `[Serializable]` |
| `.MustBeSealed` | `()` | must be sealed |
| `.MustBeStatic` | `()` | must be static |
| `.MustBeAbstract` | `()` | must be abstract |
| `.MustBePublic` | `()` | must be public |
| `.MustBeInternal` | `()` | must be internal |
| `.Must` | `(Func<ITypeInfo, bool>, string)` | must keep type names at or under 40 characters |

**Member subjects**

| Projection | Overloads | Fragment |
|---|---|---|
| `.Members` |  | Members of types |
| `.Methods` |  | Methods of types |
| `.Properties` |  | Properties of types |
| `.Fields` |  | Fields of types |
| `.Events` |  | Events of types |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.WithSuffix` | `(string)` | `*Async` methods of types |
| `.WithPrefix` | `(string)` | `Get*` methods of types |
| `.WithNameMatching` | `(string)` | `*Handler*` methods of types |
| `.Named` | `(IEnumerable<string>)` · `(string, …)` | `Main` methods of types |
| `.Returning` | `(Type, …)` · `(string, …)` on `.Methods` | Methods returning `Task` declared in types |
| `.AttributedWith` | `(Type)` · `(string)` · `<T>()` | `[ApiController]`-attributed methods of types |
| `.ThatAreStatic` | `()` | Static fields of types |
| `.ThatArePublic` | `()` | Public methods of types |
| `.ThatAreInternal` | `()` | Internal methods of types |
| `.ThatAreProtected` | `()` | Protected fields of types |
| `.ThatArePrivate` | `()` | Private fields of types |
| `.ThatAreProtectedInternal` | `()` | Protected internal methods of types |
| `.ThatArePrivateProtected` | `()` | Private protected methods of types |
| `.Except` | `(IEnumerable<MemberSelection>)` · `(MemberSelection, …)` | Methods of types, except `Main` methods of types named `Program` |
| `.Where` | `(Func<IMemberInfo, bool>, string)` | Methods that are async declared in types |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustHaveSuffix` | `(string)` | must be named `*Async` |
| `.MustHavePrefix` | `(string)` | must be named `Get*` |
| `.MustHaveNameMatching` | `(string)` | must have a name matching `*Async` |
| `.MustBePublic` | `()` | must be public |
| `.MustBeInternal` | `()` | must be internal |
| `.MustBeProtected` | `()` | must be protected |
| `.MustBePrivate` | `()` | must be private |
| `.MustBeStatic` | `()` | must be static |
| `.MustBeAbstract` | `()` | must be abstract |
| `.MustBeVirtual` | `()` | must be virtual |
| `.MustBeOverridable` | `()` | must be overridable |
| `.MustBeAttributedWith` | `(Type)` · `(string)` · `<T>()` | must be attributed with `[ApiController]` |
| `.MustNotBeAttributedWith` | `(Type, …)` · `(string, …)` · `<T>()` | must not be attributed with `[ApiController]` or `[Serializable]` |
| `.MustAcceptParameter` | `(Type)` · `(string)` · `<T>()` on `.Methods` | must accept a parameter of type `CancellationToken` |
| `.MustBeGetOnly` | `()` on `.Properties` | must be get-only |
| `.MustBeReadonly` | `()` on `.Fields` | must be readonly |
| `.Must` | `(Func<IMemberInfo, bool>, string)` | must return a Task |

**Project subjects**

| Noun | Overloads | Fragment |
|---|---|---|
| `arch.Projects` |  | Projects |

| Adjective | Overloads | Fragment |
|---|---|---|
| `.Named` | `(IEnumerable<string>)` · `(string, …)` | Project `MyApp.Web` |
| `.Matching` | `(IEnumerable<string>)` · `(string, …)` | Projects matching `MyApp.*` |
| `.Packable` | `()` | Packable projects |
| `.Except` | `(ProjectSelection)` | Projects, except project `MyApp.Tests` |
| `.Where` | `(Func<IProjectInfo, bool>, string)` | Projects that target net8.0 |

| Verb | Overloads | Fragment |
|---|---|---|
| `.MustOnlyTarget` | `(string, …)` | must target only `netstandard2.0` |
| `.MustTarget` | `(string)` | must target `netstandard2.0` |
| `.MustReferenceNoPackages` | `()` | must reference no NuGet packages other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen) |
| `.MustOnlyReferencePackages` | `(string, …)` | must reference only NuGet packages matching `Microsoft.Bcl.*`, other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen) |
| `.MustNotReferencePackages` | `(string, …)` | must not reference NuGet packages matching `Newtonsoft.Json` (declared references only; transitive dependencies are not seen) |
| `.MustSetProperty` | `(string, string)` | must set `GenerateDocumentationFile` to `true` |
| `.MustLockPackages` | `()` | must lock package restore |
| `.MustNotBePackable` | `()` | must not be packable |
| `.Must` | `(Func<IProjectInfo, bool>, string)` | must produce a package |

<!-- sliderule:end -->

### 5.1 Nouns

What the table cannot show, per noun:

- `arch.Layer("Domain", "MyApp.Domain.*")` / `arch.Layer("Core", arch.Project("MyApp.Core"))` — definition fragment: "**Domain** — `MyApp.Domain.*`", and with a `.Purpose(prose)` "**Domain** — `MyApp.Domain.*`. {purpose}": prefix-preserving, the purpose verbatim (§5.5). A layer defined by a **selection** names exactly what that selection names, at the projects it names them at (§4.1), and its own adjectives apply after; its row spells the definition instead of globs — a bare noun as its locative without the head ("**Core** — project `MyApp.Core`", "**Kernel** — projects `A` or `B`"), anything else as its reference phrase ("**Model** — types in the Core layer in `MyApp.Core.Model.*`"). The reference fragment never carries the purpose.
- `arch.Type(typeof(SqlConnection))` / `arch.Type<SqlConnection>()` — the simple name in both positions, with the FQN retained in the model. As a subject it reads "`SqlConnection` must be sealed." and its reflexive is "itself" ("`SqlConnection` must be referenced only by itself"). Any adjective switches it to types voice with the name as its locative, "public types named `SqlConnection`", so the kind, accessibility and `Except` filters reach the sentence. The phrase then reads as `arch.Types.Named` does and still means the one type
- `arch.Registered(Lifetime.Singleton)` / `arch.Registered()` — per lifetime "scoped-registered types" and "transient-registered types", and "registered types" with no lifetime — types named in a source-visible container registration (§4.7). The fragment is the noun's **head** and survives adjectives ("Singleton-registered types, except `X`, must not inject scoped-registered types." — never a false bare "Types, …"); the §5.2 `OfKind` head-substitution mechanic, pinned by an adjective-bearing-subject test.
- `arch.AnyOf(a, b, …)` / `arch.AnyOf(typeof(X), typeof(Y), …)` — the union of its operands: "types in projects `A` or `B`" when they collapse, "types in project `A` or types in `B.*`" when they do not (§6). A union has no single noun — it is the one noun-position node that renders through its own assembly arm.
- `arch.Each(a, b, …)` / `arch.Each(arch.Projects.Matching("Nop.Plugin.*"))` — a *family* — one rule over a partition into cells: "each of the Dispatch, Tracking and Invoicing layers" (and-joined, no Oxford comma; a one-cell layer family renders as the cell) / "each of the projects matching `Nop.Plugin.*`", "each of the projects `A` or `B`" (the project selection's own phrase, §4.10; the project form always says "each of"). Locative " in each of the Dispatch, Tracking and Invoicing layers". A bare family speaks in the collective voice (§6).
- `arch.Member(typeof(DateTime), nameof(DateTime.Now))` / `arch.Member(() => DateTime.Now)` — member leaf, target-only (§4.5); parens iff method: "`Task.Wait()`" (`arch.Member<Task>(t => t.Wait())`)

**The union noun (`arch.AnyOf`)** names every type any operand names. Pinned semantics:

- **Operands may be any selection** — a `Layer`, a `Registered` noun, an already-refined
  selection, or another union.
- **Nested unions flatten at mint**: `AnyOf(AnyOf(a, b), c)` ≡ `AnyOf(a, b, c)`, so prose and
  evaluation both read one leaf list. Only an *adjective-free* union operand flattens — an inner
  union carrying adjectives is a narrowed set of its own, so it stays a leaf. An empty union stays a
  leaf too: flattened, it would vanish into its parent, where §8 item 36 could not find it.
- **One operand is legal and is an identity**: same model shape, and it renders exactly as the
  bare operand would in the same position. Selections are loop-buildable (§2 principle 5), so a
  loop that happens to yield one operand must not become an error. A loop that yields none is §8
  item 36, because a union with no operand selects no type.
- **Adjectives apply to the union, not through it**: `AnyOf(a, b).Except(c)` = (a ∪ b) − c, and
  `.Methods` projects over the union. The evaluator applies union adjectives *after* the set union.
- **The multi-type sugar** `AnyOf(Type first, params Type[] more)` desugars to
  `AnyOf(arch.Type(a), arch.Type(b), …)` — identical model, identical prose. It is the multi-type
  noun `arch.Types(params Type[])` cannot be (§3.2).
- **In subject position every operand must match at least one type** — an empty operand fails the
  rule with the empty-subject violation naming that operand, so a typo'd project name inside a
  four-way union is never masked by its siblings (§4.1). This is check-time behavior, not a §8
  spec-build error: whether an operand matches depends on the codebase, not on the spec. **Target
  position reports the same grain, one register softer**: an operand that matched nothing beside one
  that did warns "rule is partly inert" naming that operand. The warning fails nothing, and the
  rule's verdict is what the other operands find (§4.1). The register differs because the positions
  differ — an empty subject means the law ranges over nothing, while a dead target operand means one
  clause of a ban has gone quiet and the rest still bites. A `Named` list is read at the same grain
  in both positions, each of its names standing as an operand of its own (§4.1, §4.2). A union
  passed as one target operand is opened the same way, each part read before the union's own
  adjectives, so a dead part of `AnyOf(a, b)` passed as one operand warns as it would if `a` and
  `b` were passed as two.
- **A union subject anchors no scoped card**: even when a `Layer` is an operand, a union has no
  single home directory, so its rule renders into the root block only (§6).

**The family noun (`arch.Each`)** names every type any cell names, and additionally carries the
partition. Pinned semantics:

- **Cells are declared layers or projects.** `Each(Layer first, params Layer[] more)` takes bare
  layers — the typing excludes adjective-bearing cells, union cells and nested families;
  `Each(ProjectSelection)` takes one project selection, whose cells are the projects it names at
  check time, each cell being exactly what `arch.Project(name)` names (N-way declarer membership,
  generated types included). Cells are never a capture in a pattern.
- **Adjectives apply to the family**: `arch.Each(a, b, c).Except(x)` narrows every cell's members,
  and a cell's own subject is the cell intersected with what the family's adjectives left
  standing. `.Authored()` on a project family drops the generated types the cells otherwise carry.
- **One layer cell is legal and is an identity** in rendering — it reads as the cell in both
  positions — and the family verbs still apply to it; the project form's cell count is a codebase
  fact, so its sentence always says "each of".
- **A family may stand only as a rule subject** — directly, or as the source of a member
  projection (`arch.Each(a, b).Methods…`). As an operand, an `Except` payload, a union operand, a
  layer definition, a scoped selection or a boundary it is spec-build item 27 (§8): a partition
  means nothing in a position that consumes a set.
- **Exactly four places read the cells**: the `MustOnly*` reference verbs' self-allowance (§4.1),
  the three family verbs `MustNotReferenceEachOther`, `MustOnlyBeReferencedByItself` and
  `MustNotHaveCircularReferences` (§5.3), card placement (§6), and the burndown, which groups a
  ratcheted family rule's remaining violations by the cell whose law each breaks — the referenced
  cell under the inbound leaf, where the subject sits at the far end of the edge. To every other
  verb, walk and renderer a family is the union of its cells — one ID, one sentence, one baseline,
  one board row naming the cells its debt sits in, one test row.
- **Cells partition the subject, copy by copy, as declared.** A type several projects declare is
  one copy per declarer (§4.1), and a cell holds the copies it names the type at: a layer defined
  by a project holds that project's copy, a project cell likewise, and a layer defined by globs
  holds every copy. Two cells overlap where both hold one copy, and a rule over a layer family
  whose cells overlap fails with a rule error naming the type and both cells, and the project
  whose copy it is where the cells share only some copies (the same layer listed twice is the
  degenerate case). So layers defined by different projects never overlap on a type both
  compile: each copy sits in one cell and is judged there per instance, and a project family
  meets the partition by construction. The partition is read from the cells as declared, before
  the family's own adjectives, because self and the far end are read there too (§4.1): an
  adjective that removes a type from the subject leaves it in both cells and cannot cure the
  overlap. Emptiness sharpens per cell as it does per union operand (§4.1).
- **A family of layers anchors every cell's card** (§6); the project form anchors none.

### 5.2 Adjectives (reduced relative clauses)

What the table cannot show, per adjective:

- `.OfKind(TypeKind.Interface)` — noun substitution: "interfaces" (kinds: Class, Interface, Struct, Enum, Delegate; records via escape hatch in v1)
- `.Named("Program")` — the exact simple name, ordinal (§4.2); "named `A` or `B`" for several. It belongs to the `WithSuffix`/`WithPrefix` family, whose fragments already say *named*, as the member whose fragment carries no `*`; all three match their argument literally
- `.Under("src/Widgets")` / `.Under("src/*/Internal", "vendor/Compiler.cs")` — the types any one of whose declaring files is under any of the path globs (§4.2): " under `src/Widgets`", " under `A` or `B`" for several, an inline clause after the locative ("types in `Spectre.Console.*` under `src/Spectre.Console/Internal`"). For a codebase whose folders are its only structure, and for one vendored file only a path picks out. What is settled about it:
  - **Root.** Every glob is relative to the solution directory, and in the build analyzer to the model file's directory, which `render` writes beside the solution, so both read one path alike. A run with no directory to read against fails the rule closed, as a rule error, rather than reading the subject as empty.
  - **Several files.** A type is under a glob when *any* file declaring it is: a partial type is wherever any of its parts is, so the author's part of a generator-completed partial puts the type in the author's folder. The opposite of `.Authored()`'s every-file rule, for the opposite question: a location selects what lives there, where generated code is what a reader wants left out.
  - **Generated files.** A generator's document carries whatever path its host gives it. A build that writes generator output puts it under the project's `obj` directory, which a source-folder glob does not reach, and a host that writes none gives a path outside the solution directory, which no glob reaches. A type only a generator declares is under a source folder only where its host put the document there.
  - **External types** are declared in no file of the solution and are under no glob, in every position.
  - **Dead globs** are read one at a time, as a `Named` list's names are (§4.1): beside globs that match, a glob that matches nothing fails the rule on a subject, warns partly inert on a forbidden-set target and stricter than written on an allow-list or a membership, is dropped by a run over one project's compilation, and is not read inside an `Except` payload.
  - **One project's view.** On a subject the adjective is decidable from one project, whose own types carry their files. Read over other projects' types, as a target or in an `Except` payload, it is left to the whole solution, because a project compiled against another's assembly sees none of its source files.
- `.Implementing(typeof(IHandler<>))` / `.Implementing("MyApp.Web.IHandler<T>")` — the string form renders byte-identically (string anchors, below)
- `.AttributedWith(typeof(ApiControllerAttribute))` / `.AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute")` — `Attribute` suffix stripped, bracketed; the string form renders byte-identically (string anchors, below)
- `.Except(selection, …)` / `.Except(typeof(X), …)` — a parenthetical canonicalized to sentence-final, after any `Where`, and closed by the composer (§6). Several operands are the union `AnyOf` mints (§5.1): `.Except(a, b)` ≡ `.Except(arch.AnyOf(a, b))`, ", except types in `A.*` or `B.*`". The `typeof` form is the dependency verbs' sugar (§3.3): `.Except(typeof(X))` ≡ `.Except(arch.Type<X>())`
- `.Where(pred, description:)` — description verbatim — canonicalized to sentence-final (§6)
- `.Authored()` — head premodifier: "authored types", "authored interfaces" (§6)
- `.ThatArePublic()` / `.ThatAreInternal()` / `.ThatArePrivate()` — head premodifier: "public types in `MyApp.*`", "internal interfaces". Stacks with `.Authored()` in the order written: "authored public types" (§6). The accessibility the declaration writes, not one computed through containers, so a public type nested in an internal one is public. The adjective half of the family `MustBePublic` / `MustBeInternal` constrain (§5.3). Only a nested type can be private, which is what `.ThatArePrivate()` is for: a nested type reports its container's namespace and answers to its own short name (§4.2), so a namespace-scoped subject reaches it and no name can pick it out again. There is no negated spelling — `.Except(arch.Types.ThatArePrivate())` is how a subject leaves nested state out, and it renders ", except private types"

`Implementing` auto-detects open generics: `typeof(IHandler<>)` means *any* construction;
`typeof(IHandler<Order>)` means that construction exactly. Generic rendering uses declared
type-parameter names: `IHandler<T>`, `IDictionary<TKey, TValue>`.

**Hierarchy matching (checker semantics).** `Implementing` and `DerivedFrom` are
**transitive with type-argument substitution** — they read the full interface closure
(Roslyn `AllInterfaces`: an interface reached through a base class or through interface
inheritance matches) and the whole base-type chain, arguments substituted. A class extending
`HandlerBase<Order>` where `HandlerBase<T> : IHandler<T>` satisfies
`Implementing(typeof(IHandler<Order>))`. An open definition matches any construction (on the
definition name); a closed construction matches that construction exactly (on the constructed
name); a **string** anchor is always the definition arm (string anchors, below). `AttributedWith`
is **declared attributes only** — no inheritance. External (metadata)
types carry a **shallow** hierarchy: extraction records their identity but not their own
bases/interfaces/attributes, so the hierarchy adjectives never match an external **subject** (a
documented boundary; pattern and name adjectives still work on externals). The **anchor**
position is not bounded that way: a *declared* type's base chain and interface closure are walked
straight through metadata, so `DerivedFrom("Microsoft.AspNetCore.Mvc.ControllerBase")` selects a
declared controller — and reaches it through an intermediate external base too — even though the
anchor is a type the spec never compiles against. The same three
matchers back the `MustImplement` / `MustDeriveFrom` / `MustBeAttributedWith`
constraint verbs and, negated per subject over the anchor list, their `MustNot*` twins (§5.3).
Under a negative the shallow-external boundary reads in the passing direction: an anchor
reachable only through an external type's own bases/interfaces never matches, so the ban
silently passes.

**Generic sugar.** Each hierarchy adjective carries a thin generic twin —
`Implementing<T>()` ≡ `Implementing(typeof(T))`, likewise `DerivedFrom<T>()` and
`AttributedWith<T>()` (the attribute twin constrained `where T : Attribute`) — that desugars to
the identical adjective, changing nothing in the model. `arch.Type<X>()` ≡ `arch.Type(typeof(X))`
is the same idea on the noun. An **open** generic has no type-argument form, so it stays `typeof`
(`Implementing(typeof(IHandler<>))`); the sugar is for the closed/non-generic single-type case.

**String anchors.** Every single-type anchor position carries a `string` overload beside the
`typeof` form: the escape hatch for a type the spec project cannot compile against, so that
naming someone else's attribute or contract does not force a package reference on the spec just
to write the `typeof`. Three families carry them, on identical terms — **attribute** positions
(this adjective's `AttributedWith`, the `Must[Not]BeAttributedWith` verbs of §5.3, and their
member-side twins in §5.7), **hierarchy** positions (the `Implementing` and `DerivedFrom`
adjectives above, and the `Must[Not]Implement` / `Must[Not]DeriveFrom` verbs of §5.3), and the
**member type** positions of §5.7 (`.Returning`'s anchor list and `MustAcceptParameter`'s
anchor), whose matchers read the §4.6 return-type and parameter facts by definition FQN and
take the string verbatim.

The string names the type **definition**'s FQN in extraction format — which is exactly what a
report prints for it, `Attribute` suffix included for an attribute and declared type-parameter
names for a generic (`"ModelContextProtocol.Server.McpServerToolAttribute"`,
`"MyApp.Web.IHandler<T>"`) — matched ordinal-exact; it matches **any construction** of that
definition, exactly as an open-generic `typeof` anchor does. A **constructed** spelling
(`"N.MarkAttribute<System.Int32>"`, `"MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>"`) names no
definition and never matches: the hatch's stated honesty boundary, not a defect, and the one
place the string form is deliberately weaker than its `typeof` twin, which can name a
construction. Nothing is inferred from the string's shape: a dotless or suffix-less spelling is
a legal name that never matches, and only blankness is validated (§8 item 15). A string
anchor renders **byte-identically** to its `typeof` twin — the display name is the last
dot-segment outside any `<...>`, and colliding simple names widen by trailing dot-segments (§6);
in attribute position the `Attribute` suffix then strips and the brackets close over the
result — so which form a spec chose is invisible to its sentences. Overloads are homogeneous:
one call takes `typeof` anchors or strings, never a mix. The policy: `typeof` whenever the type
is referenceable — the compiler checks a `typeof`, and nothing checks a string — and the string
when it is not.

**`.Authored()`** narrows a selection to the types a person wrote — the opt-out from the §4.1
boundary that puts generator output inside a project noun. A type counts as generated when either
of two things holds:

- `System.CodeDom.Compiler.GeneratedCodeAttribute` sits on it or on any type containing it, so the
  nested types a generator emits inside an attributed container ride along without carrying their
  own attribute. This arm reads the merged symbol, which is what decides the partial case:
  `[GeneratedRegex]` puts its attribute on the generated *method*, so the author's own partial class
  stays authored while the regex types the generator emits beside it do not.
- Every file declaring it is generator output. A file is generator output when the workspace
  reports it as a source-generated document, or when its leading comment carries the
  `<auto-generated>` banner. An external type (a package's, the framework's, or one from a project
  the check leaves out) is judged by the attribute alone: its files belong to another compilation,
  and a reference to it does not always carry them.

The fact is on `ITypeInfo` as `IsGenerated` (§5.6), so a `.Where` predicate can read it directly,
and the codebase survey counts it per project and per namespace so a rule author can see how much
of a project noun is generator output before writing the rule.

The second arm exists because the attribute is not universal. A compiled Razor view carries
`[RazorCompiledItemMetadata]` and a banner and no `[GeneratedCode]` at all, so under the attribute
alone a web tier's worth of views — often outnumbering the hand-written types beside them — read as
code a rule could hold its author to.

**Every declaring file, never any.** The attribute is a claim about a type; the two file signals are
claims about a file, and a file fact lifts to a type only when it holds of every file declaring it.
The `[GeneratedRegex]` case is the worked example in reverse: the generator writes the author's own
partial class and several wholly generated types into one banner-carrying file, so an any-file rule
would hand the author's class to the generated side and put it beyond every rule they wrote it for.

Nothing infers "generated" from a file path, an `obj/` directory, or a naming convention. A path
answers a different question — may a tool edit this file — and it answers this one wrongly for
exactly the partial types above. It is also redundant: a generator whose output a path convention
would catch is already caught by its provenance or its banner. Two limits follow from reading the
compilation rather than the disk. A hand-written type carrying `[GeneratedCode]` is reported
generated, because the signals are read and never second-guessed. And output the compiler never
compiled — XAML `.g.cs`, T4, anything an MSBuild task adds after the design-time build — is absent
from the model rather than misclassified in it, so no arm here can reach it.

### 5.3 Constraint verbs

What the table cannot show, per verb:

- `.MustOnlyReferenceItself()` — the leaf form: the refined subject is the whole allow-set, which every `MustOnlyReference` carries implicitly (§4.1). On a family subject (§5.1) the allow-set is each cell as declared, and the types voice reads "must reference only their own layer (external packages are not constrained by this rule)" — "project" for a project family; on a plain subject the types voice reads "must reference only themselves (external packages are not constrained by this rule)" and the collective voice keeps "itself" (§6)
- `.MustOnlyBeReferencedByItself()` — the inbound leaf: on a plain subject the allowed sources are the refined subject, a hermetic set; on a family (§5.1) each cell as declared, and the types voice reads "must be referenced only by their own layer" / "their own project"; on a plain subject in types voice "must be referenced only by themselves" (§6). Nullary (§10): the operand is the subject's own structure
- `.MustNotReferenceEachOther()` — "must not reference the others" in the collective voice, "must not reference the other layers" / "must not reference the other projects" in types voice — family subjects only (§5.1, §8 item 28): an owned instance whose far end lies in any other cell, as declared, is a violation. Symmetric, so it has no inbound twin. Nullary (§10)
- `.MustNotHaveCircularReferences()` — "must not have circular references with the others" in the collective voice, "must not have circular references with the other layers" in types voice — families of layers only (§5.1, §8 item 29): the cells may reference one another, but the cell graph — an arrow from cell to cell for every owned instance crossing between them — must hold no strongly connected component of more than one cell, and a violation is every type pair on every arrow inside such a component (§4.3). A one-cell family passes. Nullary (§10)
- `.MustNotUse(member, ...)` — member targets (§4.5)
- `.MustNotConstruct(target, ...)` — selection/type targets; the DI-construction verb (§3.3)
- `.MustNotInject(target, ...)` — selection/type targets; the captive-dependency verb (§3.3, §4.7). Never warns: an empty `Registered` operand means no such registrations exist — the win condition, the §4.1 bare-`typeof` precedent
- `.MustNotCatch(target, ...)` — selection/type exception targets; the catch verb (§3.3, §4.8)
- `.MustNotCatchUnfiltered(target, ...)` — selection/type exception targets; the filter-aware catch verb (§3.3, §4.8). A bare `catch` counts as `System.Exception` and counts as unfiltered; filter presence is syntactic, so the filter's contents are never judged
- `.MustNotSwallow(target, ...)` — selection/type exception targets; the rethrow-aware catch verb (§3.3, §4.8), banning the composite of three facts: the caught type matches, the clause spells no `when` filter, and its block does not end in a `throw`. Both conditions are syntactic — the throw fact is the block's last statement, never an all-paths analysis — so a filtered catch and a rethrowing catch are both lawful
- `.MustNotThrow(target, ...)` — selection/type exception targets; the ban polarity beside `MustOnlyThrow` (§3.3, §4.8), for the case where the forbidden thrown types are enumerable and the permitted ones are not
- `.MustOnlyThrow(target, ...)` — selection/type exception targets (§3.3, §4.8); **strict**: external thrown types are constrained too, so the fragment carries no external-packages parenthetical — the caveat's absence is the strictness rendering
- `.MustNotExpose(target, ...)` — selection/type targets in a public signature position; the signature-exposure verb (§3.3, §4.9)
- `.MustResideInProject(name)` — declared-by membership (§4.1): any declarer of a multiply-declared type satisfies it, so the verb agrees with `arch.Project`; single-name arity on `MustResideInNamespace`'s pattern (§10), several projects being `MustBelongTo` with project memberships. A project name no solution project carries warns as `MustBelongTo(arch.Project(name))` would (below)
- `.MustBelongTo(membership, ...)` — the coverage verb (§10): memberships are selections naming where a type may live (layers, projects, namespaces), resolved in subject position and or-joined with the reference phrases (§6), so the any-of reading is stated by the sentence itself; a subject type no membership names is red. A membership, or a union part, layer definition piece or `Named` name of one, that matches no solution type warns **"rule is stricter than written"** naming it (§4.1), since every subject it was meant to hold reds; a `typeof` membership stays silent. Deliberately no `Type` sugar — a bare `typeof` membership would degenerate into "must be that type"
- `.MustHaveExactlyOneCounterpart(among: selection, named: "I{Name}")` — the correspondence verb (§4.3, §10): per subject, every `{Name}` in the template is replaced by the subject's simple name — ordinal and case-sensitive, arity-free, nested types by leaf name — and exactly one type in `among:` must carry the derived name; zero counterparts and several are both red, keying the subject either way. The template renders backticked and unsubstituted, as `MustHaveSuffix` renders `*Handler`: the sentence states the law, and the law is the pattern. Deliberately one `among:` selection — a params list would reopen ALL/ANY in a worse form, exactly-one-across-the-union versus exactly-one-per-operand, which no single sentence distinguishes — so authors union with `arch.AnyOf`, whose or-join states the reading; exactly-one is per subject, never a bijection. An `among:` matching nothing reds every subject — a false red, not a miss (§4.7) — and each dead pattern in it, read per part and per name as a membership is, warns **"rule is not measured as written"** naming it (§4.1): a dead part both reds a subject whose only counterpart it held and passes one it would have made ambiguous
- `.MustBeRegistered()` — nullary; membership is `arch.Registered()`'s at any lifetime (§4.7), the false-red polarity of the honesty boundary riding with it
- `.MustImplement(type)` — also anchors by string (§5.2's escape hatch): `MustImplement("MyApp.Web.IHandler<T>")`
- `.MustDeriveFrom(type)` — string form likewise
- `.MustBeAttributedWith(type)` — also anchors by string (§5.2's escape hatch): `MustBeAttributedWith("N.XAttribute")`
- `.MustNotImplement(type, ...)` — none-of over the anchors; the negatives take `(Type first, params Type[] more)` (§10), and `(string first, params string[] more)` (§5.2), each call's list homogeneous
- `.MustNotDeriveFrom(type, ...)` — same two arities
- `.MustNotBeAttributedWith(type, ...)` — anchors bracketed and `Attribute`-stripped like the positive; also `(string first, params string[] more)` (§5.2), each call's list homogeneous

Naming note: the constraint carries its noun where a bare preposition would be ambiguous —
`MustResideInNamespace`, not `MustResideIn`, because the `Project` noun exists.

The reside pair is `MustBelongTo` with one membership: `MustResideInNamespace(g)` checks exactly
as `MustBelongTo(arch.Namespace(g))` does, and `MustResideInProject(n)` exactly as
`MustBelongTo(arch.Project(n))`. It warns exactly as that form does too: a glob or a project
name that matches no solution type raises the same stricter-than-written warning, with the same
key, message and cure. The pair exists for the tighter sentence one membership
allows: "must reside in project `X`", where the general form says "must belong to types in
project `X`".

Generic sugar: the three type-taking hierarchy verbs carry generic twins —
`MustImplement<T>()` ≡ `MustImplement(typeof(T))`, `MustDeriveFrom<T>()`, and
`MustBeAttributedWith<T>()` (`where T : Attribute`) — desugaring to the identical constraint, on
the same terms as the §5.2 adjective twins (open generics stay `typeof`).
The negatives carry the same twins — `MustNotImplement<T>()` / `MustNotDeriveFrom<T>()` /
`MustNotBeAttributedWith<T>()` (`where T : Attribute`) — each desugaring to its verb's
single-anchor call. Every one of these verbs additionally carries the §5.2 string form on both
polarities — the attribute verbs on both sides too (type and member, §5.7) — so every anchor
position in both families offers the same triple: the compile-checked `typeof`, the
no-reference string, and the generic sugar (§10).

### 5.4 Posture verbs and options

| Member | Notes |
|---|---|
| `.Enforce(constraint)` | the law; violation = red |
| `.Migrate(from: prose, to: constraint)` | `from` is descriptive prose (the OLD pattern); `to` is the checkable target constraint |
| `.Quarantine(selection)` | scope statement; desugars per §7 |
| `.Caution(selection)` | scope statement; desugars per §7 to the tripwire alone; severity warning, never red |
| `.Baseline(path)` | Migrate **and** Quarantine; ratcheted grandfather store |
| `.WhileYoureThere(MigrationPolicy)` | `MigrateIfSmall` (default) \| `AlwaysMigrate` \| `NeverMigrate` |
| `.BoundaryOnlyVia(Selection first, params Selection[] more)` | the sanctioned surface; omit entirely for a hermetic quarantine |
| `.BoundaryOnlyVia(params Type[])` | the same, as types — ≡ `arch.Type(t)` per operand (§3.3) |
| `.Dragons(prose)` / `.DragonsDoc(path)` | structural-weirdness prose / linked long-form doc |

### 5.5 Trailers

| Member | Notes |
|---|---|
| `.Because(prose)` | **required** on every rule and scope (§8 item 3) |
| `.Fix(prose)` | optional; for Quarantine containment it is auto-derived from `BoundaryOnlyVia` ("use `IBillingFacade`") and not author-overridable — `IQuarantinedScope` carries no `.Fix`, and neither does `ICautionedScope`: a caution's "what to do instead" is the sanctioned-interaction clause its dragons prose already carries |
| `.Purpose(prose)` | optional; `Layer` only: the one trailer on a definition (§3.2). Renders verbatim after the definition fragment in the module-map row (§5.1) and after the first sentence of the layer card's lede ("This directory contains the `Host` layer. {purpose} Its architecture rules:"); never places a card of its own — a layer earns a card only through an anchored Enforce/Migrate rule. Blank or multi-line is §8 item 5 and a second call item 6, both spec-wide and named by layer. |
| `.Citation(uri)` | optional; `Enforce` and `Migrate` rules only — a scope documents itself through `Dragons`/`DragonsDoc`. The canonical page the rationale rests on, as an absolute `http`/`https` URL (§8 item 30; blank or multi-line is item 5, a second call item 6). Renders as a sentence of its own, `See <{url}>.`, after the reason in the rule's context bullet — and for a Migrate rule between the reason and the boy-scout policy, so the policy still ends the paragraph. `explain` and a failed rule's `check` block print it as a `citation:` line after `because:`; `check --json` carries it as `citation` after `fix`, elided at index grain with the rest of the prose; SARIF publishes it as the descriptor's `helpUri` and, joined to the `Fix`, inside `help.text`/`help.markdown`. |
| `.Exemplar(selection)` | optional; `Enforce` and `Migrate` rules only, one call (a second is §8 item 6), several types through one union (§5.1). Names the type to copy when fixing a violation, and never takes part in the verdict. `check` resolves it after the verdict, against solution-declared types as it resolves a subject. A failed rule's block prints `exemplar: {Name} ({file}:{line})` after `fix:`, each resolved type at its first declaration site, joined with `, `, and prints no line when nothing resolved. `check --json` carries it as `exemplar` after `citation`, a list of `{type, sites}` (full name, ordinal order, every declaration site), absent when nothing resolved and elided at index grain with the prose. SARIF appends `Exemplar: {Name} ({file}:{line}).` to `help` after the `Fix`. `explain` prints it as written (`exemplar:` after `fix:`), and so does the build-time warning (`Exemplar: {phrase}.` after `Fix:`); neither extracts. Each part that matches nothing (a union operand, one name of a `Named` list, or the whole) raises its own `deadExemplar` warning, `` This rule's exemplar "{reference}" matched no solution-declared types. ``, each part quoted as it would read alone and hinted with the exact-name cure. That warning turns a passing rule's marker to `warn` and counts in `status`, and never feeds the empty-subject skip, the measured-in-full reading, promotion or the baseline refusal. A narrowed run treats it as it treats a subject: under a solution filter dead parts stay silent (§4.1), and a run over one project's compilation skips the exemplar. An exemplar that throws while resolving (a closed generic, a throwing `Where`) warns `This rule's exemplar could not be resolved: {fault}` and leaves the verdict alone. A `Where` anywhere in the exemplar keeps the rule out of the model file the build reads, as a `Where` anywhere else in the rule does. |

### 5.6 Escape hatches

Predicate input contract (`ITypeInfo`): `Name`, `Namespace`, `Kind`, `ProjectName`,
`Accessibility`, `IsSealed`, `IsStatic`, `IsAbstract`, `IsRecord`, `IsGenerated`, `FilePaths`
(declaration file paths, the ones `.Under` reads (§5.2); empty for external types), attributes, base type, implemented
interfaces. The
contract grows additively as extraction learns new facts. The flags carry C# declaration
semantics — a static class is neither sealed nor abstract, interfaces are abstract,
structs/enums/delegates are sealed — and `IsRecord` is how record rules are written in v1
(§5.2). `IsGenerated` is the fact `.Authored()` filters on (§5.2), readable here so a predicate
can combine it with anything else the contract carries.

Member-predicate input contract (`IMemberInfo`, the input to a member `.Where`/`.Must`, §4.6):
`Name`, `Kind` (`MemberKind`: Method / Property / Field / Event), `DeclaringType` (an
`ITypeInfo` — the type-side contract, reused so a member predicate can reach its declaring
type's facts), `Accessibility`, `IsStatic`, `IsAbstract`, `IsVirtual`, `IsOverride`, `IsSealed`, `IsAsync`,
`ReturnTypeFullName` (methods; `System.Void` for a void method; null otherwise),
`MemberTypeFullName` (the property/field/event type; null for methods), `Parameters` (the
declared parameters in declaration order — each an `IParameterInfo` of `Name` and
`TypeFullName`, the type definition-normalized exactly like `ReturnTypeFullName`; empty for
properties, fields, and events), `Attributes` (the member's declared attributes,
ordinal-sorted by constructed name — each an `IAttributeInfo` of `DefinitionFullName` and
`FullName`, the definition open-reduced; empty when none; declared-only per §4.6, so accessor
and `[return:]` attributes are outside it), `HasSetter` and `HasInitOnlySetter` (a property's
setter shape — any setter at all, `private set` and `init` included, then the init-only
refinement of it; both false for every non-property), `IsReadOnly` and `IsConst` (field
writability; both false for every non-field, and disjoint from each other), and `FilePaths` (declaration file paths). The flags carry the same C# declaration semantics as the member axis
(§4.6): an `override` member is not `IsVirtual` but is `IsOverride`, only a `sealed override` is
`IsSealed`, an interface member without a body is `IsAbstract`, a property
with an `init`-only setter has a setter. The
contract grows additively, exactly like `ITypeInfo`.

Project-predicate input contract (`IProjectInfo`, the input to a project `.Where`/`.Must`,
§4.10): `Name`, `TargetFrameworks` (ordinal-ordered, normalized to the short moniker; empty
where nothing evaluated the project), `PackageReferences` (every declared package, private ones
included, ordinal by name, each an `IPackageReferenceInfo` of `Name`, `FilePath` and `Line` (its
declaration site, which may be a props file above the project), `IsPrivate`, and `Frameworks`
(the target frameworks whose evaluation declared it, ordinal; empty where the evaluation could
not tell them apart) — declared references only, §4.10),
`ProjectReferences` (the names of the projects it declares a reference to, ordinal-ordered;
declared references only, as for the packages), `Properties` (every property a declaration in the
solution sets, one entry per property and evaluation, ordinal by name, each an
`IProjectPropertyInfo` of `Name`, `Value` (evaluated), `TargetFramework` (`null` where the
evaluation told no framework apart), `FilePath` and `Line` (the definition that won); empty where
nothing evaluated the project, §4.10), and the tri-state
`IsPackable` and `LocksPackages` — `bool?`, `null` meaning the evaluation never happened
rather than that the property is off. A predicate that treats unknown as false asserts
something the model never measured; treat unknown as passing, exactly as the project verbs do
(§4.10). This contract grows additively too.

Descriptions are **required parameters** (uncompilable without) and must be non-blank (§8
item 5). Phrasing conventions, pinned by example tests:

- `Where` descriptions are relative clauses continuing the noun phrase:
  `.Where(t => t.Name.Any(char.IsDigit), description: "whose name contains a digit")`
  → *"types in `MyApp.*` whose name contains a digit"*.
- `Must` descriptions are bare-infinitive verb phrases completing "must …":
  `.Must(t => t.Name.Length <= 40, description: "keep type names at or under 40 characters")`
  → *"must keep type names at or under 40 characters"*.

Descriptions are spliced verbatim (never derived from lambda source — Shouldly-style
`CallerArgumentExpression` is explicitly rejected; lambda source is not agent-consumable
prose).

**Which anchor forms survive a `net48` spec.** A spec project may target .NET Framework; the
host loads its DLL in an isolated load context and runs `Define()` there. A `typeof()` anchor
loads the anchored type's whole closure at that point, so it holds exactly while that closure
stays inside netstandard2.0. Two boundaries lie past it, both reported as spec-load errors
rather than crashes: an assembly that is not staged beside the spec DLL, and a type whose base
type or implemented interface lives in a Framework-only assembly (`System.Web.IHttpHandler`,
say). Neither is reachable by `typeof()` however the spec project is built. The anchor for
anything on that side of the line is a namespace pattern, `arch.Namespace("System.Data.*")`,
which needs no assembly load. A name pattern is not a substitute, and neither is an exact name:
a `Selection` matches only types inside the checked codebase, so
`arch.Types.WithNameMatching("SqlConnection")` and `arch.Types.Named("SqlConnection")` alike go
inert against an external type (§8, the inert-rule warning). The string anchors (§5.2) are the same
no-load form in attribute and hierarchy position: each names a type **definition** the spec
project never loads — `[McpServerTool]` on a codebase's methods is matchable without the spec
referencing the SDK that declares the attribute, and so is `IHandler<T>` on its types, which no
`typeof` spelling can offer. A namespace pattern remains the anchor for everything the string
form cannot reach, because a string anchor still names one definition rather than a set.

### 5.7 Member vocabulary (member subjects, §4.6)

**Projections** (mint a `MemberSelection`; the fragment is the subject head, §6):

- `.Methods` — the selection is a `MethodSelection`, so `.Returning` and `MustAcceptParameter` are available
- `.Properties` — the selection is a `PropertySelection`, so `MustBeGetOnly` is available
- `.Fields` — the selection is a `FieldSelection`, so `MustBeReadonly` is available

**Member adjectives** (narrow the member set; each one's placement in the sentence is given beside it, §6):

- `.WithSuffix("Async")` / `.WithPrefix("Get")` / `.WithNameMatching("*Handler*")` — head prefix naming the members: "`*Async` methods of types in `MyApp.*`", "`Get*` methods of types", "`*Handler*` members of types". The affixes are literal and the glob is a §4.2 pattern, both over the member's own name. A head prefix where the type-side twins are inline clauses, for member `Named`'s reason: an inline " named `*Async`" after the type reference would read as naming the types, and `arch.Types.Methods.WithSuffix("Speak")` and `arch.Types.WithSuffix("Speak").Methods` would render one sentence for two different subjects
- `.Named("Main")` — head prefix naming the members: "`Main` methods of types named `Program`", "`Main` or `Run` methods of types". The exact name, ordinal, reaching every overload of a method, with a `*` read as a literal character, as on the type side (§5.2). A head prefix where the type-side twin is an inline clause, because an inline " named `Main`" after the type reference would read as naming the types. Each name of a subject's list that no member carries fails on its own (§4.6)
- `.Returning(typeof(Task))` / `.Returning("System.Threading.Tasks.Task")` — declaration-level (§4.6); an open generic renders declared type-parameter names ("returning `Task<TResult>`"); multiple anchors join "returning `Task` or `Task<TResult>`", colliding simple names widening as in §6; the string form is §5.2's escape hatch, rendering byte-identically. Methods-only. A trailing clause on the kind-plural: the subject joins its types with "declared in" rather than "of", so the clause sits between the two nouns and cannot be read as narrowing the types: "methods returning `Task` declared in types in `MyApp.Web.*`" (§6)
- `.AttributedWith(typeof(McpServerToolAttribute))` / `.AttributedWith("ModelContextProtocol.Server.McpServerToolAttribute")` — head prefix: "`[McpServerTool]`-attributed" — premodifies the kind-plural (§6), so the subject reads "`[McpServerTool]`-attributed methods of types in `SlideRule.*`". Declared member attributes only (§4.6); the string form is §5.2's escape hatch, rendering byte-identically
- `.ThatAreStatic()` — head prefix: "static" — premodifies the kind-plural (§6), so the subject reads "static fields of the Core layer". A member **shape** adjective (§11); head prefixes stack across families and concatenate in authoring order
- `.ThatArePublic()` / `.ThatAreInternal()` / `.ThatAreProtected()` / `.ThatArePrivate()` / `.ThatAreProtectedInternal()` / `.ThatArePrivateProtected()` — head prefix naming the declared accessibility: "protected fields of types in `MyApp.Services.*`", "private protected methods of types". Each matches its level exactly, so `.ThatAreProtected()` leaves out the `protected internal` and `private protected` members, which have adjectives of their own, and a class member written with no modifier is private. All six levels ship where the type side ships three (§5.2), because the laws that need the protected levels are laws about members: a base class's fields and hooks are what an inheritor reaches. The adjective half of the family `MustBePublic`, `MustBeInternal`, `MustBeProtected` and `MustBePrivate` constrain; the prefixes stack with `.ThatAreStatic()` and the attribute prefix in authoring order
- `.Except(memberSelection, …)` — ", except `Main` methods of types named `Program`" — a parenthetical rendered last, after the type reference and so after any member `Where`, and closed by the composer, as on the type side (§5.2, §6). Each operand is a member selection over types of its own (`arch.Types.Named("Program").Methods.Named("Main")`), several or-joined, and is resolved and subtracted on §4.6's terms. It is the exemption form for a member a law must not reach when no name or kind picks it out of the subject alone
- `.Where(pred, description:)` — description verbatim — a trailing clause on the kind-plural like `Returning`, after any `Returning` clauses whatever the chain order: "fields that are not constants declared in types". The description completes the subject as a relative clause, so it cannot premodify the head as the other member adjectives do; after the type reference, `arch.Types.Fields.Where(p, "that are X")` would render the sentence `arch.Types.Where(q, "that are X").Fields` renders, "Fields of types that are X", for a different subject (§6)

**Member modal verbs** (turn a `MemberSelection` into a terminal `Constraint`):

- `.MustHaveSuffix("Async")` — reuses the type-side naming fragment (§5.3)
- `.MustBeProtected()` — member-only vocabulary (no type-side twin, deliberate); exact like its siblings, so a `protected internal` or `private protected` member fails it
- `.MustBePrivate()` — member-only vocabulary (no type-side twin, deliberate)
- `.MustBeVirtual()` — member-only vocabulary (no type-side twin, deliberate)
- `.MustBeOverridable()` — member-only vocabulary, the wider twin of `MustBeVirtual`: a member passes when a derived type can still override it, `IsVirtual || IsAbstract || (IsOverride && !IsSealed)` over the §4.6 flags, so an open override passes where `MustBeVirtual` reds it. It reads the declaration alone: an unsealed override in a sealed class passes, though nothing can derive from the class (whether a class can be derived from is `MustBeSealed`'s question), an interface member passes when it has no body or a default one, and a field never passes
- `.MustBeAttributedWith(typeof(X))` — reuses the type-side fragment verbatim (§5.3); also anchors by string (§5.2) and carries a generic twin (§10)
- `.MustNotBeAttributedWith(type, ...)` — none-of over the anchors, the type-side negative's `(first, more)` shape and widening; string form included
- `.MustAcceptParameter(typeof(CancellationToken))` — methods-only (it lives on `MethodSelection`, like `.Returning`, §3.2); single-anchor arity; matching is definition-level (§4.6): a non-generic anchor matches exactly, an open-generic anchor matches any construction and renders declared type-parameter names ("… of type `IProgress<T>`"), a closed-generic `typeof` anchor is refused at spec build (§8 item 20); also anchors by string (§5.2) and carries a generic twin (§10)
- `.MustBeGetOnly()` — properties-only (it lives on `PropertySelection`, like `.Returning` on `MethodSelection`, §3.2); nullary; **strict**: a property with an `init`-only setter reds, because get-only is a claim about the declaration and an init-only setter is a setter
- `.MustBeReadonly()` — fields-only; nullary; a `const` field **satisfies** it, const being readonly's superset, so the verb asks for the weakest thing that closes the hole
- `.Must(pred, description:)` — `pred` is `Func<IMemberInfo, bool>` (§5.6)

The naming verbs reuse the type-side "must be named" / "must have a name matching" strings
verbatim; `MustBeProtected`, `MustBePrivate`, `MustBeVirtual` and `MustBeOverridable` are
member-only vocabulary.

The member attribute vocabulary (`AttributedWith` + `Must[Not]BeAttributedWith`) reads the
§4.6 attribute facts. The **verbs** reuse the
type-side fragments verbatim — verb position is unambiguous, so there is nothing to
disambiguate. The **adjective** cannot: an inline " attributed with" clause would render
the type-side adjective before a projection and the member adjective after one as **one
byte-identical sentence** for two different subjects — every method of an attributed type,
versus the attributed methods of any type — and both attachment sites are live in real
specs. So the member adjective is a **head prefix**, on the same grounds as `.Authored()`
(§5.2, §6) and for the same reference-position ambiguity: its fragment premodifies the kind-plural
("`[McpServerTool]`-attributed methods of types in `SlideRule.*`"), and the two
subjects can never render alike. Stacked member head prefixes **concatenate** in authoring
order, as the type side's do (§6): "`[ApiController]`-attributed `[Audit]`-attributed methods
of types". Two attribute adjectives are an intersection, and a sentence that dropped one would
describe a wider subject than the checker uses. A repeated member prefix renders each time,
where a repeated type prefix renders once (§6). The attribute prefix names its attribute by the
short name, so `Billing.AuditAttribute` and `Sales.AuditAttribute` both read
"`[Audit]`-attributed", and dropping the second would widen the subject. The shape
adjective `.ThatAreStatic()` head-prefixes for the same reason and stacks the same way, so
prefixes concatenate across both families ("static `[Audit]`-attributed fields of
types"), and so do the six accessibility adjectives ("static private fields of types") and the
name prefixes: `Named`, the two affixes and the glob ("`Get*` `*Async` methods of types"). The generic twin is receiver-typed rather than `TSelf`-generic (§10): C# has no
partial type inference, so the sugar ships as one overload per concrete member selection —
`MemberSelection` for `.Members`/`.Events`, and `MethodSelection`, `PropertySelection` and
`FieldSelection`, each keeping its own kind-only vocabulary reachable after it.

`MustAcceptParameter` is methods-only — receiver-typed to
`MethodSelection` exactly like `.Returning`, so a parameter constraint on a property, field,
or event never type-checks. Its fragment is deliberately "a parameter of type `X`",
article-safe for arbitrary type names ("a/an `X` parameter" garden-paths on `a Order` /
`an IHandler`). It consumes the `Parameters` facts of §5.6 and evaluates as a member-shape
constraint (§4.6): a subject method passes iff any declared parameter's `TypeFullName`
matches the anchor's definition FQN.

`MustBeGetOnly` and `MustBeReadonly` are kind-scoped the same way, for the same reason: what
each verb tests is a fact only one member kind has, so the
receiver type is what keeps a setter question off a field and a `readonly` question off a
property, with no validation rule to write and no runtime refusal to render. Both read the §4.6
mutability facts and evaluate as member-shape constraints, and both are strict about the
declaration rather than about the writes: a property passes iff it declares no setter accessor
at all, and a field passes iff it is `readonly` or `const`. The receiver-typed generic sugar
is one overload per projection type (above): a projection that carries a kind-only verb
carries its sugar overload too, or the verb silently stops compiling after the sugar.

### 5.8 Project vocabulary (project subjects, §4.10)

**The noun** (mints a `ProjectSelection`; the fragment is the subject head, §6):

- `arch.Projects` — every solution-declared project, as a build artifact (§4.10)

**Project adjectives** (reduced relative clauses on the project set):

- `.Named("SlideRule")` — the head itself: "project `SlideRule`" for one name, "projects `A` or `B`" for several
- `.Matching("Zphil.*")` — head becomes "projects matching `Zphil.*`" ("matching `A` or `B`" over several globs)
- `.Packable()` — head prefix: "packable" — premodifies the head ("packable projects", "packable project `A`"), the set named by the evaluated fact that admits membership (§4.10)
- `.Except(...)` — ", except project `X`" (", except projects `A` or `B`") — the same parenthetical as the type-side clause, sentence-final after any `Where` and closed by the composer (§6). Single-operand by design: `.Named(a, b)` already says "either of these", and the stratum has no `AnyOf` to desugar a list to (§10)
- `.Where(pred, description:)` — description verbatim — canonicalized to sentence-final (§6), over `IProjectInfo` (§5.6)

**Project modal verbs** (turn a `ProjectSelection` into a terminal `Constraint`):

- `.MustOnlyTarget("netstandard2.0")` — "must target only `netstandard2.0`" ("only `netstandard2.0` or `net8.0`" for several) — strict on the `MustOnlyThrow` model, and strict by omission: the declared-framework universe is closed, so no external remainder exists for a caveat to disclaim (contrast §4.1)
- `.MustTarget("netstandard2.0")` — "must target `netstandard2.0`" — the floor `MustOnlyTarget` is the ceiling of, single-operand (§10), with no caveat on the same closed-universe argument
- `.MustReferenceNoPackages()` — zero-arity (§10); both boundaries ride in the sentence, "other than" so the first cannot read as a second `, except` clause
- `.MustOnlyReferencePackages("Microsoft.Bcl.*")` — "must reference only NuGet packages matching `Microsoft.Bcl.*`, other than private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen)" ("matching `A` or `B`" for several) — `MustReferenceNoPackages` with an allow-list, keeping both its boundaries; the privacy clause is set off by commas so it reads as an exemption rather than as a narrowing of the list
- `.MustNotReferencePackages("GreenPipes")` — "must not reference NuGet packages matching `GreenPipes` (declared references only; transitive dependencies are not seen)" — no privacy clause, because privacy exempts nothing from a ban
- `.MustSetProperty("Nullable", "enable")` — "must set `Nullable` to `enable`" — no caveat, because "set" already reads as declared by somebody, the boundary §4.10 keeps; judged per evaluation
- `.MustLockPackages()` — "must lock package restore" — zero-arity (§10), over the evaluated `RestorePackagesWithLockFile`
- `.MustNotBePackable()` — "must not be packable" — zero-arity (§10), over the evaluated `IsPackable`, which the SDK defaults on
- `.Must(pred, description:)` — `pred` is `Func<IProjectInfo, bool>` (§5.6)

## 6. Sentence assembly

- Fragments are lowercase; the renderer capitalizes sentence-initially. Identifiers and globs
  render in backticks; attributes as `[X]`; target lists join as "`A`, `B` or `C`".
- Enforce sentence: `{subject} {verb-phrase}.` → *"Types implementing `IHandler<T>` must be
  named `*Handler`."*
- **Layer voice**: a bare `Layer` subject speaks collectively — *"The Domain layer must not
  reference the Web layer."* Any adjective switches to types voice — *"Types in the Web layer
  named `*Controller` must not reference `SqlConnection`."* The switch is structural
  (adjective count > 0), hence deterministic. What defines the layer never reaches the sentence:
  a layer named as its project speaks the same collective sentence a glob-defined one does.
  A bare family (§5.1) speaks collectively too — *"Each of the Host, Adapter and Pack layers
  must not reference the others."*, *"Each of the projects matching `Nop.Plugin.*` must not
  reference the others."* — and any adjective switches it to types voice: *"Types in each of
  the Dispatch, Tracking and Invoicing layers, except types in
  `Meridian.Operations.Dispatch.Contracts.*`, `Meridian.Operations.Tracking.Contracts.*` or
  `Meridian.Operations.Invoicing.Contracts.*`, must be referenced only by their own layer."* The
  family verbs' phrases follow the voice — "the others" / "with the others" / "itself"
  collectively, "the other layers" / "with the other layers" / "their own layer" (or "projects" /
  "project") in types voice; the circular-references tail names the layers so that a types-voice
  subject is not heard as a claim about the types inside one layer — and on a plain subject
  the leaf verbs' reflexive agrees with the head the same way: "itself" collectively, where the
  subject is one layer, and "themselves" in types voice, where the head is the plural "types".
  A bare `Type` subject reads as its name, as a bare union of types already does, and takes the
  singular reflexive: *"`SqlConnection` must be referenced only by itself."* Any adjective switches it to
  types voice by the same structural rule, with the name as the locative: *"Public types named
  `SqlConnection` must be sealed."*
- **Canonicalization**: `Except` and `Where` clauses render sentence-final regardless of
  chain position. Safe because selection algebra commutes — (T∖X)∩S = (T∩S)∖X — and it
  prevents garden-path sentences ("types, except `Foo`, named `*Service`"). Among the
  sentence-final clauses themselves, `Except` renders last: every `Where` first in the order it
  was written, then every `Except`, so the parenthetical is the last thing before the junction
  that closes it. *"Types in `MyApp.*` whose name contains a digit, except `SqlConnection`, must
  be sealed."* A `Where` written after an `Except` therefore stops reading as a description of
  the exception.
- **The Except clause is a parenthetical, and the composer closes it.** Its fragment opens with
  a comma (", except `Foo`") and never closes itself: one phrase serves subject and reference
  position alike, so only the junction that follows knows whether text follows. Wherever running
  text does, a comma closes the clause — before the verb (*"Types in the Host layer, except
  `McpServerCommand`, must not use …"*, *"Methods returning `Task` declared in types in
  `MyApp.*`, except `SqlConnection`, must be named `*Async`."*), before the final joiner of a list whose penultimate item ends open (*"must not reference types
  in `MyApp.Legacy.*`, except `SqlConnection`, or `SqlCommand`"*), before a union's own clauses
  when its last operand ends open, and before a verb phrase's tail (*"…, except
  `TimeoutException`, without a `when` filter"*). A sentence-final period closes it by itself
  (*"must not reference types in `Y`, except `Z`."*), and so does a bracketed tail (*"…, except
  `SqlConnection` (external packages are not constrained by this rule)"*). A `Where` never
  follows an `Except` in the rendered sentence (above), so a phrase ends open exactly when it
  carries an `Except`.
- **Head premodification**: `.Authored()` and the accessibility adjectives (`.ThatArePublic()`,
  `.ThatAreInternal()`, `.ThatArePrivate()`) prefix the current head rather than replacing it or
  trailing the phrase — "authored types in `MyApp.*`", and "public interfaces in `MyApp.*`" where
  `OfKind` has substituted the head, wherever `OfKind` sits in the chain. Prefixes stack in the
  order written. `.Authored().ThatArePublic()` reads "authored public types",
  `.ThatArePublic().Authored()` reads "public authored types", and the checker applies both
  either way. Each type prefix names one fact, so a prefix written twice renders once. A
  repeated `.Packable()` on a project subject does the same (§4.10). A union that does not
  collapse distributes its prefixes into each operand ahead of the operand's own, as it
  distributes a head substitution, so every filter the checker applies reaches the sentence.
  `arch.AnyOf(arch.Namespace("A.*").ThatArePublic(), arch.Project("B")).Authored()` reads
  "authored public types in `A.*` or authored types in project `B`". Prefixing is what keeps the
  fact attached to the noun it narrows. A member subject renders its type selection in
  *reference* position (below), so a trailing type clause would land beside the member's own
  clauses and read as one of them — "methods returning `Task` declared in types in `A.*`,
  excluding source-generated types" can be heard as excluding methods, which is not the set the
  checker uses. The member side's own accessibility adjectives (§5.7) prefix the kind-plural on
  these grounds —
  "protected fields of types in `MyApp.*`" — and, as member prefixes, concatenate rather than
  collapse.
- **Union collapse** (§5.1): a union of two or more operands, each adjective-free and all of the
  same noun kind, hoists one head and one locative and or-joins the operand names — *"Types in
  projects `A`, `B`, `C` or `D`"*, *"Types in `A.*` or `B.*`"*, *"The Domain or Web layers"*,
  *"`X` or `Y`"* — through the same no-Oxford-comma composer every other list uses. A union of
  type nouns joins through the shared anchor-list composer, so colliding simple names widen
  identically. **Otherwise the union falls back** to or-joining its operands' own phrases —
  *"Types in project `A` or types in `B.*`"* — which is what a heterogeneous union, a union with
  an adjective-carrying operand, and a single-operand union all render as. The fallback is a
  pinned decision in both directions, not an accident. A union's own adjectives then assemble
  against the hoisted head exactly as a single selection's do (head substitution, inline,
  sentence-final): *"Interfaces in projects `A` or `B`"*, *"Types in projects `A` or `B`, except
  `X`"*, and for a union of types *"Authored types named `X` or `Y`"*. On a union that does not collapse, a head adjective distributes across the operands —
  *"Interfaces in project `A` or interfaces in `B.*`"* — so the kind filter the checker applies
  always reaches the sentence. A bare (adjective-free) union reads as its own reference in both
  positions, which is what makes the single-operand identity hold in subject position too.
- **Scoped placement**: a rule earns a layer's per-directory card only when its subject's noun
  head *is* that layer, so a refinement (adjective / `Except`) still anchors. The card lands at
  the deepest common ancestor of the files that declare the layer's types, whatever defines the
  layer, and the solution directory bounds it: only declaring files inside that directory count.
  A file a project compiles from outside the checkout, such as a source-only package's, never
  moves a card. Where several projects compile one type (§4.1), a layer defined as a project
  noun counts that project's own copy, and a glob-defined layer counts every copy. A layer with
  no counted file inside the solution directory places no card, and `render` warns that it
  matched only types declared outside the solution directory. A scope's card follows the same
  rules. A family of layers (§5.1) anchors on every cell: its bullet lands
  on each cell layer's card, and the sentence names every cell, so it reads correctly on each;
  the project form anchors none. A union subject never does (§5.1) — it has no single home
  directory even when a `Layer` is one of its operands — so a union rule renders into the root
  block only.
- **Location** (`.Under`, §5.2) renders inline after the head and its locative, as `Named` does,
  and reads the same in reference position: *"Types under `src` must not reference types under
  `src/Console/Internal`."* The globs render as written, so the sentence shows the case and the
  separators the checker matches, case aside.
- **Colliding simple names**: when two targets in one sentence share a simple name, both are
  qualified with the minimal distinguishing trailing namespace segments
  ("`Billing.Order` or `Sales.Order`"). Pinned rule. The negative hierarchy and attribute anchor
  lists (`MustNotImplement`/`MustNotDeriveFrom`/`MustNotBeAttributedWith`) widen by the same rule —
  the attribute form qualifies *inside* the brackets ("`[Billing.Audit]` or `[Sales.Audit]`") — so
  every multi-operand list disambiguates identically. Widening runs on the anchor's dot path,
  which a string attribute anchor (§5.2) supplies exactly as a `typeof` does — the dots inside
  a generic argument list belong to the argument's own path and never split — so string
  anchors collide and widen byte-identically to their `typeof` twins.
- **Member references** (§4.5) render as the backticked declaring type dot member —
  "`DateTime.Now`" — with `()` appended iff the member is a method ("`Task.Wait()`"; never
  a signature). Generic anchors use declared type-parameter names ("`Task<TResult>.Result`").
  Colliding declaring-type simple names widen by the same minimal-trailing-segments rule —
  including when the member names differ ("`Billing.Order.Total` or `Sales.Order.Refresh()`"),
  because the reader must see they are different `Order`s.
- **Member subjects** (§4.6) assemble as the member head prefixes + the kind-plural + the
  trailing member clauses + the joiner + `{selection-reference}` + the member `Except` clauses.
  The trailing clauses are the `Returning` clauses in the order written, then the member `Where`
  descriptions in the order written, whatever the chain order. The joiner is " of " when there
  are none and " declared in " when there are: "methods of types in `MyApp.Web.*`", "methods
  returning `Task` declared in types in `MyApp.Web.*`". A clause after the reference would read
  as narrowing the types, and a member `Where` there would render the sentence a type-side
  `Where` before the projection renders. Every `Where` still precedes every `Except`, as on the
  type side, and the member `Except` renders each payload in reference position, where a payload
  with clauses of its own reads the same way ("except methods returning `Task` declared in types
  named `Worker`"). The kind-plural is the projection head ("methods", "properties", …); the
  `{selection-reference}` is the underlying type selection rendered in *reference* position (the
  same "types in `MyApp.Web.*`" / "the Web layer" form a target list uses). The flagship:
  `web.Methods.Returning(typeof(Task)).MustHaveSuffix("Async")` (where `web` is `MyApp.Web.*`) →
  *"Methods returning `Task` declared in types in `MyApp.Web.*` must be named `*Async`."* The
  member attribute adjective premodifies the kind-plural
  ("`[McpServerTool]`-attributed methods of types in `SlideRule.*`"), as do `.ThatAreStatic()`,
  the accessibility adjectives ("private fields of types"), member `Named` ("`Main` methods of
  types named `Program`") and the name adjectives `WithSuffix`, `WithPrefix` and `WithNameMatching`
  ("`*Async` methods of types"), and stacked
  member prefixes concatenate in authoring order (§5.7), so every
  narrowing fact reaches the sentence — the member-side twin of the `.Authored()` head
  premodification above, on the same grounds: a trailing clause would read as narrowing the
  wrong set.
- Posture voices consume these same fragments: Enforce renders as law; Migrate renders the
  counter-prior paragraph (slots: from-prose, to-sentence, policy, baseline burndown); Quarantine
  renders dragons + sanctioned surface; Caution renders dragons alone, on the directory card and
  nowhere in the root block. Full paragraph templates are pinned by the renderer's tests; the
  grammar carries every slot they need.

## 7. Scope desugaring (the single rule model, constructively)

`arch.Scope(id).Quarantine(sel).BoundaryOnlyVia(F).Baseline(p)` reifies to ordinary rule nodes:

- **`{id}/containment`** — internally `sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)`.
  (The `sel ∪ F` payload is the same union node `arch.AnyOf` mints, §5.1.) The
  formula holds whether the facade types live inside or outside the quarantined selection.
  **`.Baseline(p)` grandfathers existing inbound references** with the same ratchet semantics
  as Migrate — day-one adoption on a real legacy codebase must not be a wall of red; only
  *new* references into the scope are violations ("nothing **new** may reference the quarantined
  scope"). Burndown shows up in `sliderule status` for free. An omitted
  `.Baseline(p)` fills the conventional default `arch/baselines/{scope-id}/containment.json` at
  build time, exactly like the Migrate default (§4.4) — the containment baseline path is never
  null post-build.
- **`{id}/tripwire`** — warning-severity, diff-aware touch check. With
  `sliderule check --diff-base <ref>`, each changed file that declares a type in the quarantined
  selection yields one warning ("does the task actually require editing dragon territory?"); the
  rule itself passes and warnings never affect the exit code. Without diff context the rule is
  skipped, with a pointer at `--diff-base`. It carries the quarantined selection (not the boundary or a
  baseline) so it can map changed files to quarantined types.
- **`arch.Scope(id).Caution(sel)` mints the tripwire alone.** A caution reifies to one node,
  `{id}/tripwire`, under the fourth posture, `Caution` — the same node a quarantine's second child
  is, mapping changed files over `sel` the same way, with no containment law beside it: new
  references into the scope are welcome, and nothing about the scope ever reds. The posture is the
  severity (§9), and a caution's is warning, so it is the one posture with no red state. Its
  tripwire warns in its own voice — "read the dragons before editing", pointing at
  `explain {id}/tripwire` — where a quarantine's asks whether the task requires editing dragon
  territory at all, and the human report prints the dragons beneath a fired tripwire of either
  posture. A caution that later gains a facade becomes a quarantine under the same ID:
  `Caution(sel)` to `Quarantine(sel).BoundaryOnlyVia(F)` keeps the `{id}/tripwire` handle, the
  `explain` pointer and the card's directory key. Reserved suffixes are the same two for every
  scope, whichever posture it carries.
- Scope children occupy the rule-ID namespace: duplicate detection runs over the
  **post-desugar** ID set, and a declared ID may not extend a scope ID — `{scope-id}/…` is
  reserved. Reserved suffixes: `containment`, `tripwire`.
- The boundary is **selections**, with the `Type` sugar every set-valued position carries
  (§3.3): `BoundaryOnlyVia(typeof(IFacade))` ≡ `BoundaryOnlyVia(arch.Type(typeof(IFacade)))`,
  identical model and identical prose, and a mixed list wraps the type. Anchor by `typeof` when
  the spec can compile against the facade (§5.2); name it otherwise —
  `BoundaryOnlyVia(arch.Types.Named("BillingFacade"))` reaches an `internal` facade, or one in a
  project the spec does not reference, with no `[InternalsVisibleTo]` and no load. Since the
  formula is indifferent to whether the surface lies inside or outside `sel`, a *consumer* is
  spelled the same way: the one caller a legacy scope sanctions becomes law rather than
  grandfathered debt. The boundary is the containment's allow-list, so a named boundary that
  matches nothing, typically a facade renamed after the spec named it, warns "rule is stricter
  than written" on `{id}/containment` naming that selection (§4.1), beside the reference
  violations it causes.
- Omitting `BoundaryOnlyVia` = hermetic quarantine (nothing outside may reference the scope).
  Because omission is legal, the **`Type` overload** deliberately stays plain `params` (not
  `(first, more)`) so that a zero-arg call reaches spec-build validation and gets the designed
  hint — "omit the call for a hermetic quarantine" (§8 item 8) — instead of an opaque compiler
  error. That is also why only one of the two carries plain `params`: two would make
  `BoundaryOnlyVia()` ambiguous and put the hint out of reach, so the `Selection` form takes
  `(first, more)` and is uncompilable at zero arguments by construction. Neither has a generic
  twin: a boundary is a variadic list, which has no type-argument form.
- A project-headed quarantined selection follows §4.1's several-projects rule: a type the
  project co-declares is in the quarantine, for containment and for the tripwire's changed-file
  mapping alike. Its scope card counts the quarantined project's own copy of that type (§6), so
  the card lands by the project's files rather than where the declarers meet.
- Nested or overlapping scopes compose as independent conjuncts, whichever posture each carries;
  there is no scope precedence, and a caution overlapping a quarantine is legal, redundant and
  unvalidated.
- **Practical note**: a `typeof` boundary usually needs the facade *implementation* type(s)
  listed alongside the interface, or the composition root's DI registration of the concrete
  facade goes red on day one: `BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))`.
  A named boundary lists the pair the same way, and needs no `[InternalsVisibleTo]` to reach the
  `internal sealed class` the implementation usually is:
  `BoundaryOnlyVia(arch.Types.Named("IBillingFacade", "BillingFacade"))`.

## 8. Spec-build validation catalog (all errors reported at once)

1. Duplicate ID over the **post-desugar** set (rules + scopes + generated children), across
   all spec classes; a declared ID may not extend a scope ID.
2. Missing posture — `Rule()`/`Scope()` without a posture verb (`.Enforce`/`.Migrate` on a rule,
   `.Quarantine`/`.Caution` on a scope); the message names both verbs the declaration could take.
3. Missing `Because` on any rule or scope, whichever posture the scope carries.
4. Missing both `Dragons` and `DragonsDoc` on a scope, quarantined or cautioned; the message names
   the posture.
5. Blank/whitespace prose anywhere; prose fields are single-line (no `\r`/`\n` — long-form prose
   links out via `DragonsDoc`). The walk covers
   a rule's `Because`, `Fix` and `Citation`, a Migrate's `from:`, a scope's `Dragons` and
   `DragonsDoc`, every escape-hatch description, and the `Baseline` path a Migrate rule or a
   quarantined scope names. A layer's `Purpose` is prose like any other
   and is reported on the layer's spec-wide terms, named by layer and with no location:
   `Blank purpose on layer 'Core'.` `Baseline` is a path rather than prose, walked here for the
   reason `DragonsDoc` is: no later stage can report a blank one. A blank path resolves to the
   solution directory rather than failing, so `check` reads no baseline and the rule reds without
   saying why, and `baseline init` reports the violations captured and then fails trying to write
   the directory itself.
6. Repeated call (`Because` twice, two `Baseline`s, two `Citation`s, two `Exemplar`s, a layer's
   `Purpose` twice, …); the `Purpose` case is spec-wide and named by layer, like item 5.
7. Malformed ID — must match `^[a-z0-9-]+(/[a-z0-9-]+)*$` (convention: `area/rule-name`).
8. `BoundaryOnlyVia()` with zero types (omit the call for a hermetic quarantine) — reachable
   through the `Type` overload alone; the `Selection` form takes `(first, more)`.
9. Duplicate layer name.
10. Selection minted on a different `Arch` instance ("selection not registered with this
    model"). A layer's selection definition is walked here too — reported spec-wide, named by
    layer and location-free, because a definition is use-independent: a bad one is caught
    whether or not any rule ever names the layer. A rule's `Exemplar` is walked here too.
11. Blank member name on an `arch.Member` used by a rule.
12. Member not **declared** on its anchored type (reflection `DeclaredOnly` typo guard);
    when the member is declared on a base type the error names that base and the `typeof`
    to use ("`System.Threading.Tasks.Task<TResult>` does not declare `Wait`; it is declared
    on base type `System.Threading.Tasks.Task` — use `typeof(Task)`").
13. `Member` minted on a different `Arch` instance (the §3.2 fresh-instance contract, member
    flavor).
14. Closed-generic `.Returning` anchor (§4.6): a `.Returning(typeof(Task<int>))` is refused
    because member return-type matching is definition-level — the error names the closed
    construction and the open definition to use ("`System.Threading.Tasks.Task<System.Int32>` is a closed generic;
    `.Returning` matches definition-level — use `typeof(Task<>)`"). A non-generic anchor
    (`typeof(Task)`) and an open-generic anchor (`typeof(Task<>)`) are both accepted. The
    checker carries a matching backstop (§4.6). The string arm is outside this item: a
    constructed spelling names no definition and matches nothing (§5.2), and nothing is
    inferred from a string's shape.
15. Blank/whitespace glob, name or affix — a namespace pattern, a type- or member-name pattern,
    an exact type name (`Named`, reported as `Blank type name on '{id}'.` — blank is the whole
    check, a name having no structure to validate), an exact member name (member `Named`, reported
    as `Blank member name on '{id}'.`, the label item 11 reports under a code of its own), a path
    glob (`Under`, `Blank path glob on '{id}'.`), a project name, or a suffix/prefix left empty. A blank affix is vacuously true and a blank
    glob throws at check time; both are almost always an authoring slip. Applies on the type and
    member sides alike, to layer globs and to every operand of a layer's selection definition,
    its `Except` payloads included (both reported spec-wide, named by layer), and to the project
    name on the noun and the verb alike — `arch.Project("")` in any position and
    `MustResideInProject("")`, each as `Blank project name on '{id}'.` String anchors (§5.2) report
    through this same family in every position of all three families, adjective and
    verbs alike, under a label naming which kind of anchor was left empty: "attribute name"
    (`Blank attribute name on '{id}'.`), "interface name", "base type name", "return type name"
    and "parameter type name". Blankness is the
    whole of a string anchor's well-formedness: a dotless or suffix-less spelling is a legal
    name that never matches (§5.2), and no other shape check exists.
16. Dead namespace subtree pattern — a trailing `.*` whose literal prefix carries a `*` (e.g.
    `MyApp.*.Controllers.*`). The subtree operator (§4.2) matches everything before the trailing
    `.*` literally, so the pattern never matches anything; the error names it and steers to a
    literal prefix ("… has a trailing `.*` subtree operator but its literal prefix contains a
    `*`, which never matches; anchor the subtree on a literal prefix"). An interior standalone
    `*` with no trailing subtree operator (`MyApp.*.Orders`, §4.2) is legitimate segment matching
    and accepted, as is a lone `*`. Type-name globs and affixes have no subtree operator, so this
    never applies to them — only the blank check (item 15) does. `NamespacePattern.Validate` owns
    the verdict, so the matcher and its build-time gate cannot drift.
17. Repeated posture — a rule given more than one posture verb (`.Enforce`/`.Migrate`), or a scope
    given a posture verb (`.Quarantine`/`.Caution`) more than once, or both. The stage machine (§3.2) makes the *fluent* double-call
    uncompilable: the posture verbs live only on `IRuleBuilder`/`IScopeBuilder`, and the first call
    hands back a stage type without them. But those builders are mutable, so a **stored** builder
    reference (`var b = arch.Rule(id); b.Enforce(...); b.Migrate(...);`) can call a posture verb
    twice, and the second silently overwrites the first — the model keeps only the last posture.
    This item catches that stored-reference re-call: the count rides on the registration
    (`RuleRegistration.PostureCount` / `ScopeRegistration.PostureCount`) and a count > 1 is the error.
18. Unresolvable member-anchor expression (§4.5) — an `arch.Member<T>(x => ...)` /
    `arch.Member(() => ...)` lambda the resolver cannot reduce to a declared `(type, name)`. One
    code (`MemberExpressionUnresolvable`) carrying eight messages, each steering to the cure: a
    non-member body (`x => x.A + 1`), a method group (`x => x.M` or `() => T.M`, steered to the
    invocation form `x => x.M()` / `() => T.M()`), a receiver not reached directly on the lambda
    parameter through an identity cast — a chained access (`x => x.A.B`), a captured local or field,
    or a user-defined conversion — a static member reached through the typed instance form, an
    instance member reached through the parameterless static form, an indexer accessor (`get_Item`,
    `IsSpecialName`), a compile-time constant body (a `const` field, `enum` member, or literal
    the compiler inlines to its value), and an object-creation body (`() => new Foo()`, including
    target-typed `new()`) steered to the `MustNotConstruct` verb (§5.3). Reported **before** item 12 for the same member: an
    expression anchor is resolved from a real member and generic-normalized at mint, so item 12
    (and the check-time closed-generic refusal, §4.5) can never fire on it — those stay live only
    for the `typeof` form. The poison rides on the `Member` leaf itself: the diagnostic is stored
    on the leaf, whose `DeclaringType`/`Name`/`IsMethod` throw if read (fail closed), so it is
    collected in the same all-at-once pass as every other error.
19. Undefined `Lifetime` value on an `arch.Registered` noun used by a rule, a scope, or a layer's
    definition — a cast like `(Lifetime)7` names no defined lifetime; the error names the
    undefined value and the defined ones ("`(Lifetime)7` is not a defined `Lifetime` — use
    `Lifetime.Singleton`, `Lifetime.Scoped`, or `Lifetime.Transient`") and reports in the same
    all-at-once pass.
20. Closed-generic `MustAcceptParameter` anchor (§5.7): a
    `.MustAcceptParameter(typeof(IProgress<int>))` is refused because parameter-type matching
    is definition-level — the error names the closed construction and the open definition to
    use ("`System.IProgress<System.Int32>` is a closed generic; `MustAcceptParameter` matches
    definition-level — use `typeof(IProgress<>)`"). A non-generic anchor
    (`typeof(CancellationToken)`) and an open-generic anchor (`typeof(IProgress<>)`) are both
    accepted. The checker carries a matching backstop (§4.6). The string arm is outside this
    item, as for item 14.
21. Category-invalid hierarchy anchor, both polarities (§5.2, §5.3): a `Must[Not]Implement`
    anchor must be an interface; a `Must[Not]DeriveFrom` anchor must not be an interface; a
    `Must[Not]BeAttributedWith` anchor must derive from `System.Attribute` (`typeof(Attribute)`
    itself is refused — declared-attribute matching could never match it). A wrong-category
    anchor can never match, which makes a positive an always-red rule and a negative an
    always-pass — both silent authoring slips, so the error steers to the right verb
    ("`System.Exception` is not an interface; `MustNotImplement` requires an interface anchor —
    use `MustNotDeriveFrom` for a base class"; "`System.IDisposable` is an interface;
    `MustDeriveFrom` requires a non-interface anchor — use `MustImplement` for an interface";
    "`System.Attribute` does not derive from `System.Attribute`; `MustNotBeAttributedWith`
    requires an attribute anchor"). The member-side `Must[Not]BeAttributedWith` verbs (§5.7)
    carry the same check with the same messages. It applies to `typeof` anchors **only**, in
    both families: a string anchor (§5.2) carries no category to check — the name is just a
    string, extraction facts are the only authority on what it names, and refusing a spelling
    the host cannot load would break the escape hatch's whole point — so a wrong string is loud
    on a positive (always red) and silent on a negative, the honesty cost of the hatch. Blankness
    (item 15) is checked on a string anchor everywhere, including the adjectives; category is
    checked nowhere on one. The **adjectives**
    are unchecked on both sides and across every hierarchy family: a
    wrong-category adjective empties the subject and the fail-on-empty default (§4.1, §4.6)
    reds it loudly, whereas the silent slip this item exists to catch is the always-passing
    `MustNot` verb. Reported in the same all-at-once pass with the rule's
    spec-source `file:line`, like items 19/20.
22. Project selection minted on a different `Arch` instance — the §3.2 fresh-instance
    contract, project flavor ("A project selection used by '{id}' was minted on a different
    Arch instance; it is not registered with this model."). Its own code beside items 10 and
    13 so the message names what was foreign. The walk includes `Except` payloads — the one
    way a project selection nests, and therefore the one way a foreign one can hide inside a
    local subject.
23. Blank/whitespace project name or glob on the artifact stratum — a `.Named` operand
    (`Blank project name on '{id}'.`) or a `.Matching` glob (`Blank project name pattern on
    '{id}'.`) left empty. A blank name matches no project and a blank glob matches every
    one; both are almost always an authoring slip, and the two failure shapes are far enough
    apart that neither is left for check time to explain. Same message shape as item 15, so
    a reader who has met one blank-operand error has met them all.
24. Blank/whitespace target framework on `MustOnlyTarget` or `MustTarget` (`Blank target
    framework on '{id}'.`). A blank moniker matches nothing, so on `MustOnlyTarget` it silently
    narrows the allow-list rather than widening it — the rule stays green until a project
    targets the framework the author meant to permit — and on `MustTarget` it requires a
    framework no project targets, so the rule reds every evaluated project.
25. Blank/whitespace counterpart name template on `MustHaveExactlyOneCounterpart` (`Blank
    counterpart name template on '{id}'.`). Rides item 15's walk — one label, one case — so a
    reader who has met one blank-operand error has met them all.
26. Counterpart name template with no `{Name}` placeholder (`The counterpart name template
    'IService' on '{id}' contains no '{Name}' placeholder, so every subject derives the same
    fixed name — a cardinality claim, not a correspondence; use a template such as 'I{Name}'
    (substitution is case-sensitive).`). On item 21's argument: a placeholder-free template is
    a constant predicate — a cardinality law wearing a correspondence law's clothes — that reds
    or greens the whole subject set together, and is almost never what the author meant.
    Substitution replaces every occurrence and matches case-sensitively, which is what lets the
    likeliest slip, `{name}`, fail at spec build instead of checking a constant name. A blank
    template is item 25's alone, on item 18's reported-before pattern.
27. A family (`arch.Each`, §5.1) anywhere but subject position (`` A family (`arch.Each`) used as
    an operand by '{id}'; a family may stand only as a rule subject. `` — the position word
    varies: `an operand`, `an Except payload`, `a union operand`, `a scoped selection`, `a
    boundary`, `an exemplar`; a layer definition reports spec-wide, named by layer: `` … used as a layer
    definition by layer 'X'; … ``). A partition means nothing in a position that consumes a set,
    and every walk below reaches each of those positions. A family as the source of a member
    projection is a subject, and legal.
28. `MustNotReferenceEachOther` on a plain selection (`` `MustNotReferenceEachOther` on '{id}'
    needs a family subject (`arch.Each`); over a plain selection write `MustNotReference`. ``).
    The verb's targets are the subject's own cells (§5.3), so a subject without cells names
    nothing to forbid, and the verb the author meant has a name.
29. `MustNotHaveCircularReferences` on anything but a family of layers, in two wordings: a plain
    selection (`` `MustNotHaveCircularReferences` on '{id}' needs a family of layers
    (`arch.Each`); over a plain selection there are no layers to reference each other. ``) and a
    family of projects (`` `MustNotHaveCircularReferences` on '{id}' needs a family of layers;
    projects cannot have circular references, so over a family of projects the law holds by
    construction. Write `MustNotReferenceEachOther` or an ordering rule. ``). A plain selection
    has no cells to close a circle; a family of projects cannot close one, because the build
    refuses circular project references, and a rule that cannot go red is a promise the checker
    never keeps.

30. A `Citation` that is not an absolute `http`/`https` URL (`` Malformed citation on
    '{id}': '{value}' is not an absolute http(s) URL. ``) — a pasted page title, a repository
    path, or a URI on another scheme. Every consuming surface treats the value as a link and
    hands it on unread: the context bullet autolinks it, SARIF publishes it as `helpUri`. Only a
    non-blank, single-line value reaches this check, so a whitespace citation reports once, as
    item 5.
31. Blank layer name (`Blank layer name; name the layer in the first argument to arch.Layer(...).`).
    The name is what every rule, report and rendered block calls the layer by, and what tells a
    second layer from it, so it cannot be left out. Spec-wide like the other layer errors, and the
    one of them that names no layer — the name is what is missing. A blank-named layer is held out
    of item 9's duplicate set: two unnamed layers are two of this error, not a collision.
32. Blank/whitespace package pattern on `MustOnlyReferencePackages` or `MustNotReferencePackages`
    (`Blank package pattern on '{id}'.`). No package has an empty identifier, so a blank pattern
    matches nothing: it narrows an allow-list, which then reds on the package the author meant to
    permit, and it empties a ban, which then stays green whatever the projects reference.
33. Blank/whitespace property name or value on `MustSetProperty` (`Blank property name on
    '{id}'.`, `Blank property value on '{id}'.`). No property has an empty name, and MSBuild
    treats a property set to nothing as unset, so either way the rule asks for something no
    project can declare and reds every evaluated project. One code for both, since both fail in
    that one direction; the label says which was left out.
34. A member `Except` payload of another single kind than the selection it subtracts from
    (`` `Except` on '{id}' takes fields from methods, so it removes nothing: no member is both.
    Project the payload with `.Methods` or `.Members`. ``). No member is two kinds at once, so the
    exclusion is a no-op its author did not mean; a `.Members` selection on either side holds every
    kind and clashes with none. Reported at the rule's location, at every nesting depth.
35. A path glob on `Under` written in a shape no declaring file can match (`` The path glob '/src'
    on '{id}' is rooted; a path glob is relative to the solution directory. ``): a rooted glob, a
    backslash, a `.` or `..` segment, an empty segment or a trailing `/`, and a `**` written inside
    a longer segment, each with a reason clause of its own (§4.2). Refused rather than normalized,
    so what the model carries is what the author wrote; the matcher owns the verdict, so what it
    refuses and what this catalog reports cannot drift. A blank glob is item 15's alone.
36. An empty operand list on a sequence form (§3.3): `arch.AnyOf` with no parts, project `.Named`
    or `.Matching` with no names or globs, member `.Named` with no names, member `.Except` with no
    payloads, `MustNotUse` with no members (`` `arch.AnyOf` on '{id}' was given an empty list, so
    it selects nothing; pass at least one operand. ``). The `(first, more)` forms keep each of
    these uncompilable, and only a list computed at run time reaches them. Each renders like a law
    and constrains no code, so the message names the call and what an empty one does: "selects no
    project", "selects no member", "removes nothing", "bans nothing".
    It is reported wherever the other walks reach, and for a layer definition on a layer's
    spec-wide terms.

Item 5 also reaches the member escape-hatch descriptions: a blank or multi-line member `Where`
(`Func<IMemberInfo,bool>`) or member `Must` description is caught by the same prose walk,
which descends through a member constraint's subject and verb (§4.6) — and the
project escape-hatch descriptions the same way: a project `Must` description, and project
`Where` descriptions wherever the subject nests one, `Except` payloads included (§4.10). A member
`Except` payload (§4.6) is walked as the subject is, at every nesting depth: its type selection
reaches items 10, 15, 16, 19, 27 and 36, and its own member adjectives items 5, 14, 15 and 36.

Every walk in this catalog descends through a union (§5.1) on both axes — its operands *and* its
own adjectives — so `AnyOf(a, b).Where(p, "")` reaches item 5, `AnyOf(a, b).InNamespace("")`
reaches items 15–16, a foreign operand or a foreign `Except` payload inside a union reaches
item 10, and an empty union nested in another reaches item 36. **Per-operand emptiness is not a
§8 error**: whether an operand matches any type is a fact about the codebase, not the spec, so it
is check-time — one empty-subject violation naming
the operand (§4.1, §5.1). The walks descend through a family the same way: a family's layer cells
and its own adjectives are walked (a foreign cell reaches item 10, a blank glob on a family's
adjective item 15), and the project selection inside a project family reaches items 22, 23 and 36.
Two of a family's cells holding one copy of a type is likewise a codebase fact and is check-time — a
rule error naming the type and both cells (§5.1).

Every walk that visits a rule's constraint also visits its `Exemplar` (§5.5): a foreign selection
reaches item 10, a blank name or glob item 15, a dead subtree pattern item 16, a blank `Where`
description item 5, an undefined `Lifetime` item 19, a family item 27 and an empty union item 36.
Whether the exemplar matches any type is check-time, like an operand's emptiness: a warning, never an error.

Enforced at compile time instead (no catalog entry): zero-target dependency verbs — the shapes
that are meant to name no target have verbs of their own, `MustOnlyReferenceItself()` and
`MustOnlyBeReferencedByItself()`, and `MustNotReferenceEachOther()` and
`MustNotHaveCircularReferences()` name their targets through the subject's cells (§5.3) —
zero-member `MustNotUse` through its `(first, more)` forms (its sequence form reaches item 36),
zero-glob layers and zero-cell families (`(first, more)` signatures), and adjective-bearing,
union or nested family cells (`Each` takes `Layer`s); missing escape-hatch descriptions (required parameters); trailers before postures (absent from stage
types); adjectives or modal verbs on a `Member` (a leaf outside the selection hierarchy,
§3.2); `.Returning` and `MustAcceptParameter` off any projection but `.Methods` (they live
only on `MethodSelection`, §3.2, §4.6, §5.7). Several member-anchor-expression mistakes are refused by the C# compiler outright,
so they never reach item 18: an event anchor (CS0070), an inaccessible member (CS0122), a
`ref`-returning member (CS8153), a `ref struct` receiver (CS8640), and a static member reached
through an instance expression (CS0176). A method-group body is the one that compiles (with
warning CS8974) and so is caught at spec build (item 18), not by the compiler.

Every error that names a rule, scope, or member additionally renders the offending anchor's
spec-source `file:line` (file name only, captured by `[CallerFilePath]`/`[CallerLineNumber]`
on the `Rule`/`Scope`/five `Member` factories) so each lands as a jump target — e.g.
`ArchitectureSpec.cs:17: …`. Duplicate-layer and layer-definition errors are location-free (a layer name
is already a unique greppable string).

All-errors-at-once is a deliberate divergence from EF Core's fail-fast `ModelValidator`: an
agent fixing a spec sees every problem in one pass.

**Reported at the literal as well.** Items 1, 5, 7, 9, 11, 15, 16, 23 to 26, 30 to 33 and 35 are decided by
string literals alone, with no model and no codebase behind them, so the contract package also carries
a Roslyn analyzer that reports them as compiler warnings on the literal while the spec is being
written, in the editor and in `dotnet build`. Their diagnostic IDs run from `SR1000` to `SR1035`, one
per kind of literal error (an unanchored subtree pattern is `SR1017`),
and the message is the item's sentence without its `on '{id}'` clause, since the warning's location
already says which literal. One owner in the contract decides each of these verdicts for both
readers, so the two cannot come to say different things. This catalog stays the authority. The
analyzer reads compile-time string constants only, and it compares IDs and layer names within one
method body, so a value built at run time, or a duplicate split across methods, spec classes or a rule
pack, is reported here at spec build and not there. A spec that builds with no warning can still fail
this catalog.

## 9. Deliberate divergences from the prior art

| Divergence | Prior-art contrast | Rationale |
|---|---|---|
| No `Check()`/`GetResult()` terminal | ArchUnit `check()`, NetArchTest `GetResult()` | rules are data; the CLI/adapter walks the finalized model |
| No violation-snapshot "freeze" | ArchUnit `FreezingArchRule` | "freeze" there means accept current violations as a baseline — that is `Migrate(...).Baseline(...)` here; `Quarantine` contains a scope, it does not accept its violations |
| No `noClasses()`, no `ShouldNot()` | Java ArchUnit, NetArchTest | lexical polarity; NetArchTest's gate+verb coexistence permits `.ShouldNot().NotBeSealed()` — ArchUnitNET already went negation-in-verb, we follow it |
| No `.As(...)` description override | ArchUnit/ArchUnitNET | free text never replaces structured prose; the ID names the rule, `Because` carries rationale |
| No constraint-level or whole-rule `And`/`Or` | `andShould()`, `IArchRule.And()` | one sentence per rule; compound requirements = multiple rules (atomic IDs for baselining/burndown); ArchUnit documents its own left-to-right precedence as a gotcha |
| No `When`/`Unless` | FluentValidation | specs are unconditional law; conditionality lives in the selection (FluentValidation's `ApplyConditionTo.AllValidators` retroactive rebinding is the cautionary tale) |
| No severity dial | FluentValidation `WithSeverity` | the posture is the severity |
| No conventions layer | EF Core | explicit only in v1 |
| Escape hatch hard-fails without description | FluentValidation `Must` defaults to *"The specified condition was not met for '{PropertyName}'."* | matches Java `DescribedPredicate`/`ArchCondition` (constructor-required description) and ArchUnitNET `FollowCustomPredicate(func, description)` |
| No source-text-derived descriptions | Shouldly `CallerArgumentExpression` | lambda source is not agent-consumable prose |
| All-errors spec validation | EF Core fail-fast `ModelValidator` | agents fix specs in one pass |
| Deterministic multi-spec discovery, loud failures | EF `ApplyConfigurationsFromAssembly` (order undefined, silent skips) | specs are law; law must load predictably |
| "reference", not "depend on" | ArchUnit family "depend on"/"access" | v1 edges are literally Roslyn type references; "depends on" over-claims for a type-level edge |
| One rule over a family, never N | ArchUnit `slices().matching("..(*)..").should().notDependOnEachOther()` | a family's cells are declared layers or the projects a selection names, never a capture in a pattern, and the rule stays one rule — one ID, one sentence, one baseline, one test row — because a cell is read only by the self-allowance, the family verbs, card placement and the burndown (§5.1); child rules minted from the codebase would put the codebase into the committed block and into the adapter's spec-time rows |
| A circle's debt is the pairs on its arrows, never the circle | ArchUnit `slices().should().beFreeOfCycles()` — cycles as findings, capped at `cycles.maxNumberToDetect` (100) and `cycles.maxNumberOfDependenciesPerEdge` (20) | a violation of `MustNotHaveCircularReferences` is every type pair on every arrow inside a strongly connected component of the cell graph (§4.3), so the edge key, the baseline, the ratchet and every renderer are untouched, nothing is capped, and a broken circle leaves stale entries rather than a rewritten cycle list; the intended direction is blamed beside the stray back-reference by design, and the author who knows the direction writes the ordering rule |

## 10. Naming morphology (style guide for vocabulary growth)

- **Nouns**: bare plurals or PascalCase names (`Types`, `Projects`, `Layer`, `Project`); the registration
  noun is a bare participle (`Registered`) — it names the set by the fact that admits
  membership. The family noun is a bare quantifier (`Each`): it names the set by how the sentence
  ranges over it, and reads as its own "each of" head.
- **Projections**: bare plurals naming the member kind (`Members`, `Methods`, `Properties`,
  `Fields`, `Events`) — they read as "{plural} of {selection}" (§4.6, §5.7).
- **Adjectives**: participles (`Implementing`, `DerivedFrom`, `Returning`, `Named`) or prepositional
  phrases (`InNamespace`, `WithSuffix`, `OfKind`). A bare past participle (`Authored`) names the
  set by the fact that admits membership, the adjective twin of the `Registered` noun, and reads
  attributively in front of the head (§6); the bare adjective `Packable` names the artifact set
  the same way (§5.8). The glob adjective differs by stratum, and the difference carries
  information: `WithNameMatching` on types and members, where a name sits beside a namespace, a
  kind or a signature and the adjective says which of them the glob reads (the
  `WithSuffix`/`WithPrefix` family); `Matching` on projects, whose name is the whole identity, so
  a property word would say nothing. `Named` is the exact form on the type, member and project
  strata (§5.2, §5.7, §5.8). `Under` is a bare preposition taking path globs (§5.2): nothing else
  about a type is a place on disk, so "types under `src`" needs no noun to say which kind of place. A verb-plus-object compound is not licensed:
  `ExceptGenerated` would both duplicate `Except` and stop reading as a modifier of the noun.
- **Constraints**: `Must[Not]` + verb phrase; polarity lexical; the noun rides along where a
  bare preposition would be ambiguous (`MustResideInNamespace`). The member-access verb is
  *use* (`MustNotUse`), glossary-pinned as *"use = a source-level member access"* — the
  member analog of "reference" (§4.1), chosen over "call"/"access" because it covers
  invocations, reads, writes, and subscriptions in one word. The constructor-ban verb is
  *construct* (`MustNotConstruct`), glossary-pinned as *"construct = a source-level object
  creation (`new`, including target-typed `new()`)"* — the third axis beside reference and use.
  The injection-ban verb is *inject* (`MustNotInject`), glossary-pinned as *"inject = a
  source-level constructor-parameter dependency (primary constructors included)"* — the fourth
  axis (§4.7). The catch verb is *catch* (`MustNotCatch`), glossary-pinned as *"catch = a
  source-level `catch` clause (a bare `catch` counts as `System.Exception`)"* — the fifth axis
  (§4.8). Its filter-aware sibling `MustNotCatchUnfiltered` narrows that axis rather than
  opening a new one: the qualifier rides the verb name as a bare past participle and spells
  itself out in the fragment (*"must not catch {list} without a `when` filter"*), because a
  `when` filter is not a type and so cannot be an operand (§3.3). Its rethrow-aware sibling
  `MustNotSwallow` narrows the same axis again, and takes a **verb word of its own** rather than
  a third participle on `catch`, because what it bans is a composite of three facts — the caught
  type, the absent filter, and the absent terminal `throw` — and no bare participle names a
  composite: `MustNotCatchUnfilteredUnrethrown` enumerates the conditions instead of naming what
  they add up to, and reads as a fourth qualifier rather than a law. *Swallow* is the word the
  code review already uses for holding a failure and continuing, and it is a verb, so it takes
  the `Must[Not]` + verb-phrase form directly. `MustNotHaveCircularReferences` takes the same
  licence on the reference axis: a circular reference is a composite of references that together
  close a circle, no bare participle names it, and "have" carries the noun phrase every .NET
  developer already knows — the build's own word for the project case — so the verb word is
  "have circular references" rather than an "acyclic" that would not spell its law in the
  sentence. The glossary clause glosses the *fact*
  (*"catch = a source-level `catch` clause"*), not the verb, so a spec that swaps one catch
  verb for another renders that clause byte-identically — the axis-gating discipline, one
  level down.
  The throw verb is *throw*
  (the strict allow-list `MustOnlyThrow` and its ban twin `MustNotThrow`), glossary-pinned as
  *"throw = a source-level `throw` of the thrown expression's type (bare rethrows `throw;`
  are not recorded)"* — the sixth axis (§4.8). The exposure verb is *expose* (`MustNotExpose`),
  glossary-pinned as *"expose = a public signature position (return, parameter, or
  property/field/event type) on a public member of an externally visible type"* — the seventh
  axis (§4.9). The glossary line composes from these per-axis clauses (reference always; use iff a
  member-target rule; construct iff a ctor rule; inject iff an injection rule; catch iff a
  catch rule; throw iff a throw rule; expose iff an exposure rule) joined with "; "
  and closed by the drill-down tail, so each axis
  gates independently and a spec without a given axis renders no clause for it. A `Registered` noun
  anywhere in a rule — subject or operand — or the `MustBeRegistered` verb, whose membership is
  that noun's (§4.7), additionally gates its own glossary line on the same
  byte-identical-without-it terms: *"registered = named in a source-level container registration
  (`AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`/`AddHostedService`/`AddDbContext`/
  `AddDbContextPool`/`AddHttpClient<TClient>`); keyed-service and raw `ServiceDescriptor`
  registrations, assembly scanning, factory internals and framework defaults are not seen."*
- **Posture verbs**: imperative (`Enforce`, `Migrate`, `Quarantine`, `Caution`). **Options**: nouns
  (`Baseline`) or deliberate idiom (`WhileYoureThere` — it names the boy-scout rule). Its policies
  each answer the question the option asks, as imperatives (`MigrateIfSmall`, `AlwaysMigrate`,
  `NeverMigrate`); a policy is never named for what every policy shares.
  **Trailers**: conjunctions (`Because`) / nouns (`Fix`, `Purpose`, `Citation`, `Exemplar`).
  `Exemplar` takes a selection and has no string twin. The anchor-form triple below governs
  positions that name one type; a selection position already has all three spellings through its
  nouns (`arch.Type(typeof(X))`, `arch.Type<X>()`, `arch.Types.Named("X")`), and a `string`
  overload would sit beside `Fix(string)` and compile there by mistake.
- `(first, params more)` signatures wherever an empty list would be meaningless. Seven of them
  also take a sequence, for a list a helper computes (§3.3), and an empty one is §8 item 36. A
  sequence parameter is named for what it holds (`selections`, `types`, `names`, `globs`,
  `exclusions`, `members`), which is the name a null throws under.
  Where a verb requires a fact rather than bounding it (a `MustOnly*` list is a bound, and the
  word "only" fixes its reading), a list is licensed only where the fact admits one value per
  subject. The ambiguity sits at the call site, not in the sentence: a C# argument list has no
  join word, so where a subject can hold several of the thing named (the parameters a method
  declares, the interfaces a type implements) an author writes `(typeof(A), typeof(B))` meaning
  both as readily as either, and the rendered "`A` or `B`" would then state a law the author
  did not mean. So `MustAcceptParameter` and the positive hierarchy verbs are single-anchor,
  and a second required anchor is a second rule. A negation has no such reading: "must not
  implement `A` or `B`" is none-of under either join, so the `MustNot*` twins take
  `(Type first, params Type[] more)`. A membership has none either: `MustBelongTo` asks where a
  subject may live, a question with one answer per subject, so its list can only be read as
  alternatives, and it takes `(Selection first, params Selection[] more)` with the or-joined
  sentence stating the reading the call site already had. `MustResideInProject` stays
  single-name on
  `MustResideInNamespace`'s pattern (several projects is `MustBelongTo` with project
  memberships), and `MustBeRegistered` is nullary: its membership is the codebase's
  registration facts (§4.7), not an authored operand. `MustBeGetOnly` and `MustBeReadonly` are
  nullary for the same reason and a different one: what they test is a fact of the declaration
  itself, so there is no operand to take — the receiver type carries everything the verb needs
  to know. `MustOnlyBeReferencedByItself`, `MustNotReferenceEachOther` and
  `MustNotHaveCircularReferences` are nullary on the outbound leaf's clause: their operand is
  the subject's own structure — the refined subject, or the cells a family declares (§5.1) —
  and there is nothing to list.
  `MustLockPackages` and `MustNotBePackable` are nullary on that same clause (§5.8),
  and `MustReferenceNoPackages()` is the deliberate inversion of this bullet's rule: an empty
  list of the references a consumer receives is the law it states, so the emptiness takes its
  own verb rather than an empty argument list, while `MustOnlyTarget` keeps `(first, more)` — an empty allow-list would be meaningless
  there. `MustTarget` requires a fact a project holds several of, so it is single-operand on the
  hierarchy verbs' argument: `("net8.0", "net48")` reads as both and as either, and a second
  required framework is a second rule. `MustOnlyReferencePackages` and `MustNotReferencePackages`
  keep `(first, more)`: the allow-list is a bound whose reading "only" fixes, and a ban is none-of
  under either join. `MustSetProperty` takes exactly its pair, a name and a value: a property holds
  one value in a build, so neither operand admits a list. `Except` carries the pair on the type stratum (several
  exclusions are the union `AnyOf` mints, and the `Type` sugar rides beside it exactly as on the
  dependency verbs, §3.3), carries it on the member stratum with the list held as the payloads
  themselves, no union behind it (§5.7), and stays single-operand on the project stratum, where `.Named(a, b)` and `.Matching(a, b)` already
  say "either of these" and no `AnyOf` exists to desugar a list to.
- **Anchor-form triples.** A single-type anchor position ships `Type` / `string` / `<T>`
  together — the compile-checked `typeof`, the §5.2 no-reference escape hatch, and the
  generic sugar — all reifying to one internal anchor, so form choice is invisible to the
  model and the sentence. The legs are not owed equally. The `string` leg is the obligation:
  it is the only leg with semantic reach, the one spelling for a type the spec cannot
  reference, and a position without it is not an anchor position. The `<T>` leg is sugar,
  owed where a type argument can spell the position's main use
  (`MustAcceptParameter<CancellationToken>()`) and declined where it cannot: `.Returning`'s
  main use is the open generic, which no type argument spells, so it carries no `<T>` twin
  (§4.6) — the exception this rule names rather than a violation of it. The discipline holds
  for all three families, attribute, hierarchy and member type, and one union serves them:
  what differs between them is which extracted facts the matcher reads and whether the prose
  brackets the name, neither of which is a property of the anchor. A new anchor position ships
  the `Type` and `string` legs, and the `<T>` leg unless its main use has no type-argument
  form — or it is not an anchor position.
  The rule governs positions that name a *type*: `MustHaveExactlyOneCounterpart`'s `named:`
  derives a name rather than naming one — `MustHaveSuffix`'s bare-string precedent — so no
  triple is owed there, and `Named` selects by simple name on the same precedent.
  The generic twin is `TSelf`-generic where inference allows and
  **receiver-typed where it does not**: C# has no partial type inference, so a
  `TSelf`-generic member adjective twin would force both type arguments at every call site
  (`AttributedWith<MethodSelection, MyAttribute>()`); it ships instead as one overload per
  concrete member selection — `MemberSelection` (serving `.Members` and `.Events`),
  `MethodSelection` keeping `.Returning` and `MustAcceptParameter` reachable after the sugar,
  `PropertySelection` keeping `MustBeGetOnly`, and `FieldSelection` keeping `MustBeReadonly`.
  The set is closed by the same triple discipline: a projection that mints a concrete selection
  type ships that type's sugar overload, or its kind-only verb stops compiling after the sugar.
- Named arguments are the documentation convention for prose parameters (`from:`, `to:`,
  `description:`) and for the correspondence verb's operands (`among:`, `named:`).
- **Admission rule**: a new vocabulary member ships with model node + fragment(s) + pinned
  string test + checker semantics (or an explicit "reifies now, checks later" note) + its row in
  the §5 table, which the suite will not let it ship without — or it does not ship. A fact family
  ships whole: no half of it reifying ahead of the half that reads it.

## 11. Growth paths (designed-for, not built)

Each entry is a new, explicitly named semantic, never a widening of one pinned above, and a
consumer arriving with evidence is what admits it (§10).

On the member axis (§4.5, §4.6): the `MustOnlyUse` / `MustNotBeUsedBy` verb twins;
member-granular *source* attribution on the dependency verbs (a violation names the using
*type*, not the using member); string-FQN *member* anchoring — the escape hatch for a member
that neither `typeof` + `nameof` nor an expression lambda can name (a member on a type the spec
project cannot reference); indexer/operator bans (the syntax-walk boundary moves,
§4.5); and the remaining subject-side member *shape* adjectives (`.Methods.ThatAreVirtual()`,
`.ThatAreAbstract()`, …) beside `.ThatAreStatic()` — the constraint-side verbs and the
`IMemberInfo` flags exist (§5.7, §5.6).

On the parameter facts (§4.6): the `WithParameterOfType` adjective — the adjective-position
twin of `MustAcceptParameter`, selecting rather than constraining; the `MustNotAcceptParameter`
negative twin; multi-`Type` arity (an argument list has no join word, so a list of anchors
reads as both as readily as either, §10); richer `IParameterInfo` facts (ref kind, optionality,
default values, `params`, ordinal position); hierarchy- or assignability-aware parameter
matching (the exception-axis bar applies: an explicitly named new semantic, never a widening of
definition-level-exact); and `graph` parameter data.

On the mutability facts (§4.6): `MustBeInitOnly` — the fact is recorded (`HasInitOnlySetter`) and
no verb reads it, so "a plain setter is banned, `init` is fine" is expressible through the escape
hatch and would need no extraction change to become a verb; a `MustBeMutable` opposite, which
would be a strange thing for an architecture rule to demand; and `IsRequired`, not extracted
because nothing reads it — §5.6 grows additively when something does. The property-side twin of
a static-mutability law is **not** a gap: `.Properties.ThatAreStatic().MustBeGetOnly()` composes
over the same facts, so a fields-only law that leaves static `{ get; private set; }` properties
outside its subject is an authoring choice.

On the injection axis (§4.7): the `MustOnlyInject` / `MustNotBeInjectedBy` twins; a
lifetime-scoped `MustBeRegistered(Lifetime)` (the nullary verb is any-lifetime, §5.3);
property- and method-injection edges (constructor parameters are the recorded form); recognition growth beyond
the pinned call table — keyed services, `ServiceDescriptor`/`TryAddEnumerable`, assembly-scanning
registrars — the table is the fence, everything outside it the documented honesty boundary;
lifetime facts on `ITypeInfo` (registration membership stays model-side, resolved at evaluation);
and `graph` registration data.

On the exception axis (§4.8): the `MustOnlyCatch` allow-list and the `MustNotBeCaughtBy` /
`MustNotBeThrownBy` passive twins; hierarchy-aware operand matching (exact definition-level FQN
is pinned, so a ban on `Exception` does not reach derived catches — a hierarchy-matching form
would be a new, explicitly named semantic, not a widening of this one); and `graph` catch/throw
data.

Declined: **member-granular source attribution on catch edges**, and member subjects for the
catch verbs. Sanctioned broad handlers are small single-role boundary classes where the type
effectively *is* the site, so finer granularity recovers nothing; a consumer arriving with
evidence reopens it. **Site-granular sanctioning as a spec surface is declined outright**: a
per-site ledger inside a spec re-invents baseline mechanics under another name, and the candour
argument that puts an exemption in the spec where it is read cuts equally against a ledger in
disguise.

On the exposure axis (§4.9): the `MustOnlyExpose` allow-list complement — the §4.1
`MustOnly*` external exemption would apply to it naturally, but v1's canon for surface
policy is the Not-form — and the `MustNotBeExposedBy` passive twin; accessibility variants
(a protected-inclusive surface, or minting from declared rather than effective visibility —
v1 mints only from effectively-public members, because an internal type has no external
contract); generic-constraint-clause positions (`where T : Order` mints nothing in v1 — a
constraint clause names a type without placing it in a signature position); member-granular
*source* attribution (the edge keys the exposing *type*, not the exposing member — the same
gap the member axis lists above); laundering awareness — a signature declared
`object` exposes `System.Object` and nothing more (the §4.9 honesty boundary: the edge is a
static-signature fact, and internal members are not surface), so a flow- or cast-aware form
would be a new, explicitly named semantic, never a widening of the exact match; and `graph`
exposure data (exposure edges stay out of the graph).

On the project stratum (§4.10), each waiting on a consumer: a project-stratum `.Under(pathGlob…)`,
the type side's adjective (§5.2) over a project file's directory, which also waits on a project-path
fact; `MustSetPackageAsset` (the `ExcludeAssets=runtime` shape); a framework-scoped package verb, an allow-list or ban over one framework's references, which
`.Must` over `IPackageReferenceInfo.Frameworks` spells today; `AnyOf` over project selections;
and card placement for artifact selections — a project rule renders into the root block only.

On the string anchors (§5.2): **constructed-generic** anchoring — a string naming a
construction rather than a definition (`"MyApp.Web.IHandler<MyApp.Web.InvoiceCreated>"`)
renders, and matches nothing. Making it match that construction, the way a closed `typeof`
anchor does, would be a new and explicitly named semantic on the exception axis's terms, never
a widening of the definition-level-exact one pinned here: that silence is what makes a
string anchor's meaning independent of whether the spec author happened to spell type
arguments. Also unbuilt: a string form for the *set*-valued anchor positions — the dependency
verbs' `Type` sugar and `arch.Type(...)`, where a namespace pattern is already the no-load
form (§5.6) and a string would only duplicate it. `arch.Type(string fullName)` is the one
exception worth naming: the string arm of the Type noun's own triple (§10) would render the bare
backticked name exactly (one definition, FQN in the model) and add namespace precision for a
simple name that collides, which `Named` (§5.2) reaches every instance of. It costs FQN spelling
at every site, and no exemption in the corpus collides today, so it waits on that collision.

On correspondence (§5.3): the at-least-one twin `MustHaveCounterpart`, wanting a consumer whose
law tolerates several
counterparts; richer templates — a second placeholder (`{Namespace}`, `{Project}`), escaping for
a literal `{Name}`, case-insensitive or glob-shaped matching — each wanting a real consumer
before the template stops being a substitution and starts being a language; correspondence keyed
by attribute arguments, the instance that fails at runtime when broken — every wire DTO carrying
`[JsonSerializable(typeof(X))]` — which waits on attribute-argument facts, §4.6's attribute facts
being name pairs only; set-to-set counting, where the law is about the two sets rather than each
subject; and the
arm distinction in the document — `Violation.Detail` reaches the JSON channel only, the human
report reads it for the identity-less kinds alone and SARIF never does, so the site policy
carries missing-versus-ambiguous until a design reaches all three channels.

Elsewhere: a family whose cells are the matches
of one namespace pattern (ArchUnit's `(*)` capture) — no consumer, the field's families being
projects and the corpus's declared layers; an intersection
combinator (`arch.AnyOf` is union only; `Except` covers difference); unions of *member*
selections (the projections compose over a union subject already, and a member `Except` takes its
payloads as a list, §5.7); a layered-architecture
macro for the ordering half (the coverage half is `MustBelongTo`, the independence half is
`arch.Each(…).MustNotReferenceEachOther()`, the no-order law — peers that may reference each
other but not in a circle — is `arch.Each(…).MustNotHaveCircularReferences()`, and ordering
stays explicit `MustNotReference` pairs — N overlapping sentences under one ID would muddy a
baseline where a partition's cells do not);
the remaining type-side shape adjectives (`.ThatAreSealed()`, `.ThatAreStatic()`,
`.ThatAreAbstract()`) — the constraint-side verbs and the `ITypeInfo` flags exist (§5.3, §5.6);
only the adjective position is missing. The accessibility leg of that entry shipped
(`.ThatArePublic()` / `.ThatAreInternal()` / `.ThatArePrivate()`, §5.2), on a consumer that could
not be written without it: a namespace-scoped residency subject that has to leave private nested
types out, which no name and no glob can reach; a
`.MayMatchNothing()` opt-out from the fail-on-empty default (§4.1); a `MustBeOfKind` constraint
twin.

On computed lists (§3.3): sequence forms for the other `(first, more)` lists, each waiting on a spec
seen computing one: `Each`'s cells, the negative anchor lists (`MustNotImplement`,
`MustNotDeriveFrom`, `MustNotBeAttributedWith`), `.Returning`, `MustOnlyTarget` and the package
pattern pair (`MustOnlyReferencePackages`, `MustNotReferencePackages`). Until then a computed list
reaches one of them as `(list[0], list.Skip(1).ToArray())`.

## 12. Canonical sample spec

Compiles as pinned test code.

```csharp
public sealed class ArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*").Purpose("Domain holds the order and customer model.");
        Layer web    = arch.Layer("Web",    "MyApp.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")
                         .MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                from: "Controllers open SqlConnection directly (legacy Active Record style).",
                to: web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
            .Baseline("arch/baseline.json")
            .WhileYoureThere(MigrationPolicy.MigrateIfSmall)
            .Because("Repository pattern for testability — ADR-012.")
            .Fix("Inject the repository.")
            .Exemplar(arch.Types.Named("OrdersRepository"));

        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Baseline("arch/baseline.json")
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");

        arch.Scope("domain/pricing")
            .Caution(arch.Types.Named("PricingEngine"))
            .Dragons("Discount rules are evaluated in declaration order and the first match wins; " +
                     "the catalogue states exceptions before defaults. Do not sort or de-duplicate the list.")
            .Because("Every checkout path calls PricingEngine directly; a facade would cost more than it guards.");

        arch.Rule("naming/handlers")
            .Enforce(arch.Types.Implementing(typeof(IHandler<>)).MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based (see HandlerRegistry).");

        arch.Rule("di/handlers-via-registry")
            .Enforce(arch.Types.Except(arch.Type<HandlerRegistry>())
                         .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>))))
            .Because("Handlers are resolved through HandlerRegistry; direct construction bypasses discovery.");

        arch.Rule("style/type-name-length")
            .Enforce(arch.Types.InNamespace("MyApp.*")
                         .Must(t => t.Name.Length <= 40,
                               description: "keep type names at or under 40 characters"))
            .Because("Long type names break the generated architecture tables.");
    }
}
```

## 13. Composition: rule packs

A rule pack is an ordinary class library. It exports static methods that take a caller's `Arch`:
most declare a rule on it, and some hand back material a caller's own rule is built from. A
consuming spec project references the pack and calls the ones it wants. There is no plugin host,
no manifest, no include directive, and no discovery: a rule lands only where a spec calls for it.
Every claim below is pinned by a test.

**Many spec classes, one `Arch`.** A build runs every discovered spec's `Define` against a single
`Arch` instance, and rules land in the order the specs ran (§3.2). A pack call inside one spec and
a hand-written rule inside another compose into one model with no coordination between them.

**IDs dedupe across every source.** The duplicate-ID check (§8 item 1) runs over the whole
post-desugar ID set, so taking a rule from a pack and also writing it locally is a spec-build
error rather than two silent reports of the same law. This is why packs need no ID-prefix scheme:
a prefix would trade one loud duplicate for two quiet near-duplicates, which is the failure the
check exists to prevent.

**A pack composes with the public vocabulary only.** `Selection` and `Constraint` are closed
hierarchies (§3), so a pack has nothing private to build with. A pack-declared rule and the
hand-written equivalent therefore reify to the same nodes and render the same sentence; a pack
cannot produce a rule an inline spec could not.

**Invocation is always explicit.** Referencing a pack adds no rules. Spec discovery runs over one
assembly and never reaches into what that assembly references (§9, "no conventions layer"), so a
referenced pack contributes exactly what the spec asked for. Opting out of a pack rule is not
calling its method; there is no suppression mechanism because none is needed.

**Provenance is free.** Rule anchors capture `[CallerFilePath]`/`[CallerLineNumber]` at the call
site (§8), and inside a pack that site is the pack's own source. A pack forwards nothing and
declares nothing extra, and its rules still report at a `file:line` a reader can open. Where an ID
collides, the report lands at the *first authored* occurrence, so which file is named follows the
order the specs ran.

**Posture belongs to the consumer.** The same pack rule is `Enforce` in a codebase that already
keeps it and `Migrate`, against its own baseline, in one that does not. A pack that shipped a
posture would be asserting a fact about code it has never seen. Baseline paths stay conventional
(§4.4), so a pack never names a path either.

**The pack owns `Because` and its `Citation`; the consumer may override `Fix` and name an
`Exemplar`.** The reason a rule exists — and the canonical page that reason rests on — is the same
everywhere, so both ship with the rule. Remediation names local types, so it does not, and neither
does the type to copy: a pack has no exemplar of its own, and passes on the one the consumer names.
Both are parameters rather than trailers: a pack method returns `void`, which makes exactly one
`Because`, one `Citation`, one `Fix` and at most one `Exemplar` reach the rule and a second trailer
uncompilable rather than a repeated-trailer error (§8 item 6).

**A pack may export what a rule is about, not only the rule.** A codebase that must write its own
version of a pack rule (its own ID, because the reason names local stakes; its own exempt seam) can
still hold the pack's definition of what that rule governs. A pack method that hands back the
banned members instead of declaring a rule leaves the consumer every stage of the sentence and
shares only the list, so a member added to the pack's ban is added to both rules at once.
`MustNotUse` takes one member and the rest (§3.3), so an exported set arrives in that shape and
spreads into the call.

**Anchor doctrine for a self-hosting pack.** A pack that ships inside a codebase it governs writes
its member anchors as `arch.Member(typeof(X), nameof(X.M))`, never the expression form. An
expression anchor is real syntax: it mints a use edge attributed to the pack's own type, which
then appears as a violation of the pack's own rule. `nameof` operands mint nothing, and the two
forms reify identically (§4.5), so the doctrine costs nothing but has to be deliberate.
