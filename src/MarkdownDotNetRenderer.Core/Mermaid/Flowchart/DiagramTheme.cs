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

namespace MarkdownDotNetRenderer.Mermaid.Flowchart;

/// <summary>
/// Tunable visual styling for diagram emitters, the counterpart to <see cref="LayoutMetrics"/>:
/// where <see cref="LayoutMetrics"/> tunes graph layout, this tunes paint and box geometry.
/// Lengths are CSS pixels and colours are CSS colour literals emitted as inline SVG attributes.
/// </summary>
/// <param name="NodeFill">Fill of node shapes and of the backing box behind edge labels.</param>
/// <param name="NodeStroke">Stroke of node shapes.</param>
/// <param name="EdgeStroke">Stroke of edge lines, paths, and arrowheads.</param>
/// <param name="TextFill">Fill of node and edge label text.</param>
/// <param name="NodeStrokeWidth">Stroke width of node shapes.</param>
/// <param name="EdgeStrokeWidth">Stroke width of edge lines and paths.</param>
/// <param name="CornerRadius">Rounding radius applied at edge polyline bends.</param>
/// <param name="ArrowInset">Distance a directed edge stops short of its target shape.</param>
/// <param name="LabelWrapChars">Soft wrap width, in characters, for node labels.</param>
/// <param name="HorizontalPadding">Horizontal padding added around a node's label text.</param>
/// <param name="VerticalPadding">Vertical padding added around a node's label text.</param>
/// <param name="MinNodeWidth">Lower bound on a node box's width.</param>
/// <param name="MinNodeHeight">Lower bound on a node box's height.</param>
/// <param name="SelfLoopBulge">How far a self-loop's control points sit past the node's right edge.</param>
/// <param name="SelfLoopLabelGap">Gap between a self-loop curve and its label box.</param>
public sealed record DiagramTheme(
    string NodeFill = "#ffffff",
    string NodeStroke = "#33415a",
    string EdgeStroke = "#55637a",
    string TextFill = "#111827",
    double NodeStrokeWidth = 1.5,
    double EdgeStrokeWidth = 1.5,
    double CornerRadius = 6,
    double ArrowInset = 2,
    int LabelWrapChars = 22,
    double HorizontalPadding = 24,
    double VerticalPadding = 16,
    double MinNodeWidth = 56,
    double MinNodeHeight = 34,
    double SelfLoopBulge = 28,
    double SelfLoopLabelGap = 6)
{
    /// <summary>The defaults documented in docs/04-mermaid-engine.md.</summary>
    public static DiagramTheme Default { get; } = new();
}
