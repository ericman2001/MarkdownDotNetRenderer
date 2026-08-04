# Phase 5 — DOCX Output

**Goal:** `--format docx` produces a valid Word document containing the prose (headings,
paragraphs, lists, tables, code, quotes) and the diagrams embedded as **SVG only**.

DOCX is sequenced *after* ODT ([phase 2](phase-2-odf-output.md)) because it is the more
expensive path: it is the only feature in the project that adds a NuGet dependency, and that
dependency is reflection-based and hostile to AOT. ODT already gives Windows recipients a
Word-openable document. DOCX remains worth building because Word treats ODF as a
convert-on-import path with a fidelity warning, while a directly-written `.docx` is rendered
exactly as authored — and because SVG diagram display through Word's ODF importer is uncertain
(phase 2 measures it and records the result, which is the main input to how urgent this phase
really is). The PNG raster fallback that makes those diagrams visible in older Word is a further
step on top of this one, [phase 6](phase-6-docx-png-fallback.md).

**Prerequisites:** [phase 1](phase-1-html-flowchart.md) complete — `DocumentContent`,
`IDocumentWriter`, and the flowchart renderer already exist and are format-agnostic.
[Phase 2](phase-2-odf-output.md) is strongly recommended first: it establishes the
Markdown-to-office prose mapping (headings, nested lists, tables, code blocks, quotes) and its
test fixtures, which this phase mirrors in OOXML.

**Reading:** [05-output-writers](../05-output-writers.md) (the mapping table and the exact
OOXML pieces), [06-aot-and-dependencies](../06-aot-and-dependencies.md) (the OpenXml AOT
mitigation, which is a hard requirement of this phase), [03-core-api](../03-core-api.md)
(`WRITER001` diagnostics).

## Scope

In scope: the `DocumentFormat.OpenXml` dependency, `DocxDocumentWriter`, style/numbering parts,
SVG image embedding, DOCX-specific diagnostics, structural tests, and containment of AOT/trim
warnings.

Out of scope: PNG raster fallback ([phase 6](phase-6-docx-png-fallback.md)), ODT
([phase 2](phase-2-odf-output.md)), headers/footers, page setup beyond defaults, table of
contents, cross-references, tracked changes, templates/`.dotx`.

## Tasks

1. **Add the dependency.** `DocumentFormat.OpenXml` (latest stable, pinned exact version) to
   `Core.csproj`. Update `THIRD-PARTY-NOTICES.md` (MIT) and the dependency table in
   [06-aot-and-dependencies](../06-aot-and-dependencies.md) if the version policy changes.

2. **Contain the AOT damage — do this first, not last.** Confirm what warnings the reference
   produces under `IsAotCompatible` + `TreatWarningsAsErrors`, then apply the mitigation from
   [06-aot-and-dependencies](../06-aot-and-dependencies.md): keep every OpenXml type inside
   `Writers/Docx*`, expose no OpenXml type in public API, and suppress trim/AOT warnings
   narrowly at the file/member level with an explanatory comment. If narrow suppression proves
   unworkable, execute the documented fallback (split the writer into a separate
   `MarkdownDotNetRenderer.OpenXml` assembly) and record the decision in that doc.

3. **`DocxDocumentWriter` skeleton.** `FileExtension = ".docx"`,
   `ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"`.
   Create a `WordprocessingDocument` of type `Document` over the destination stream, add
   `MainDocumentPart`, `Body`, and a final `SectionProperties` (Letter/A4 default, 1-inch
   margins).

4. **Style and numbering parts.**
   - `StyleDefinitionsPart` defining `Normal`, `Heading1`–`Heading6`, a monospace `CodeChar`
     run style, and a `Quote` paragraph style, all derived from `RenderOptions.FontFamily` where
     applicable.
   - `NumberingDefinitionsPart` with one bullet abstract numbering and one decimal abstract
     numbering, each defining levels 0–4 with sane indents, plus the `NumberingInstance`s that
     paragraphs reference.

5. **Prose mapping.** Implement a Markdig AST → OOXML visitor covering every row of the mapping
   table in [05-output-writers](../05-output-writers.md): headings, paragraphs, inline
   emphasis/strong/strikethrough/code, links (with `HyperlinkRelationship`), nested bullet and
   ordered lists (`ilvl` = depth), task lists (`☒`/`☐` prefix), GFM tables (header row, borders,
   per-column alignment), fenced/indented code blocks (shaded single-cell table, one paragraph
   per line, preserved spaces), block quotes, thematic breaks, and local images. Unsupported
   constructs (raw HTML, anything unmapped) render as plain text plus a `WRITER001` diagnostic —
   never an exception.

6. **Diagram embedding (SVG-only).** Reuse the diagram sizing/alt-text logic already exercised
   by the ODT writer, then implement precisely the four OOXML pieces from
   [05-output-writers](../05-output-writers.md): an `image/svg+xml` `ImagePart`; a
   `Drawing`/`wp:inline` with `wp:extent` in EMU (`px * 9525`) and `wp:docPr` carrying the alt
   text; `pic:pic` with `blipFill`/`spPr`; and the
   `a:blip/a:extLst/a:ext[@uri="{96DAC541-7B7A-43D3-8B79-37D633B846F1}"]` wrapper containing
   `asvg:svgBlip r:embed="…"`. With no raster part, `a:blip/@r:embed` points at the SVG part as
   well. Build the `svgBlip` via `OpenXmlUnknownElement` from an XML string (string
   construction, not reflection). **Factor this so a second (raster) blip relationship can be
   added in [phase 6](phase-6-docx-png-fallback.md) without restructuring.**

7. **Fallback code blocks** (unsupported diagram types) reuse the code-block rendering from task
   5, so the verbatim mermaid source is preserved in the DOCX too.

8. **Determinism.** Fixed core-properties timestamps, `docPr` ids from a per-document counter,
   and stable relationship-id assignment, so repeated renders produce identical `document.xml`
   ([07-testing-strategy](../07-testing-strategy.md)).

9. **Wire up.** `MarkdownRenderer`'s writer `switch` gains the `Docx` branch; the CLI's
   `--format docx` now works and its extension inference maps `.docx` → `OutputFormat.Docx`.

10. **Tests** — area 9 of [07-testing-strategy](../07-testing-strategy.md): package opens,
    `OpenXmlValidator` reports zero errors, structural assertions for headings/lists/tables/runs,
    the SVG image part + extension element + resolvable `r:embed`, fallback code-block content,
    and determinism across two renders. Also assert the HTML path still passes the AOT smoke test
    after the OpenXml reference lands.

## Acceptance criteria

- [ ] `mdrender -i samples/kitchen-sink.md -o out.docx -f docx` produces a file that **opens in
      Microsoft Word without a repair prompt**, with correct headings, paragraphs, nested lists,
      tables, code blocks, and quotes. *Not verifiable here — no Word install is available on a
      Linux build machine; see "Observed behaviour" below for what was verified instead.*
- [x] The same file opens in **LibreOffice Writer on Linux** with correct text structure — and,
      better than expected, **with the diagrams displayed**.
- [ ] Diagrams display as crisp vector images in Word 2016+/Microsoft 365. *Not verifiable here;
      the OOXML written is the structure Word documents for SVG pictures and is asserted by test,
      but it has not been opened in Word.*
- [x] `OpenXmlValidator` reports **zero** validation errors for every fixture.
- [x] Structural XML tests pass: heading style ids, table row/cell counts and header row, list
      `NumberingProperties`/`ilvl`, run properties for bold/italic/strike/code, the
      `image/svg+xml` part, the `wp:extent` EMU values, and the `{96DAC541-…}` `asvg:svgBlip`
      with a resolvable relationship.
- [x] Unsupported diagram types still fall back to a readable code block in DOCX, with the same
      diagnostics as HTML, and no exception.
- [x] No OpenXml type appears in any public API signature; all OpenXml usage is confined to the
      DOCX writer files. **No suppression was needed** — the reference raises no `IL2xxx`/`IL3xxx`
      warning for the APIs used.
- [x] The **HTML** path still publishes and runs under `PublishAot=true` (unchanged from phase 1).
      Whether the DOCX path works under AOT is explicitly tested and the result documented in
      [06-aot-and-dependencies](../06-aot-and-dependencies.md); if it does not, the CLI reports a
      clear error instead of crashing.
- [x] `build/verify` prints `PASS` — verified locally on Linux; `windows-latest` and
      `macos-latest` are covered by the CI matrix, which runs the same script. The AOT step did **not** fail because
      of `DocumentFormat.OpenXml`, so nothing had to be weakened; the gate now also renders DOCX
      with the native binary and byte-compares it against the managed render.

## Observed behaviour

Measured on Linux with `DocumentFormat.OpenXml 3.5.1` on .NET 9, rendering
`samples/kitchen-sink.md`.

| Consumer | Result |
| --- | --- |
| `OpenXmlValidator` (Office 2007–2021 schemas) | Zero errors on every fixture. |
| **LibreOffice Writer 7.3.7** | Opens with no repair prompt. Headings, nested lists, the GFM table with its header row, shaded code blocks, quotes and the horizontal rule all come through. The SVG diagram pictures **are imported and displayed** — converting the `.docx` to `.odt` shows each diagram as a `draw:image` with the original SVG retained plus a raster replacement LibreOffice generated itself, and they appear in its PDF export. |
| **Microsoft Word** | **Not tested — no Word install is available on the build machine**, and Office automation is out of scope for this project ([06](../06-aot-and-dependencies.md)). What is asserted by test instead: the package structure Word requires (content types, `word/document.xml`, styles, numbering, core properties) and the exact SVG picture markup Word 2016+ reads — `a:blip/a:extLst/a:ext[@uri="{96DAC541-…}"]/asvg:svgBlip` with an `r:embed` that resolves to an `image/svg+xml` part. This row must be filled in from a real Word install before the two Word acceptance boxes above can be ticked. |
| Native AOT binary | `--format docx` works, and its output is byte-identical to the managed render. |

The LibreOffice result is better than [05-output-writers](../05-output-writers.md) predicted, which
narrows [phase 6](phase-6-docx-png-fallback.md) to older Word, Google Docs and WordPad rather than
"every non-Word consumer".
