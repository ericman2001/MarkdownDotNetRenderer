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

    /// <summary>How many rungs out an end label may be tried before giving up.</summary>
    private const int ClearanceRungs = 12;

    /// <summary>
    /// The eight ways out of a crowded spot, in the order ties between equally near candidates are
    /// broken, so the choice never depends on anything but the geometry.
    /// </summary>
    private static readonly (double X, double Y)[] CandidateDirections =
    [
        (0, -1), (0, 1), (1, 0), (-1, 0),
        (Diagonal, -Diagonal), (Diagonal, Diagonal),
        (-Diagonal, -Diagonal), (-Diagonal, Diagonal),
    ];

    /// <summary>The component of a unit vector at 45 degrees.</summary>
    private const double Diagonal = 0.70710678118654752;

    /// <summary>
    /// What one overlap costs a candidate label spot, against 1 for drifting out past the boxes:
    /// drifting is only untidy, where an overlap erases something.
    /// </summary>
    private const int OverlapCost = 4;

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

    /// <summary>
    /// Emits every edge's mid-edge label and end labels. Labels are placed for the diagram as a
    /// whole rather than one edge at a time, because each is opaque and so has to clear the labels
    /// already placed as well as the boxes.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="placement">The placement whose edges are labelled.</param>
    /// <param name="paint">Label paint and padding.</param>
    /// <param name="options">Render options supplying the font family.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    public static void EmitLabels(
        SvgBuilder svg,
        GraphPlacement placement,
        GraphEdgePaint paint,
        RenderOptions options,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<EdgeLabelAnchors> anchors = LabelAnchors(placement, paint, fontSize);
        for (int i = 0; i < placement.Edges.Count; i++)
        {
            GraphEdgeSpec edge = placement.Edges[i].Edge;
            if (string.IsNullOrEmpty(edge.Label) &&
                string.IsNullOrEmpty(edge.StartLabel) &&
                string.IsNullOrEmpty(edge.EndLabel))
            {
                continue;
            }

            svg.StartElement("g")
                .Attribute("class", "mdnr-edge-label")
                .Attribute("data-source", edge.SourceId)
                .Attribute("data-target", edge.TargetId);

            Emit(edge.Label, anchors[i].Mid);
            Emit(edge.StartLabel, anchors[i].Start);
            Emit(edge.EndLabel, anchors[i].End);

            svg.EndElement();
        }

        void Emit(string? text, LayoutPoint? anchor)
        {
            if (text is { Length: > 0 } && anchor is { } at)
            {
                EmitBoxedText(svg, text, at, paint, options, fontSize);
            }
        }
    }

    /// <summary>
    /// Where every edge's labels are centred, in edge order. Each label clears the labels placed
    /// before it, so the result depends on that order and not on which edge is asked about.
    /// </summary>
    /// <param name="placement">The placement whose edges are labelled.</param>
    /// <param name="paint">Supplies the gaps, padding, and clearance.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>One entry per placed edge, in the same order.</returns>
    public static IReadOnlyList<EdgeLabelAnchors> LabelAnchors(
        GraphPlacement placement,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);

        var taken = new List<LabelRect>();
        var anchors = new List<EdgeLabelAnchors>(placement.Edges.Count);
        foreach (PlacedEdge edge in placement.Edges)
        {
            EdgeLabelAnchors placed = LabelAnchors(edge, placement, paint, fontSize, taken);
            anchors.Add(placed);
            Take(edge.Edge.Label, placed.Mid);
            Take(edge.Edge.StartLabel, placed.Start);
            Take(edge.Edge.EndLabel, placed.End);
        }

        return anchors;

        void Take(string? text, LayoutPoint? anchor)
        {
            if (text is { Length: > 0 } && anchor is { } at)
            {
                taken.Add(LabelRect.Around(at, LabelBoxSize(text, paint, fontSize)));
            }
        }
    }

    /// <summary>
    /// Where one edge's three labels are centred. Every label is placed off the line rather than on
    /// it, and clear of every box and of the labels already placed, because each is drawn with an
    /// opaque backing rect after the connector, its glyphs, and the boxes themselves.
    /// </summary>
    /// <param name="edge">The placed edge.</param>
    /// <param name="placement">The placement the edge belongs to, which supplies its two boxes and
    /// every other box a label has to stay off.</param>
    /// <param name="paint">Supplies the gaps, padding, and clearance.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <param name="taken">Rects of the labels already placed, which this one clears too.</param>
    /// <returns>The anchor of each label the edge actually carries, or every anchor
    /// <see langword="null"/> when an end of the edge is not placed.</returns>
    public static EdgeLabelAnchors LabelAnchors(
        PlacedEdge edge,
        GraphPlacement placement,
        GraphEdgePaint paint,
        double fontSize,
        IReadOnlyList<LabelRect> taken)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(taken);

        if (!placement.NodesById.TryGetValue(edge.Edge.SourceId, out PlacedNode? source) ||
            !placement.NodesById.TryGetValue(edge.Edge.TargetId, out PlacedNode? target))
        {
            return new EdgeLabelAnchors(null, null, null);
        }

        if (edge.IsSelfLoop)
        {
            return SelfLoopLabelAnchors(edge.Edge, source, paint, fontSize);
        }

        // An edge's own three labels are placed one after another for the same reason, so each is
        // added to the obstacles as it is placed.
        var obstacles = new List<LabelRect>(taken);
        List<LayoutPoint> points = Route(edge, source, target, paint);
        LayoutPoint? mid = Place(
            edge.Edge.Label,
            ClearOfLoopBand(
                edge.Edge.Label,
                edge.Edge.Label is { Length: > 0 } label
                    ? ClearOfBoxes(
                        label,
                        Beside(label, GraphGeometry.MidPoint(points), points[0], points[^1]))
                    : null,
                points[^1]));
        LayoutPoint? start = Place(
            edge.Edge.StartLabel,
            EndLabelAnchor(edge.Edge.StartLabel, points[0], points[1], edge.Edge.StartMarkerInset));
        LayoutPoint? end = Place(
            edge.Edge.EndLabel,
            EndLabelAnchor(edge.Edge.EndLabel, points[^1], points[^2], edge.Edge.EndMarkerInset));

        return new EdgeLabelAnchors(mid, start, end);

        LayoutPoint? Place(string? text, LayoutPoint? anchor)
        {
            if (text is { Length: > 0 } && anchor is { } at)
            {
                obstacles.Add(LabelRect.Around(at, LabelBoxSize(text, paint, fontSize)));
            }

            return anchor;
        }

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

            return PlaceClear(text, anchor);
        }

        // An end label has to clear three kinds of thing at once — the boxes, the labels already
        // placed, and every connector, whose stroke its opaque rect would otherwise break into what
        // reads as a dashed line. Escaping them one at a time only trades one overlap for another,
        // so instead a fixed ladder of candidate offsets around the anchor is scored against all
        // three at once and the nearest clear candidate wins — nearest, because a label far from the
        // line it annotates no longer says which relation it belongs to. That terminates, and taking
        // the least bad candidate when none is clear keeps the result defined either way.
        LayoutPoint? PlaceClear(string text, LayoutPoint? anchor)
        {
            if (anchor is not { } at)
            {
                return anchor;
            }

            NodeSize label = LabelBoxSize(text, paint, fontSize);
            double halfWidth = (label.Width / 2) + paint.LabelClearance;
            double halfHeight = (label.Height / 2) + paint.LabelClearance;
            double step = (label.Height / 2) + paint.LabelClearance;
            LayoutPoint best = at;
            int fewest = int.MaxValue;

            // Rungs are walked outwards and every direction on a rung is the same distance out, so
            // the first clear candidate found is a nearest one.
            for (int rung = 0; rung <= ClearanceRungs; rung++)
            {
                foreach ((double dirX, double dirY) in CandidateDirections)
                {
                    var candidate = new LayoutPoint(
                        at.X + (dirX * step * rung), at.Y + (dirY * step * rung));
                    int cost = Cost(candidate, halfWidth, halfHeight);
                    if (cost == 0)
                    {
                        return candidate;
                    }

                    if (cost < fewest)
                    {
                        fewest = cost;
                        best = candidate;
                    }

                    if (rung == 0)
                    {
                        break;
                    }
                }
            }

            return best;
        }

        int Cost(LayoutPoint at, double halfWidth, double halfHeight)
        {
            // Leaving the area the boxes occupy counts against a candidate too: the canvas grows to
            // fit it, but a label out in the empty margin reads less as belonging to its line.
            int count = at.X - halfWidth < 0 || at.X + halfWidth > placement.Width ||
                at.Y - halfHeight < 0 || at.Y + halfHeight > placement.Height
                ? 1
                : 0;
            foreach (LabelRect box in Obstacles())
            {
                if (at.X + halfWidth > box.Left && at.X - halfWidth < box.Left + box.Width &&
                    at.Y + halfHeight > box.Top && at.Y - halfHeight < box.Top + box.Height)
                {
                    count += OverlapCost;
                }
            }

            foreach ((LayoutPoint from, LayoutPoint to) in Connectors())
            {
                // A segment the rect does not meet leaves the rect's centre where it is.
                if (PushOffSegment(at, halfWidth, halfHeight, from, to) != at)
                {
                    count += OverlapCost;
                }
            }

            return count;
        }

        IEnumerable<(LayoutPoint From, LayoutPoint To)> Connectors()
        {
            foreach (PlacedEdge other in placement.Edges)
            {
                if (other.IsSelfLoop ||
                    !placement.NodesById.TryGetValue(other.Edge.SourceId, out PlacedNode? from) ||
                    !placement.NodesById.TryGetValue(other.Edge.TargetId, out PlacedNode? to))
                {
                    continue;
                }

                List<LayoutPoint> route = Route(other, from, to, paint);
                for (int i = 1; i < route.Count; i++)
                {
                    yield return (route[i - 1], route[i]);
                }
            }
        }

        // Nudges a label out of anything it still overlaps, whose text it would otherwise erase,
        // taking whichever of the four ways out moves it least. Its own two boxes come first, since
        // clearing a bystander must not push it back over them.
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
                foreach (LabelRect box in Obstacles())
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

        IEnumerable<LabelRect> Obstacles()
        {
            yield return LabelRect.Of(source);
            yield return LabelRect.Of(target);
            foreach (PlacedNode node in placement.Nodes)
            {
                if (!ReferenceEquals(node, source) && !ReferenceEquals(node, target))
                {
                    yield return LabelRect.Of(node);
                }
            }

            foreach (LabelRect rect in obstacles)
            {
                yield return rect;
            }
        }
    }

    /// <summary>
    /// Moves a label's centre the shortest axis-aligned distance that takes its rect off a
    /// connector segment, or leaves it where it is when the two do not meet.
    /// </summary>
    private static LayoutPoint PushOffSegment(
        LayoutPoint at,
        double halfWidth,
        double halfHeight,
        LayoutPoint from,
        LayoutPoint to)
    {
        (double, double)? overY = Span(from.X, to.X, from.Y, to.Y, at.X - halfWidth, at.X + halfWidth);
        (double, double)? overX = Span(from.Y, to.Y, from.X, to.X, at.Y - halfHeight, at.Y + halfHeight);
        if (overY is not ({ } lowY, { } highY) || overX is not ({ } lowX, { } highX) ||
            highY <= at.Y - halfHeight || lowY >= at.Y + halfHeight)
        {
            return at;
        }

        // Each candidate puts the whole crossing part of the segment on one side of the rect.
        double down = highY - (at.Y - halfHeight);
        double up = (at.Y + halfHeight) - lowY;
        double right = highX - (at.X - halfWidth);
        double left = (at.X + halfWidth) - lowX;
        double byY = down <= up ? down : -up;
        double byX = right <= left ? right : -left;
        return Math.Abs(byX) <= Math.Abs(byY)
            ? new LayoutPoint(at.X + byX, at.Y)
            : new LayoutPoint(at.X, at.Y + byY);

        // The range the segment's other coordinate covers while this one is inside the band.
        static (double Low, double High)? Span(
            double fromBand,
            double toBand,
            double fromValue,
            double toValue,
            double bandLow,
            double bandHigh)
        {
            if (fromBand == toBand)
            {
                return fromBand < bandLow || fromBand > bandHigh
                    ? null
                    : (Math.Min(fromValue, toValue), Math.Max(fromValue, toValue));
            }

            double first = (bandLow - fromBand) / (toBand - fromBand);
            double second = (bandHigh - fromBand) / (toBand - fromBand);
            double start = Math.Max(0, Math.Min(first, second));
            double end = Math.Min(1, Math.Max(first, second));
            if (start > end)
            {
                return null;
            }

            double low = fromValue + ((toValue - fromValue) * start);
            double high = fromValue + ((toValue - fromValue) * end);
            return (Math.Min(low, high), Math.Max(low, high));
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
