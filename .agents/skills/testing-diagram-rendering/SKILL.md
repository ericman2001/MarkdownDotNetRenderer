---
name: testing-diagram-rendering
description: How to build, render and visually verify mdrender's mermaid diagram output (HTML inline SVG and ODT pictures) end to end.
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
glyph pops into view. Note the ODT/PDF render may still show the glyph, so a browser check is
required; the ODT path also drops SVG marker `||` bars and `crit` styling.

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

## Useful adversarial fixture

Cover: pie with 8 slices and a very long title, state LR + TD with a self-loop and an alias declared
after first use, class with generics `List~int~` plus `*--` / `o--` / self relation, ER with no
attributes and a self relationship, one-day gantt, and an unsupported type (`quadrantChart`) to
confirm the MERMAID001 code-block fallback.

## Devin Secrets Needed

None.
