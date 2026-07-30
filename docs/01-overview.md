# 01 — Project Overview

MarkdownDotNetRenderer is a pure-C# renderer that turns GitHub-Flavored Markdown
(including embedded Mermaid diagrams) into **self-contained HTML** and office documents
(**ODT**, then **DOCX**), with **no JavaScript anywhere in the rendering path**.

## Goals

1. **Share Markdown with non-technical readers.** A `.md` file with diagrams is useless to a
   reviewer who has a browser and an office suite and nothing else. The renderer produces a
   single `.html` file (double-click, it opens) or an office document — `.odt` or `.docx` — that
   contains the prose *and* the diagrams.
2. **Full GFM support** — tables, task lists, autolinks, strikethrough, fenced code —
   via [Markdig](https://github.com/xoofx/markdig)'s advanced pipeline.
3. **Mermaid diagrams rendered in-process, in C#.** Fenced ` ```mermaid ` blocks become
   inline SVG, produced by hand-written C# layout and SVG emission code.
4. **Two consumable artifacts**: a reusable library (`MarkdownDotNetRenderer.Core`) and a
   single-file console executable (`MarkdownDotNetRenderer.Cli`).
5. **AOT-friendly**: the HTML path must publish cleanly with `PublishAot` and no trim
   warnings.
6. **Cross-platform**: the library, CLI, and test suite build and run on **Linux, macOS, and
   Windows**. Linux is the primary development and verification target, not an afterthought —
   which rules out Windows-only APIs and Office automation entirely
   ([06-aot-and-dependencies](06-aot-and-dependencies.md)). One committed script defines a
   passing build and a three-OS CI matrix runs that same script
   ([07-testing-strategy](07-testing-strategy.md)).
7. **An open-format office output first.** OpenDocument Text (`.odt`) is the first office format
   ([phase 2](phases/phase-2-odf-output.md)): it adds no dependency, is AOT-clean, carries SVG
   natively, and opens in LibreOffice/OpenOffice *and* in Word 2010+. DOCX follows
   ([phase 5](phases/phase-5-docx.md)) because Word renders a natively-written `.docx` exactly as
   authored, while it treats `.odt` as a convert-on-import path.

## The "pure C# / no JS" philosophy

Every other Mermaid-to-image path in the .NET ecosystem ultimately shells out to
JavaScript: mermaid-cli under Node, a headless Chromium/Puppeteer instance, a hosted
Kroki/mermaid.ink service, or an embedded JS engine such as Jint running the real
`mermaid.js`. All of these are explicitly **out of scope**:

| Rejected approach | Why it is rejected |
| --- | --- |
| Node.js sidecar / `mermaid-cli` | Requires Node on every machine; process launch; huge install |
| Headless Chromium (Puppeteer/Playwright) | ~100 MB+ browser download; sandbox and CI headaches |
| Remote service (mermaid.ink, Kroki) | Network dependency; sends potentially confidential diagrams off-box |
| Embedded JS engine (Jint, ClearScript) | Reflection-heavy, hostile to AOT; still "JavaScript in the path" |
| Client-side `<script src="mermaid.js">` in the HTML output | Output is no longer self-contained/offline; DOCX cannot run scripts at all |

The consequence we accept: we re-implement a *subset* of Mermaid layout ourselves, and we
accept lower visual fidelity (see below). The benefit: a single self-contained
executable with two NuGet dependencies, deterministic offline output, and no runtime
script execution in artifacts we hand to other people.

## Fidelity target

**"Structurally correct and readable," not pixel-perfect Mermaid.** A rendered flowchart
must show every node with its correct label, every edge with the correct direction and
edge label, and a sane non-overlapping layout. It does **not** need to match
`mermaid.js`'s exact fonts, corner radii, palette, curve style, or spacing.

## Non-goals

- Pixel-parity with `mermaid.js` output or support for Mermaid themes/directives
  (`%%{init: ...}%%`), `classDef`, or custom CSS.
- Rendering *every* Mermaid diagram type at once. Diagram types arrive in phases; anything
  unimplemented degrades gracefully to a preformatted code block (never an exception).
- PDF output.
- Raster (PNG) diagram output. DOCX embeds SVG only for now; PNG rasterization is
  deferred — see [phase 6](phases/phase-6-docx-png-fallback.md).
- Automating an installed Office suite (Word/LibreOffice COM/UNO) to produce documents. All
  formats are written directly as OOXML/ODF packages, which is what keeps the tool usable on a
  headless Linux build agent.
- A general-purpose graph layout library. Layout code exists only to serve diagram
  rendering and is intentionally simple.
- Editing/round-tripping existing DOCX/ODT files, or importing document templates.

## Target users

- **Developers who write docs in Markdown** and must deliver a reviewable artifact to
  managers, clients, or auditors on Windows, or to LibreOffice users on Linux.
- **Build/CI pipelines** — typically Linux containers — that need to publish Markdown docs as
  HTML or an office document without installing Node, a browser, or an office suite in the image.
- **Library consumers**, including closed-source applications, which is why the license
  is LGPLv3 (see [08-licensing](08-licensing.md)).

## Resolved decisions

| Area | Decision |
| --- | --- |
| Language / runtime | C# on **.NET 9** (`net9.0`), `LangVersion latest`, `Nullable enable` |
| AOT | CLI sets `<PublishAot>true</PublishAot>`; Core sets `<IsAotCompatible>true</IsAotCompatible>` and stays reflection-free |
| JavaScript | **None.** No Node, no headless browser, no JS engine, no `<script>` in output |
| Markdown parser | Markdig, GFM/advanced pipeline (`UseAdvancedExtensions`) |
| Outputs | Self-contained HTML (inline SVG), then ODT ([phase 2](phases/phase-2-odf-output.md)), then DOCX ([phase 5](phases/phase-5-docx.md)) |
| Platforms | Linux, macOS, Windows — all first-class; no Windows-only APIs, no Office automation |
| DOCX diagrams | **SVG-only** embedding (newer Word). PNG raster fallback **deferred** |
| Mermaid engine | Pluggable `IDiagramRenderer` per diagram type, dispatched on the first token |
| Unsupported diagrams | Fall back to the raw mermaid source as a fenced/preformatted code block; **never throw** |
| Mermaid fidelity | Structurally correct and readable; not pixel-perfect |
| ODT writer | Hand-written ODF XML + `ZipArchive`; no dependency, fully AOT-clean |
| DOCX writer | DocumentFormat.OpenXml (MIT), isolated behind `IDocumentWriter` |
| API shape | Async: `Task<byte[]> RenderAsync(...)`, plus `Task RenderFileAsync(...)` |
| Projects | `MarkdownDotNetRenderer.Core` (library), `MarkdownDotNetRenderer.Cli` (exe), `MarkdownDotNetRenderer.Tests` (xUnit) |
| License | **LGPLv3** (linkable from closed-source consumers) |

## Where to go next

- [02 — Architecture](02-architecture.md) for the pipeline and layout.
- [04 — Mermaid engine](04-mermaid-engine.md) for the interesting part.
- [phase 0](phases/phase-0-scaffolding.md) and [phase 1](phases/phase-1-html-flowchart.md)
  are the first two units of implementation work.
- [phase 2](phases/phase-2-odf-output.md) for the OpenDocument output that follows it.
