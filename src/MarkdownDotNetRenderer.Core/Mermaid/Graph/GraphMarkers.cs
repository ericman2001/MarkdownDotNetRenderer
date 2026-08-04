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

using MarkdownDotNetRenderer.Mermaid.Flowchart;
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
/// The shared line-end glyph library. Each glyph is drawn as ordinary geometry at the endpoint it
/// belongs to, rotated to follow its line, rather than referenced through an SVG
/// <c>&lt;marker&gt;</c>. This phase-4 approach keeps the glyph geometry self-contained and preserves
/// the historical ER cardinality-bar workaround, while the phase-1/3 arrow markers are also
/// preserved for consumers that support them (docs/04-mermaid-engine.md).
/// </summary>
public static class GraphMarkers
{
    /// <summary>Side of the square glyph box every glyph is drawn in.</summary>
    public const double ViewBoxSide = 10;

    /// <summary>The glyph's name, used as the <c>data-marker</c> value of the group drawn.</summary>
    /// <param name="marker">The glyph.</param>
    /// <returns>The name.</returns>
    public static string Name(GraphMarker marker) => marker switch
    {
        GraphMarker.HollowTriangle => "triangle",
        GraphMarker.FilledDiamond => "diamond-filled",
        GraphMarker.HollowDiamond => "diamond-hollow",
        GraphMarker.ErExactlyOne => "er-one",
        GraphMarker.ErZeroOrOne => "er-zero-one",
        GraphMarker.ErOneOrMany => "er-one-many",
        GraphMarker.ErZeroOrMany => "er-zero-many",
        _ => "arrow",
    };

    /// <summary>
    /// How far a line carrying this glyph must stop short of the box it points at so the glyph is
    /// drawn beside the box instead of underneath it. Every glyph except
    /// <see cref="GraphMarker.Arrow"/> is drawn entirely behind its reference point, so it needs
    /// the glyph's full length; the arrow's tip sits on the endpoint and needs none.
    /// </summary>
    /// <param name="marker">The glyph.</param>
    /// <param name="size">Marker width and height, in stroke-width units.</param>
    /// <param name="strokeWidth">Stroke width of the line, which scales the marker.</param>
    /// <returns>The clearance in CSS pixels.</returns>
    public static double EndpointInset(GraphMarker marker, double size, double strokeWidth) =>
        marker == GraphMarker.Arrow ? 0 : size * strokeWidth;

    /// <summary>
    /// Draws one glyph at a line's endpoint, pointing the way the line arrives there, at the same
    /// size and stroke width an equivalent <c>&lt;marker&gt;</c> would have produced.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="marker">The glyph.</param>
    /// <param name="at">The endpoint the glyph's reference point sits on.</param>
    /// <param name="from">A point back along the line, which fixes the glyph's rotation.</param>
    /// <param name="stroke">Glyph stroke colour.</param>
    /// <param name="strokeWidth">Stroke width of the line, which also scales the glyph.</param>
    /// <param name="background">Fill of the hollow glyphs.</param>
    /// <param name="size">Glyph width and height, in stroke-width units.</param>
    public static void EmitAt(
        SvgBuilder svg,
        GraphMarker marker,
        LayoutPoint at,
        LayoutPoint from,
        string stroke,
        double strokeWidth,
        string background,
        double size)
    {
        ArgumentNullException.ThrowIfNull(svg);

        double scale = size * strokeWidth / ViewBoxSide;
        if (scale <= 0)
        {
            return;
        }

        double angle = Math.Atan2(at.Y - from.Y, at.X - from.X) * 180 / Math.PI;

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge-marker")
            .Attribute("data-marker", Name(marker))
            .Attribute(
                "transform",
                $"translate({SvgBuilder.Number(at.X)} {SvgBuilder.Number(at.Y)}) " +
                $"rotate({SvgBuilder.Number(angle)}) " +
                $"scale({SvgBuilder.Number(scale)}) " +
                $"translate({SvgBuilder.Number(-RefX(marker))} {SvgBuilder.Number(-ViewBoxSide / 2)})");

        // The group scales the glyph box, so the stroke has to be pre-divided to land on the
        // line's own width once drawn.
        EmitGlyph(svg, marker, stroke, strokeWidth / scale, background);

        svg.EndElement();
    }

    /// <summary>Where along the glyph the line's endpoint sits.</summary>
    private static double RefX(GraphMarker marker) =>
        marker == GraphMarker.Arrow ? ViewBoxSide - 1 : ViewBoxSide;

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
