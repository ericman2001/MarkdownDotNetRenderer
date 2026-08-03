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
/// <param name="MarkerSize">Line-end glyph width and height, in stroke-width units.</param>
/// <param name="MarkerFill">Fill of the hollow line-end glyphs.</param>
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
    double LabelClearance = 8,
    double MarkerSize = 9,
    string MarkerFill = "#ffffff")
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
    /// <summary>How many times a label is swept out of the boxes it overlaps.</summary>
    private const int ClearancePasses = 4;

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

        EmitMarkers(svg, points, edge.Edge, paint);

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

        EdgeLabelAnchors anchors = LabelAnchors(edge, placement, paint, fontSize);

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge-label")
            .Attribute("data-source", edge.Edge.SourceId)
            .Attribute("data-target", edge.Edge.TargetId);

        Emit(edge.Edge.Label, anchors.Mid);
        Emit(edge.Edge.StartLabel, anchors.Start);
        Emit(edge.Edge.EndLabel, anchors.End);

        svg.EndElement();

        void Emit(string? text, LayoutPoint? anchor)
        {
            if (text is { Length: > 0 } && anchor is { } at)
            {
                EmitBoxedText(svg, text, at, paint, options, fontSize);
            }
        }
    }

    /// <summary>
    /// Where an edge's three labels are centred. Every label is placed off the line rather than on
    /// it, and clear of every box in the diagram, because each is drawn with an opaque backing rect after the
    /// connector, its marker glyphs, and the boxes themselves.
    /// </summary>
    /// <param name="edge">The placed edge.</param>
    /// <param name="placement">The placement the edge belongs to, which supplies its two boxes and
    /// every other box a label has to stay off.</param>
    /// <param name="paint">Supplies the gaps, padding, and clearance.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>The anchor of each label the edge actually carries, or every anchor
    /// <see langword="null"/> when an end of the edge is not placed.</returns>
    public static EdgeLabelAnchors LabelAnchors(
        PlacedEdge edge,
        GraphPlacement placement,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);

        if (!placement.NodesById.TryGetValue(edge.Edge.SourceId, out PlacedNode? source) ||
            !placement.NodesById.TryGetValue(edge.Edge.TargetId, out PlacedNode? target))
        {
            return new EdgeLabelAnchors(null, null, null);
        }

        if (edge.IsSelfLoop)
        {
            return SelfLoopLabelAnchors(edge.Edge, source, paint, fontSize);
        }

        List<LayoutPoint> points = Route(edge, source, target, paint);
        return new EdgeLabelAnchors(
            ClearOfLoopBand(
                edge.Edge.Label,
                edge.Edge.Label is { Length: > 0 } label
                    ? ClearOfBoxes(
                        label,
                        Beside(label, GraphGeometry.MidPoint(points), points[0], points[^1]))
                    : null,
                points[^1]),
            EndLabelAnchor(edge.Edge.StartLabel, points[0], points[1], edge.Edge.StartMarkerInset),
            EndLabelAnchor(edge.Edge.EndLabel, points[^1], points[^2], edge.Edge.EndMarkerInset));

        // A label sitting beside the line, a whole label-height off it, never covers the line or
        // anything drawn along it.
        LayoutPoint? Beside(string? text, LayoutPoint at, LayoutPoint from, LayoutPoint to)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            (double normalX, double normalY) = Normal(from, to);
            NodeSize box = LabelBoxSize(text, paint, fontSize);

            // The rect has to clear the line along the direction it is pushed, so which of its own
            // dimensions has to fit depends on which way the normal points.
            double offset = (Math.Abs(normalX) > Math.Abs(normalY)
                ? box.Width / 2
                : box.Height / 2) + paint.LabelClearance;
            return new LayoutPoint(at.X + (normalX * offset), at.Y + (normalY * offset));
        }

        // A route leaving a self-looping box starts inside the band that box reserves for its loop
        // and the loop's labels, so a label on it is pushed past the band instead of sharing it.
        LayoutPoint? ClearOfLoopBand(string? text, LayoutPoint? anchor, LayoutPoint last)
        {
            if (text is not { Length: > 0 } || anchor is not { } at || source.RightReserve <= 0)
            {
                return anchor;
            }

            double half = LabelBoxSize(text, paint, fontSize).Width / 2;
            double clear = source.Left + source.Width + source.RightReserve + paint.LabelClearance;
            return at.X - half >= clear || last.X <= clear
                ? at
                : new LayoutPoint(clear + half, at.Y);
        }

        // An end label also has to clear the box its endpoint touches, so it steps inwards along
        // the line by its own extent before stepping off the line.
        LayoutPoint? EndLabelAnchor(
            string? text,
            LayoutPoint endpoint,
            LayoutPoint inward,
            double markerInset)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            NodeSize box = LabelBoxSize(text, paint, fontSize);
            double along = markerInset + paint.EndLabelGap +
                (Math.Abs(inward.X - endpoint.X) > Math.Abs(inward.Y - endpoint.Y)
                    ? box.Width / 2
                    : box.Height / 2);
            LayoutPoint? anchor =
                Beside(text, GraphGeometry.Along(endpoint, inward, along), endpoint, inward);

            // A route that runs at an angle leaves the label beside it still catching the corner of
            // the box it points at, so it is pushed further off the line until it is clear.
            return ClearOfBoxes(text, anchor);
        }

        // Nudges a label out of any box it still overlaps, whose text it would otherwise erase,
        // taking whichever of the four ways out moves it least. Its own two boxes come first, since
        // clearing a bystander box must not push it back over them.
        LayoutPoint? ClearOfBoxes(string text, LayoutPoint? anchor)
        {
            if (anchor is not { } at)
            {
                return anchor;
            }

            NodeSize label = LabelBoxSize(text, paint, fontSize);
            double halfWidth = (label.Width / 2) + paint.LabelClearance;
            double halfHeight = (label.Height / 2) + paint.LabelClearance;

            // Stepping out of one box can step into the next, so the sweep repeats a fixed number
            // of times: bounded, so still solver-free and deterministic.
            for (int pass = 0; pass < ClearancePasses; pass++)
            {
                LayoutPoint before = at;
                foreach (PlacedNode box in Obstacles())
                {
                    if (at.X + halfWidth <= box.Left || at.X - halfWidth >= box.Left + box.Width ||
                        at.Y + halfHeight <= box.Top || at.Y - halfHeight >= box.Top + box.Height)
                    {
                        continue;
                    }

                    double left = box.Left - halfWidth;
                    double right = box.Left + box.Width + halfWidth;
                    double up = box.Top - halfHeight;
                    double down = box.Top + box.Height + halfHeight;
                    double byX = Math.Abs(left - at.X) <= Math.Abs(right - at.X) ? left : right;
                    double byY = Math.Abs(up - at.Y) <= Math.Abs(down - at.Y) ? up : down;
                    at = Math.Abs(byX - at.X) <= Math.Abs(byY - at.Y)
                        ? new LayoutPoint(byX, at.Y)
                        : new LayoutPoint(at.X, byY);
                }

                if (at.X == before.X && at.Y == before.Y)
                {
                    break;
                }
            }

            return at;
        }

        IEnumerable<PlacedNode> Obstacles()
        {
            yield return source;
            yield return target;
            foreach (PlacedNode node in placement.Nodes)
            {
                if (!ReferenceEquals(node, source) && !ReferenceEquals(node, target))
                {
                    yield return node;
                }
            }
        }
    }

    /// <summary>
    /// Lays a self-loop's labels out in a row to the right of the loop: they cannot go on the
    /// curve, which is too short, and stacking them at one point would have them erase each other.
    /// </summary>
    private static EdgeLabelAnchors SelfLoopLabelAnchors(
        GraphEdgeSpec edge,
        PlacedNode source,
        GraphEdgePaint paint,
        double fontSize)
    {
        double cursor = SelfLoopRight(source, paint) + paint.EndLabelGap;
        return new EdgeLabelAnchors(Next(edge.Label), Next(edge.StartLabel), Next(edge.EndLabel));

        LayoutPoint? Next(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            double width = LabelBoxSize(text, paint, fontSize).Width;
            var anchor = new LayoutPoint(cursor + (width / 2), source.CenterY);
            cursor += width + paint.LabelClearance;
            return anchor;
        }
    }

    /// <summary>The unit normal of the direction from one point to another.</summary>
    private static (double X, double Y) Normal(LayoutPoint from, LayoutPoint to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        return length == 0 ? (0, -1) : (dy / length, -dx / length);
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
        if (edge.Edge.StartMarker is not null)
        {
            start = GraphGeometry.Along(
                start, firstInner, paint.ArrowInset + edge.Edge.StartMarkerInset);
        }

        points[0] = start;

        LayoutPoint lastInner = points[^2];
        LayoutPoint end = GraphGeometry.Clip(
            target.CenterX, target.CenterY, target.Width, target.Height, target.Shape, lastInner);
        if (edge.Edge.EndMarker is not null)
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

    /// <summary>
    /// How much room a self-looping box needs to its right for the loop and the row of labels
    /// beside it. Reserving it during layout is what keeps both clear of whatever is placed beside
    /// the box.
    /// </summary>
    /// <param name="edge">The self-loop.</param>
    /// <param name="paint">Supplies the bulge, the end-label gap, and the clearance.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>The clearance in CSS pixels, measured from the box's right edge.</returns>
    public static double SelfLoopReserve(
        GraphEdgeSpec edge,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(paint);

        double labels = 0;
        foreach (string? text in new[] { edge.Label, edge.StartLabel, edge.EndLabel })
        {
            if (text is { Length: > 0 })
            {
                labels += LabelBoxSize(text, paint, fontSize).Width + paint.LabelClearance;
            }
        }

        return (paint.SelfLoopBulge * CurveExtentFraction) +
            (labels == 0 ? 0 : paint.EndLabelGap + labels);
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

        return svg;
    }

    /// <summary>Draws the glyphs the edge's ends carry, each rotated to follow its line.</summary>
    private static void EmitMarkers(
        SvgBuilder svg,
        IReadOnlyList<LayoutPoint> points,
        GraphEdgeSpec edge,
        GraphEdgePaint paint)
    {
        Emit(edge.StartMarker, points[0], points[1]);
        Emit(edge.EndMarker, points[^1], points[^2]);

        void Emit(GraphMarker? marker, LayoutPoint at, LayoutPoint from)
        {
            if (marker is { } glyph)
            {
                GraphMarkers.EmitAt(
                    svg,
                    glyph,
                    at,
                    from,
                    paint.Stroke,
                    paint.StrokeWidth,
                    paint.MarkerFill,
                    paint.MarkerSize);
            }
        }
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
