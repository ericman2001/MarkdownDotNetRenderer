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

using MarkdownDotNetRenderer.Mermaid.Graph;

namespace MarkdownDotNetRenderer.Mermaid.State;

/// <summary>
/// Paint and box metrics for <see cref="StateRenderer"/>. Lengths are CSS pixels, colours are CSS
/// colour literals; nothing here is hard-coded at a call site (docs/09-conventions.md).
/// </summary>
/// <param name="StateFill">Fill of an ordinary state.</param>
/// <param name="StateStroke">Stroke of an ordinary state.</param>
/// <param name="StateStrokeWidth">Stroke width of state and note boxes.</param>
/// <param name="PseudoFill">Fill of the <c>[*]</c> pseudo-state circles.</param>
/// <param name="PseudoRadius">Radius of the <c>[*]</c> pseudo-state circles.</param>
/// <param name="EndRingGap">Gap between the end state's inner disc and its ring.</param>
/// <param name="NoteFill">Fill of a note box.</param>
/// <param name="NoteStroke">Stroke of a note box.</param>
/// <param name="NoteDashArray">Dash pattern of a note box's border.</param>
/// <param name="TextFill">Fill of label text.</param>
/// <param name="LabelWrapChars">Soft wrap width, in characters, for state and note labels.</param>
/// <param name="HorizontalPadding">Padding either side of a label inside its box.</param>
/// <param name="VerticalPadding">Padding above and below a label inside its box.</param>
/// <param name="MinWidth">Smallest state box width.</param>
/// <param name="MinHeight">Smallest state box height.</param>
/// <param name="MarkerSize">Arrowhead marker size, in stroke-width units.</param>
public sealed record StateTheme(
    string StateFill = "#ffffff",
    string StateStroke = "#33415a",
    double StateStrokeWidth = 1.5,
    string PseudoFill = "#33415a",
    double PseudoRadius = 9,
    double EndRingGap = 3,
    string NoteFill = "#fffbe6",
    string NoteStroke = "#b7952f",
    string NoteDashArray = "4 3",
    string TextFill = "#111827",
    int LabelWrapChars = 22,
    double HorizontalPadding = 20,
    double VerticalPadding = 12,
    double MinWidth = 56,
    double MinHeight = 32,
    double MarkerSize = 8)
{
    /// <summary>Paint for the transition lines and their labels.</summary>
    public GraphEdgePaint Edge { get; init; } = GraphEdgePaint.Default;

    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static StateTheme Default { get; } = new();
}
