# 07 — Testing Strategy

Framework: **xUnit**, one project `tests/MarkdownDotNetRenderer.Tests`, targeting `net9.0`,
running on Linux, macOS, and Windows ([06-aot-and-dependencies](06-aot-and-dependencies.md)).
No mocking framework — the seams in this design (`IDiagramRenderer`, `IDocumentWriter`) are
trivial to implement by hand as test doubles.

## Guiding principle: structural, not pixel

We never compare rendered images, and we never assert exact coordinates. A layout tweak that
moves a node 3 px must not turn CI red. Instead:

| Assertion style | Use for |
| --- | --- |
| **Structural** — parse the output XML/HTML and assert on nodes, attributes, counts, relationships | Most diagram and writer tests |
| **Invariant** — assert properties that must hold for any correct layout (no overlaps, monotonic layer coordinates, all edges present) | Layout algorithms |
| **Golden file** — compare to a checked-in expected file | Whole-document HTML rendering of a stable sample |
| **Contains/absence** — substring checks | Narrow guarantees like "no `<script>`" |
| Pixel/image diff | **Never** |

Structural assertions parse SVG/HTML/OOXML with `System.Xml.Linq` (`XDocument`) and query with
LINQ-to-XML, so tests read as "there are 6 `rect` elements inside `g.mdnr-node`" rather than
string matching.

## Test areas

### 1. Markdig GFM integration

- The pipeline (`MarkdownPipelineFactory`) enables the expected extensions: tables, task lists,
  autolinks, strikethrough, and the rest of the advanced set. Assert by rendering small
  fixtures and checking the resulting HTML (`<table>`, `<del>`, checkbox input/marker,
  autolinked `<a href>`).
- Round-trip sanity: headings 1–6, nested lists, blockquotes, inline code, fenced code with a
  language, and thematic breaks all produce the expected top-level HTML elements.
- Character handling: non-ASCII text, `<`/`&` in prose, and emoji survive to output correctly
  encoded.

### 2. Mermaid block extraction from the AST

- Fences matched: ` ```mermaid `, ` ```Mermaid `, ` ```mermaid title=x ` → extracted.
- Fences **not** matched: ` ```mermaidish `, ` ```csharp `, bare ` ``` `, and an *indented*
  code block containing the word mermaid → left as ordinary code.
- Multiple mermaid blocks in one document are extracted in document order, and interleaved
  prose keeps its position relative to them (assert the order of blocks in
  `DocumentContent`).
- A mermaid block inside a list item or blockquote is still found (the extractor descends
  containers).
- The reported `SourceLine` matches the fence's line in the input.

### 3. `FlowchartRenderer` — parsing

- Header parsing: `flowchart TD`, `flowchart TB`, `graph LR`, `graph TD;`, with and without
  trailing comments.
- Node shapes and labels: `A[Label]`, `B(Label)`, `C([Label])`, `D{Label}`, bare `E`; label
  text with spaces, punctuation, and characters needing XML escaping (`<`, `&`, quotes) —
  assert the escaped form appears and the raw form does not.
- Edges: `A --> B`, `A --- B`, `A -->|yes| B`, `A -- yes --> B`, chains `A --> B --> C`
  (expands to two edges), and multiple statements on one line separated by `;`.
- Ignored constructs (`subgraph`, `classDef`, `click`, `style`) do not fail the render and
  produce at most one `MERMAID003` each; nodes declared inside a `subgraph` still appear.

### 4. `FlowchartRenderer` — layout invariants

Against a set of hand-built graphs (a chain, a diamond, a wide fan-out, a graph with a cycle,
a disconnected pair, a long-span edge):

- **All content present**: node count in the SVG equals parsed node count; edge/path count
  equals parsed edge count; every node label string appears in some `<text>`/`<tspan>`.
- **Layering**: for a `TD` graph, every edge's target node `y` is strictly greater than its
  source's (excluding edges that the cycle-breaker reversed); for `LR`, the same on `x`.
- **No overlap**: no two node bounding boxes intersect.
- **Bounds**: every node box lies inside the SVG `viewBox`; the `viewBox` matches
  `width`/`height`.
- **Determinism**: rendering the same source twice yields byte-identical SVG (guards against
  dictionary-ordering or randomness creeping into layout).
- **Direction**: the same graph rendered `TD` vs `LR` yields the same node/edge counts and
  labels but transposed dominant axis.
- **Guards**: a synthetic graph beyond the node cap returns `Success = false` with
  `MERMAID004` and does not hang (wrap in a generous timeout).

### 5. SVG structure

- Root `<svg>` has `xmlns`, `width`, `height`, `viewBox`, `role="img"`, `aria-label`.
- Arrowhead `<marker>` is defined once in `<defs>` and referenced by `marker-end` on directed
  edges; undirected (`---`) edges have no `marker-end`.
- Edge labels appear as `<text>` at a position between their endpoints.
- Output is well-formed XML — every SVG test parses with `XDocument.Parse`, which is itself the
  strongest single assertion in the suite.
- IDs are unique within one fragment, and two diagrams in one document do not collide.

### 6. Dispatcher fallback

- `sequenceDiagram` (before phase 3), `gantt`, `classDiagram`, `erDiagram`, and a nonsense
  `bananaDiagram` all return `Success = false` with `MERMAID001`.
- Rendering a document containing such a block yields a complete document whose diagram
  position holds a code block with the **verbatim original source**, and the render **does not
  throw** and produces exactly one warning diagnostic.
- A renderer double that throws on purpose is caught by `MermaidRenderer` and converted to
  `MERMAID002` — proving the backstop.
- Empty and whitespace-only mermaid blocks degrade gracefully.

### 7. `HtmlDocumentWriter`

- **Self-contained**: output contains no `<script`, no `on*=` attribute, no `src=`/`href=` to
  `http://`/`https://` in `head`/`style`, and no `@import`.
- Output is parseable, has a `<!DOCTYPE html>`, one `<html lang>`, `<meta charset="utf-8">`, a
  `<title>` from `DocumentTitle` (falling back to the first H1, then a default).
- Diagram SVG appears inline (an `<svg>` element in the body, not an `<img>`), inside a
  `<figure>`.
- `IncludeDefaultCss = false` omits the built-in CSS; `AdditionalCss` appears in `<style>`.
- **Golden file**: a `samples/kitchen-sink.md` covering every supported Markdown construct plus
  one flowchart renders byte-identically to `expected/kitchen-sink.html`, with `\n` endings, on
  every OS. A documented `UPDATE_GOLDEN=1` env-var switch rewrites the expectation to make
  intentional changes a one-command, reviewable diff.

### 8. `OdtDocumentWriter` (phase 2)

`mimetype` is the first zip entry and stored uncompressed with exactly the expected bytes;
`content.xml`, `styles.xml`, `meta.xml`, and `META-INF/manifest.xml` are present and well-formed
(`XDocument.Parse`); the manifest lists exactly the entries actually in the zip, with correct
media types; headings map to `text:h` with the right `text:outline-level`; nested lists map to
nested `text:list`; GFM tables map to `table:table` with the right row/cell counts and a header
row; emphasis maps to `text:span` with the expected automatic style properties; the diagram
`draw:image/@xlink:href` resolves to an existing `Pictures/*.svg` entry whose content matches the
fragment; fallback code blocks preserve the verbatim mermaid source; and two renders of the same
input are byte-identical.

### 9. `DocxDocumentWriter` (phase 6)

- The package opens with `WordprocessingDocument.Open` without validation errors, and
  `OpenXmlValidator` (2019 target) reports zero errors for the kitchen-sink sample.
- Structural XML: heading paragraphs carry the right `ParagraphStyleId`; a GFM table becomes a
  `Table` with the right row/cell counts and a header row; list items carry
  `NumberingProperties` with the right `ilvl`; bold/italic runs carry the right
  `RunProperties`.
- Diagram embedding: an `ImagePart` with content type `image/svg+xml` exists; the `Drawing`
  contains a `wp:extent` in EMU matching the fragment size; the
  `{96DAC541-…}` extension with `asvg:svgBlip` is present and its `r:embed` resolves to that
  image part.
- Fallback code blocks appear as monospaced content, and the original mermaid text is present.
- Determinism: two renders of the same input produce identical `document.xml`.

### 10. End-to-end and cross-platform

- `IMarkdownRenderer.RenderAsync` for every shipped format on the kitchen-sink sample: non-empty
  output, expected magic bytes (`<!DOCTYPE` / `PK\x03\x04`), and only the expected diagnostics.
- Selecting a format whose writer has not shipped yet throws `NotSupportedException` naming the
  format — asserted, so the pre-phase-2/6 behaviour is defined rather than accidental.
- `RenderFileAsync` writes the file, creates missing directories or fails cleanly, and returns
  the same bytes as `RenderAsync`.
- Cancellation: a pre-cancelled token yields `OperationCanceledException`.
- Path/encoding tests use `Path.Combine` and a temp directory, with no case-insensitivity
  assumptions, so they pass on Linux.
- CI runs the full suite on `ubuntu-latest`, `windows-latest`, and `macos-latest`, plus the
  **AOT smoke test**: `dotnet publish -r <rid> /p:PublishAot=true` for the CLI and a run of the
  native binary rendering a sample to HTML (and, from phase 2, to ODT), asserting exit code 0 and
  a non-empty file. That job is what actually enforces the AOT policy.

## Fixtures and conventions

- `tests/.../Fixtures/*.md` for inputs, `tests/.../Expected/*` for golden files, both
  `CopyToOutputDirectory=PreserveNewest`.
- One test class per production type, named `<Type>Tests`; test names read as
  `Method_Condition_ExpectedResult`.
- Theory-driven cases for the syntax matrices (node shapes, edge forms, diagram types).
- Every phase's acceptance criteria must be backed by at least one test in that phase; a phase
  is not done when its tests are "to be added later".
