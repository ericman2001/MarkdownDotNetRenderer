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

/// <summary>The line-end glyphs the phase-4 graph diagram types draw.</summary>
public enum GraphMarker
{
    /// <summary>An open chevron: a plain directed edge or association.</summary>
    Arrow,

    /// <summary>An unfilled triangle: class-diagram inheritance and realization.</summary>
    HollowTriangle,

    /// <summary>A filled diamond: class-diagram composition.</summary>
    FilledDiamond,

    /// <summary>An unfilled diamond: class-diagram aggregation.</summary>
    HollowDiamond,

    /// <summary>ER <c>||</c>: exactly one.</summary>
    ErExactlyOne,

    /// <summary>ER <c>|o</c>: zero or one.</summary>
    ErZeroOrOne,

    /// <summary>ER <c>}|</c>: one or many.</summary>
    ErOneOrMany,

    /// <summary>ER <c>}o</c>: zero or many.</summary>
    ErZeroOrMany,
}

/// <summary>
/// The shared <c>&lt;defs&gt;</c> marker library. A renderer asks for exactly the glyphs its
/// diagram uses; each is defined once per fragment under an id derived from the diagram source, so
/// several diagrams in one HTML document never collide (docs/04-mermaid-engine.md).
/// </summary>
public static class GraphMarkers
{
    /// <summary>Side of the square marker viewBox every glyph is drawn in.</summary>
    public const double ViewBoxSide = 10;

    /// <summary>The marker id for one glyph within one diagram.</summary>
    /// <param name="idPrefix">The diagram's id prefix, from <see cref="DiagramIds.ForSource"/>.</param>
    /// <param name="marker">The glyph.</param>
    /// <returns>The marker id.</returns>
    public static string Id(string idPrefix, GraphMarker marker)
    {
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        string suffix = marker switch
        {
            GraphMarker.Arrow => "arrow",
            GraphMarker.HollowTriangle => "triangle",
            GraphMarker.FilledDiamond => "diamond-filled",
            GraphMarker.HollowDiamond => "diamond-hollow",
            GraphMarker.ErExactlyOne => "er-one",
            GraphMarker.ErZeroOrOne => "er-zero-one",
            GraphMarker.ErOneOrMany => "er-one-many",
            GraphMarker.ErZeroOrMany => "er-zero-many",
            _ => "arrow",
        };

        return $"{idPrefix}-{suffix}";
    }

    /// <summary>
    /// Writes a <c>&lt;defs&gt;</c> block defining the requested glyphs, in
    /// <see cref="GraphMarker"/> order and without duplicates so the output is deterministic
    /// whatever order the caller collected them in.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="idPrefix">The diagram's id prefix.</param>
    /// <param name="markers">The glyphs the diagram references.</param>
    /// <param name="stroke">Glyph stroke colour.</param>
    /// <param name="strokeWidth">Glyph stroke width.</param>
    /// <param name="background">Fill of the hollow glyphs.</param>
    /// <param name="size">Marker width and height, in stroke-width units.</param>
    public static void EmitDefs(
        SvgBuilder svg,
        string idPrefix,
        IReadOnlyCollection<GraphMarker> markers,
        string stroke,
        double strokeWidth,
        string background,
        double size)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);
        ArgumentNullException.ThrowIfNull(markers);

        svg.StartElement("defs");

        foreach (GraphMarker marker in Enum.GetValues<GraphMarker>())
        {
            if (!markers.Contains(marker))
            {
                continue;
            }

            StartMarker(svg, Id(idPrefix, marker), RefX(marker), size);
            EmitGlyph(svg, marker, stroke, strokeWidth, background);
            svg.EndElement();
        }

        svg.EndElement();
    }

    /// <summary>Where along the glyph the line's endpoint sits.</summary>
    private static double RefX(GraphMarker marker) =>
        marker == GraphMarker.Arrow ? ViewBoxSide - 1 : ViewBoxSide;

    private static void StartMarker(SvgBuilder svg, string id, double refX, double size) =>
        svg.StartElement("marker")
            .Attribute("id", id)
            .Attribute("viewBox", $"0 0 {SvgBuilder.Number(ViewBoxSide)} {SvgBuilder.Number(ViewBoxSide)}")
            .Attribute("refX", refX)
            .Attribute("refY", ViewBoxSide / 2)
            .Attribute("markerWidth", size)
            .Attribute("markerHeight", size)
            .Attribute("orient", "auto-start-reverse");

    private static void EmitGlyph(
        SvgBuilder svg,
        GraphMarker marker,
        string stroke,
        double strokeWidth,
        string background)
    {
        switch (marker)
        {
            case GraphMarker.HollowTriangle:
                Shape(svg, "M 0 0 L 10 5 L 0 10 z", background, stroke, strokeWidth);
                break;

            case GraphMarker.FilledDiamond:
                Shape(svg, "M 0 5 L 5 0 L 10 5 L 5 10 z", stroke, stroke, strokeWidth);
                break;

            case GraphMarker.HollowDiamond:
                Shape(svg, "M 0 5 L 5 0 L 10 5 L 5 10 z", background, stroke, strokeWidth);
                break;

            case GraphMarker.ErExactlyOne:
                Shape(svg, "M 4 1 L 4 9 M 8 1 L 8 9", "none", stroke, strokeWidth);
                break;

            case GraphMarker.ErZeroOrOne:
                Shape(svg, "M 8 1 L 8 9", "none", stroke, strokeWidth);
                Circle(svg, 3, background, stroke, strokeWidth);
                break;

            case GraphMarker.ErOneOrMany:
                Shape(svg, "M 1 1 L 1 9 M 3 5 L 10 1 M 3 5 L 10 5 M 3 5 L 10 9", "none", stroke, strokeWidth);
                break;

            case GraphMarker.ErZeroOrMany:
                Shape(svg, "M 4 5 L 10 1 M 4 5 L 10 5 M 4 5 L 10 9", "none", stroke, strokeWidth);
                Circle(svg, 1.5, background, stroke, strokeWidth);
                break;

            case GraphMarker.Arrow:
            default:
                Shape(svg, "M 0 0 L 10 5 L 0 10", "none", stroke, strokeWidth);
                break;
        }
    }

    private static void Shape(
        SvgBuilder svg,
        string path,
        string fill,
        string stroke,
        double strokeWidth) =>
        svg.StartElement("path")
            .Attribute("d", path)
            .Attribute("fill", fill)
            .Attribute("stroke", stroke)
            .Attribute("stroke-width", strokeWidth)
            .EndElement();

    private static void Circle(
        SvgBuilder svg,
        double centerX,
        string fill,
        string stroke,
        double strokeWidth) =>
        svg.StartElement("circle")
            .Attribute("cx", centerX)
            .Attribute("cy", ViewBoxSide / 2)
            .Attribute("r", 1.5)
            .Attribute("fill", fill)
            .Attribute("stroke", stroke)
            .Attribute("stroke-width", strokeWidth)
            .EndElement();
}
