# Phase 4 — Additional Diagram Types (Roadmap)

**Goal:** broaden Mermaid coverage, one `IDiagramRenderer` at a time, in priority order. This
phase is a **roadmap**: each sub-phase below is independently shippable and can be handed to a
separate agent.

**Prerequisites:** [phase 1](phase-1-html-flowchart.md) for the engine and layered layout;
[phase 2](phase-2-odf-output.md) (and [phase 5](phase-5-docx.md), once it lands) if office-format
verification is in the sub-phase's acceptance criteria;
[phase 3](phase-3-sequence-diagrams.md) as the worked example of "add a renderer, change nothing
else".

**Reading:** [04-mermaid-engine](../04-mermaid-engine.md).

## Standing rules for every sub-phase

1. **The unsupported-type fallback stays in place** until a type's renderer ships, and remains
   the behaviour for everything not yet implemented. It is never removed.
2. **No pipeline or writer changes.** A new diagram type means: a new folder under
   `Mermaid/`, a new `IDiagramRenderer`, one line added to the built-in registry, fixtures, and
   tests. A sub-phase that seems to require touching `MarkdownRenderer` or a writer is a signal
   that the abstraction needs a design discussion first, not a workaround.
3. **Reuse, don't fork.** `SvgBuilder`, `TextMetrics`, the marker definitions, and
   `LayeredLayout` are shared. Graph-shaped diagrams reuse `LayeredLayout`; list-shaped diagrams
   need no layout engine at all.
4. **Subset-first.** Ship the common 80% of a diagram type's syntax with `MERMAID003` for the
   rest, rather than delaying on completeness. Document the supported subset in the sub-phase's
   own notes and in the README's support table.
5. **Same test shape** as flowcharts/sequences ([07-testing-strategy](../07-testing-strategy.md)):
   parsing matrix, structural SVG assertions, layout invariants, determinism, well-formed XML,
   graceful degradation. No pixel comparisons.
6. **Fidelity target is unchanged**: structurally correct and readable, not pixel-perfect
   Mermaid.

## Priority order

Priority weighs *frequency in real design documents* against *implementation cost*
(does it need a layout solver?).

| # | Type | Keywords | Layout need | Cost | Priority |
| --- | --- | --- | --- | --- | --- |
| 4a | Pie chart | `pie` | None (trigonometry) | Low | **1** — implemented |
| 4b | State diagram | `stateDiagram`, `stateDiagram-v2` | Reuse `LayeredLayout` | Low–Medium | **2** — implemented |
| 4c | Class diagram | `classDiagram` | Reuse `LayeredLayout` + compartment boxes | Medium | **3** — implemented |
| 4d | ER diagram | `erDiagram` | Reuse `LayeredLayout` + crow's-foot markers | Medium | **4** — implemented |
| 4e | Gantt chart | `gantt` | None (date → x scale) | Medium (date parsing) | **5** — implemented |
| 4f | Git graph | `gitGraph` | None (lane assignment) | Medium | 6 |
| 4g | User journey | `journey` | None (table-like) | Low | 7 |
| 4h | Mindmap | `mindmap` | Tree layout (new) | Medium | 8 |
| 4i | Quadrant / XY chart | `quadrantChart`, `xychart-beta` | Axis scaling | Medium | 9 |
| — | Timeline, requirement, C4, sankey, block, packet, radar | various | varies | High | Not planned |

### 4a — `pie` (priority 1)

Subset: `pie` header with optional `title <text>`, optional `showData`, then `"Label" : value`
lines. Layout: sum values, compute each slice's angle, emit `<path>` arc wedges from a fixed
centre/radius, plus a legend column of colour swatches and `Label — value (pct)` text using a
small fixed palette (deterministic, colour-blind-friendly ordering). Guards: zero/negative
values and an empty chart degrade to the code-block fallback. Cheapest win in the list and
common in status docs.

### 4b — `stateDiagram` / `stateDiagram-v2` (priority 2)

Subset: `[*] --> S` start and `S --> [*]` end pseudo-states (rendered as filled circles),
`S1 --> S2 : label`, `state "Long name" as S`, and simple `note` lines. Deferred with
`MERMAID003`: composite/nested states (`state X { … }` — contents rendered flat), concurrency
(`--`), choice/fork/join pseudo-states, and directives. Layout: identical to flowcharts
(`LayeredLayout`, default `TD`), with stadium node shapes; direction statements `direction LR`
map onto the existing axis mapping. Highest value-per-line because the layout already exists.

### 4c — `classDiagram` (priority 3)

Subset: `class Foo { +int Bar \n +Baz() }` member blocks, `Foo : +method()` shorthand, and the
relation set `<|--` (inheritance), `*--` (composition), `o--` (aggregation), `-->`
(association), `..>` (dependency), `..|>` (realization), each with an optional `: label` and
optional cardinality strings. Deferred: generics beyond plain text, annotations
(`<<interface>>` rendered as a text line), namespaces, `click`/`style`. Rendering: a
three-compartment box (name / attributes / operations) with divider lines, sized by the widest
member; relations reuse `LayeredLayout` (inheritance edges pointing "up" define the ranking) with
per-relation line style and a distinct `<marker>` (hollow triangle, filled/hollow diamond, open
arrow) plus a dashed stroke where required.

### 4d — `erDiagram` (priority 4)

Subset: `A ||--o{ B : label` relationship syntax with the four cardinality glyph pairs, and
optional attribute blocks `A { string name PK }` rendered as a two-compartment box. Layout via
`LayeredLayout`; the work is the crow's-foot marker set (one/many, optional/mandatory) at **both**
ends, which means edge rendering needs `marker-start` support — a small, well-scoped extension to
the shared edge emitter.

### 4e — `gantt` (priority 5)

Subset: `title`, `dateFormat`, `axisFormat`, `section <name>`, and task lines
`<name> :[tags,] <id>, <start>, <duration|end>`, supporting `YYYY-MM-DD` dates, `Nd`/`Nw`
durations, and `after <id>` dependencies. Layout: no solver — map dates to x with a linear scale
across the overall span, one row per task grouped under section headers, plus a date axis with
tick labels. Rendering: `<rect>` bars, `done`/`active`/`crit`/`milestone` tags mapped to fill and
shape (milestone = diamond). Cost is concentrated in date parsing and axis tick selection; use
`DateOnly`/`TimeSpan` with `InvariantCulture` only, no culture-dependent parsing.

### 4f–4i (lower priority)

Sketches only, to be expanded when they reach the top of the list. `gitGraph`: commits along a
horizontal lane per branch, merge curves between lanes. `journey`: a table-like grid of
sections × actors with score glyphs. `mindmap`: a new simple radial/indented tree layout —
notable as the first type needing layout code that flowcharts don't provide. `quadrantChart` /
`xychart-beta`: axis scaling plus point/line/bar plotting, closer to charting than to graph
layout.

## Implemented subsets

Sub-phases 4a–4e shipped together. Each lives in its own folder under
`src/MarkdownDotNetRenderer.Core/Mermaid/` (`Pie/`, `State/`, `Class/`, `Er/`, `Gantt/`) as a
parser, a model record set, a layout step, and an `IDiagramRenderer`, wired into the explicit
registry in `MermaidRenderer.CreateBuiltInRenderers`. `Writers/` and `MarkdownRenderer` were not
touched; `samples/diagram-gallery.md` is the fixture that carries all five through HTML, ODT, and
the native smoke render in `build/verify.{sh,ps1}`.

Three of the five are graph-shaped and reuse `LayeredLayout` through a shared adapter added in
this phase (`Mermaid/Graph/`): `GraphLayoutAdapter` (specs → placement), `GraphEdgePainter`
(routing, stroking, `marker-start`/`marker-end`, labels), `GraphMarkers` (the glyph set),
`DiagramSvg` (root envelope) and `SvgText` (text/line emission). That is why the ER
`marker-start` extension cost one shared field rather than a new edge emitter.

| Type | Implemented | Deferred with `MERMAID003` |
| --- | --- | --- |
| `pie` | `title` (header-tail or its own line), `showData`, `"Label" : value` slices, legend with swatch + share (+ raw value under `showData`) | Any other statement. Empty charts and non-positive or non-numeric values fail with `MERMAID002` |
| `stateDiagram`, `stateDiagram-v2` | `[*] --> S`, `S --> [*]`, `S1 --> S2 : label`, `state "Long name" as S`, `note … : text`, `direction TD`/`TB`/`LR` | Composite/nested states (contents rendered flat), concurrency (`--`), choice/fork/join pseudo-states, `classDef`/`style`/`click`, directives, `BT`/`RL` (mapped onto the supported axis) |
| `classDiagram` (and `classDiagram-v2`) | `class Foo { … }` blocks, `Foo : +member` shorthand, `<<annotation>>`, all of `<|--`, `--|>`, `<|..`, `..|>`, `*--`, `--*`, `o--`, `--o`, `-->`, `<--`, `..>`, `<..`, `--`, `..`, with optional `: label` and quoted cardinalities on either end | Namespaces, `click`, `style`, `classDef`, callbacks, links, notes, accessibility directives, generics beyond plain text |
| `erDiagram` | `A ||--o{ B : label` with all four cardinality glyph pairs on both ends, identifying (`--`) vs non-identifying (`..`, dashed), attribute blocks `A { string name PK "comment" }` | Directives and any unrecognized statement |
| `gantt` | `title`, `dateFormat YYYY-MM-DD`, `section`, task lines `<name> :[tags,] [id,] <start>, <duration|end>` with ISO dates, `Nd`/`Nw` durations, `after <id>` dependencies, and the `done`/`active`/`crit`/`milestone` tags | `axisFormat`, non-ISO `dateFormat`, sub-day durations, `excludes`/`todayMarker`/`tickInterval`, and any other statement |

### Layout decisions

- **Pie** uses a fixed centre and radius; slice angles start at twelve o'clock and the final wedge
  takes the remaining angle so floating-point rounding cannot leave a hairline gap. Colours come
  from a fixed eight-entry palette indexed by slice order, so they never depend on hashing.
- **State/class/ER** inherit the flowchart's layering, so ranking, column order, and edge routing
  are already deterministic; box sizes come from `TextMetrics` over the widest line plus named
  padding constants in each type's `*Theme`/`LayoutMetrics`.
- **Gantt** maps dates to x with a single linear scale over the whole chart span — no solver. The
  axis tick step is the smallest of a fixed candidate list (`1, 2, 7, 14, 28, 56, 112, 364` days)
  that keeps ticks at least `MinTickSpacing` apart, so the axis is a pure function of the span.
  All date handling is `DateOnly`/day arithmetic with `InvariantCulture`.
- Zero-length spans (a milestone, or a `0d` task) collapse to a diamond or to a minimum bar width
  rather than to an invisible or negative-width rectangle.

## Acceptance criteria (apply per sub-phase)

- [x] The type's fixtures render as readable SVG in **HTML** and in every office format that has
      shipped (ODT from phase 2; DOCX once phase 5 lands) — with no changes to `MarkdownRenderer`
      or to any writer.
- [x] Every parsed element appears in the output (all labels present, all edges/slices/bars
      accounted for) and nothing overlaps illegibly.
- [x] Unsupported syntax **within** the type emits at most one `MERMAID003` per construct and
      still renders the rest; malformed source falls back to the code block with `MERMAID002`;
      **nothing throws**.
- [x] Diagram types not yet implemented continue to fall back with `MERMAID001`, verified by a
      test enumerating the still-unsupported keywords.
- [x] SVG is well-formed XML, deterministic across repeated renders, and identical across
      Linux/macOS/Windows.
- [x] Tests follow [07-testing-strategy](../07-testing-strategy.md) (structural/invariant, never
      pixel), and `build/verify` still prints `PASS` on all three OSes (warning-free build, green
      tests, AOT smoke test).
- [x] The support table in `README.md` and the roadmap table above are updated in the same change.
