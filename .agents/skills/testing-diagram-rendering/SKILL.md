---
name: testing-diagram-rendering
description: How to build, render and visually verify mdrender's mermaid diagram output (HTML inline SVG, ODT and DOCX pictures) end to end.
---

# Testing MarkdownDotNetRenderer diagram output

No server, no credentials. Everything runs locally with the .NET 9 SDK on `$HOME/.dotnet`.

## Build and render

```bash
export PATH="$HOME/.dotnet:$PATH"
build/verify.sh                       # authoritative gate; also AOT-publishes the CLI
BIN=artifacts/publish/linux-x64/mdrender      # prefer the native binary, it is what verify.sh smoke-tests
$BIN --input samples/diagram-gallery.md --output /tmp/gallery.html --format html
$BIN --input samples/diagram-gallery.md --output /tmp/gallery.odt --format odt
$BIN --input samples/diagram-gallery.md --output /tmp/gallery.docx --format docx
```

`dotnet run --project src/MarkdownDotNetRenderer.Cli -c Release -- ...` works too but prints build
noise. Diagnostics (MERMAID001/002/003) go to stderr; exit code stays 0 for fallbacks.

## Checking the HTML

* Open `file:///tmp/gallery.html` in Chrome. Chrome page zoom via xdotool needs `ctrl+equal`
  (`ctrl+plus` does nothing); `ctrl+0` resets.
* Quick fallback check: `grep -c "<pre" /tmp/gallery.html` — the gallery must be 0,
  `samples/kitchen-sink.md` must be 3 (mindmap, malformed flowchart, plain code fence).

## The failure mode to look for first

Diagram fragments paint edge/cardinality labels **last**, each behind an opaque white `<rect>`.
Anything drawn earlier in the same pixels disappears. This has repeatedly caused:

* node captions clipped ("Waiting for inpu"),
* class composition/aggregation `marker-start` diamonds invisible,
* ER crow's-foot glyphs and the connector line hidden,
* one edge label erasing another (self-loop labels vanishing).

Diagnose it by dumping geometry rather than squinting at pixels:

```bash
python3 - <<'EOF'
import re
h=open('/tmp/gallery.html').read()
i=h.find('class diagram'); j=h.find('</svg>',i)
for m in re.finditer(r'<(rect|line|path|text)\b[^>]*>', h[i:j]):
    print(' '.join(m.group(0).split()))
EOF
```

Compare each white `fill="#ffffff" stroke="none"` rect's span against the line/marker span
(marker length = `markerWidth × stroke-width`, e.g. 9 × 1.5 = 13.5 user units). If they overlap,
that glyph is occluded. Confirm in the browser by nudging the line's `x1/x2` in the console — the
glyph pops into view. A browser check is required because the ODT/PDF render may disagree; current
LibreOffice checks preserve the phase-4 `||` bars and `crit` styling.

Beware false positives: a white label rect may overlap the tail 13.5 units of an **undirected**
(`---`) link, which has no `marker-end` at all, or sit beside an arrowhead that still paints on top.
Before reporting occlusion, confirm the edge actually has `marker-end` and zoom into the pixels in
Chrome (`ctrl+equal` ×4) — the geometry dump alone over-reports.

## Behaviour-preserving refactors: diff against the base branch

The strongest check for a "no behaviour change" PR is a byte-for-byte comparison, and this renderer
is fully deterministic (even `meta.xml` carries no timestamp), so it works:

```bash
git worktree add /home/ubuntu/mdr-main main      # never disturb the branch checkout
(cd /home/ubuntu/mdr-main && build/verify.sh)    # builds a second native binary
# render every sample with both binaries, then:
cmp -s out/branch/x.html out/main/x.html                       # HTML: expect identical
# ODT is a zip; compare extracted entries, not the archive bytes:
(cd b && unzip -qo ../out/branch/x.odt); (cd m && unzip -qo ../out/main/x.odt); diff -r b m
```

Any difference at all is a finding. If a future change adds a timestamp to `meta.xml`, exclude just
that entry rather than abandoning the technique.

## Coordinates inside `mdnr-*` groups are pre-shift

Geometry emitted inside the diagram groups is in the layout's own space; the renderer wraps the
drawing in `<g transform="translate(ox oy)">` computed by `GraphCanvas`. Parse that offset and apply
it before comparing anything against the `viewBox`, otherwise a rect at `y = -18` looks off-canvas
when it actually lands in the top margin. Label-vs-box and label-vs-line checks are unaffected
(same space). Glyph transforms use **space**-separated args: `translate(161.19 82) rotate(0)
scale(1.2) translate(-9 -5)` — a regex expecting `translate(x,y)` silently matches nothing.

A "label far from its own line" heuristic based only on `<line>` elements produces false positives:
self-loops and dashed edges are `<path>`, so their labels always measure far away. Confirm those
visually.

## Checking the ODT

```bash
libreoffice --headless --convert-to pdf /tmp/gallery.odt   # then open the PDF in Chrome
```
Diagrams must appear as pictures, not empty frames.

## Checking the DOCX

A headless docx→odt/pdf conversion is **not** sufficient evidence that a Word document renders: it
exercises LibreOffice's import filter but not its picture rendering path. Open the file in the GUI:

```bash
soffice --writer /tmp/gallery.docx &
sleep 4
wmctrl -r "gallery.docx - LibreOffice Writer" -b add,maximized_vert,maximized_horz
```

`wmctrl` matches on the exact window title, which is `<basename> - LibreOffice Writer`. Never use
`xdotool key super+Up` to maximize — it half-tiles. The first GUI launch may show a "Tip of the day"
and a default-format dialog; dismiss both before screenshotting.

What to confirm visually, in this order:

1. **No repair/recovery prompt.** Any "the file is corrupt / Word document needs repair" dialog is a
   packaging bug, not a cosmetic one.
2. **Diagrams are pictures**, not blank frames, grey placeholders, red X boxes, or squashed aspect
   ratios. Mermaid diagrams ship as `image/svg+xml` parts referenced by `a:blip` plus an
   `asvg:svgBlip` extension (GUID `{96DAC541-7B7A-43D3-8B79-37D633B846F1}`). LibreOffice 7.3.7
   honours this and renders them correctly.
3. **Structure**: distinct heading sizes, nested list indents, task-list `☒`/`☐`, GFM table bold
   header + per-column alignment, shaded monospace code with leading spaces, block quote, rule.

Cheap pre-checks before opening the GUI (fast triage if a picture is missing):

```bash
unzip -l out.docx | grep media/                       # one svg part per rendered diagram
unzip -p out.docx word/document.xml | grep -c asvg:svgBlip     # must equal the diagram count
unzip -p out.docx '\[Content_Types\].xml' | grep -o 'image/svg+xml'
unzip -p out.docx word/_rels/document.xml.rels | tr '>' '>\n' | grep image
```

If `media/` has the right parts but the GUI shows empty frames, suspect the drawing extent (EMU
conversion) or the `a:blip r:embed` id, not the image bytes.

### Word compatibility is NOT covered by a LibreOffice check

Microsoft Word is not installed on these boxes and cannot be tested. Note this explicitly as untested
rather than implying DOCX is universally verified. Word normally expects `a:blip r:embed` to point at
a **raster** fallback with the SVG only in the `asvg:svgBlip` extension; if the writer points
`r:embed` straight at the SVG part, LibreOffice is fine but older Word builds may have no fallback.
Flag this for a human with Word.

### Determinism and strict mode are cheap extra signals

```bash
# render twice, compare — the writer is fully deterministic (fixed 2026-01-01 zip timestamps)
md5sum /tmp/det1.docx /tmp/det2.docx        # must match
mdrender -i samples/kitchen-sink.md -o /tmp/x.docx -f docx --strict; echo $?   # expect 2
```

## Useful adversarial fixture

Cover: pie with 8 slices and a very long title, state LR + TD with a self-loop and an alias declared
after first use, class with generics `List~int~` plus `*--` / `o--` / self relation, ER with no
attributes and a self relationship, one-day gantt, and an unsupported type (`quadrantChart`) to
confirm the MERMAID001 code-block fallback.

## Expected diagnostics from the stock samples

Useful as a regression baseline — these are correct, not failures, and exit code stays 0:

* `kitchen-sink.md`: MERMAID001 (mindmap unsupported), MERMAID002 (malformed flowchart),
  MERMAID003 ×3 (`subgraph`, `classDef`, `click` ignored).
* `sequence-demo.md`: MERMAID003 (`loop` not laid out).
* `diagram-gallery.md`, `flowchart-demo.md`: silent.

Fallback blocks must contain the **verbatim** mermaid source including original indentation; diff the
rendered text against the sample's line range rather than eyeballing it.

## Devin Secrets Needed

None.
