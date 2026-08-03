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

namespace MarkdownDotNetRenderer.Mermaid.Graph;

/// <summary>How an edge line is stroked.</summary>
public enum GraphLineStyle
{
    /// <summary>A continuous line.</summary>
    Solid,

    /// <summary>A dashed line, e.g. a class-diagram dependency or a state note link.</summary>
    Dashed,
}

/// <summary>A box to place: its identity, its measured size, and its boundary shape.</summary>
/// <param name="Id">Unique node id within the diagram.</param>
/// <param name="Width">Box width in CSS pixels.</param>
/// <param name="Height">Box height in CSS pixels.</param>
/// <param name="Shape">Boundary shape used when clipping edge endpoints.</param>
public sealed record GraphNodeSpec(string Id, double Width, double Height, ClipShape Shape);

/// <summary>A connection to place, with the markers each end carries.</summary>
/// <param name="SourceId">Id of the source node; the layout ranks it above the target.</param>
/// <param name="TargetId">Id of the target node.</param>
/// <param name="Label">Mid-edge label, or <see langword="null"/>.</param>
/// <param name="Line">How the line is stroked.</param>
/// <param name="StartMarkerId">Marker id drawn at the source end, or <see langword="null"/>.</param>
/// <param name="EndMarkerId">Marker id drawn at the target end, or <see langword="null"/>.</param>
/// <param name="StartLabel">Small label near the source end (a cardinality), or <see langword="null"/>.</param>
/// <param name="EndLabel">Small label near the target end (a cardinality), or <see langword="null"/>.</param>
public sealed record GraphEdgeSpec(
    string SourceId,
    string TargetId,
    string? Label = null,
    GraphLineStyle Line = GraphLineStyle.Solid,
    string? StartMarkerId = null,
    string? EndMarkerId = null,
    string? StartLabel = null,
    string? EndLabel = null);

/// <summary>A placed box.</summary>
/// <param name="Id">The node id.</param>
/// <param name="CenterX">Box centre on the x axis.</param>
/// <param name="CenterY">Box centre on the y axis.</param>
/// <param name="Width">Box width.</param>
/// <param name="Height">Box height.</param>
/// <param name="Shape">Boundary shape used when clipping edge endpoints.</param>
public sealed record PlacedNode(
    string Id,
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    ClipShape Shape)
{
    /// <summary>Left edge of the box.</summary>
    public double Left => CenterX - (Width / 2);

    /// <summary>Top edge of the box.</summary>
    public double Top => CenterY - (Height / 2);
}

/// <summary>A placed connection: its spec plus the centre-to-centre route it follows.</summary>
/// <param name="Edge">The edge spec.</param>
/// <param name="Points">Route from source to target; at least two points.</param>
/// <param name="IsSelfLoop">Whether source and target are the same node.</param>
public sealed record PlacedEdge(
    GraphEdgeSpec Edge,
    IReadOnlyList<LayoutPoint> Points,
    bool IsSelfLoop);

/// <summary>A laid-out graph-shaped diagram.</summary>
/// <param name="Nodes">Placed nodes, in the order they were supplied.</param>
/// <param name="Edges">Placed edges, in the order they were supplied.</param>
/// <param name="Width">Total width including margins.</param>
/// <param name="Height">Total height including margins.</param>
public sealed record GraphPlacement(
    IReadOnlyList<PlacedNode> Nodes,
    IReadOnlyList<PlacedEdge> Edges,
    double Width,
    double Height)
{
    /// <summary>The placed nodes keyed by id.</summary>
    public IReadOnlyDictionary<string, PlacedNode> NodesById { get; } =
        Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
}

/// <summary>The outcome of a graph placement attempt.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Placement">The placement, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record GraphPlacementResult(
    bool Success,
    GraphPlacement? Placement,
    string? FailureMessage);

/// <summary>
/// Lets a non-flowchart diagram type reuse <see cref="LayeredLayout"/> without inventing its own
/// ranking or ordering: the caller supplies measured boxes and connections, this maps them onto the
/// layered layout's input, and maps the result back onto shape-agnostic placements. State, class,
/// and ER diagrams all place their boxes through here (docs/04-mermaid-engine.md).
/// </summary>
public static class GraphLayoutAdapter
{
    /// <summary>Places boxes and routes connections with the shared layered layout.</summary>
    /// <param name="nodes">The boxes to place; ids must be unique.</param>
    /// <param name="edges">The connections; endpoints naming unknown nodes are dropped.</param>
    /// <param name="direction">Which axis layers advance along.</param>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <returns>The placement, or a failure when a guard tripped.</returns>
    public static GraphPlacementResult Compute(
        IReadOnlyList<GraphNodeSpec> nodes,
        IReadOnlyList<GraphEdgeSpec> edges,
        FlowDirection direction,
        LayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(metrics);

        var shapes = new Dictionary<string, ClipShape>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, NodeSize>(StringComparer.Ordinal);
        var flowNodes = new List<FlowNode>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNodeSpec node = nodes[i];
            shapes[node.Id] = node.Shape;
            sizes[node.Id] = new NodeSize(node.Width, node.Height);
            // The label is empty because this adapter only borrows the layout, never the painting:
            // each diagram type draws its own box contents from its own model.
            flowNodes.Add(new FlowNode(node.Id, string.Empty, FlowNodeShape.Rectangle, i));
        }

        // Edges naming an unknown node are dropped here rather than by the layout, so the routed
        // results line up one-for-one with the specs they came from.
        var usable = new List<GraphEdgeSpec>(edges.Count);
        var flowEdges = new List<FlowEdge>(edges.Count);
        foreach (GraphEdgeSpec edge in edges)
        {
            if (!sizes.ContainsKey(edge.SourceId) || !sizes.ContainsKey(edge.TargetId))
            {
                continue;
            }

            usable.Add(edge);
            flowEdges.Add(new FlowEdge(edge.SourceId, edge.TargetId, edge.Label, Directed: true));
        }

        LayoutResult result = LayeredLayout.Compute(
            new FlowchartModel(direction, flowNodes, flowEdges), sizes, metrics);
        if (!result.Success || result.Layout is null)
        {
            return new GraphPlacementResult(false, null, result.FailureMessage);
        }

        var placedNodes = new List<PlacedNode>(nodes.Count);
        foreach (LayoutNode node in result.Layout.Nodes)
        {
            if (node.IsVirtual)
            {
                continue;
            }

            placedNodes.Add(new PlacedNode(
                node.Id,
                node.CenterX,
                node.CenterY,
                node.Width,
                node.Height,
                shapes.TryGetValue(node.Id, out ClipShape shape) ? shape : ClipShape.Box));
        }

        var placedEdges = new List<PlacedEdge>(result.Layout.Edges.Count);
        for (int i = 0; i < result.Layout.Edges.Count; i++)
        {
            LayoutEdge routed = result.Layout.Edges[i];
            placedEdges.Add(new PlacedEdge(usable[i], routed.Points, routed.IsSelfLoop));
        }

        return new GraphPlacementResult(
            true,
            new GraphPlacement(
                placedNodes, placedEdges, result.Layout.Width, result.Layout.Height),
            null);
    }
}
