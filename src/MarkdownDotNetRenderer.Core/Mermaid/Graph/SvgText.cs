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

using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Graph;

/// <summary>
/// Label emission shared by the phase-4 diagram renderers: one <c>&lt;text&gt;</c> element with a
/// <c>&lt;tspan&gt;</c> per wrapped line, every visual attribute inline so the fragment renders
/// identically inline in HTML and as a standalone SVG part in an office package.
/// </summary>
public static class SvgText
{
    /// <summary>Writes a multi-line block whose vertical centre is <paramref name="centerY"/>.</summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="lines">The already-wrapped lines; at least one.</param>
    /// <param name="centerX">Anchor on the x axis.</param>
    /// <param name="centerY">Vertical centre of the block.</param>
    /// <param name="anchor">SVG <c>text-anchor</c> value.</param>
    /// <param name="options">Render options supplying the font family.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <param name="fill">Text fill colour.</param>
    /// <param name="weight">Optional <c>font-weight</c> value.</param>
    public static void EmitCentered(
        SvgBuilder svg,
        IReadOnlyList<string> lines,
        double centerX,
        double centerY,
        string anchor,
        RenderOptions options,
        double fontSize,
        string fill,
        string? weight = null)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        Start(svg, centerX, centerY, anchor, options, fontSize, fill, weight);

        for (int i = 0; i < lines.Count; i++)
        {
            double dy = i == 0 ? -(lines.Count - 1) * lineHeight / 2 : lineHeight;
            svg.StartElement("tspan")
                .Attribute("x", centerX)
                .Attribute("dy", dy)
                .Text(lines[i])
                .EndElement();
        }

        svg.EndElement();
    }

    /// <summary>Writes a multi-line block whose first line's centre sits half a line below
    /// <paramref name="top"/>.</summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="lines">The already-wrapped lines; at least one.</param>
    /// <param name="anchorX">Anchor on the x axis.</param>
    /// <param name="top">Top of the text block.</param>
    /// <param name="anchor">SVG <c>text-anchor</c> value.</param>
    /// <param name="options">Render options supplying the font family.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <param name="fill">Text fill colour.</param>
    /// <param name="weight">Optional <c>font-weight</c> value.</param>
    public static void EmitFromTop(
        SvgBuilder svg,
        IReadOnlyList<string> lines,
        double anchorX,
        double top,
        string anchor,
        RenderOptions options,
        double fontSize,
        string fill,
        string? weight = null)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        Start(svg, anchorX, top, anchor, options, fontSize, fill, weight);

        for (int i = 0; i < lines.Count; i++)
        {
            svg.StartElement("tspan")
                .Attribute("x", anchorX)
                .Attribute("dy", i == 0 ? lineHeight / 2 : lineHeight)
                .Text(lines[i])
                .EndElement();
        }

        svg.EndElement();
    }

    /// <summary>Writes a single line of text centred on a point.</summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="x">Anchor on the x axis.</param>
    /// <param name="y">Vertical centre of the line.</param>
    /// <param name="anchor">SVG <c>text-anchor</c> value.</param>
    /// <param name="options">Render options supplying the font family.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <param name="fill">Text fill colour.</param>
    /// <param name="weight">Optional <c>font-weight</c> value.</param>
    public static void EmitLine(
        SvgBuilder svg,
        string text,
        double x,
        double y,
        string anchor,
        RenderOptions options,
        double fontSize,
        string fill,
        string? weight = null)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        Start(svg, x, y, anchor, options, fontSize, fill, weight);
        svg.Text(text);
        svg.EndElement();
    }

    private static void Start(
        SvgBuilder svg,
        double x,
        double y,
        string anchor,
        RenderOptions options,
        double fontSize,
        string fill,
        string? weight)
    {
        svg.StartElement("text")
            .Attribute("x", x)
            .Attribute("y", y)
            .Attribute("text-anchor", anchor)
            .Attribute("dominant-baseline", "middle")
            .Attribute("font-family", options.FontFamily)
            .Attribute("font-size", fontSize)
            .Attribute("fill", fill);

        if (weight is { Length: > 0 })
        {
            svg.Attribute("font-weight", weight);
        }
    }
}
