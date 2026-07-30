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

namespace MarkdownDotNetRenderer.Writers;

/// <summary>An ordered, format-agnostic description of the document to write.</summary>
/// <param name="Blocks">The document's blocks, in document order.</param>
public sealed record DocumentContent(IReadOnlyList<DocumentBlock> Blocks);

/// <summary>One unit of document content. See the derived records for the cases.</summary>
public abstract record DocumentBlock;

/// <summary>
/// A run of prose, held as references into the Markdig AST. The AST is never mutated: the block
/// list is our own, and writers render these nodes with the pipeline that parsed them.
/// </summary>
/// <param name="Nodes">The consecutive Markdig blocks making up this prose run.</param>
public sealed record ProseBlock(IReadOnlyList<Block> Nodes) : DocumentBlock;

/// <summary>A rendered diagram: an <c>&lt;svg&gt;</c> fragment with intrinsic size in CSS pixels.</summary>
/// <param name="SvgFragment">The <c>&lt;svg&gt;</c> element, without an XML prolog.</param>
/// <param name="Width">Intrinsic width in CSS pixels.</param>
/// <param name="Height">Intrinsic height in CSS pixels.</param>
/// <param name="AltText">Accessible description of the diagram.</param>
public sealed record DiagramBlock(string SvgFragment, double Width, double Height, string? AltText)
    : DocumentBlock;

/// <summary>A verbatim code block, used for the unsupported/malformed mermaid fallback.</summary>
/// <param name="Text">The verbatim source text.</param>
/// <param name="Language">Info-string language, e.g. <c>mermaid</c>.</param>
public sealed record CodeBlock(string Text, string? Language) : DocumentBlock;

/// <summary>Assembles a <see cref="DocumentContent"/> into one output format's byte stream.</summary>
public interface IDocumentWriter
{
    /// <summary>File extension including the dot, e.g. <c>.html</c>.</summary>
    string FileExtension { get; }

    /// <summary>MIME type of the produced document.</summary>
    string ContentType { get; }

    /// <summary>Writes the document to <paramref name="destination"/>.</summary>
    /// <param name="content">The blocks to write.</param>
    /// <param name="destination">The stream to write to; left open.</param>
    /// <param name="options">Options controlling the render.</param>
    /// <param name="cancellationToken">Token observed between blocks.</param>
    /// <returns>A task completing when the document has been written.</returns>
    Task WriteAsync(
        DocumentContent content,
        Stream destination,
        RenderOptions options,
        CancellationToken cancellationToken = default);
}
