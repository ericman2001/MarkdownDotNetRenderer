# Phase 3 — Sequence Diagrams

**Goal:** `sequenceDiagram` blocks render as readable SVG in every output format, using a
deterministic left-to-right actor / top-to-bottom message layout. **No graph solver is
involved.**

**Prerequisites:** [phase 1](phase-1-html-flowchart.md) (dispatch, SVG plumbing, HTML writer)
and, for the office-format acceptance criteria, [phase 2](phase-2-odf-output.md). Adding this
renderer requires **no changes** to the pipeline or to any writer — that is the design payoff
being validated here.

**Reading:** [04-mermaid-engine](../04-mermaid-engine.md).

## Scope

In scope: `Sequence/SequenceParser.cs`, `Sequence/SequenceModel.cs`,
`Sequence/SequenceRenderer.cs` (an `IDiagramRenderer` for `["sequenceDiagram"]`), its
registration in the built-in registry, and tests.

### Supported syntax subset

| Syntax | Meaning |
| --- | --- |
| `sequenceDiagram` | Header (optional `autonumber` on a following line) |
| `participant A` / `participant A as Alice` | Explicit actor with optional display label |
| `actor A` | Same as `participant` (rendered identically in this phase) |
| `A->>B: text` | Solid line, filled arrowhead |
| `A-->>B: text` | Dashed line, filled arrowhead |
| `A->B: text` / `A-->B: text` | Solid/dashed line, open arrowhead |
| `A-xB: text` / `A--xB: text` | Line ending in a cross |
| `A->>A: text` | Self-message, drawn as a loop back to the same lifeline |
| `Note left of A: text`, `Note right of A: text`, `Note over A,B: text` | Note box |
| `autonumber` | Prefix each message label with a sequence number |
| `%% comment` | Ignored |

Deferred, each emitting at most one `MERMAID003` and rendering the enclosed messages inline
without their frame: `loop`/`alt`/`else`/`opt`/`par`/`critical`/`break` blocks and their `end`
markers, `activate`/`deactivate` (and `+`/`-` activation shorthand), `box` grouping, `rect`,
`link`/`links`, and `title`. Nothing is dropped silently — the messages inside a `loop` still
appear in order, only the labelled frame is missing.

## Layout (deterministic, no solver)

1. **Actor order** = order of first mention (explicit `participant`/`actor` declarations first,
   in declaration order, then any actor first seen in a message).
2. **Actor columns**: each actor gets a header box sized from `TextMetrics`; columns are laid out
   left-to-right with a fixed gap, and each column's centre x is its lifeline x. Column width is
   `max(header width, widest message label needing that span / 2)`, clamped so total width
   respects `RenderOptions.MaxDiagramWidth` (labels wrap rather than the diagram overflowing).
3. **Lifelines**: a dashed vertical `<line>` per actor from below the header box to the bottom of
   the diagram.
4. **Messages** are placed top-to-bottom in **source order**, each consuming a fixed row height
   (plus extra rows when its label wraps). Each message is a horizontal `<line>`/`<path>` from
   source lifeline to target lifeline with the arrowhead/cross marker for its type, and its label
   centred above the line with an opaque backing rect.
5. **Self-messages** consume a taller row and are drawn as a small rectangular loop out from and
   back to the same lifeline, with the label to its right.
6. **Notes** consume their own row(s): a rounded rect spanning one lifeline (offset left/right) or
   from the first to the last named lifeline (`over A,B`), with wrapped text inside.
7. **Height** = header + sum of row heights + bottom margin; diagram size and `viewBox` computed
   as in [04-mermaid-engine](../04-mermaid-engine.md).

Because every position derives from source order and measured text, output is deterministic by
construction — no iteration, no heuristics.

## Tasks

1. Implement the parser for the subset above (line-oriented, tolerant, never throwing; unknown
   lines are skipped with a single `MERMAID003`).
2. Implement the model: ordered actors, ordered rows (message | self-message | note), and message
   line/arrow style enums.
3. Implement the renderer: reuse `SvgBuilder`/`TextMetrics`; define the needed markers
   (filled arrow, open arrow, cross) once in `<defs>` with per-diagram unique ids.
4. Register `SequenceRenderer` in `MermaidRenderer`'s built-in list. Confirm the fallback path is
   now *not* taken for `sequenceDiagram` while still being taken for other types.
5. Add fixtures: a small request/response exchange, one with notes and a self-message, one using
   `autonumber`, one wrapped in a `loop` (to prove graceful degradation), and one malformed.
6. Tests mirroring the flowchart suite structure
   ([07-testing-strategy](../07-testing-strategy.md)): parsing matrix, structural SVG assertions
   (one lifeline per actor, one message line per message, all labels present, correct marker per
   arrow type), invariants (message rows strictly increasing in y; lifeline x matches its actor
   column; everything inside the viewBox), determinism, well-formed XML, and the
   `MERMAID003`-with-content behaviour for deferred constructs.
7. Update the roadmap table in [phase 4](phase-4-additional-diagrams.md) and the README to move
   `sequenceDiagram` from "planned" to "supported", and verify the same fixtures in whichever
   office formats have shipped.

## Acceptance criteria

- [ ] A sample Markdown file containing a `sequenceDiagram` renders in **HTML** as readable
      inline SVG: each actor has a labelled header and a dashed lifeline, each message is an
      arrow between the correct lifelines in source order with its label legible, and nothing
      overlaps illegibly.
- [ ] The same sample renders in **ODT** with the diagram visible in LibreOffice Writer — with
      **no changes to `OdtDocumentWriter`**, proving the writer/diagram separation holds. Once
      [phase 5](phase-5-docx.md) lands, the same must hold for `DocxDocumentWriter` in Word 2016+.
- [ ] All four arrow styles (`->>`, `-->>`, `->`, `-x`) are visually distinguishable, and
      self-messages and notes render correctly.
- [ ] `loop`/`alt`/`opt`/`activate` constructs do not break the render: the contained messages
      still appear in order, with at most one `MERMAID003` per construct type.
- [ ] Malformed sequence source falls back to the code block with `MERMAID002`; no exception.
- [ ] Emitted SVG is well-formed XML and byte-identical across repeated renders and across
      Linux/macOS/Windows.
- [ ] Build warning-free, tests green on all three OSes, and the AOT smoke test still passes.
