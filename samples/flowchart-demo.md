# Flowchart demo

A short document with prose and two mermaid flowcharts, used for manual checks and by the
`build/verify` smoke test.

## Top-down

The release pipeline, drawn top-down:

```mermaid
flowchart TD
    A[Write Markdown] --> B{Contains mermaid?}
    B -->|yes| C[Render diagram as SVG]
    B -->|no| D[Render prose only]
    C --> E([Self-contained HTML])
    D --> E
```

## Left-to-right

The same idea laid out left-to-right, with an undirected link and a label on the long form:

```mermaid
graph LR
    Source[Markdown source] --> Parse(Markdig parse)
    Parse -- walk AST --> Dispatch{Block type}
    Dispatch -->|mermaid| Svg[Inline SVG]
    Dispatch -->|prose| Html[GFM HTML]
    Svg --- Html
```

Both diagrams are hand-written SVG: no JavaScript, no external requests, no fonts to install.
