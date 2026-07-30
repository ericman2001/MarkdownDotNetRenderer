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

namespace MarkdownDotNetRenderer.Mermaid;

/// <summary>
/// Renders one family of mermaid diagram types to an SVG fragment. Implementations must be total
/// functions: catch their own parse failures and return
/// <see cref="DiagramRenderResult.Failed(string, string)"/> instead of throwing.
/// </summary>
public interface IDiagramRenderer
{
    /// <summary>Diagram-type keywords this renderer handles, e.g. <c>["flowchart", "graph"]</c>.</summary>
    IReadOnlyCollection<string> DiagramTypes { get; }

    /// <summary>Lays out the diagram and emits SVG.</summary>
    /// <param name="mermaidSource">The complete mermaid source, including its header line.</param>
    /// <param name="options">Options controlling fonts and sizing.</param>
    /// <returns>The rendered fragment, or a failure result.</returns>
    DiagramRenderResult Render(string mermaidSource, RenderOptions options);
}
