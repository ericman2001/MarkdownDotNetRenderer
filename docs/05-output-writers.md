# 05 — Output Writers

Both writers implement `IDocumentWriter` ([03-core-api](03-core-api.md)) and consume the same
format-agnostic `DocumentContent` (prose blocks, diagram blocks, fallback code blocks).

| Writer | Format | Extension | Dependency | Phase |
| --- | --- | --- | --- | --- |
| `HtmlDocumentWriter` | Self-contained HTML5 | `.html` | Markdig only | [1](phases/phase-1-html-flowchart.md) — implemented |
| `OdtDocumentWriter` | OpenDocument Text (LibreOffice/OpenOffice; also opens in Word 2010+) | `.odt` | none (hand-written XML + zip) | [2](phases/phase-2-odf-output.md) — implemented |
| `DocxDocumentWriter` | OOXML WordprocessingML | `.docx` | DocumentFormat.OpenXml | [5](phases/phase-5-docx.md) |

ODT is implemented before DOCX because it needs no dependency and is AOT-clean; DOCX follows
because Word renders a natively-written `.docx` exactly as authored, where it treats `.odt` as a
convert-on-import path. See [phase 2](phases/phase-2-odf-output.md) and
[phase 5](phases/phase-5-docx.md).

## HtmlDocumentWriter

### Output shape

One file, no external requests, no scripts:

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>…RenderOptions.DocumentTitle or first H1 or "Document"…</title>
<style>/* minimal built-in CSS + RenderOptions.AdditionalCss */</style>
</head>
<body>
<main class="markdown-body">
  …Markdig HTML for prose…
  <figure class="mermaid-figure"><svg …>…</svg></figure>
  <pre><code class="language-mermaid">…unsupported diagram source…</code></pre>
</main>
</body>
</html>
```

### How it works

- **Prose** is rendered by Markdig's own `HtmlRenderer` over the AST nodes of each
  `ProseBlock`, reusing the exact pipeline that parsed the document so GFM extensions
  (tables, task lists, autolinks, strikethrough, footnotes) render as Markdig intends.
  Writing per-block rather than calling `Markdown.ToHtml` on the whole document is what lets
  diagram output be interleaved at the right positions.
- **Diagrams** are inlined verbatim inside a `<figure>`. The fragment already carries
  `width`/`height`/`viewBox` and inline presentation attributes, so no CSS is required for it
  to render; the built-in CSS only adds `max-width:100%; height:auto` so wide diagrams shrink
  on narrow screens.
- **Fallback code blocks** get HTML-escaped text inside `<pre><code>`.
- **Escaping** is Markdig's for prose. SVG is inserted raw — it is produced by our own
  `SvgBuilder`, which escapes all user-derived text and attribute values, so no untrusted
  markup reaches the output. Raw HTML *in the Markdown source* is passed through exactly as
  Markdig would (Markdown is trusted input, same trust model as GitHub rendering a README).
- **Encoding**: UTF-8, no BOM, `\n` line endings; `<meta charset="utf-8">` guarantees correct
  interpretation from a local `file://` open.

### Minimal built-in CSS

Roughly 20 rules, inline in `<style>`, covering: a readable max-width body column, system
font stack from `RenderOptions.FontFamily`, heading spacing, `code`/`pre` monospace and
background, GFM table borders and header shading, blockquote left border, task-list bullet
suppression, and the diagram figure rule above. No resets, no frameworks, no web fonts, no
`@import` (an `@import` would make the file non-self-contained). `IncludeDefaultCss = false`
plus `AdditionalCss` gives full control to a consumer who wants their own look.

The rules themselves live in `src/MarkdownDotNetRenderer.Core/Writers/default.css`, registered
as an `<EmbeddedResource>` in the Core `.csproj` and therefore **compiled into the assembly**.
`HtmlDocumentWriter.BuildDefaultCss(fontFamily)` reads that resource once via
`Assembly.GetManifestResourceStream`, normalises it to `\n` line endings with no trailing
newline, and substitutes the `__FONT_FAMILY__` placeholder with `HtmlEscape(fontFamily)`. Its
signature and contract are unchanged: it returns CSS text *without* a wrapping `<style>`
element.

This is purely a build-time editability improvement and changes nothing about the runtime
contract:

- The CSS is an assembly resource, **not** a runtime-loaded external config file. Nothing is
  read from disk or the network while rendering, and there is no way for a deployment to
  swap it.
- The text is still emitted inline inside the document's `<style>` block, so output stays
  self-contained and byte-identical (same rule order, same `\n` joining).
- `@import`, `url(http…)`, and any other external reference remain forbidden inside the file.

**Convention:** edit the stylesheet in `default.css`, never by reintroducing inline C# string
literals.

### Non-negotiables

- No `<script>` tag ever, and no `on*` attributes — a unit test asserts their absence.
- No `http(s)://` references in the emitted `head`/`style`, so the file renders identically
  offline.
- Opens correctly by double-click in Chrome, Edge, Firefox, and Safari, on Windows, Linux, and
  macOS.

## OdtDocumentWriter

OpenDocument Text is the natively-supported format of LibreOffice/OpenOffice and is the
better artifact for recipients who don't run Microsoft Word — including Linux users. Word 2010+
can also open it, at converter-level fidelity. Implemented in
[phase 2](phases/phase-2-odf-output.md), which also records the observed application behaviour.

- An `.odt` is a zip containing `mimetype` (stored uncompressed, first entry), `content.xml`,
  `styles.xml`, `meta.xml`, `META-INF/manifest.xml`, plus `Pictures/`.
- No NuGet dependency is needed: `System.IO.Compression.ZipArchive` plus
  `System.Xml.XmlWriter` are enough, and both are AOT-clean. This makes the ODT path *more*
  AOT-friendly than the DOCX path.
- ODF consumes SVG natively (`draw:frame`/`draw:image` referencing a `Pictures/*.svg` entry),
  so no rasterizer is needed there either.

### Structure of the implementation

| Type | Responsibility |
| --- | --- |
| `OdfNames` | Every ODF-mandated literal: namespace prefixes/URIs, the package entry names, media types, the ODF version, the XML prolog, and the `Pictures/diagram-{0}.svg` name template |
| `OdtXml` | The shared `XmlWriter` settings (UTF-8 without BOM, LF, no indentation because whitespace inside a `text:p` is content) plus prefix-aware element/attribute helpers and invariant length formatting |
| `OdtTheme` | The injectable visual record (fonts, sizes, colours, page geometry) with `OdtTheme.Default`, mirroring `DiagramTheme` |
| `OdtStyles` | `styles.xml` (named styles: `Heading_20_1`…`Heading_20_6`, `Preformatted_20_Text`, `Quotations`, `Horizontal_20_Line`, table/list/graphic styles, page layout and master page) plus the automatic-style collection with a dedupe cache keyed by property set |
| `OdtPackageWriter` | Zip mechanics: uncompressed `mimetype` first, then the parts in insertion order, then `META-INF/manifest.xml` generated from the entries actually added |
| `OdtDocumentWriter` | The `IDocumentWriter` implementation: the Markdig AST → `content.xml` visitor, the diagram frames, and `meta.xml` |

### Markdown → ODF mapping

| Markdown | ODF |
| --- | --- |
| `# … ######` | `text:h` with `text:outline-level` and style `Heading_20_1`…`Heading_20_6` |
| Paragraph | `text:p` with style `Standard` |
| `**bold**`, `*italic*`, `~~strike~~`, `` `code` `` | `text:span` referencing a deduplicated automatic text style (`fo:font-weight`, `fo:font-style`, `style:text-line-through-style`, monospace font + background) |
| Link | `text:a` with `xlink:href` |
| Bullet / ordered list | `text:list` (style `Bullet_20_List` / `Numbered_20_List`) with `text:list-item`; nested lists nest inside their item and carry their own style |
| Task list (`- [x]`) | List item paragraph prefixed with `☒`/`☐` |
| Table (GFM) | `table:table` + `table:table-column`, header row inside `table:table-header-rows`, cell alignment from the GFM alignment row on the cell-paragraph automatic style |
| Fenced/indented code block | One `text:p` with style `Preformatted_20_Text` per source line; runs of spaces become `text:s`, tabs `text:tab`, so indentation survives |
| Block quote | `text:p` with style `Quotations` (indent + left border) |
| Thematic break | Empty `text:p` with style `Horizontal_20_Line` (bottom border) |
| Image (`![]()`) | Alt text (or the URL when there is none): the writer has no base directory to resolve local files against |
| Raw HTML block/inline | The raw text, so nothing is silently dropped |

### Diagram embedding

Each `DiagramBlock` becomes `Pictures/diagram-N.svg` (the fragment verbatim, with an XML prolog
added), a manifest entry of media type `image/svg+xml`, and a `draw:frame`/`draw:image` inside a
paragraph. Sizes are physical: `svg:width`/`svg:height` in inches via `CssUnits.PixelsToInches`
(`px / 96`), formatted with `InvariantCulture` and scaled down to the text column when a diagram is
wider than the page. `DiagramBlock.AltText` is carried into `svg:title` and `svg:desc`.

### Determinism

`meta.xml` uses a fixed creation/modification timestamp and a version-free generator string; the
title comes from `RenderOptions.DocumentTitle`, else the first H1, else `Document`. Zip entry
timestamps are pinned, entry order is fixed, and picture, frame, table, and automatic-style names
are sequential, so two renders of the same input are byte-identical on every platform.

Automatic styles must precede the body in `content.xml` but are only discovered while writing it,
so the body is written twice: once into a discarded buffer to fill the dedupe cache, then for real.
The cache is keyed by property set, so the second pass adds nothing and the names agree.

## DocxDocumentWriter

Built on **DocumentFormat.OpenXml** (MIT). The writer creates a
`WordprocessingDocument` over the destination stream, adds a `MainDocumentPart`, and appends
`Body` children per block.

### Markdown → WordprocessingML mapping

| Markdown | OOXML |
| --- | --- |
| `# … ######` | `Paragraph` with `ParagraphStyleId` `Heading1`…`Heading6` (styles defined in `StyleDefinitionsPart`) |
| Paragraph | `Paragraph` of `Run`/`Text` (with `Text.Space = Preserve` where needed) |
| `**bold**`, `*italic*`, `~~strike~~`, `` `code` `` | `RunProperties`: `Bold`, `Italic`, `Strike`, `RunFonts` monospace + shading |
| Link | `Hyperlink` + a `HyperlinkRelationship` on the main part |
| Bullet / ordered list | `Paragraph` with `NumberingProperties` (`NumberingId`, `ilvl` = nesting depth) against a `NumberingDefinitionsPart` defining one bullet and one decimal abstract numbering |
| Task list (`- [x]`) | Bullet paragraph prefixed with `☒`/`☐` (checkbox content controls are out of scope) |
| Table (GFM) | `Table` with `TableProperties` (`TableBorders`, `TableLayout auto`), header row `TableRowProperties/TableHeader` + bold runs, cell alignment from the GFM alignment row |
| Fenced/indented code block | Single-cell shaded `Table`, monospace runs, one `Paragraph` per source line |
| Block quote | `Paragraph` with `ParagraphProperties/Indentation` + left `ParagraphBorders` |
| Thematic break | `Paragraph` with a bottom border |
| Image (`![]()`) | `Drawing` with an `ImagePart` when the target is a readable local file; otherwise alt text plus `WRITER001` |
| Raw HTML block/inline | Plain text of the raw content plus `WRITER001` |

A `StyleDefinitionsPart` is generated once with the heading, normal, code, and quote styles so
the document looks reasonable and remains restyleable in Word.

### SVG-only diagram embedding

Word 2016+ (and Microsoft 365) supports native SVG images. The OOXML pieces involved:

1. **The SVG part.** `MainDocumentPart.AddImagePart` with content type `image/svg+xml`; the
   fragment is written to it with an XML prolog added. This yields a relationship id, e.g.
   `rId7`.
2. **A `Drawing` / `wp:inline`** containing `wp:extent` (size in EMU, `px * 9525`),
   `wp:docPr` (`id`, `name`, `descr` = the diagram alt text), and a
   `a:graphic/a:graphicData` with URI
   `http://schemas.openxmlformats.org/drawingml/2006/picture`.
3. **A `pic:pic`** with `pic:nvPicPr`, `pic:blipFill/a:blip`, and `pic:spPr/a:xfrm/a:ext`
   matching the extent, with `a:prstGeom prst="rect"`.
4. **The SVG reference itself** goes in an extension list on the blip:
   `a:blip/a:extLst/a:ext` with
   `uri="{96DAC541-7B7A-43D3-8B79-37D633B846F1}"` wrapping an
   `asvg:svgBlip r:embed="rId7"` element in namespace
   `http://schemas.microsoft.com/office/drawing/2016/SVG/main`.

Note the structural quirk: in Microsoft's design, `a:blip/@r:embed` normally points at the
**raster** fallback and `asvg:svgBlip/@r:embed` at the SVG. With SVG-only embedding there is
no raster part, so `a:blip/@r:embed` also points at the SVG part (`rId7`). Newer Word reads
the `svgBlip` and renders crisp vector output. **Older Word, WordPad, Google Docs, and
LibreOffice may show a placeholder or nothing for these images** — that is the accepted
tradeoff of SVG-only, and the reason the raster fallback phase exists.

Because DocumentFormat.OpenXml has no strongly-typed `asvg:svgBlip`, this element is added as
an `OpenXmlUnknownElement` built from an XML string — deliberate, and it is *string
construction*, not reflection, so it does not affect AOT compatibility.

### PNG fallback: deferred

Adding a PNG next to the SVG (a second `ImagePart` referenced by `a:blip/@r:embed`) would make
diagrams display in every Word version and in LibreOffice. It requires an SVG rasterizer,
every candidate of which drags in native or reflection-heavy dependencies that conflict with
the AOT goal. **Explicitly deferred** to
[phase 6](phases/phase-6-docx-png-fallback.md); nothing in the DOCX writer may assume a
rasterizer exists, and the drawing-construction code should be factored so a second blip
relationship can be slotted in later without restructuring.

### Determinism

Core properties (`created`/`modified`) are set to a fixed timestamp (or one supplied via
options) and `docPr` ids are assigned from a per-document counter, so identical input
produces byte-identical packages apart from zip metadata — which keeps structural tests and
golden-file comparisons viable ([07-testing-strategy](07-testing-strategy.md)).

## Choosing a writer

`MarkdownRenderer` maps `RenderOptions.Format` to a writer with a plain `switch`; a format whose
writer has not shipped yet throws `NotSupportedException` naming the format. The mapping
also drives the CLI's `--format` values and its default output extension when `--output` is
omitted.
