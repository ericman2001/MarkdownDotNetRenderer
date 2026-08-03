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

namespace MarkdownDotNetRenderer.Mermaid.Pie;

/// <summary>
/// Paint for <see cref="PieRenderer"/>, the pie counterpart of
/// <see cref="Flowchart.DiagramTheme"/> and the pair to <see cref="PieMetrics"/>: this tunes
/// *how* the chart is painted, <see cref="PieMetrics"/> tunes *where* things go
/// (docs/09-conventions.md). Lengths are CSS pixels, colours are CSS colour literals.
/// </summary>
/// <param name="SliceStroke">Stroke separating adjacent slices.</param>
/// <param name="SliceStrokeWidth">Stroke width separating adjacent slices.</param>
/// <param name="TextFill">Fill of the title and legend text.</param>
/// <param name="LabelWrapChars">Soft wrap width, in characters, for the title.</param>
/// <param name="TitleWeight">Font weight of the title.</param>
public sealed record PieTheme(
    string SliceStroke = "#ffffff",
    double SliceStrokeWidth = 1,
    string TextFill = "#111827",
    int LabelWrapChars = 36,
    string TitleWeight = "600")
{
    /// <summary>
    /// The slice palette, applied in source order. Eight fixed, well-separated hues in a
    /// deterministic order — a colour-blind-friendly qualitative ramp — reused cyclically for
    /// charts with more slices than colours.
    /// </summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#4477aa",
        "#ee6677",
        "#228833",
        "#ccbb44",
        "#66ccee",
        "#aa3377",
        "#bbbbbb",
        "#004488",
    ];

    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static PieTheme Default { get; } = new();

    /// <summary>The palette colour for a slice's source position.</summary>
    /// <param name="order">The slice's 0-based source index.</param>
    /// <returns>A CSS colour literal.</returns>
    public static string Colour(int order) => Palette[order % Palette.Count];
}
