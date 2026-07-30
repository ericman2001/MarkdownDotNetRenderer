# Phase 6 — DOCX PNG Raster Fallback (Deferred / Optional)

> **Status: FUTURE / OPTIONAL — deferred by design.** No other phase may depend on this one, and
> no rasterizer dependency may be added before the decision below is explicitly made and
> recorded. This document exists so the tradeoff is understood, not to schedule work. It is last
> in the sequence on purpose: it extends the DOCX writer from [phase 5](phase-5-docx.md), so it
> cannot precede it, and [phase 2](phase-2-odf-output.md) may remove the need for it entirely.

**Goal (if undertaken):** make diagrams visible in **every** consumer of the generated `.docx`,
including Word 2013 and earlier, WordPad, Google Docs, and LibreOffice Writer, by embedding a PNG
raster image alongside the SVG.

**Prerequisites:** [phase 5](phase-5-docx.md) (the DOCX writer this phase extends).

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

1. **[Phase 2 — ODT output](phase-2-odf-output.md).** LibreOffice/OpenOffice consume SVG
   natively via ODF, this needs no new dependency, and recent Word can open `.odt` directly — so
   it serves both the non-Word recipient *and* (with converter-level fidelity) the Word
   recipient. This is the cheapest way to close most of the compatibility gap, which is why it is
   sequenced early rather than here.
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
5. **DOCX writer change** — the reason task 6 of [phase 5](phase-5-docx.md) was factored for it:
   add a second
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
- [ ] The default build (no raster package referenced) is **byte-for-byte unchanged** from the
      pre-phase output, keeps exactly two runtime dependencies, and still publishes AOT-clean for
      the HTML and ODT paths.
- [ ] The rasterizer decision, license, native-asset story, and AOT impact are recorded in
      [06-aot-and-dependencies](../06-aot-and-dependencies.md).
- [ ] No GPL/AGPL-licensed rasterizer is used ([08-licensing](../08-licensing.md)).
- [ ] `build/verify.sh` prints `PASS` on Linux, and the same script is run manually on Windows
      (`build/verify.ps1`) — a native rasterizer makes per-OS verification unavoidable, which is
      itself an argument against adopting one.
