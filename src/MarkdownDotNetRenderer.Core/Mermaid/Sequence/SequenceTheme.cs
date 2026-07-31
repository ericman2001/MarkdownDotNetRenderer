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

namespace MarkdownDotNetRenderer.Mermaid.Sequence;

/// <summary>
/// Paint and box geometry for <see cref="SequenceRenderer"/> — the sequence-diagram counterpart of
/// <see cref="Flowchart.DiagramTheme"/>, and the pair to <see cref="SequenceMetrics"/>, which
/// tunes *where* things go while this tunes *how they are painted* (docs/09-conventions.md).
/// Lengths are CSS pixels and colours are CSS colour literals emitted as inline SVG attributes.
/// </summary>
/// <param name="ActorFill">Fill of the actor header boxes.</param>
/// <param name="ActorStroke">Stroke of the actor header boxes.</param>
/// <param name="ActorStrokeWidth">Stroke width of the actor header boxes.</param>
/// <param name="ActorCornerRadius">Corner rounding of the actor header boxes.</param>
/// <param name="LifelineStroke">Stroke of the dashed lifelines.</param>
/// <param name="LifelineStrokeWidth">Stroke width of the dashed lifelines.</param>
/// <param name="LifelineDashArray">Dash pattern of the lifelines.</param>
/// <param name="MessageStroke">Stroke of message lines and arrowheads.</param>
/// <param name="MessageStrokeWidth">Stroke width of message lines.</param>
/// <param name="MessageDashArray">Dash pattern of dashed (<c>--&gt;</c>) message lines.</param>
/// <param name="NoteFill">Fill of note boxes.</param>
/// <param name="NoteStroke">Stroke of note boxes.</param>
/// <param name="NoteStrokeWidth">Stroke width of note boxes.</param>
/// <param name="NoteCornerRadius">Corner rounding of note boxes.</param>
/// <param name="TextFill">Fill of all label text.</param>
/// <param name="LabelBackground">Fill of the opaque backing rect behind a message label.</param>
/// <param name="LabelPaddingX">Horizontal padding of the backing rect behind a message label.</param>
/// <param name="LabelPaddingY">Vertical padding of the backing rect behind a message label.</param>
/// <param name="LabelWrapChars">Soft wrap width, in characters, for labels and note text.</param>
/// <param name="MarkerSize">Width and height of the arrowhead markers.</param>
public sealed record SequenceTheme(
    string ActorFill = "#eef2ff",
    string ActorStroke = "#33415a",
    double ActorStrokeWidth = 1.5,
    double ActorCornerRadius = 4,
    string LifelineStroke = "#94a3b8",
    double LifelineStrokeWidth = 1,
    string LifelineDashArray = "4 4",
    string MessageStroke = "#55637a",
    double MessageStrokeWidth = 1.5,
    string MessageDashArray = "6 4",
    string NoteFill = "#fff7d6",
    string NoteStroke = "#c9a227",
    double NoteStrokeWidth = 1,
    double NoteCornerRadius = 4,
    string TextFill = "#111827",
    string LabelBackground = "#ffffff",
    double LabelPaddingX = 4,
    double LabelPaddingY = 1,
    int LabelWrapChars = 28,
    double MarkerSize = 8)
{
    /// <summary>The defaults documented in docs/phases/phase-3-sequence-diagrams.md.</summary>
    public static SequenceTheme Default { get; } = new();
}
