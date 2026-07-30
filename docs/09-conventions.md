# 09 — Maintainability Conventions

Where a value belongs in this codebase. The goal is that anything a maintainer might want to
tweak is findable in one place, and that nobody has to hunt through emit code for a hex colour
or a CSS rule.

## 1. Styling and visual values → injectable records

Colours, stroke widths, padding, minimum box sizes, wrap widths, and similar visual/geometry
values go in a dedicated `sealed record` with defaulted parameters and a `static Default`
singleton, injected through a constructor overload:

- `LayoutMetrics` — graph layout geometry and size guards
  ([04-mermaid-engine](04-mermaid-engine.md)).
- `DiagramTheme` — diagram paint and box geometry
  ([04-mermaid-engine](04-mermaid-engine.md#styling-and-geometry-diagramtheme)).
- `OdtTheme` — ODF fonts, sizes, colours, and page geometry
  ([05-output-writers](05-output-writers.md#odtdocumentwriter)).

New diagram types follow the same shape rather than adding inline `private const` fields to a
renderer. Consequence: helpers that read these values are instance methods, or take the record
as a parameter; they are not `static` with a hard-coded constant.

## 2. Large CSS/markup blobs → embedded resources

Multi-line CSS or markup lives in a real `.css`/`.xml` file registered as an
`<EmbeddedResource>` and read once at startup — never as a wall of C# string literals. The
current instance is `Writers/default.css`, consumed by
`HtmlDocumentWriter.BuildDefaultCss` ([05-output-writers](05-output-writers.md#minimal-built-in-css)).

This is a *build-time* editability choice only. Embedded resources are compiled into the
assembly, so the self-contained-output guarantee is untouched: nothing is read from disk or the
network at render time, the text is still emitted inline, and `@import`/external references stay
forbidden. Runtime-loaded external config files are **not** an acceptable substitute.

Runtime substitution uses an explicit placeholder token (e.g. `__FONT_FAMILY__`) replaced after
reading, so the resource stays a valid, lintable file on its own.

## 3. Spec/protocol constants and diagnostic codes → centralized `const` fields

These are not styling and must not be made configurable: making them injectable would let a
caller emit an invalid document or an unrecognisable diagnostic. They stay as `const` fields on
the type that owns the concept:

- Diagnostic codes (`MERMAID001`, `WRITER001`, …) on `RenderDiagnostic`.
- CLI exit codes in `CommandLine`.
- XML namespaces, MIME types, and format literals (e.g. the SVG namespace on `SvgBuilder`; every
  ODF namespace, package entry name, media type, and name template on `OdfNames`).
- Unit conversions defined by a spec, on the type that owns the unit (`CssUnits.PixelsPerInch`),
  shared by every writer that needs physical sizes rather than duplicated per format.

## 4. User-facing render defaults → `RenderOptions`

Anything a *consumer* of the library is expected to set belongs in `RenderOptions`, which is
kept deliberately minimal ([03-core-api](03-core-api.md)). Internal maintainability records like
`DiagramTheme` do not expand that surface; promoting one of their knobs to `RenderOptions` is a
separate, deliberate API decision.

## Determinism caveat

Defaults in these records and resources are part of the output contract: the golden-file and
structural tests ([07-testing-strategy](07-testing-strategy.md)) compare rendered bytes, so
changing a default is a visible behaviour change and must be made intentionally, with the golden
files updated in the same change.
