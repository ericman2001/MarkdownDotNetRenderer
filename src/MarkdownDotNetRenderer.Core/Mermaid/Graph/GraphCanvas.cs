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
/// The canvas a placement needs once its edge decoration is taken into account. The layered layout
/// sizes itself from node boxes alone, but self-loops bulge past their box and edge labels are as
/// wide as their text, so both can fall outside that size. This records the shift and the width
/// that keep every drawn thing inside the fragment's <c>viewBox</c>.
/// </summary>
/// <param name="OffsetX">How far the whole drawing is translated right; zero when nothing sits
/// left of the margin.</param>
/// <param name="Width">Total width after the shift, including margins.</param>
/// <param name="Height">Total height, which edge decoration never changes.</param>
public sealed record GraphCanvas(double OffsetX, double Width, double Height)
{
    /// <summary>Measures the canvas a placement needs.</summary>
    /// <param name="placement">The placed nodes and routed edges.</param>
    /// <param name="paint">The edge paint the renderer draws with.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="margin">Margin kept around the drawing.</param>
    /// <returns>The shift and size to emit with.</returns>
    public static GraphCanvas Measure(
        GraphPlacement placement,
        GraphEdgePaint paint,
        double fontSize,
        double margin)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(paint);

        // The layered layout already keeps every box inside the margin, so only decoration can
        // push these bounds outwards.
        double left = margin;
        double right = placement.Width - margin;

        foreach (PlacedEdge edge in placement.Edges)
        {
            if (!placement.NodesById.TryGetValue(edge.Edge.SourceId, out PlacedNode? source) ||
                !placement.NodesById.TryGetValue(edge.Edge.TargetId, out PlacedNode? target))
            {
                continue;
            }

            if (edge.IsSelfLoop)
            {
                right = Math.Max(right, GraphEdgePainter.SelfLoopRight(source, paint));
                Include(edge.Edge.Label, GraphEdgePainter.SelfLoopLabelAnchor(source, paint));
                continue;
            }

            List<LayoutPoint> points = GraphEdgePainter.Route(edge, source, target, paint);
            Include(edge.Edge.Label, GraphGeometry.MidPoint(points));

            (LayoutPoint startAnchor, LayoutPoint endAnchor) =
                GraphEdgePainter.EndLabelAnchors(points, paint);
            Include(edge.Edge.StartLabel, startAnchor);
            Include(edge.Edge.EndLabel, endAnchor);
        }

        double offsetX = Math.Max(0, margin - left);
        return new GraphCanvas(
            offsetX,
            Math.Max(placement.Width, right + offsetX + margin),
            placement.Height);

        void Include(string? text, LayoutPoint anchor)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            double half = GraphEdgePainter.LabelBoxSize(text, paint, fontSize).Width / 2;
            left = Math.Min(left, anchor.X - half);
            right = Math.Max(right, anchor.X + half);
        }
    }

    /// <summary>
    /// The smallest layer gap that keeps every mid-edge label clear of the boxes it sits between:
    /// the label is centred on the route, so the gap it spans along the main axis must hold the
    /// whole backing rect plus its clearance. Without this a wide label's opaque rect would erase
    /// the text of the boxes it overlaps.
    /// </summary>
    /// <param name="edges">The edges about to be laid out.</param>
    /// <param name="direction">Which axis layers advance along.</param>
    /// <param name="paint">Supplies the label padding and clearance.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <returns>The required gap in CSS pixels, or zero when no edge carries a label.</returns>
    public static double MinLayerGap(
        IReadOnlyList<GraphEdgeSpec> edges,
        FlowDirection direction,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(paint);

        double gap = 0;
        foreach (GraphEdgeSpec edge in edges)
        {
            if (string.IsNullOrEmpty(edge.Label) || edge.SourceId == edge.TargetId)
            {
                continue;
            }

            NodeSize box = GraphEdgePainter.LabelBoxSize(edge.Label, paint, fontSize);
            double extent = direction == FlowDirection.LeftRight ? box.Width : box.Height;
            gap = Math.Max(gap, extent + (2 * paint.LabelClearance));
        }

        return gap;
    }

    /// <summary>Applies the measured layer gap to a metrics record.</summary>
    /// <param name="metrics">The layout geometry to widen.</param>
    /// <param name="edges">The edges about to be laid out.</param>
    /// <param name="direction">Which axis layers advance along.</param>
    /// <param name="paint">Supplies the label padding and clearance.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <returns>Metrics whose layer gap also fits the widest edge label.</returns>
    public static LayoutMetrics WithLabelledLayerGap(
        LayoutMetrics metrics,
        IReadOnlyList<GraphEdgeSpec> edges,
        FlowDirection direction,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        double gap = MinLayerGap(edges, direction, paint, fontSize);
        return gap <= metrics.LayerGap ? metrics : metrics with { LayerGap = gap };
    }

    /// <summary>
    /// Opens the group that shifts the drawing right by <see cref="OffsetX"/>, or nothing when no
    /// shift is needed. The caller closes it with <see cref="EndShift"/>.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    public void StartShift(SvgBuilder svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        if (OffsetX > 0)
        {
            svg.StartElement("g")
                .Attribute("transform", $"translate({SvgBuilder.Number(OffsetX)} 0)");
        }
    }

    /// <summary>Closes the group <see cref="StartShift"/> opened, if any.</summary>
    /// <param name="svg">The builder to write to.</param>
    public void EndShift(SvgBuilder svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        if (OffsetX > 0)
        {
            svg.EndElement();
        }
    }
}
