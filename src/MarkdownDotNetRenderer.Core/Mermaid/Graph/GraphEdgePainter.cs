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

/// <summary>
/// Paint and geometry for the shared edge emitter. Values are CSS pixels and CSS colour literals;
/// each diagram type owns an instance in its own theme record rather than hard-coding them at a
/// call site (docs/09-conventions.md).
/// </summary>
/// <param name="Stroke">Stroke of edge lines and paths.</param>
/// <param name="StrokeWidth">Stroke width of edge lines and paths.</param>
/// <param name="DashArray">Dash pattern used by <see cref="GraphLineStyle.Dashed"/> edges.</param>
/// <param name="CornerRadius">Rounding radius applied at polyline bends.</param>
/// <param name="ArrowInset">Distance an edge stops short of a shape when it carries a marker.</param>
/// <param name="SelfLoopBulge">How far a self-loop's control points sit past the box's right edge.</param>
/// <param name="TextFill">Fill of edge-label text.</param>
/// <param name="LabelBackground">Fill of the opaque backing rect behind an edge label.</param>
/// <param name="LabelPaddingX">Horizontal padding of the backing rect behind an edge label.</param>
/// <param name="LabelPaddingY">Vertical padding of the backing rect behind an edge label.</param>
/// <param name="EndLabelGap">Gap between an end label (a cardinality) and its endpoint.</param>
/// <param name="LabelClearance">Space left either side of a mid-edge label's backing rect, which
/// sets the smallest layer gap a labelled edge can be laid out with.</param>
public sealed record GraphEdgePaint(
    string Stroke = "#55637a",
    double StrokeWidth = 1.5,
    string DashArray = "6 4",
    double CornerRadius = 6,
    double ArrowInset = 2,
    double SelfLoopBulge = 28,
    string TextFill = "#111827",
    string LabelBackground = "#ffffff",
    double LabelPaddingX = 4,
    double LabelPaddingY = 2,
    double EndLabelGap = 10,
    double LabelClearance = 8)
{
    /// <summary>The defaults shared by the phase-4 graph-shaped diagram types.</summary>
    public static GraphEdgePaint Default { get; } = new();
}

/// <summary>
/// Emits the routed connections of a graph-shaped diagram: endpoints clipped to their boxes,
/// straight lines for single-layer spans and rounded polylines for longer ones, markers at either
/// or both ends (ER diagrams need both), and labels with an opaque backing rect.
/// </summary>
public static class GraphEdgePainter
{
    /// <summary>Emits one edge's geometry.</summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="edge">The placed edge.</param>
    /// <param name="placement">The placement the edge belongs to.</param>
    /// <param name="paint">Stroke, dash, and inset values.</param>
    public static void EmitEdge(
        SvgBuilder svg,
        PlacedEdge edge,
        GraphPlacement placement,
        GraphEdgePaint paint)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);

        if (!placement.NodesById.TryGetValue(edge.Edge.SourceId, out PlacedNode? source) ||
            !placement.NodesById.TryGetValue(edge.Edge.TargetId, out PlacedNode? target))
        {
            return;
        }

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge")
            .Attribute("data-source", edge.Edge.SourceId)
            .Attribute("data-target", edge.Edge.TargetId);

        if (edge.IsSelfLoop)
        {
            // Control points are offset horizontally only, so the loop stays inside the box's own
            // vertical band; it does reach past the box on the x axis, which is why the canvas is
            // widened for it (see GraphCanvas).
            double loopX = source.Left + source.Width;
            double loopStartY = source.CenterY - (source.Height / 4);
            double loopEndY = source.CenterY + (source.Height / 4);
            double controlX = loopX + paint.SelfLoopBulge;
            string loop =
                $"M {SvgBuilder.Number(loopX)} {SvgBuilder.Number(loopStartY)} " +
                $"C {SvgBuilder.Number(controlX)} {SvgBuilder.Number(loopStartY)} " +
                $"{SvgBuilder.Number(controlX)} {SvgBuilder.Number(loopEndY)} " +
                $"{SvgBuilder.Number(loopX)} {SvgBuilder.Number(loopEndY)}";
            StartGeometry(svg, "path", edge.Edge, paint).Attribute("d", loop).EndElement();
            svg.EndElement();
            return;
        }

        List<LayoutPoint> points = Route(edge, source, target, paint);
        if (points.Count == 2)
        {
            StartGeometry(svg, "line", edge.Edge, paint)
                .Attribute("x1", points[0].X)
                .Attribute("y1", points[0].Y)
                .Attribute("x2", points[1].X)
                .Attribute("y2", points[1].Y)
                .EndElement();
        }
        else
        {
            StartGeometry(svg, "path", edge.Edge, paint)
                .Attribute("d", GraphGeometry.BuildRoundedPath(points, paint.CornerRadius))
                .EndElement();
        }

        svg.EndElement();
    }

    /// <summary>Emits one edge's mid-edge label and end labels, if it has any.</summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="edge">The placed edge.</param>
    /// <param name="placement">The placement the edge belongs to.</param>
    /// <param name="paint">Label paint and padding.</param>
    /// <param name="options">Render options supplying the font family.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    public static void EmitLabels(
        SvgBuilder svg,
        PlacedEdge edge,
        GraphPlacement placement,
        GraphEdgePaint paint,
        RenderOptions options,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(options);

        bool hasLabel = !string.IsNullOrEmpty(edge.Edge.Label);
        bool hasEndLabels = !string.IsNullOrEmpty(edge.Edge.StartLabel) ||
            !string.IsNullOrEmpty(edge.Edge.EndLabel);
        if ((!hasLabel && !hasEndLabels) ||
            !placement.NodesById.TryGetValue(edge.Edge.SourceId, out PlacedNode? source) ||
            !placement.NodesById.TryGetValue(edge.Edge.TargetId, out PlacedNode? target))
        {
            return;
        }

        List<LayoutPoint> points = edge.IsSelfLoop
            ? [SelfLoopLabelAnchor(source, paint)]
            : Route(edge, source, target, paint);

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge-label")
            .Attribute("data-source", edge.Edge.SourceId)
            .Attribute("data-target", edge.Edge.TargetId);

        if (hasLabel)
        {
            LayoutPoint anchor = GraphGeometry.MidPoint(points);
            EmitBoxedText(svg, edge.Edge.Label!, anchor, paint, options, fontSize);
        }

        (LayoutPoint startAnchor, LayoutPoint endAnchor) = EndLabelAnchors(points, paint);

        if (!string.IsNullOrEmpty(edge.Edge.StartLabel))
        {
            EmitBoxedText(svg, edge.Edge.StartLabel!, startAnchor, paint, options, fontSize);
        }

        if (!string.IsNullOrEmpty(edge.Edge.EndLabel))
        {
            EmitBoxedText(svg, edge.Edge.EndLabel!, endAnchor, paint, options, fontSize);
        }

        svg.EndElement();
    }

    /// <summary>Clips an edge's centre-to-centre route to the two boxes it connects.</summary>
    /// <param name="edge">The placed edge.</param>
    /// <param name="source">The source box.</param>
    /// <param name="target">The target box.</param>
    /// <param name="paint">Supplies the marker inset.</param>
    /// <returns>The clipped route, source end first.</returns>
    public static List<LayoutPoint> Route(
        PlacedEdge edge,
        PlacedNode source,
        PlacedNode target,
        GraphEdgePaint paint)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(paint);

        var points = new List<LayoutPoint>(edge.Points);
        LayoutPoint firstInner = points[1];
        LayoutPoint start = GraphGeometry.Clip(
            source.CenterX, source.CenterY, source.Width, source.Height, source.Shape, firstInner);
        if (edge.Edge.StartMarkerId is { Length: > 0 })
        {
            start = GraphGeometry.Along(
                start, firstInner, paint.ArrowInset + edge.Edge.StartMarkerInset);
        }

        points[0] = start;

        LayoutPoint lastInner = points[^2];
        LayoutPoint end = GraphGeometry.Clip(
            target.CenterX, target.CenterY, target.Width, target.Height, target.Shape, lastInner);
        if (edge.Edge.EndMarkerId is { Length: > 0 })
        {
            end = GraphGeometry.Along(end, lastInner, paint.ArrowInset + edge.Edge.EndMarkerInset);
        }

        points[^1] = end;
        return points;
    }

    /// <summary>The size of the backing rect drawn behind a label of this text.</summary>
    /// <param name="text">The label text.</param>
    /// <param name="paint">Supplies the label padding.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>The rect's width and height.</returns>
    public static NodeSize LabelBoxSize(string text, GraphEdgePaint paint, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(paint);

        return new NodeSize(
            TextMetrics.MeasureWidth(text, fontSize) + (2 * paint.LabelPaddingX),
            TextMetrics.LineHeight(fontSize) + (2 * paint.LabelPaddingY));
    }

    /// <summary>Where a self-loop's label is centred.</summary>
    /// <param name="source">The looping box.</param>
    /// <param name="paint">Supplies the bulge and the end-label gap.</param>
    /// <returns>The label's anchor point.</returns>
    public static LayoutPoint SelfLoopLabelAnchor(PlacedNode source, GraphEdgePaint paint)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paint);

        return new LayoutPoint(
            source.Left + source.Width + paint.SelfLoopBulge + paint.EndLabelGap, source.CenterY);
    }

    /// <summary>The rightmost x a self-loop's curve reaches.</summary>
    /// <param name="source">The looping box.</param>
    /// <param name="paint">Supplies the bulge.</param>
    /// <returns>The rightmost x in CSS pixels.</returns>
    public static double SelfLoopRight(PlacedNode source, GraphEdgePaint paint)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(paint);

        return source.Left + source.Width + (paint.SelfLoopBulge * CurveExtentFraction);
    }

    /// <summary>How far a cubic with both control points at the bulge actually reaches.</summary>
    private const double CurveExtentFraction = 0.75;

    /// <summary>Anchors of an edge's end labels; also used when sizing the canvas.</summary>
    /// <param name="points">The clipped route, source end first.</param>
    /// <param name="paint">Supplies the end-label gap.</param>
    /// <returns>The source-end and target-end anchors.</returns>
    public static (LayoutPoint Start, LayoutPoint End) EndLabelAnchors(
        IReadOnlyList<LayoutPoint> points,
        GraphEdgePaint paint)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(paint);

        return (
            EndLabelAnchor(points[0], points.Count > 1 ? points[1] : points[0], paint),
            EndLabelAnchor(points[^1], points.Count > 1 ? points[^2] : points[^1], paint));
    }

    private static LayoutPoint EndLabelAnchor(
        LayoutPoint endpoint,
        LayoutPoint inward,
        GraphEdgePaint paint) =>
        GraphGeometry.Along(endpoint, inward, paint.EndLabelGap);

    private static SvgBuilder StartGeometry(
        SvgBuilder svg,
        string element,
        GraphEdgeSpec edge,
        GraphEdgePaint paint)
    {
        svg.StartElement(element)
            .Attribute("stroke", paint.Stroke)
            .Attribute("stroke-width", paint.StrokeWidth)
            .Attribute("fill", "none");

        if (edge.Line == GraphLineStyle.Dashed)
        {
            svg.Attribute("stroke-dasharray", paint.DashArray);
        }

        if (edge.StartMarkerId is { Length: > 0 })
        {
            svg.Attribute("marker-start", $"url(#{edge.StartMarkerId})");
        }

        if (edge.EndMarkerId is { Length: > 0 })
        {
            svg.Attribute("marker-end", $"url(#{edge.EndMarkerId})");
        }

        return svg;
    }

    private static void EmitBoxedText(
        SvgBuilder svg,
        string text,
        LayoutPoint anchor,
        GraphEdgePaint paint,
        RenderOptions options,
        double fontSize)
    {
        NodeSize box = LabelBoxSize(text, paint, fontSize);
        double width = box.Width;
        double height = box.Height;

        svg.StartElement("rect")
            .Attribute("x", anchor.X - (width / 2))
            .Attribute("y", anchor.Y - (height / 2))
            .Attribute("width", width)
            .Attribute("height", height)
            .Attribute("fill", paint.LabelBackground)
            .Attribute("stroke", "none")
            .EndElement();

        SvgText.EmitLine(
            svg, text, anchor.X, anchor.Y, "middle", options, fontSize, paint.TextFill);
    }
}
