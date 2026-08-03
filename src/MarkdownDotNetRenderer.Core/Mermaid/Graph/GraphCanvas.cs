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
/// <param name="OffsetY">How far the whole drawing is translated down; zero when nothing sits
/// above the margin.</param>
/// <param name="Width">Total width after the shift, including margins.</param>
/// <param name="Height">Total height after the shift, including margins.</param>
public sealed record GraphCanvas(double OffsetX, double OffsetY, double Width, double Height)
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
        double top = margin;
        double bottom = placement.Height - margin;

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
            }

            EdgeLabelAnchors anchors =
                GraphEdgePainter.LabelAnchors(edge, placement, paint, fontSize);
            Include(edge.Edge.Label, anchors.Mid);
            Include(edge.Edge.StartLabel, anchors.Start);
            Include(edge.Edge.EndLabel, anchors.End);
        }

        double offsetX = Math.Max(0, margin - left);
        double offsetY = Math.Max(0, margin - top);
        return new GraphCanvas(
            offsetX,
            offsetY,
            Math.Max(placement.Width, right + offsetX + margin),
            Math.Max(placement.Height, bottom + offsetY + margin));

        void Include(string? text, LayoutPoint? anchor)
        {
            if (string.IsNullOrEmpty(text) || anchor is not { } at)
            {
                return;
            }

            NodeSize box = GraphEdgePainter.LabelBoxSize(text, paint, fontSize);
            left = Math.Min(left, at.X - (box.Width / 2));
            right = Math.Max(right, at.X + (box.Width / 2));
            top = Math.Min(top, at.Y - (box.Height / 2));
            bottom = Math.Max(bottom, at.Y + (box.Height / 2));
        }
    }

    /// <summary>
    /// The smallest layer gap that keeps an edge's decoration clear of the boxes it sits between.
    /// Labels are drawn beside the line, but they still span the gap along the main axis, so it has
    /// to hold a whole backing rect; and a gap has to leave a visible run of connector between the
    /// marker glyphs the two ends carry.
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
            if (edge.SourceId == edge.TargetId)
            {
                continue;
            }

            double insets = edge.StartMarkerInset + edge.EndMarkerInset;
            bool decorated = edge.Label is { Length: > 0 } ||
                edge.StartLabel is { Length: > 0 } || edge.EndLabel is { Length: > 0 };
            if (!decorated && insets == 0)
            {
                continue;
            }

            double needed = insets + MinVisibleConnector;
            if (edge.Label is { Length: > 0 })
            {
                needed = Math.Max(needed, Extent(edge.Label) + (2 * paint.LabelClearance) + insets);
            }

            // Each end label steps in from its endpoint by half its own extent before stepping off
            // the line, so both have to fit between the glyphs.
            double ends = 0;
            if (edge.StartLabel is { Length: > 0 })
            {
                ends += paint.EndLabelGap + (Extent(edge.StartLabel) / 2);
            }

            if (edge.EndLabel is { Length: > 0 })
            {
                ends += paint.EndLabelGap + (Extent(edge.EndLabel) / 2);
            }

            gap = Math.Max(gap, ends == 0 ? needed : Math.Max(needed, insets + ends));
        }

        return gap;

        double Extent(string text)
        {
            NodeSize box = GraphEdgePainter.LabelBoxSize(text, paint, fontSize);
            return direction == FlowDirection.LeftRight ? box.Width : box.Height;
        }
    }

    /// <summary>How much bare connector a labelled or marked edge always shows.</summary>
    private const double MinVisibleConnector = 12;

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
    /// Reserves room to the right of every self-looping box for its loop and label, so the loop is
    /// laid out as part of the box instead of landing on whatever is placed beside it.
    /// </summary>
    /// <param name="nodes">The measured boxes.</param>
    /// <param name="edges">The edges about to be laid out.</param>
    /// <param name="paint">Supplies the bulge, gap, and clearance.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <returns>The boxes, with a reserve on the ones that loop back to themselves.</returns>
    public static IReadOnlyList<GraphNodeSpec> WithSelfLoopReserves(
        IReadOnlyList<GraphNodeSpec> nodes,
        IReadOnlyList<GraphEdgeSpec> edges,
        GraphEdgePaint paint,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var reserves = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (GraphEdgeSpec edge in edges)
        {
            if (edge.SourceId != edge.TargetId)
            {
                continue;
            }

            double reserve = GraphEdgePainter.SelfLoopReserve(edge, paint, fontSize);
            reserves[edge.SourceId] = Math.Max(
                reserves.TryGetValue(edge.SourceId, out double existing) ? existing : 0, reserve);
        }

        if (reserves.Count == 0)
        {
            return nodes;
        }

        var result = new List<GraphNodeSpec>(nodes.Count);
        foreach (GraphNodeSpec node in nodes)
        {
            result.Add(reserves.TryGetValue(node.Id, out double reserve)
                ? node with { RightReserve = reserve }
                : node);
        }

        return result;
    }

    /// <summary>
    /// Opens the group that shifts the drawing right by <see cref="OffsetX"/>, or nothing when no
    /// shift is needed. The caller closes it with <see cref="EndShift"/>.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    public void StartShift(SvgBuilder svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        if (OffsetX > 0 || OffsetY > 0)
        {
            svg.StartElement("g")
                .Attribute(
                    "transform",
                    $"translate({SvgBuilder.Number(OffsetX)} {SvgBuilder.Number(OffsetY)})");
        }
    }

    /// <summary>Closes the group <see cref="StartShift"/> opened, if any.</summary>
    /// <param name="svg">The builder to write to.</param>
    public void EndShift(SvgBuilder svg)
    {
        ArgumentNullException.ThrowIfNull(svg);

        if (OffsetX > 0 || OffsetY > 0)
        {
            svg.EndElement();
        }
    }
}
