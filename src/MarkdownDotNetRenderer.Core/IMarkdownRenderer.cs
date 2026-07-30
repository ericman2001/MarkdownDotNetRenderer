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

namespace MarkdownDotNetRenderer;

/// <summary>
/// Renders Markdown (with mermaid diagrams) to one of the supported output formats.
/// Implementations degrade unsupported content into diagnostics rather than throwing; see
/// docs/03-core-api.md for the error-handling contract.
/// </summary>
public interface IMarkdownRenderer
{
    /// <summary>Renders Markdown text to the bytes of the configured output format.</summary>
    /// <param name="markdown">The Markdown source.</param>
    /// <param name="options">Options controlling the render.</param>
    /// <param name="cancellationToken">Token observed between blocks.</param>
    /// <returns>The rendered bytes and any diagnostics.</returns>
    Task<RenderResult> RenderAsync(
        string markdown,
        RenderOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Reads <paramref name="inputPath"/>, renders it, and writes <paramref name="outputPath"/>.</summary>
    /// <param name="inputPath">Path of the Markdown file to read.</param>
    /// <param name="outputPath">Path of the document to write.</param>
    /// <param name="options">Options controlling the render.</param>
    /// <param name="cancellationToken">Token observed between blocks.</param>
    /// <returns>The rendered bytes and any diagnostics.</returns>
    Task<RenderResult> RenderFileAsync(
        string inputPath,
        string outputPath,
        RenderOptions options,
        CancellationToken cancellationToken = default);
}
