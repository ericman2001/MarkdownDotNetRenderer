# Phase 1 — Vertical Slice: Markdown → Self-Contained HTML with Flowchart SVG

**Goal:** the first genuinely useful build. Given a Markdown file containing prose and
` ```mermaid ` flowcharts, produce **one self-contained `.html` file** with the prose rendered
as GFM and each flowchart rendered as hand-written inline SVG — no JavaScript anywhere.

**Prerequisites:** [phase 0](phase-0-scaffolding.md) complete (solution builds, tests run,
Markdig referenced).

**Reading, in order:** [03-core-api](../03-core-api.md) (exact API shapes),
[04-mermaid-engine](../04-mermaid-engine.md) (dispatch, syntax subset, layout algorithm),
[05-output-writers](../05-output-writers.md) (HTML writer),
[07-testing-strategy](../07-testing-strategy.md) (what to assert).

## Scope

In scope: Markdig GFM pipeline, mermaid block extraction, the `IDiagramRenderer` /
`MermaidRenderer` dispatch with fallback, `FlowchartRenderer` for `flowchart`/`graph` in `TD`
and `LR`, `SvgBuilder` + `TextMetrics`, `HtmlDocumentWriter`, the public `MarkdownRenderer`,
the CLI, and unit tests for all of it.

Out of scope: ODT ([phase 2](phase-2-odf-output.md)), DOCX ([phase 6](phase-6-docx.md)),
sequence diagrams ([phase 3](phase-3-sequence-diagrams.md)), any other diagram type
([phase 4](phase-4-additional-diagrams.md)), `subgraph` boxes, Mermaid theming/`classDef`, and
PNG rasterization ([phase 5](phase-5-docx-png-fallback.md)).

`OutputFormat.Odt` and `OutputFormat.Docx` must exist in the enum, but selecting either in this
phase returns a clear `NotSupportedException`-style error message from the CLI naming the phase
that implements it (documented, tested), not a crash.

## Tasks

### 1. Markdown pipeline

- `Markdown/MarkdownPipelineFactory.cs`: a static factory returning a cached
  `MarkdownPipeline` built with `new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()`.
  One pipeline instance is reused for parse and HTML rendering so extension behaviour is
  consistent.

### 2. Mermaid block extraction

- `Markdown/MermaidBlockExtractor.cs`: walk the `MarkdownDocument` (descending into container
  blocks) and produce the ordered `DocumentBlock` list, splitting prose runs around mermaid
  fenced code blocks. Match rule: info string's first token equals `mermaid`,
  `OrdinalIgnoreCase` ([04-mermaid-engine](../04-mermaid-engine.md)). Capture the fence's line
  number for diagnostics.

### 3. Mermaid dispatch

- `Mermaid/IDiagramRenderer.cs`, `Mermaid/DiagramRenderResult.cs`,
  `Mermaid/MermaidRenderer.cs` exactly as sketched in
  [04-mermaid-engine](../04-mermaid-engine.md): normalize source, skip comments/directives,
  read the first token, look up an explicit registry dictionary, and `try/catch` the renderer
  call as a backstop. Unknown type → `MERMAID001`, `Success = false`.

### 4. SVG plumbing

- `Svg/SvgBuilder.cs`: `StringBuilder`-based emitter with `StartElement`/`Attribute`/`Text`
  helpers that XML-escape all values; numeric formatting always
  `CultureInfo.InvariantCulture` with a fixed decimal precision (2 places) so output is stable.
- `Svg/TextMetrics.cs`: static advance-width table, `MeasureWidth(string, double fontSize)`,
  and `WrapLabel(string, int maxChars)` returning lines for `tspan` emission.

### 5. FlowchartRenderer

Split into parser / model / layout / renderer as in
[02-architecture](../02-architecture.md):

- `Flowchart/FlowchartParser.cs` — the Phase 1 syntax subset: header direction (`TD`/`TB`/`LR`,
  with `BT`/`RL` accepted and reversed + `MERMAID003`), node shapes `A[..]`, `A(..)`,
  `A([..])`, `A{..}`, bare ids, edges `-->`, `---`, `-->|label|`, `-- label -->`, chains, `;`
  statement separators, `%%` comments. Ignored constructs emit at most one `MERMAID003` each and
  never fail the parse. Nodes are registered in first-mention order.
- `Flowchart/LayeredLayout.cs` — the four-step simplified Sugiyama layout described in
  [04-mermaid-engine](../04-mermaid-engine.md): cycle-breaking DFS + longest-path ranking →
  barycenter ordering with a fixed sweep count and stable sort → coordinate assignment with
  virtual nodes for long edges → axis mapping for `TD`/`LR`. Enforce the node/edge guard
  (`MERMAID004`).
- `Flowchart/FlowchartRenderer.cs` — `IDiagramRenderer` for `["flowchart", "graph"]`: emit
  `<defs>` marker, edges (lines/paths, endpoints clipped to shape boundaries, `marker-end` only
  for directed edges), nodes (shape + wrapped label `tspan`s), then edge labels with backing
  rects. Produce the root `<svg>` with `width`/`height`/`viewBox`/`role`/`aria-label` and
  per-diagram unique ID prefix.

### 6. HTML writer

- `Writers/IDocumentWriter.cs` and `Writers/HtmlDocumentWriter.cs` per
  [05-output-writers](../05-output-writers.md): document shell, minimal inline CSS honouring
  `IncludeDefaultCss`/`AdditionalCss`/`FontFamily`, Markdig `HtmlRenderer` per prose block,
  inline `<svg>` in a `<figure>`, escaped `<pre><code class="language-mermaid">` for fallbacks,
  UTF-8 without BOM and `\n` endings.

### 7. Public API

- `RenderOptions`, `OutputFormat`, `RenderResult`, `RenderDiagnostic`, `IMarkdownRenderer`, and
  `MarkdownRenderer` exactly as in [03-core-api](../03-core-api.md). `RenderAsync` writes to a
  `MemoryStream`; `RenderFileAsync` reads the input asynchronously and writes to a `FileStream`.
  Honour `CancellationToken` between blocks.

### 8. CLI

`mdrender --input <path> --output <path> [--format html|odt|docx] [--title <text>] [--strict] [--help] [--version]`

- Hand-rolled argument parsing (no `System.CommandLine`, keeping the dependency budget at
  two). Accept `-i`/`-o`/`-f` short forms.
- `--format` defaults to `html`, or is inferred from the `--output` extension when that is
  unambiguous; `--output` defaults to the input path with the format's extension.
- Print diagnostics to **stderr**, one per line, as
  `<severity> <code> [line N]: <message>`. Exit `0` on success (warnings included), `1` on
  usage/IO errors, `2` when `--strict` and any warning occurred.
- Nothing but diagnostics goes to stderr, and nothing goes to stdout on success, so the tool
  composes in scripts.

### 9. Tests

Implement areas 1–7 of [07-testing-strategy](../07-testing-strategy.md): pipeline extensions,
extraction (positive/negative fences, ordering, nesting, line numbers), flowchart parsing
matrix, layout invariants (content preserved, layering monotonic, no overlaps, in-bounds,
deterministic, `TD` vs `LR`, guards), SVG structure and well-formedness, dispatcher fallback
(including a deliberately-throwing renderer double), and HTML writer self-containment plus the
`kitchen-sink` golden file. Add `samples/kitchen-sink.md` and a small
`samples/flowchart-demo.md` for manual checks.

## Acceptance criteria

- [ ] Given `samples/flowchart-demo.md` containing prose plus at least one
      ` ```mermaid ` `flowchart TD` and one `flowchart LR`, `mdrender -i … -o out.html` produces
      a **single** `.html` file that opens by double-click in Chromium/Firefox on Linux and in
      Edge/Chrome on Windows, with prose and both diagrams visible and readable.
- [ ] The output contains **no** `<script>`, no `on*=` handler, and no `http://`/`https://`
      resource reference — diagrams are inline `<svg>`, and the file renders correctly with the
      network disconnected.
- [ ] Each rendered flowchart shows every node with its correct label, every edge with correct
      direction, every edge label, and no overlapping node boxes.
- [ ] A document containing a `sequenceDiagram` (or any other non-flowchart type) renders
      successfully: that block appears as a preformatted code block with the verbatim mermaid
      source, exactly one `MERMAID001` warning is reported, and **no exception is thrown**.
- [ ] Malformed flowchart source degrades to the same code-block fallback with `MERMAID002`.
- [ ] Every emitted SVG parses as well-formed XML.
- [ ] `--format odt` and `--format docx` each produce a clear "not implemented until phase 2/6"
      error and a non-zero exit code, not a crash or an empty file.
- [ ] `dotnet build` is warning-free and `dotnet test` fully green on Linux, macOS, and Windows.
- [ ] The AOT CI job publishes the CLI with `PublishAot=true` and the **native binary
      successfully renders the sample to HTML** — this is the phase's proof that the HTML path is
      AOT-clean.
- [ ] The golden-file test for `kitchen-sink.md` passes identically on all three OSes.
