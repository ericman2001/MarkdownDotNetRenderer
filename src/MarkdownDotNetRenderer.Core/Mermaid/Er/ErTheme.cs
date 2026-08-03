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

namespace MarkdownDotNetRenderer.Mermaid.Er;

/// <summary>
/// Paint and box metrics for <see cref="ErRenderer"/>. Lengths are CSS pixels, colours are CSS
/// colour literals (docs/09-conventions.md).
/// </summary>
/// <param name="BoxFill">Fill of an entity's attribute compartment.</param>
/// <param name="BoxStroke">Stroke of an entity box and its divider.</param>
/// <param name="BoxStrokeWidth">Stroke width of an entity box and its divider.</param>
/// <param name="HeaderFill">Fill of the entity-name compartment.</param>
/// <param name="TextFill">Fill of entity and attribute text.</param>
/// <param name="NameWeight">Font weight of the entity name.</param>
/// <param name="HorizontalPadding">Padding either side of the widest line.</param>
/// <param name="CompartmentPadding">Padding above and below a compartment's lines.</param>
/// <param name="EmptyCompartmentHeight">Height of the attribute compartment when there are none.</param>
/// <param name="MinWidth">Smallest entity box width.</param>
/// <param name="MarkerSize">Crow's-foot marker size, in stroke-width units.</param>
public sealed record ErTheme(
    string BoxFill = "#ffffff",
    string BoxStroke = "#33415a",
    double BoxStrokeWidth = 1.5,
    string HeaderFill = "#e8eef6",
    string TextFill = "#111827",
    string NameWeight = "600",
    double HorizontalPadding = 12,
    double CompartmentPadding = 6,
    double EmptyCompartmentHeight = 8,
    double MinWidth = 104,
    double MarkerSize = 10)
{
    /// <summary>Paint for the relationship lines and their labels.</summary>
    public GraphEdgePaint Edge { get; init; } = GraphEdgePaint.Default with { EndLabelGap = 22 };

    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static ErTheme Default { get; } = new();
}
