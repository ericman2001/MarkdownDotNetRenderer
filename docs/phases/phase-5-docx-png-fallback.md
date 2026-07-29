# Phase 5 — DOCX PNG Raster Fallback (Deferred / Optional)

> **Status: FUTURE / OPTIONAL — deferred by design.** Nothing in phases 0–4 may depend on this
> phase, and no rasterizer dependency may be added before the decision below is explicitly made
> and recorded. This document exists so the tradeoff is understood, not to schedule work.

**Goal (if undertaken):** make diagrams visible in **every** consumer of the generated `.docx`,
including Word 2013 and earlier, WordPad, Google Docs, and LibreOffice Writer, by embedding a PNG
raster image alongside the SVG.

**Prerequisites:** [phase 2](phase-2-docx.md).

**Reading:** [05-output-writers](../05-output-writers.md) (the blip structure that this phase
completes), [06-aot-and-dependencies](../06-aot-and-dependencies.md) (the dependency budget this
phase would breach).

## Why it is deferred

SVG-only embedding already works in Word 2016+/Microsoft 365, which covers the primary target
audience. Adding a raster fallback requires an **SVG rasterizer**, and every candidate conflicts
with a stated project goal:

| Candidate | Problem |
| --- | --- |
| SkiaSharp (+ Svg.Skia) | Native `libSkiaSharp` per RID; AOT-publishable but adds tens of MB and native-asset deployment complexity; Svg.Skia pulls further dependencies |
| ImageSharp (+ ImageSharp.Drawing) | Managed (good for AOT) but has **no SVG parser** — we would have to rasterize our own primitives ourselves; also a commercial/Six Labors Split license consideration |
| Magick.NET | Very large native dependency; heavy per-RID assets |
| `System.Drawing.Common` | **Windows-only since .NET 7** — breaks the Linux requirement outright |
| Resvg / librsvg via P/Invoke | Native, per-RID binaries to build and ship; GPL/LGPL considerations for librsvg |
| Our own rasterizer | We control the SVG we emit, so a small managed rasterizer is conceivable — but it is a real project (paths, strokes, text glyphs) and text rendering needs font data we deliberately do not carry |

So the tradeoff is explicit: **broad Word compatibility vs. a two-dependency, native-free,
AOT-clean, cross-platform build.** The current answer is to keep the clean build.

## Alternatives that avoid a rasterizer entirely

Prefer these before accepting a rasterizer. They may make this phase unnecessary.

1. **[Phase 6 — ODT output](phase-6-odf-output.md).** LibreOffice/OpenOffice consume SVG
   natively via ODF, needs no new dependency, and directly serves recipients who don't have
   modern Word. This is the cheapest way to close most of the compatibility gap and is the
   recommended path.
2. **DrawingML-native diagrams.** Emit diagrams as *native Word shapes* (`wps:wsp` /
   `mc:AlternateContent` groups) instead of an image. Since our layout is already a set of rects,
   lines, polygons, and text runs, translating it to DrawingML shapes is mechanical — and the
   result renders in every Word version, is editable in Word, and needs **zero** new
   dependencies. This is architecturally the most attractive option: it would be a second
   "diagram backend" consuming the same layout model, and the reason the layout stage is kept
   separate from SVG emission.
3. **Alt-text plus the mermaid source** in older-Word documents, so the information is at least
   present. Already effectively the fallback behaviour for unsupported types.

## Tasks (only if the rasterizer route is chosen)

1. **Record the decision** in [06-aot-and-dependencies](../06-aot-and-dependencies.md): the
   chosen library, its license, its native assets, its per-RID publishing story, and whether AOT
   remains supported. Update the dependency table and `THIRD-PARTY-NOTICES.md`.
2. **Introduce `ISvgRasterizer`** (`byte[] RasterizePng(string svgFragment, double width, double height, double scale)`)
   in Core, with a null/no-op default. Rasterization must be an **opt-in, injectable** capability
   so the default build keeps its current dependency profile.
3. **Ship the implementation in a separate package** (e.g. `MarkdownDotNetRenderer.Raster`) that
   Core does not reference, so consumers who don't want native assets never acquire them.
4. **`RenderOptions` additions**: `RasterFallback` (`None` | `WhenAvailable` | `Required`) and
   `RasterScale` (default 2× for high-DPI printing).
5. **DOCX writer change** — the reason task 6 of phase 2 was factored for it: add a second
   `ImagePart` (`image/png`), point `a:blip/@r:embed` at the **PNG** and leave
   `asvg:svgBlip/@r:embed` pointing at the SVG. This is the shape Microsoft actually intends:
   modern Word prefers the `svgBlip`, everything else shows the PNG.
6. **Behaviour when unavailable**: `WhenAvailable` with no rasterizer registered → current
   SVG-only output plus an informational diagnostic; `Required` with none → a clear error.
7. **CLI**: `--raster [auto|on|off]` and `--raster-scale <n>`.
8. **Tests**: both image parts exist with the right content types; `a:blip/@r:embed` resolves to
   the PNG and `svgBlip/@r:embed` to the SVG; the PNG has a valid signature and plausible
   dimensions (no pixel comparison); SVG-only behaviour is unchanged when no rasterizer is
   registered; and the AOT smoke test still passes for the HTML path.
9. **Cross-platform verification**: the same `.docx` shows diagrams in Word on Windows **and** in
   LibreOffice Writer on Linux.

## Acceptance criteria (if undertaken)

- [ ] A generated `.docx` displays diagrams in Word 2013, LibreOffice Writer (Linux), and Google
      Docs, while Word 2016+/365 still shows the crisp **vector** version.
- [ ] The default build (no raster package referenced) is **byte-for-byte unchanged** from
      phase 2/3 output, keeps exactly two runtime dependencies, and still publishes AOT-clean for
      the HTML path.
- [ ] The rasterizer decision, license, native-asset story, and AOT impact are recorded in
      [06-aot-and-dependencies](../06-aot-and-dependencies.md).
- [ ] No GPL/AGPL-licensed rasterizer is used ([08-licensing](../08-licensing.md)).
- [ ] Tests green and build warning-free on Linux, macOS, and Windows.
