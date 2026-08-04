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

using System.Reflection;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownDotNetRenderer.Markdown;

namespace MarkdownDotNetRenderer.Writers;

/// <summary>
/// Assembles one self-contained HTML5 document: prose rendered by Markdig's own
/// <see cref="HtmlRenderer"/>, diagrams inlined as <c>&lt;svg&gt;</c>, fallbacks as escaped
/// <c>&lt;pre&gt;&lt;code&gt;</c>. No scripts, no external references, UTF-8 without a BOM and
/// <c>\n</c> line endings, so output is byte-identical on every platform.
/// </summary>
public sealed class HtmlDocumentWriter : IDocumentWriter
{
    private const string Newline = "\n";
    private const string DefaultCssResourceName = "MarkdownDotNetRenderer.Writers.default.css";
    private const string FontFamilyToken = "__FONT_FAMILY__";

    private static readonly string DefaultCssTemplate = LoadDefaultCssTemplate();

    private readonly MarkdownPipeline _pipeline;

    /// <summary>Creates a writer using the shared default pipeline.</summary>
    public HtmlDocumentWriter()
        : this(MarkdownPipelineFactory.Default)
    {
    }

    /// <summary>Creates a writer that renders prose with a specific pipeline.</summary>
    /// <param name="pipeline">The pipeline that parsed the document.</param>
    public HtmlDocumentWriter(MarkdownPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public string FileExtension => ".html";

    /// <inheritdoc />
    public string ContentType => "text/html; charset=utf-8";

    /// <summary>The built-in minimal stylesheet, emitted when <see cref="RenderOptions.IncludeDefaultCss"/> is set.</summary>
    /// <remarks>
    /// The rules live in the <c>Writers/default.css</c> embedded resource, compiled into this
    /// assembly at build time. Nothing is read from disk or the network at runtime, so output stays
    /// self-contained; the CSS text is still emitted inline inside the document's
    /// <c>&lt;style&gt;</c> element.
    /// </remarks>
    /// <param name="fontFamily">Body font stack, from the render options.</param>
    /// <returns>CSS text without a wrapping <c>&lt;style&gt;</c> element.</returns>
    public static string BuildDefaultCss(string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);

        return DefaultCssTemplate.Replace(
            FontFamilyToken,
            HtmlEscape(fontFamily),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the embedded stylesheet once, normalising to <c>\n</c> line endings without a trailing
    /// newline so the emitted document is byte-identical regardless of how the file was checked out.
    /// </summary>
    private static string LoadDefaultCssTemplate()
    {
        Assembly assembly = typeof(HtmlDocumentWriter).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(DefaultCssResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"The embedded stylesheet '{DefaultCssResourceName}' is missing from " +
                $"'{assembly.GetName().Name}'.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string css = reader.ReadToEnd();
        return string.Join(
            Newline,
            css.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')));
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        DocumentContent content,
        Stream destination,
        RenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        string html = Render(content, options, cancellationToken);
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html);
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renders the document to an HTML string.</summary>
    /// <param name="content">The blocks to write.</param>
    /// <param name="options">Options controlling title, CSS, and fonts.</param>
    /// <param name="cancellationToken">Token observed between blocks.</param>
    /// <returns>The complete HTML document.</returns>
    public string Render(
        DocumentContent content,
        RenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>").Append(Newline);
        html.Append("<html lang=\"en\">").Append(Newline);
        html.Append("<head>").Append(Newline);
        html.Append("<meta charset=\"utf-8\">").Append(Newline);
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append(Newline);
        html.Append("<title>")
            .Append(HtmlEscape(DocumentTitle.Resolve(content, options)))
            .Append("</title>")
            .Append(Newline);

        string? additional = string.IsNullOrWhiteSpace(options.AdditionalCss)
            ? null
            : options.AdditionalCss;
        if (options.IncludeDefaultCss || additional is not null)
        {
            html.Append("<style>").Append(Newline);
            if (options.IncludeDefaultCss)
            {
                html.Append(BuildDefaultCss(options.FontFamily)).Append(Newline);
            }

            if (additional is not null)
            {
                html.Append(additional).Append(Newline);
            }

            html.Append("</style>").Append(Newline);
        }

        html.Append("</head>").Append(Newline);
        html.Append("<body>").Append(Newline);
        html.Append("<main class=\"markdown-body\">").Append(Newline);

        foreach (DocumentBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (block)
            {
                case ProseBlock prose:
                    html.Append(RenderProse(prose));
                    break;

                case DiagramBlock diagram:
                    html.Append("<figure class=\"mermaid-figure\">").Append(Newline);
                    html.Append(diagram.SvgFragment).Append(Newline);
                    html.Append("</figure>").Append(Newline);
                    break;

                case CodeBlock code:
                    string language = string.IsNullOrEmpty(code.Language)
                        ? string.Empty
                        : $" class=\"language-{HtmlEscape(code.Language)}\"";
                    html.Append("<pre><code").Append(language).Append('>');
                    html.Append(HtmlEscape(code.Text));
                    html.Append("</code></pre>").Append(Newline);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Document block type '{block.GetType().Name}' is not supported by the " +
                        "HTML writer.");
            }
        }

        html.Append("</main>").Append(Newline);
        html.Append("</body>").Append(Newline);
        html.Append("</html>").Append(Newline);
        return html.ToString();
    }

    private string RenderProse(ProseBlock prose)
    {
        var text = new StringWriter { NewLine = Newline };
        var renderer = new HtmlRenderer(text);
        _pipeline.Setup(renderer);

        foreach (Block node in prose.Nodes)
        {
            renderer.Write(node);
        }

        text.Flush();
        return text.ToString();
    }

    private static string HtmlEscape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
