# Phase 2 — OpenDocument (ODT) Output for LibreOffice / OpenOffice

**Goal:** the second output format, `OutputFormat.Odt`, producing an OpenDocument Text (`.odt`)
file that opens natively in LibreOffice Writer, Apache OpenOffice, and Collabora — with prose
and **native SVG diagrams**, and **no new dependency**.

ODT comes before DOCX ([phase 5](phase-5-docx.md)) deliberately: it is the cheaper of the two
office formats by every measure — no NuGet dependency, no reflection, no trim/AOT suppressions,
and SVG diagrams that simply work — so it delivers a shareable office document sooner and with
less risk. It also solves the prose-mapping problem (headings, lists, tables, code, quotes)
once, in a simpler format, which the DOCX writer then mirrors.

**Prerequisites:** [phase 1](phase-1-html-flowchart.md) (the `IDocumentWriter` seam and
diagram SVG). Nothing else.

**Reading:** [05-output-writers](../05-output-writers.md) (writer table and ODT sketch),
[06-aot-and-dependencies](../06-aot-and-dependencies.md) (why this path is the most AOT-friendly
office format), [07-testing-strategy](../07-testing-strategy.md) (area 8).

## Why ODT

- Not every recipient has Microsoft Word. On Linux especially, LibreOffice is the default office
  suite, and the project treats Linux as a first-class platform.
- **ODF consumes SVG natively.** LibreOffice renders an SVG referenced from `draw:image`
  directly, so the diagram-visibility problem that motivates
  [phase 6](phase-6-docx-png-fallback.md) simply does not exist here — no rasterizer, no
  compatibility tradeoff.
- **Zero new dependencies.** An `.odt` is a zip of XML: `System.IO.Compression.ZipArchive` plus
  `System.Xml.XmlWriter`, both in the BCL and both AOT-clean. Unlike the DOCX path, the ODT path
  can be fully AOT-supported with no reflection and no warning suppressions
  ([06-aot-and-dependencies](../06-aot-and-dependencies.md)).
- LibreOffice can convert `.odt` → `.docx`/PDF headlessly if a consumer needs those, so this
  format is also a useful interchange base.
- Word 2010+ and Microsoft 365 **can** open `.odt` directly, so this format is not
  Linux-only in practice. It is not a *replacement* for DOCX, though: Word treats ODF as an
  import/convert path (with a fidelity warning), and whether Word's ODF importer renders an SVG
  referenced from `draw:image` is unverified — see the acceptance criteria below, which record it
  as an explicit empirical check rather than an assumption. That uncertainty is precisely why
  [phase 5](phase-5-docx.md) still exists.

## Package structure to produce

| Entry | Notes |
| --- | --- |
| `mimetype` | Content `application/vnd.oasis.opendocument.text`. **Must be the first zip entry and stored uncompressed** (`CompressionLevel.NoCompression`), or LibreOffice may refuse the file |
| `META-INF/manifest.xml` | `manifest:file-entry` for the root document and **every** part, including each picture, with correct media types |
| `content.xml` | `office:document-content` → `office:body/office:text` with the document flow, plus automatic styles |
| `styles.xml` | `office:document-styles`: named paragraph/text/table/graphic styles (`Heading_20_1`…, `Preformatted_20_Text`, `Quotations`) and page layout |
| `meta.xml` | `office:document-meta`: title from `RenderOptions.DocumentTitle`, generator string, fixed timestamps for determinism |
| `Pictures/diagram-N.svg` | One SVG part per diagram (with an XML prolog) |

All XML is written with `XmlWriter` using explicit namespace prefixes (`office`, `text`, `style`,
`table`, `draw`, `fo`, `svg`, `xlink`) — hand-written, no serializer, no reflection.

## Markdown → ODF mapping

| Markdown | ODF |
| --- | --- |
| `# … ######` | `text:h` with `text:outline-level` 1–6 and `text:style-name="Heading_20_N"` |
| Paragraph | `text:p` |
| `**bold**` / `*italic*` / `~~strike~~` / `` `code` `` | `text:span` with an automatic `style:text-properties` style (`fo:font-weight`, `fo:font-style`, `style:text-line-through-style`, monospace `style:font-name`) |
| Link | `text:a xlink:href` |
| Bullet / ordered list | `text:list` (nested for depth) with a `text:list-style` declaring bullet or number levels |
| Task list | `text:list` item whose text begins `☒`/`☐` |
| Table | `table:table` + `table:table-column` + `table:table-row`/`table:table-cell`; header row in `table:table-header-rows`; cell alignment via automatic paragraph styles |
| Fenced code block | `text:p` with `Preformatted_20_Text`, one paragraph per line, `text:s`/`text:tab` for runs of spaces |
| Block quote | `text:p` with an indented, left-bordered automatic style |
| Thematic break | `text:p` with a bottom-border style |
| Image | `draw:frame`/`draw:image` referencing a `Pictures/` entry |
| Diagram (SVG) | `draw:frame` with `svg:width`/`svg:height` in **cm or in** (`px / 96` inches — note ODF uses physical units, unlike OOXML's EMU) containing `draw:image xlink:href="Pictures/diagram-N.svg"`, plus `svg:title`/`svg:desc` from the alt text |
| Unsupported (raw HTML, etc.) | Plain text + `WRITER001` |

## Tasks

1. **Extend the format enum**: add `OutputFormat.Odt`, map it in `MarkdownRenderer`'s writer
   `switch`, add `.odt` to the CLI's `--format` values and extension inference, and add
   `RenderOptions.Odt`. Verify nothing in Core needs to change beyond the switch — that is the
   design being validated.
2. **`Writers/Odt/OdtPackageWriter.cs`**: the zip mechanics — uncompressed `mimetype` first,
   then the parts, then a manifest generated from the actual entry list (never hard-coded).
3. **`Writers/Odt/OdtStyles.cs`**: emit `styles.xml` and the automatic-style collection used by
   `content.xml`, with a small style-deduplication cache keyed by the property set (so N bold
   spans share one automatic style and output stays deterministic).
4. **`Writers/Odt/OdtDocumentWriter.cs`**: the Markdig AST → `content.xml` visitor implementing
   the mapping table, plus diagram frames and fallback code blocks.
5. **Diagram parts**: write each SVG fragment to `Pictures/diagram-N.svg` with an XML prolog,
   register it in the manifest as `image/svg+xml`, and reference it from a sized `draw:frame`.
   Convert px → physical units with `InvariantCulture` formatting.
6. **Determinism**: fixed `meta.xml` timestamps, sequential picture and automatic-style names,
   and fixed zip entry order, so repeated renders produce identical bytes.
7. **Tests** — area 8 of [07-testing-strategy](../07-testing-strategy.md): `mimetype` is entry 0
   and uncompressed with the exact expected bytes; all parts present and well-formed
   (`XDocument.Parse`); the manifest lists exactly the entries in the zip; heading outline levels,
   list nesting, table row/cell counts, and span properties are correct; the diagram frame's
   `xlink:href` resolves to an existing `Pictures/*.svg` whose content matches the fragment;
   fallback code blocks preserve the verbatim mermaid source; and determinism across two renders.
8. **AOT**: include the ODT path in the AOT smoke test — publish the CLI with `PublishAot=true`
   and render a sample to `.odt` with the native binary. This path must be warning-free without
   any suppressions.
9. **Docs**: mark ODT as supported in [05-output-writers](../05-output-writers.md), the README
   support table, and [01-overview](../01-overview.md)'s decision table; note in
   [phase 6](phase-6-docx-png-fallback.md) that ODT closes much of the compatibility gap that
   phase motivated. Record the observed behaviour of **Word's** ODF import (especially SVG
   diagram display) here, since it informs how much [phase 5](phase-5-docx.md) is still needed.

## Acceptance criteria

- [ ] `mdrender -i samples/kitchen-sink.md -o out.odt -f odt` produces a file that **opens in
      LibreOffice Writer on Linux and on Windows without a repair or format warning**, with
      correct headings, paragraphs, nested lists, tables, code blocks, and quotes.
- [ ] Diagrams **display as vector images** in LibreOffice Writer — no rasterizer, no PNG.
- [ ] The file also opens in Apache OpenOffice Writer (prose correct; SVG support may vary by
      version — record the observed behaviour).
- [ ] **Measured, not assumed:** open the same `.odt` in a recent Microsoft Word and record what
      happens to the prose *and* to the SVG diagrams (rendered / rasterized / missing). Write the
      result into this document — it is the main input to how [phase 5](phase-5-docx.md) is
      prioritized.
- [ ] `mimetype` is the first zip entry, stored uncompressed, with exactly
      `application/vnd.oasis.opendocument.text`.
- [ ] `META-INF/manifest.xml` lists every entry actually present, with correct media types, and
      all XML parts are well-formed.
- [ ] Unsupported diagram types fall back to a readable code block with the same diagnostics as
      the HTML and DOCX paths; nothing throws.
- [ ] **No new NuGet dependency** was added, and the ODT path publishes and runs under
      `PublishAot=true` with **no** trim/AOT warning suppressions.
- [ ] Repeated renders are byte-identical, and output is identical across Linux, macOS, and
      Windows.
- [ ] Build warning-free and tests green on all three OSes.
