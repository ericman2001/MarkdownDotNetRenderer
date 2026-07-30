# 04 — Mermaid Rendering Engine

This is the heart of the project: turning Mermaid source text into SVG **entirely in C#**,
with no JS engine and no external process. See
[01-overview](01-overview.md) for why.

## Extraction

`MermaidBlockExtractor` walks the Markdig AST and selects `FencedCodeBlock` nodes whose
info string's first whitespace-delimited token equals `mermaid`, compared with
`OrdinalIgnoreCase`. Matching only the first token means ` ```mermaid `,
` ```Mermaid `, and ` ```mermaid title="Flow" ` all match, while ` ```mermaidish ` does
not. The block's raw lines (with the fence and any indentation stripped by Markdig) become
the diagram source; the block's `Line` property provides the `SourceLine` for diagnostics.

## Dispatch

```csharp
public interface IDiagramRenderer
{
    /// <summary>Diagram-type keywords this renderer handles, e.g. ["flowchart", "graph"].</summary>
    IReadOnlyCollection<string> DiagramTypes { get; }

    /// <summary>Lay out and emit SVG. Must not throw for malformed input.</summary>
    DiagramRenderResult Render(string mermaidSource, RenderOptions options);
}

public sealed record DiagramRenderResult(
    bool Success,
    string? SvgFragment,
    double Width,
    double Height,
    string? AltText,
    IReadOnlyList<RenderDiagnostic> Diagnostics)
{
    public static DiagramRenderResult Failed(string code, string message) => ...;
}

public sealed class MermaidRenderer
{
    public MermaidRenderer();                                    // built-in renderers
    public MermaidRenderer(IEnumerable<IDiagramRenderer> renderers);

    public DiagramRenderResult Render(string mermaidSource, RenderOptions options);
}
```

`MermaidRenderer` builds a `Dictionary<string, IDiagramRenderer>` (ordinal-ignore-case)
from each renderer's `DiagramTypes`. The registry is populated by an **explicit list in
code** — no assembly scanning, no attributes, no `Activator` — which is what keeps Core
reflection-free and AOT-clean.

### Diagram-type detection

`Render` normalizes the source before dispatch:

1. Strip a UTF-8 BOM; normalize CRLF/CR to LF.
2. Skip blank lines, `%%` comment lines, and `%%{ ... }%%` directive blocks (emitting
   `MERMAID003` once if a directive was present and ignored).
3. Take the first remaining line; the diagram type is its first token, cut at the first
   whitespace **or** the first of `;`, `:`, or `(`. Examples:
   - `flowchart TD` → `flowchart`
   - `graph LR;` → `graph`
   - `sequenceDiagram` → `sequenceDiagram`
   - `stateDiagram-v2` → `stateDiagram-v2` (registered as its own key)
4. Look up the key. A miss returns `MERMAID001` with `Success = false`.

The whole *source* (not the remainder) is passed to the renderer so it can re-read the
header line for direction and other options.

## Unsupported-type fallback

`MermaidRenderer.Render` **never throws** and never returns partial garbage. On failure
(`Success = false`), `MarkdownRenderer` converts the block into a `CodeBlock` containing the
original mermaid source with language `mermaid`, and appends the diagnostic. Each writer
renders that as:

- **HTML**: `<pre><code class="language-mermaid">…escaped…</code></pre>` — visually identical
  to what a plain Markdown-to-HTML converter would do.
- **DOCX**: a monospaced paragraph per line, inside a lightly shaded single-cell table so it
  reads as a code block.

This guarantee is what allows shipping diagram types incrementally: a Phase 1 build handed a
`gantt` chart still produces a complete, valid document.

Renderers themselves are also required to be total functions: every `IDiagramRenderer`
implementation catches its own parse failures and returns `DiagramRenderResult.Failed(...)`,
and `MermaidRenderer` additionally wraps the call in a `try/catch` as a backstop that
converts any escaped exception into `MERMAID002`.

## SVG emission approach

Diagrams are emitted as **hand-written SVG** via a small `SvgBuilder` (a `StringBuilder`
wrapper with attribute/text escaping). No SVG DOM library, no XML serializer, no
reflection.

Primitives used:

| Element | Use |
| --- | --- |
| `<rect rx ry>` | Rectangular / rounded node shapes |
| `<ellipse>` | Stadium and circle-ish node shapes |
| `<polygon>` | Rhombus (decision) nodes, arrowheads |
| `<line>` / `<path>` | Edges: straight segments, or orthogonal polylines for multi-layer spans |
| `<text>` | Node and edge labels; `text-anchor="middle"`, `dominant-baseline="middle"` |
| `<tspan>` | Wrapped label lines (one `tspan` per line with `dy`) |
| `<defs><marker>` | A single shared arrowhead marker referenced by `marker-end` |
| `<g>` | Grouping per node/edge, carrying a `data-id` for testability |

Fragment contract (see [02-architecture](02-architecture.md)):

```html
<svg xmlns="http://www.w3.org/2000/svg" width="640" height="320"
     viewBox="0 0 640 320" role="img" aria-label="flowchart">
  <defs><marker id="mdnr-arrow" ...>…</marker></defs>
  <g class="mdnr-node" data-id="A"> <rect .../><text ...>Start</text> </g>
  …
</svg>
```

Rules:

- Always emit `xmlns`, `width`, `height`, and `viewBox`. `width`/`height` are unitless CSS
  pixels so HTML sizes it correctly and the DOCX writer can convert to EMU
  (`emu = px * 9525`).
- No `<style>`, no CSS classes that require external CSS, no `<script>`, no external
  references. All visual attributes (`fill`, `stroke`, `font-family`, `font-size`) are
  inline presentation attributes so the fragment renders identically inline in HTML and as a
  standalone SVG part in DOCX.
- IDs are prefixed (`mdnr-`) and made unique per diagram (`mdnr-arrow-3`) because multiple
  inline SVGs share one HTML document's ID space.
- `role="img"` plus `aria-label` for accessibility; `AltText` is derived from the diagram
  type and node count (e.g. "flowchart with 6 nodes and 7 edges").

### Text metrics without a graphics stack

Node sizing needs text width, and we have no font rasterizer (adding one would break AOT —
see [06-aot-and-dependencies](06-aot-and-dependencies.md)). `TextMetrics` therefore
*estimates*: a static per-character advance-width table for a representative sans-serif at
1 em (wide for `M W m @`, narrow for `i l . ' `), summed and scaled by font size, with a
conservative multiplier. Estimation error is acceptable because the fidelity target is
"readable, not pixel-perfect", and padding absorbs the error. Labels longer than a
configured character budget wrap at word boundaries into multiple `tspan` lines.

## Phase 1: flowcharts

### Supported syntax subset

Header: `flowchart <DIR>` or `graph <DIR>`, where `<DIR>` ∈ `TD`, `TB` (treated as `TD`),
`LR`. `BT`/`RL` are accepted by the parser and rendered as their reverse (`TD`/`LR`) with an
`MERMAID003` info diagnostic in Phase 1.

| Syntax | Meaning |
| --- | --- |
| `A --> B` | Edge with arrowhead from `A` to `B` |
| `A --- B` | Edge without arrowhead |
| `A -->|text| B` | Edge with label `text` |
| `A -- text --> B` | Alternative edge-label form |
| `A[Label]` | Rectangle node with label |
| `A(Label)` | Rounded-rectangle node |
| `A([Label])` | Stadium node |
| `A{Label}` | Rhombus (decision) node |
| `A` (bare) | Node whose label is its id |
| `A --> B --> C` | Edge chain, expanded to two edges |
| `%% comment` | Ignored |

Explicitly ignored in Phase 1 (each emits `MERMAID003` at most once): `subgraph`/`end`,
`classDef`/`class`/`style`, `click`, `linkStyle`, dotted/thick edge variants (rendered as
plain edges), and `&` multi-node shorthand. `subgraph` contents are still rendered as
ordinary nodes so no information is lost — only the visual grouping box is missing.

Parsing is line-oriented and hand-written: for each statement line, match node-definition
and edge patterns, registering nodes on first mention in source order (which fixes the
tie-breaking order used later by the layout).

### Layout: simplified layered (Sugiyama-style)

Four steps. All are deterministic — no randomness, no iteration-count-dependent output.

**1. Layer ranking (longest path).** Assign each node a layer such that every edge points
from a lower to a higher layer:

- Compute the DAG condensation implicitly: run a DFS to detect back-edges and mark them as
  *reversed* edges. Reversed edges are laid out as if reversed, then drawn with the
  arrowhead on the original end. This keeps cycles from breaking the ranking.
- `layer(n) = 0` for sources; otherwise `layer(n) = 1 + max(layer(p))` over predecessors
  (longest-path ranking, computed in topological order of the acyclic-ified graph). Longest
  path is chosen over network-simplex because it is trivially implementable and, for
  diagrams of documentation size, visually fine.
- Guard: if the graph exceeds a node/edge cap (default 500 nodes / 1000 edges), abort with
  `MERMAID004` and fall back to the code block rather than risk a pathological layout.

**2. Within-layer ordering.** Minimize crossings cheaply:

- Initial order per layer = source order of first mention.
- Then a fixed number (default 4) of alternating down/up sweeps using the **barycenter
  heuristic**: each node's key is the mean position of its neighbours in the adjacent layer;
  nodes are stably sorted by that key, with source order as the tie-break. Fixed sweep count
  plus stable sort = deterministic output.
- Long edges (spanning more than one layer) get **virtual nodes** inserted in the
  intermediate layers, so they participate in ordering and are drawn as polylines that route
  between nodes rather than through them.

**3. Coordinate assignment.**

- Each node's box size comes from `TextMetrics` (estimated label size + padding), with a
  per-shape minimum.
- *Cross-axis*: nodes in a layer are packed left-to-right (TD) or top-to-bottom (LR) with a
  fixed gap (default 40 px), then each layer is centred against the widest layer. A cheap
  refinement pass nudges each node toward the average cross-axis coordinate of its
  neighbours where free space allows, which visibly straightens chains.
- *Main axis*: layer `k`'s origin is the running sum of previous layer extents plus a fixed
  layer gap (default 60 px).
- Virtual nodes are zero-size points and act as polyline bend coordinates.
- Total diagram size is the bounding box plus a margin. If width exceeds
  `RenderOptions.MaxDiagramWidth`, the `viewBox` stays at intrinsic size while `width` is
  clamped and `height` scaled proportionally, so the browser/Word scales it down rather than
  clipping.

**4. Edge routing and emission.**

- Edge endpoints are clipped to the boundary of the source/target shape along the line to the
  next bend point, so arrowheads touch the shape edge, not the centre.
- Single-layer edges are straight `<line>`s; multi-layer edges are `<path>`s through their
  virtual-node bends (polyline with small rounded corners).
- Arrowheads via a shared `<marker>` and `marker-end`.
- Edge labels are drawn at the segment midpoint with a small opaque background `<rect>` so
  the label stays readable where it overlaps an edge.
- Direction mapping: **TD** = layers advance in `+y`, cross-axis is `x`; **LR** = layers
  advance in `+x`, cross-axis is `y`. Only the axis mapping differs; ranking, ordering, and
  coordinate logic are shared, and the renderer applies the mapping when writing coordinates.

### Rendering order

`<defs>` first, then all edges, then all nodes, then edge labels. Nodes after edges means
opaque node fills cover any edge that grazes them; labels last keeps them on top.

## Phase 3+: other diagram types

Each new diagram type is a new `IDiagramRenderer` registered in the built-in list; nothing
else in the pipeline changes. `sequenceDiagram` (see
[phase 3](phases/phase-3-sequence-diagrams.md)) needs no graph solver at all — actors are
placed left-to-right in first-mention order and messages stack top-to-bottom in source
order. `classDiagram`, `stateDiagram`, `pie`, and `gantt` are sketched in
[phase 4](phases/phase-4-additional-diagrams.md); `pie` and `gantt` are also solver-free,
while `classDiagram`/`stateDiagram` reuse the Phase 1 layered layout, which is why that code
lives in `Mermaid/` shared space rather than inside `Flowchart/`.
