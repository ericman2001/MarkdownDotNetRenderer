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
`mindmap` still produces a complete, valid document.

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

### Styling and geometry: `DiagramTheme`

Visual values are **not** inline `private const` fields on a renderer. They live in
`DiagramTheme` (`Mermaid/Flowchart/DiagramTheme.cs`), a `sealed record` with defaulted
parameters and a `DiagramTheme.Default` singleton — deliberately the same shape as
`LayoutMetrics`, so the two form a pair: `LayoutMetrics` tunes *where the graph goes*,
`DiagramTheme` tunes *how it is painted*.

```csharp
public sealed record DiagramTheme(
    string NodeFill = "#ffffff",
    string NodeStroke = "#33415a",
    string EdgeStroke = "#55637a",
    string TextFill = "#111827",
    double NodeStrokeWidth = 1.5,
    double EdgeStrokeWidth = 1.5,
    double CornerRadius = 6,      // rounding at edge-polyline bends
    double ArrowInset = 2,        // how far a directed edge stops short of its target
    int LabelWrapChars = 22,      // soft wrap width for node labels
    double HorizontalPadding = 24,
    double VerticalPadding = 16,
    double MinNodeWidth = 56,
    double MinNodeHeight = 34,
    double SelfLoopBulge = 28,
    double SelfLoopLabelGap = 6)
{
    public static DiagramTheme Default { get; } = new();
}
```

It is injected exactly like `LayoutMetrics`:

```csharp
new FlowchartRenderer();                                 // Default metrics + Default theme
new FlowchartRenderer(metrics);                          // Default theme
new FlowchartRenderer(metrics, theme);                   // both explicit
FlowchartRenderer.MeasureNodes(model, fontSize, theme);  // theme optional, defaults to Default
```

Because emitters read the injected instance, the emit helpers that need styling are instance
methods rather than `static`. `MeasureNodes` stays `static` with an optional theme parameter,
since box sizing is a pure function of model + font size + theme.

**Convention:** any new diagram styling or box-geometry value goes in `DiagramTheme` (or an
analogous record for a future diagram type), never as an inline `private const` in a renderer.
The defaults in the record are the documented defaults; changing them changes rendered output,
so they are covered by the SVG structural tests.

### Text metrics without a graphics stack

Node sizing needs text width, and we have no font rasterizer (adding one would break AOT —
see [06-aot-and-dependencies](06-aot-and-dependencies.md)). `TextMetrics` therefore
*estimates*: a static per-character advance-width table for a representative sans-serif at
1 em (wide for `M W m @`, narrow for `i l . ' `), summed and scaled by font size, with a
conservative multiplier. Estimation error is acceptable because the fidelity target is
"readable, not pixel-perfect", and padding absorbs the error. Labels longer than
`DiagramTheme.LabelWrapChars` wrap at word boundaries into multiple `tspan` lines.

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

## Sequence diagrams (implemented, phase 3)

`sequenceDiagram` is the second registered type and is the proof that adding a diagram type
touches nothing but `Mermaid/`: it needs no graph solver at all. `Sequence/SequenceParser.cs`
reads participants, messages, notes, and `autonumber`; `Sequence/SequenceLayout.cs` places
actors left-to-right in declaration-then-first-mention order and stacks events top-to-bottom in
source order; `Sequence/SequenceRenderer.cs` emits the SVG. Geometry lives in `SequenceMetrics`
and paint in `SequenceTheme`, the sequence counterparts of `LayoutMetrics`/`DiagramTheme`.

Column gaps are the one place the layout looks at the whole diagram: each gap starts at
`ActorGap` and is widened just enough that every message or `Note over` label spanning that pair
of lifelines fits between them. That is a single pass over the events, so the result is a pure
function of the model — repeated renders are byte-identical.

Self-messages get a taller row with a rectangular loop drawn to the right of their lifeline;
notes take a row of their own, and a `Note left of` the first actor shifts the whole diagram
right rather than being clipped. The four arrow spellings map to four `<marker>` definitions
(filled `->>`, open `->`, cross `-x`, async `-)`), each emitted once per diagram with an id
derived from the source (`DiagramIds`) so several diagrams can share one HTML document.

Fragments (`loop`, `alt`, `opt`, `par`, `critical`, `break`, `rect`, `box`), activations, and
`title` are recognized but not drawn: each reports one `MERMAID003` per keyword and the messages
inside still render in source order.

## Additional diagram types (implemented, phase 4)

Five more types ship in [phase 4](phases/phase-4-additional-diagrams.md), each a folder under
`Mermaid/` with the same parser / model / layout / renderer split and one line in the built-in
registry. Nothing else in the pipeline changed, and no writer changed — which is the
design-validation point the phase existed to prove.

Three of them are graph-shaped, so they share one set of helpers rather than forking the
flowchart: `Graph/GraphLayoutAdapter.cs` wraps `LayeredLayout` behind a `GraphNodeSpec` /
`GraphEdgeSpec` pair, `Graph/GraphEdgePainter.cs` routes and strokes the edges and draws a glyph at
either end, `Graph/GraphMarkers.cs` owns the glyph set, and `Graph/DiagramSvg.cs` and
`Graph/SvgText.cs` own the root envelope and text emission.

Those end glyphs are drawn as ordinary geometry — a `<g>` translated to the endpoint and rotated to
follow its line — rather than referenced through an SVG `<marker>`. A `<marker>` is the part of SVG
the ODT rasterizer does not implement, which silently dropped the ER cardinality bars; plain paths
render everywhere and keep the fragment self-contained, with no `<defs>` ids to keep unique per
document. The line stops a glyph's length short of the box (`GraphMarkers.EndpointInset`) so the
glyph is not painted underneath it.

| Type | Layout | Notes |
| --- | --- | --- |
| `pie` | Trigonometry, no solver | Fixed centre/radius; the last wedge takes the remaining angle so rounding can never leave a hairline gap. `PieTheme.Palette` is a fixed, colour-blind-friendly eight-colour cycle, indexed by slice order |
| `stateDiagram`, `stateDiagram-v2` | `LayeredLayout` | `[*]` becomes the synthetic `__start`/`__end` nodes drawn as filled circles; ordinary states are stadiums, notes are dashed boxes |
| `classDiagram` | `LayeredLayout` | Three compartments (name/annotation, attributes, operations) divided by lines, sized by the widest member; each relation kind maps to one end glyph plus an optional dashed stroke; a generic's type parameters (`Repo~T~`) are not part of its identity, so a later plain `Repo` is the same box, captioned `Repo<T>` |
| `erDiagram` | `LayeredLayout` | Two-compartment entity boxes; each end of a relationship carries its own crow's-foot glyph, so the four cardinality glyphs appear at both ends of the line |
| `gantt` | Linear date → x scale, no solver | `DateOnly` arithmetic with `InvariantCulture` only; a `crit` bar's outline is drawn wider than a plain bar's so it survives being rasterized into a document; the axis tick step is picked from a fixed candidate list (`1, 2, 7, 14, 28, 56, 112, 364` days) as the smallest one that keeps ticks `MinTickSpacing` apart |

Every one of these is a pure function of its model: no iterative solving, no dictionary
enumeration order in the output, and all numbers formatted through `SvgBuilder.Number`, so
repeated renders are byte-identical. The supported subsets and the constructs deferred with
`MERMAID003` are recorded in
[phase 4](phases/phase-4-additional-diagrams.md#implemented-subsets).

## Phase 4f+: the remaining diagram types

`gitGraph`, `journey`, `mindmap`, `quadrantChart`, and `xychart-beta` are still unimplemented and
keep falling back with `MERMAID001`. Each is, again, a new folder plus one registry line.
