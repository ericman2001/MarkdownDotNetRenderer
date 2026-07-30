# 03 — Core Library Public API

All signatures below are **illustrative**: they define the intended shape and contracts, not
final code. Namespace root: `MarkdownDotNetRenderer`.

## Output format and options

```csharp
namespace MarkdownDotNetRenderer;

public enum OutputFormat
{
    Html,

    /// <summary>OpenDocument Text for LibreOffice/OpenOffice. Added in phase 2.</summary>
    Odt,

    /// <summary>OOXML WordprocessingML for Microsoft Word. Added in phase 5.</summary>
    Docx,
}

public sealed class RenderOptions
{
    public OutputFormat Format { get; init; } = OutputFormat.Html;

    /// <summary>Value for the HTML &lt;title&gt; / document core-properties title.</summary>
    public string? DocumentTitle { get; init; }

    /// <summary>Extra CSS injected into the HTML &lt;style&gt; block. HTML only.</summary>
    public string? AdditionalCss { get; init; }

    /// <summary>Emit the built-in minimal stylesheet. HTML only.</summary>
    public bool IncludeDefaultCss { get; init; } = true;

    /// <summary>Base font family used for SVG diagram labels and office-document body text.</summary>
    public string FontFamily { get; init; } = "Segoe UI, Arial, sans-serif";

    /// <summary>Base font size, in points, for diagram labels.</summary>
    public double DiagramFontSize { get; init; } = 12;

    /// <summary>Max diagram width in CSS pixels; layout wraps/scales to fit.</summary>
    public double MaxDiagramWidth { get; init; } = 900;

    public static RenderOptions Html { get; } = new() { Format = OutputFormat.Html };
    public static RenderOptions Odt { get; } = new() { Format = OutputFormat.Odt };
    public static RenderOptions Docx { get; } = new() { Format = OutputFormat.Docx };
}
```

`RenderOptions` is deliberately small. Theming knobs beyond the above are a non-goal
(see [01-overview](01-overview.md)).

That non-goal is unchanged by `DiagramTheme`
(see [04-mermaid-engine](04-mermaid-engine.md#styling-and-geometry-diagramtheme)), which
centralises diagram colours, stroke widths, and box geometry that used to be inline `const`
fields inside `FlowchartRenderer`. It is a maintainability construct, injected into a renderer
the same way `LayoutMetrics` is; `RenderOptions` does **not** surface it, and adding a knob to
`DiagramTheme` does not add one to the public render surface. Only values genuinely meant as
user-facing render defaults belong in `RenderOptions`.

## The renderer

```csharp
public interface IMarkdownRenderer
{
    /// <summary>Render Markdown text to the bytes of the configured output format.</summary>
    Task<RenderResult> RenderAsync(
        string markdown,
        RenderOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Read <paramref name="inputPath"/>, render, and write <paramref name="outputPath"/>.</summary>
    Task<RenderResult> RenderFileAsync(
        string inputPath,
        string outputPath,
        RenderOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    public MarkdownRenderer();

    /// <summary>Overload for injecting/extending the diagram-renderer registry.</summary>
    public MarkdownRenderer(MermaidRenderer mermaidRenderer);
}
```

### Why async

Rendering itself is CPU-bound and synchronous, but I/O is not: `RenderFileAsync` reads and
writes files, and library consumers are overwhelmingly in async call stacks (ASP.NET
handlers, CLI `Main`). A synchronous-only API would force `.Result` deadlock hazards on them.
The compute portion is invoked directly (not wrapped in `Task.Run`); only genuine I/O
awaits. A convenience `Render(...)`/`RenderToString(...)` sync overload may be added later
but the async pair is the primary surface.

## Results and diagnostics

```csharp
public sealed class RenderResult
{
    /// <summary>Rendered document bytes: UTF-8 (no BOM) HTML, or the ODT/DOCX package.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Non-fatal issues encountered while rendering.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; }

    public bool HasWarnings => Diagnostics.Count > 0;
}

public enum DiagnosticSeverity { Info, Warning }

public sealed record RenderDiagnostic(
    DiagnosticSeverity Severity,
    string Code,          // e.g. "MERMAID001"
    string Message,
    int? SourceLine);     // 1-based line in the input Markdown, when known
```

`RenderFileAsync` returns the same `RenderResult`; `Content` is still populated so callers
can hash or re-use the bytes without re-reading the file.

### Diagnostic codes

| Code | Severity | Meaning | Behaviour |
| --- | --- | --- | --- |
| `MERMAID001` | Warning | Unsupported diagram type | Raw mermaid emitted as a code block |
| `MERMAID002` | Warning | Diagram type recognized but the source failed to parse | Raw mermaid emitted as a code block |
| `MERMAID003` | Info | Recognized directive/feature ignored (e.g. `%%{init}%%`, `classDef`) | Diagram rendered without it |
| `MERMAID004` | Warning | Diagram exceeded a layout guard (node/edge count, cycle depth) | Raw mermaid emitted as a code block |
| `WRITER001` | Warning | Markdown construct unsupported by the writer (e.g. raw inline HTML in ODT/DOCX) | Construct rendered as plain text |

## Error-handling contract

**Hard rule: no diagram or Markdown content in a syntactically valid Markdown file may
throw.** The renderer's exception surface is limited to:

- `ArgumentNullException` / `ArgumentException` for null or nonsensical arguments.
- `IOException` / `UnauthorizedAccessException` from `RenderFileAsync` file access.
- `OperationCanceledException` when the token is cancelled.

Everything else — unknown diagram types, malformed mermaid, unresolvable edges, absurdly
large graphs — becomes a `RenderDiagnostic` plus a graceful degradation:

- **Unsupported diagram type** → the block renders as a preformatted code block containing
  the original mermaid source verbatim (HTML: `<pre><code class="language-mermaid">`;
  ODT/DOCX: a monospaced, preserved-whitespace paragraph). The reader still sees the diagram
  definition, so no information is lost.
- **Malformed source in a supported type** → same fallback, with `MERMAID002`.
- **Unsupported Markdown construct in an office format** (raw HTML blocks, footnote layouts we do not
  map) → best-effort plain-text rendering plus `WRITER001`.

The CLI surfaces diagnostics on stderr and exits `0` when only warnings occurred, so
warnings never break a build; a `--strict` switch upgrades any warning to exit code `2`.

## The writer abstraction

```csharp
/// <summary>An ordered, format-agnostic description of the document to write.</summary>
public sealed record DocumentContent(IReadOnlyList<DocumentBlock> Blocks);

public abstract record DocumentBlock;

/// <summary>A run of prose held as Markdig AST nodes, to be rendered by the writer.</summary>
public sealed record ProseBlock(Markdig.Syntax.ContainerBlock Nodes) : DocumentBlock;

/// <summary>A rendered diagram: an &lt;svg&gt; fragment with intrinsic size in CSS pixels.</summary>
public sealed record DiagramBlock(string SvgFragment, double Width, double Height, string? AltText)
    : DocumentBlock;

/// <summary>A verbatim code block (used for the unsupported-mermaid fallback).</summary>
public sealed record CodeBlock(string Text, string? Language) : DocumentBlock;

public interface IDocumentWriter
{
    /// <summary>File extension including the dot, e.g. ".html".</summary>
    string FileExtension { get; }

    /// <summary>MIME type of the produced document.</summary>
    string ContentType { get; }

    Task WriteAsync(
        DocumentContent content,
        Stream destination,
        RenderOptions options,
        CancellationToken cancellationToken = default);
}
```

Writers write to a caller-supplied `Stream`, which lets `RenderAsync` target a
`MemoryStream` and `RenderFileAsync` target a `FileStream` without buffering twice. See
[05-output-writers](05-output-writers.md) for the implementations. Selecting a format whose
writer has not shipped yet (`Odt` before [phase 2](phases/phase-2-odf-output.md), `Docx` before
[phase 5](phases/phase-5-docx.md)) throws
`NotSupportedException` with a message naming the format — an API-misuse error, distinct from the
content-degradation cases below.

All paths are handled with `Path`/`Path.Combine` and no assumption of a case-insensitive file
system, so the API behaves identically on Linux, macOS, and Windows.

## Usage sketch

```csharp
var renderer = new MarkdownRenderer();

// In-memory
var result = await renderer.RenderAsync(markdownText, RenderOptions.Html);
var html = Encoding.UTF8.GetString(result.Content.Span);

// To disk
await renderer.RenderFileAsync("design.md", "design.odt", RenderOptions.Odt);

foreach (var d in result.Diagnostics)
    Console.Error.WriteLine($"{d.Severity} {d.Code} (line {d.SourceLine}): {d.Message}");
```
