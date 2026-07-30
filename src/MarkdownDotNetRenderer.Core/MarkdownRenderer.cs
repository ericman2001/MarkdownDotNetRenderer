// MarkdownDotNetRenderer
// Copyright (C) 2026 MarkdownDotNetRenderer contributors
//
// This library is free software; you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option) any
// later version.
//
// This library is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along
// with this library; see the file LICENSE.LESSER. If not, see
// <https://www.gnu.org/licenses/>.

using Markdig.Syntax;
using MarkdownDotNetRenderer.Markdown;
using MarkdownDotNetRenderer.Mermaid;
using MarkdownDotNetRenderer.Writers;

namespace MarkdownDotNetRenderer;

/// <summary>
/// Orchestrates a render: parse with Markdig, walk the AST, dispatch mermaid blocks to the
/// diagram engine, and hand the ordered block list to the writer selected by
/// <see cref="RenderOptions.Format"/>. Writer selection is an explicit switch and the diagram
/// registry an explicit list, so nothing here needs reflection
/// (docs/06-aot-and-dependencies.md).
/// </summary>
public sealed class MarkdownRenderer : IMarkdownRenderer
{
    /// <summary>Product name, exposed so the CLI and tests share a single source of truth.</summary>
    public const string ProductName = "MarkdownDotNetRenderer";

    private readonly MermaidRenderer _mermaidRenderer;

    /// <summary>Creates a renderer with the built-in diagram-renderer registry.</summary>
    public MarkdownRenderer()
        : this(new MermaidRenderer())
    {
    }

    /// <summary>Creates a renderer over a specific diagram-renderer registry.</summary>
    /// <param name="mermaidRenderer">The mermaid dispatcher to use.</param>
    public MarkdownRenderer(MermaidRenderer mermaidRenderer)
    {
        ArgumentNullException.ThrowIfNull(mermaidRenderer);
        _mermaidRenderer = mermaidRenderer;
    }

    /// <summary>The file extension a format's documents use, including the dot.</summary>
    /// <param name="format">The output format.</param>
    /// <returns>The extension, e.g. <c>.html</c>.</returns>
    public static string GetFileExtension(OutputFormat format) => format switch
    {
        OutputFormat.Html => ".html",
        OutputFormat.Odt => ".odt",
        OutputFormat.Docx => ".docx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format."),
    };

    /// <summary>
    /// Selects the writer for a format. Formats whose writer has not shipped yet throw
    /// <see cref="NotSupportedException"/> naming the phase that implements them.
    /// </summary>
    /// <param name="format">The output format.</param>
    /// <returns>The writer for that format.</returns>
    public static IDocumentWriter CreateWriter(OutputFormat format) => format switch
    {
        OutputFormat.Html => new HtmlDocumentWriter(MarkdownPipelineFactory.Default),
        OutputFormat.Odt => throw new NotSupportedException(
            "ODT output is not implemented until phase 2; use --format html for now."),
        OutputFormat.Docx => throw new NotSupportedException(
            "DOCX output is not implemented until phase 5; use --format html for now."),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format."),
    };

    /// <inheritdoc />
    public async Task<RenderResult> RenderAsync(
        string markdown,
        RenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // Selecting the writer first means an unsupported format fails before any output exists.
        IDocumentWriter writer = CreateWriter(options.Format);

        var diagnostics = new List<RenderDiagnostic>();
        DocumentContent content = BuildContent(markdown, options, diagnostics, cancellationToken);

        using var buffer = new MemoryStream();
        await writer.WriteAsync(content, buffer, options, cancellationToken).ConfigureAwait(false);

        return new RenderResult
        {
            Content = buffer.ToArray(),
            Diagnostics = diagnostics,
        };
    }

    /// <inheritdoc />
    public async Task<RenderResult> RenderFileAsync(
        string inputPath,
        string outputPath,
        RenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // Fail on an unshipped format before touching the file system.
        _ = CreateWriter(options.Format);

        string markdown = await File.ReadAllTextAsync(inputPath, cancellationToken)
            .ConfigureAwait(false);
        RenderResult result = await RenderAsync(markdown, options, cancellationToken)
            .ConfigureAwait(false);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var file = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await file.WriteAsync(result.Content, cancellationToken).ConfigureAwait(false);
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private DocumentContent BuildContent(
        string markdown,
        RenderOptions options,
        List<RenderDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        MarkdownDocument document = Markdig.Markdown.Parse(markdown, MarkdownPipelineFactory.Default);
        IReadOnlyList<ExtractedBlock> extracted = MermaidBlockExtractor.Extract(document);

        var blocks = new List<DocumentBlock>(extracted.Count);
        foreach (ExtractedBlock block in extracted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (block)
            {
                case ProseRun prose:
                    blocks.Add(new ProseBlock(prose.Nodes));
                    break;

                case MermaidDiagramSource mermaid:
                    DiagramRenderResult rendered =
                        _mermaidRenderer.Render(mermaid.Source, options);
                    foreach (RenderDiagnostic diagnostic in rendered.Diagnostics)
                    {
                        diagnostics.Add(diagnostic.WithSourceLine(mermaid.SourceLine));
                    }

                    blocks.Add(rendered is { Success: true, SvgFragment: not null }
                        ? new DiagramBlock(
                            rendered.SvgFragment,
                            rendered.Width,
                            rendered.Height,
                            rendered.AltText)
                        : new Writers.CodeBlock(mermaid.Source, MermaidBlockExtractor.MermaidInfo));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Extracted block type '{block.GetType().Name}' is not handled.");
            }
        }

        return new DocumentContent(blocks);
    }
}
