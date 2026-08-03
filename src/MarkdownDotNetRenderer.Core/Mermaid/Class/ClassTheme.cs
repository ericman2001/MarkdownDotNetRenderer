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

namespace MarkdownDotNetRenderer.Mermaid.Class;

/// <summary>
/// Paint and box metrics for <see cref="ClassRenderer"/>. Lengths are CSS pixels, colours are CSS
/// colour literals (docs/09-conventions.md).
/// </summary>
/// <param name="BoxFill">Fill of a class box.</param>
/// <param name="BoxStroke">Stroke of a class box and of its compartment dividers.</param>
/// <param name="BoxStrokeWidth">Stroke width of a class box and its dividers.</param>
/// <param name="HeaderFill">Fill of the name compartment.</param>
/// <param name="TextFill">Fill of member text.</param>
/// <param name="NameWeight">Font weight of the class name.</param>
/// <param name="HorizontalPadding">Padding either side of the widest line.</param>
/// <param name="CompartmentPadding">Padding above and below a compartment's lines.</param>
/// <param name="EmptyCompartmentHeight">Height of a compartment with no members.</param>
/// <param name="MinWidth">Smallest class box width.</param>
public sealed record ClassTheme(
    string BoxFill = "#ffffff",
    string BoxStroke = "#33415a",
    double BoxStrokeWidth = 1.5,
    string HeaderFill = "#eef2f7",
    string TextFill = "#111827",
    string NameWeight = "600",
    double HorizontalPadding = 12,
    double CompartmentPadding = 6,
    double EmptyCompartmentHeight = 10,
    double MinWidth = 96)
{
    /// <summary>Paint for the relation lines, their labels, their cardinalities, and their glyphs.</summary>
    public GraphEdgePaint Edge { get; init; } = GraphEdgePaint.Default with { MarkerSize = 9 };

    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static ClassTheme Default { get; } = new();
}
