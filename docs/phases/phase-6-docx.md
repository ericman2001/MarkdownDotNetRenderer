# Phase 6 — DOCX Output

**Goal:** `--format docx` produces a valid Word document containing the prose (headings,
paragraphs, lists, tables, code, quotes) and the diagrams embedded as **SVG only**.

DOCX is sequenced *after* ODT ([phase 2](phase-2-odf-output.md)) because it is the more
expensive path: it is the only feature in the project that adds a NuGet dependency, and that
dependency is reflection-based and hostile to AOT. ODT already gives Windows recipients a
Word-openable document. DOCX remains worth building because Word treats ODF as a
convert-on-import path with a fidelity warning, while a directly-written `.docx` is rendered
exactly as authored — and because SVG diagram display through Word's ODF importer is uncertain
(phase 2 measures it and records the result, which is the main input to how urgent this phase
really is).

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

Out of scope: PNG raster fallback ([phase 5](phase-5-docx-png-fallback.md)), ODT
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
   added in phase 5 without restructuring.**

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
      tables, code blocks, and quotes.
- [ ] The same file opens in **LibreOffice Writer on Linux** with correct text structure
      (diagram images may not display there — documented and accepted for SVG-only; Linux users
      are better served by the ODT output from [phase 2](phase-2-odf-output.md), and broad
      raster compatibility is [phase 5](phase-5-docx-png-fallback.md)).
- [ ] Diagrams display as crisp vector images in Word 2016+/Microsoft 365.
- [ ] `OpenXmlValidator` reports **zero** validation errors for every fixture.
- [ ] Structural XML tests pass: heading style ids, table row/cell counts and header row, list
      `NumberingProperties`/`ilvl`, run properties for bold/italic/strike/code, the
      `image/svg+xml` part, the `wp:extent` EMU values, and the `{96DAC541-…}` `asvg:svgBlip`
      with a resolvable relationship.
- [ ] Unsupported diagram types still fall back to a readable code block in DOCX, with the same
      diagnostics as HTML, and no exception.
- [ ] No OpenXml type appears in any public API signature; all OpenXml usage is confined to the
      DOCX writer files; any trim/AOT suppression is narrowly scoped and commented.
- [ ] The **HTML** path still publishes and runs under `PublishAot=true` (unchanged from phase 1).
      Whether the DOCX path works under AOT is explicitly tested and the result documented in
      [06-aot-and-dependencies](../06-aot-and-dependencies.md); if it does not, the CLI reports a
      clear error instead of crashing.
- [ ] Build warning-free and tests green on Linux, macOS, and Windows.
