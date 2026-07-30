# MarkdownDotNetRenderer

[![CI](https://github.com/ericman2001/MarkdownDotNetRenderer/actions/workflows/ci.yml/badge.svg)](https://github.com/ericman2001/MarkdownDotNetRenderer/actions/workflows/ci.yml)

A **pure C#** renderer that turns GitHub-Flavored Markdown — including embedded
[Mermaid](https://mermaid.js.org/) diagrams — into **self-contained HTML** and office documents
(**ODT**, then **DOCX**) you can hand to anyone, with **no JavaScript anywhere in the rendering
path**.

> **Status: phase 1 (HTML + flowcharts) complete.** `mdrender` turns Markdown containing
> ` ```mermaid ` `flowchart`/`graph` blocks into a single self-contained HTML file with inline,
> hand-written SVG. ODT ([phase 2](docs/phases/phase-2-odf-output.md)) and DOCX
> ([phase 5](docs/phases/phase-5-docx.md)) writers, and diagram types beyond flowcharts, are
> still to come; selecting them reports a clear error or degrades to a code block. Start with
> [docs/01-overview.md](docs/01-overview.md).

## Why

You write your design docs in Markdown with Mermaid diagrams. Your reviewer has a browser and an
office suite. Existing options for bridging that gap all drag JavaScript into the build: a Node
sidecar running `mermaid-cli`, a headless Chromium, a remote rendering service, or an embedded
JS engine. This project renders Mermaid **in-process, in C#**, so a single small executable —
with two NuGet dependencies and no browser, no Node, and no network — produces the artifact.

## The no-JS constraint

Explicitly excluded from the rendering path:

- No Node.js sidecar, no `mermaid-cli`
- No headless browser (Puppeteer/Playwright/Chromium)
- No remote service (mermaid.ink, Kroki)
- No embedded JS engine (Jint, ClearScript, Jurassic)
- No `<script>` tags in the output — the HTML renders diagrams offline, from inline `<svg>`

The tradeoff we accept: Mermaid layout is re-implemented in C# for a *subset* of diagram
types, targeting **"structurally correct and readable," not pixel-perfect Mermaid**. Diagram
types that aren't implemented yet degrade gracefully to a preformatted code block containing
the original mermaid source — they never throw and never lose information.

## Build targets

| Target | Description |
| --- | --- |
| `MarkdownDotNetRenderer.Core` | Reusable library. `IsAotCompatible`, reflection-free, async API |
| `MarkdownDotNetRenderer.Cli` | Console executable (`mdrender`), published with `PublishAot` |
| `MarkdownDotNetRenderer.Tests` | xUnit test suite |

**Runtime:** C# on .NET 9. **Platforms:** Linux, macOS, and Windows are all first-class —
no Windows-only APIs and no Office automation; every format is written directly as a file
package, so it works on a headless Linux build agent.

## Verifying a build

There is exactly one definition of a passing build: a committed script that runs restore → build
(warnings are errors) → tests → AOT publish → a render with the native binary, and prints a single
`PASS`/`FAIL` line.

```bash
build/verify.sh            # Linux/macOS; --no-aot to skip the native publish
pwsh build/verify.ps1      # Windows; -NoAot to skip the native publish
```

**Run `build/verify.sh` (or `build/verify.ps1`) before pushing.** It is the single definition of
a green build; if it prints `PASS` locally, CI should too.

CI runs that same script on `ubuntu-latest`, `windows-latest`, and `macos-latest` — the workflow
contains no build logic of its own, so "passes on my machine" and "passes in CI" cannot diverge,
and anyone can reproduce the exact gate in one command. Details in
[07 — Testing strategy](docs/07-testing-strategy.md).

## Output formats

| Format | Diagrams | Status |
| --- | --- | --- |
| Self-contained HTML | Inline `<svg>` | [Phase 1](docs/phases/phase-1-html-flowchart.md) |
| ODT (LibreOffice / OpenOffice; also opens in Word 2010+) | Native SVG, no new dependency | [Phase 2](docs/phases/phase-2-odf-output.md) |
| DOCX (Word) | Embedded **SVG only** (Word 2016+/365) | [Phase 5](docs/phases/phase-5-docx.md) |
| DOCX with PNG fallback | Raster for older Word | [Phase 6](docs/phases/phase-6-docx-png-fallback.md) — deferred/optional |

ODT comes before DOCX deliberately: it needs no new dependency, is fully AOT-clean, and carries
SVG natively, so it delivers a shareable office document sooner and with less risk. DOCX follows
because Word renders a natively-written `.docx` exactly as authored, while it treats `.odt` as a
convert-on-import path.

## Mermaid roadmap

| Diagram type | Phase |
| --- | --- |
| `flowchart` / `graph` (TD, LR) | [Phase 1](docs/phases/phase-1-html-flowchart.md) |
| `sequenceDiagram` | [Phase 3](docs/phases/phase-3-sequence-diagrams.md) |
| `pie`, `stateDiagram`, `classDiagram`, `erDiagram`, `gantt`, then others | [Phase 4](docs/phases/phase-4-additional-diagrams.md) (prioritized) |
| Anything not yet implemented | Falls back to a code block with a warning diagnostic — never an exception |

## Usage

```csharp
var renderer = new MarkdownRenderer();
RenderResult result = await renderer.RenderAsync(markdownText, RenderOptions.Html);
await renderer.RenderFileAsync("design.md", "design.html", RenderOptions.Html);
```

```bash
mdrender --input design.md --output design.html --format html
mdrender --input samples/kitchen-sink.md            # --output defaults to the format's extension
mdrender --help
```

Diagnostics go to stderr as `<severity> <code> [line N]: <message>`; stdout stays empty on a
successful render. Exit codes: `0` success (warnings included), `1` usage or I/O error, `2`
`--strict` with warnings.

`samples/flowchart-demo.md` and `samples/kitchen-sink.md` are runnable examples;
`samples/expected/kitchen-sink.html` is the golden output the test suite compares against
byte-for-byte. Regenerate it with:

```bash
dotnet run --project src/MarkdownDotNetRenderer.Cli -- \
  -i samples/kitchen-sink.md -o samples/expected/kitchen-sink.html
```

## Documentation

### Design

| Document | Contents |
| --- | --- |
| [01 — Overview](docs/01-overview.md) | Goals, constraints, non-goals, target users, resolved-decision table |
| [02 — Architecture](docs/02-architecture.md) | Pipeline and data flow, component responsibilities, proposed solution layout |
| [03 — Core API](docs/03-core-api.md) | `RenderOptions`, `IMarkdownRenderer`, `IDocumentWriter`, diagnostics and error contract |
| [04 — Mermaid engine](docs/04-mermaid-engine.md) | `IDiagramRenderer` dispatch, fallback behaviour, SVG emission, the layered flowchart layout |
| [05 — Output writers](docs/05-output-writers.md) | HTML, ODT, and DOCX writer designs, including the SVG-embedding OOXML/ODF details |
| [06 — Dependencies, AOT & cross-platform](docs/06-aot-and-dependencies.md) | Dependency budget and licenses, AOT/trim policy and risks, Linux/macOS/Windows requirements |
| [07 — Testing strategy](docs/07-testing-strategy.md) | xUnit approach: structural, invariant, and golden-file assertions — never pixel comparisons; the `build/verify` gate and the three-OS matrix |
| [08 — Licensing](docs/08-licensing.md) | LGPLv3 rationale, what it means for consumers, dependency-license compatibility |

### Phased implementation

Each phase document is self-contained — scope, prerequisites, concrete tasks, and explicit
acceptance criteria — so it can be handed off and executed independently.

| Phase | Document | Outcome |
| --- | --- | --- |
| 0 | [Scaffolding](docs/phases/phase-0-scaffolding.md) | Solution, three projects, shared build props, the `build/verify.{sh,ps1}` gate, and a three-OS CI matrix that invokes it |
| 1 | [HTML + flowcharts](docs/phases/phase-1-html-flowchart.md) | The vertical slice: Markdown → self-contained HTML with inline flowchart SVG, plus the CLI |
| 2 | [ODF (ODT) output](docs/phases/phase-2-odf-output.md) | `OdtDocumentWriter`: LibreOffice/OpenOffice output with native SVG and no new dependency |
| 3 | [Sequence diagrams](docs/phases/phase-3-sequence-diagrams.md) | `sequenceDiagram` support with a deterministic, solver-free layout |
| 4 | [Additional diagrams](docs/phases/phase-4-additional-diagrams.md) | Prioritized roadmap: `pie`, `stateDiagram`, `classDiagram`, `erDiagram`, `gantt`, … |
| 5 | [DOCX](docs/phases/phase-5-docx.md) | `DocxDocumentWriter` with SVG-only diagram embedding |
| 6 | [DOCX PNG fallback](docs/phases/phase-6-docx-png-fallback.md) | **Deferred/optional**: rasterization for older Word, and the dependency/AOT tradeoff |

## Dependencies

Two runtime packages, total. Neither pulls a native or JavaScript dependency.

| Package | License | Used for |
| --- | --- | --- |
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause | GFM Markdown parsing |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | MIT | DOCX writing (isolated behind `IDocumentWriter`) |

See [06 — Dependencies, AOT & cross-platform](docs/06-aot-and-dependencies.md) for the policy
that keeps it that way.

## License

**LGPLv3** (`LGPL-3.0-or-later`), chosen so closed-source applications can link the library
while improvements to the library itself flow back.

Since LGPLv3 is a set of additional permissions on top of GPLv3, both texts are included:

- [`LICENSE`](LICENSE) — GNU General Public License v3.0
- [`LICENSE.LESSER`](LICENSE.LESSER) — GNU Lesser General Public License v3.0

Rationale and dependency-compatibility analysis: [08 — Licensing](docs/08-licensing.md).
