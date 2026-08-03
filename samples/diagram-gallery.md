# Diagram gallery

The phase 4 diagram types, each rendered as inline SVG with no JavaScript and no external
resources.

## Pie chart

```mermaid
pie showData
    title Render time by stage
    "Parse" : 12
    "Layout" : 43
    "Emit SVG" : 27
    "Write file" : 8
```

## State diagram

```mermaid
stateDiagram-v2
    direction LR
    state "Waiting for input" as Idle
    [*] --> Idle
    Idle --> Parsing : markdown arrives
    Parsing --> Laying_out : model built
    Laying_out --> Emitting : coordinates fixed
    Emitting --> [*]
    Parsing --> Failed : malformed
    Failed --> [*]
    note right of Failed : falls back to a code block
```

## Class diagram

```mermaid
classDiagram
    direction TD
    class IDiagramRenderer {
        <<interface>>
        +DiagramTypes
        +Render(source, options)
    }
    class FlowchartRenderer {
        -LayeredLayout layout
        +Render(source, options)
    }
    class PieRenderer {
        +Render(source, options)
    }
    class DiagramRenderResult {
        +bool Success
        +string SvgFragment
    }
    class Registry~TRenderer~ {
        +Add(renderer)
    }
    Registry o-- IDiagramRenderer : holds
    IDiagramRenderer <|.. FlowchartRenderer
    IDiagramRenderer <|.. PieRenderer
    FlowchartRenderer ..> DiagramRenderResult : returns
    FlowchartRenderer "1" *-- "1" LayeredLayout
```

## Entity relationship diagram

```mermaid
erDiagram
    DOCUMENT ||--o{ BLOCK : contains
    BLOCK ||--o| DIAGRAM : "may render"
    DOCUMENT {
        string title
        string sourcePath PK
    }
    BLOCK {
        string kind
        int sourceLine
    }
    DIAGRAM {
        string diagramType
        string svgFragment
    }
```

## Gantt chart

```mermaid
gantt
    title Renderer phases
    dateFormat YYYY-MM-DD
    section Foundations
    Scaffolding      :done, p0, 2026-01-05, 5d
    HTML and flowchart :done, p1, after p0, 2w
    section Writers
    ODT writer       :active, crit, p2, after p1, 10d
    Sequence diagrams :p3, after p2, 1w
    section Diagrams
    Additional types :p4, after p3, 12d
    Phase 4 complete :milestone, m1, after p4, 0d
```
