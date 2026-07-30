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

namespace MarkdownDotNetRenderer.Mermaid.Flowchart;

/// <summary>Tunable geometry for <see cref="LayeredLayout"/>. All values are CSS pixels.</summary>
/// <param name="LayerGap">Gap between consecutive layers along the main axis.</param>
/// <param name="NodeGap">Gap between neighbours within one layer, along the cross axis.</param>
/// <param name="Margin">Margin around the whole diagram.</param>
/// <param name="MaxNodes">Node-count guard; exceeding it fails the layout with MERMAID004.</param>
/// <param name="MaxEdges">Edge-count guard; exceeding it fails the layout with MERMAID004.</param>
/// <param name="OrderingSweeps">Number of alternating barycenter sweeps; fixed for determinism.</param>
public sealed record LayoutMetrics(
    double LayerGap = 60,
    double NodeGap = 40,
    double Margin = 16,
    int MaxNodes = 500,
    int MaxEdges = 1000,
    int OrderingSweeps = 4)
{
    /// <summary>The defaults documented in docs/04-mermaid-engine.md.</summary>
    public static LayoutMetrics Default { get; } = new();
}

/// <summary>A positioned node. Virtual nodes are zero-size bend points for long edges.</summary>
public sealed class LayoutNode
{
    internal LayoutNode(string id, FlowNode? node, bool isVirtual, double width, double height)
    {
        Id = id;
        Node = node;
        IsVirtual = isVirtual;
        Width = width;
        Height = height;
    }

    /// <summary>Node id; synthetic for virtual nodes.</summary>
    public string Id { get; }

    /// <summary>The parsed node, or <see langword="null"/> for a virtual bend point.</summary>
    public FlowNode? Node { get; }

    /// <summary>Whether this is a virtual bend point rather than a real node.</summary>
    public bool IsVirtual { get; }

    /// <summary>Box width in CSS pixels.</summary>
    public double Width { get; }

    /// <summary>Box height in CSS pixels.</summary>
    public double Height { get; }

    /// <summary>0-based layer index.</summary>
    public int Layer { get; internal set; }

    /// <summary>Box centre on the x axis.</summary>
    public double CenterX { get; internal set; }

    /// <summary>Box centre on the y axis.</summary>
    public double CenterY { get; internal set; }

    /// <summary>Left edge of the box.</summary>
    public double Left => CenterX - (Width / 2);

    /// <summary>Top edge of the box.</summary>
    public double Top => CenterY - (Height / 2);
}

/// <summary>A routed edge: the original edge plus the points its polyline passes through.</summary>
/// <param name="Edge">The parsed edge.</param>
/// <param name="Points">Centre-to-centre route in the edge's own direction; at least two points.</param>
/// <param name="Reversed">Whether the cycle-breaker laid this edge out reversed.</param>
/// <param name="IsSelfLoop">Whether source and target are the same node.</param>
public sealed record LayoutEdge(
    FlowEdge Edge,
    IReadOnlyList<LayoutPoint> Points,
    bool Reversed,
    bool IsSelfLoop);

/// <summary>A point in diagram coordinates.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>The laid-out diagram.</summary>
/// <param name="Direction">The direction the layout was computed for.</param>
/// <param name="Nodes">Real and virtual nodes with final coordinates.</param>
/// <param name="Edges">Routed edges, in source order.</param>
/// <param name="Width">Total diagram width including margins.</param>
/// <param name="Height">Total diagram height including margins.</param>
public sealed record FlowchartLayout(
    FlowDirection Direction,
    IReadOnlyList<LayoutNode> Nodes,
    IReadOnlyList<LayoutEdge> Edges,
    double Width,
    double Height);

/// <summary>The outcome of a layout attempt.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Layout">The layout, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record LayoutResult(bool Success, FlowchartLayout? Layout, string? FailureMessage);

/// <summary>
/// The simplified Sugiyama-style layered layout described in docs/04-mermaid-engine.md:
/// cycle-breaking DFS plus longest-path ranking, barycenter ordering with a fixed sweep count and
/// stable sorts, then coordinate assignment with virtual nodes for long edges. Fully
/// deterministic: no randomness and no iteration counts derived from input size.
/// </summary>
public static class LayeredLayout
{
    /// <summary>Lays out a parsed flowchart.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="sizes">Box size per node id, from text metrics.</param>
    /// <param name="metrics">Geometry and guards.</param>
    /// <returns>The layout, or a failure when a guard tripped.</returns>
    public static LayoutResult Compute(
        FlowchartModel model,
        IReadOnlyDictionary<string, NodeSize> sizes,
        LayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(metrics);

        if (model.Nodes.Count > metrics.MaxNodes)
        {
            return new LayoutResult(
                false,
                null,
                $"The flowchart has {model.Nodes.Count} nodes, above the limit of {metrics.MaxNodes}.");
        }

        if (model.Edges.Count > metrics.MaxEdges)
        {
            return new LayoutResult(
                false,
                null,
                $"The flowchart has {model.Edges.Count} edges, above the limit of {metrics.MaxEdges}.");
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodes = new List<LayoutNode>(model.Nodes.Count);
        foreach (FlowNode node in model.Nodes)
        {
            NodeSize size = sizes.TryGetValue(node.Id, out NodeSize found)
                ? found
                : new NodeSize(80, 36);
            index[node.Id] = nodes.Count;
            nodes.Add(new LayoutNode(node.Id, node, isVirtual: false, size.Width, size.Height));
        }

        int realNodeCount = nodes.Count;
        var oriented = new List<OrientedEdge>(model.Edges.Count);
        for (int i = 0; i < model.Edges.Count; i++)
        {
            FlowEdge edge = model.Edges[i];
            if (!index.TryGetValue(edge.SourceId, out int source) ||
                !index.TryGetValue(edge.TargetId, out int target))
            {
                continue;
            }

            oriented.Add(new OrientedEdge(i, source, target, source == target));
        }

        MarkBackEdges(realNodeCount, oriented);
        int[] layers = AssignLayers(realNodeCount, oriented);

        // Layer buckets keep an explicit order per layer; virtual nodes join them below.
        int layerCount = layers.Length == 0 ? 0 : layers.Max() + 1;
        var layerNodes = new List<List<int>>(layerCount);
        for (int layer = 0; layer < layerCount; layer++)
        {
            layerNodes.Add([]);
        }

        for (int i = 0; i < realNodeCount; i++)
        {
            nodes[i].Layer = layers[i];
            layerNodes[layers[i]].Add(i);
        }

        // Virtual nodes for edges spanning more than one layer.
        var chains = new Dictionary<int, List<int>>();
        var segments = new List<(int From, int To)>();
        foreach (OrientedEdge edge in oriented)
        {
            int from = edge.LayoutSource;
            int to = edge.LayoutTarget;
            if (edge.IsSelfLoop)
            {
                continue;
            }

            int span = layers[to] - layers[from];
            if (span <= 1)
            {
                segments.Add((from, to));
                continue;
            }

            var chain = new List<int>();
            int previous = from;
            for (int layer = layers[from] + 1; layer < layers[to]; layer++)
            {
                var virtualNode = new LayoutNode(
                    $"virtual-{edge.Index}-{layer}", null, isVirtual: true, 0, 0)
                {
                    Layer = layer,
                };
                int virtualIndex = nodes.Count;
                nodes.Add(virtualNode);
                layerNodes[layer].Add(virtualIndex);
                chain.Add(virtualIndex);
                segments.Add((previous, virtualIndex));
                previous = virtualIndex;
            }

            segments.Add((previous, to));
            chains[edge.Index] = chain;
        }

        OrderLayers(nodes, layerNodes, segments, metrics.OrderingSweeps);
        AssignCoordinates(model.Direction, nodes, layerNodes, segments, metrics);

        var routed = new List<LayoutEdge>(model.Edges.Count);
        foreach (OrientedEdge edge in oriented)
        {
            FlowEdge original = model.Edges[edge.Index];
            if (edge.IsSelfLoop)
            {
                LayoutNode self = nodes[edge.LayoutSource];
                routed.Add(new LayoutEdge(
                    original,
                    [new LayoutPoint(self.CenterX, self.CenterY)],
                    edge.Reversed,
                    IsSelfLoop: true));
                continue;
            }

            var points = new List<LayoutPoint>
            {
                Center(nodes[edge.LayoutSource]),
            };

            if (chains.TryGetValue(edge.Index, out List<int>? chain))
            {
                foreach (int virtualIndex in chain)
                {
                    points.Add(Center(nodes[virtualIndex]));
                }
            }

            points.Add(Center(nodes[edge.LayoutTarget]));

            if (edge.Reversed)
            {
                points.Reverse();
            }

            routed.Add(new LayoutEdge(original, points, edge.Reversed, IsSelfLoop: false));
        }

        (double width, double height) = Normalize(nodes, routed, metrics.Margin);
        var layout = new FlowchartLayout(model.Direction, nodes, routed, width, height);
        return new LayoutResult(true, layout, null);
    }

    private static LayoutPoint Center(LayoutNode node) => new(node.CenterX, node.CenterY);

    /// <summary>
    /// Iterative DFS marking back edges as reversed, so ranking sees an acyclic graph. Reversed
    /// edges are drawn with the arrowhead on their original end.
    /// </summary>
    private static void MarkBackEdges(int nodeCount, List<OrientedEdge> edges)
    {
        var outgoing = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            outgoing[i] = [];
        }

        for (int i = 0; i < edges.Count; i++)
        {
            if (!edges[i].IsSelfLoop)
            {
                outgoing[edges[i].Source].Add(i);
            }
        }

        const int White = 0;
        const int Gray = 1;
        const int Black = 2;
        var color = new int[nodeCount];
        var cursor = new int[nodeCount];
        var stack = new List<int>();

        for (int start = 0; start < nodeCount; start++)
        {
            if (color[start] != White)
            {
                continue;
            }

            color[start] = Gray;
            stack.Add(start);
            while (stack.Count > 0)
            {
                int node = stack[^1];
                if (cursor[node] < outgoing[node].Count)
                {
                    int edgeIndex = outgoing[node][cursor[node]++];
                    int next = edges[edgeIndex].Target;
                    if (color[next] == Gray)
                    {
                        edges[edgeIndex] = edges[edgeIndex] with { Reversed = true };
                    }
                    else if (color[next] == White)
                    {
                        color[next] = Gray;
                        stack.Add(next);
                    }
                }
                else
                {
                    color[node] = Black;
                    stack.RemoveAt(stack.Count - 1);
                }
            }
        }
    }

    /// <summary>Longest-path ranking over the acyclic-ified graph, in deterministic order.</summary>
    private static int[] AssignLayers(int nodeCount, List<OrientedEdge> edges)
    {
        var layers = new int[nodeCount];
        var indegree = new int[nodeCount];
        var outgoing = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            outgoing[i] = [];
        }

        foreach (OrientedEdge edge in edges)
        {
            if (edge.IsSelfLoop)
            {
                continue;
            }

            outgoing[edge.LayoutSource].Add(edge.LayoutTarget);
            indegree[edge.LayoutTarget]++;
        }

        // A sorted set as the ready queue keeps the topological order (and therefore the layout)
        // independent of hash ordering.
        var ready = new SortedSet<int>();
        for (int i = 0; i < nodeCount; i++)
        {
            if (indegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        int processed = 0;
        while (ready.Count > 0)
        {
            int node = ready.Min;
            ready.Remove(node);
            processed++;
            foreach (int next in outgoing[node])
            {
                layers[next] = Math.Max(layers[next], layers[node] + 1);
                if (--indegree[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        if (processed == nodeCount)
        {
            return layers;
        }

        // Defensive: a residual cycle (should not happen after back-edge marking) is broken by
        // ranking the remaining nodes after their already-ranked predecessors.
        for (int i = 0; i < nodeCount; i++)
        {
            if (indegree[i] > 0)
            {
                indegree[i] = 0;
                foreach (OrientedEdge edge in edges)
                {
                    if (!edge.IsSelfLoop && edge.LayoutTarget == i)
                    {
                        layers[i] = Math.Max(layers[i], layers[edge.LayoutSource] + 1);
                    }
                }
            }
        }

        return layers;
    }

    private static void OrderLayers(
        List<LayoutNode> nodes,
        List<List<int>> layerNodes,
        List<(int From, int To)> segments,
        int sweeps)
    {
        var predecessors = new List<int>[nodes.Count];
        var successors = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            predecessors[i] = [];
            successors[i] = [];
        }

        foreach ((int from, int to) in segments)
        {
            successors[from].Add(to);
            predecessors[to].Add(from);
        }

        var position = new int[nodes.Count];
        UpdatePositions(layerNodes, position);

        for (int sweep = 0; sweep < sweeps; sweep++)
        {
            bool downward = sweep % 2 == 0;
            if (downward)
            {
                for (int layer = 1; layer < layerNodes.Count; layer++)
                {
                    SortLayer(layerNodes[layer], predecessors, position);
                }
            }
            else
            {
                for (int layer = layerNodes.Count - 2; layer >= 0; layer--)
                {
                    SortLayer(layerNodes[layer], successors, position);
                }
            }

            UpdatePositions(layerNodes, position);
        }
    }

    private static void SortLayer(List<int> layer, List<int>[] neighbours, int[] position)
    {
        var keyed = new List<(int Node, double Key, int Tie)>(layer.Count);
        for (int i = 0; i < layer.Count; i++)
        {
            int node = layer[i];
            List<int> adjacent = neighbours[node];
            double key = i;
            if (adjacent.Count > 0)
            {
                double sum = 0;
                foreach (int neighbour in adjacent)
                {
                    sum += position[neighbour];
                }

                key = sum / adjacent.Count;
            }

            keyed.Add((node, key, i));
        }

        // OrderBy is a stable sort; the explicit tie-break makes that explicit rather than implied.
        int slot = 0;
        foreach ((int node, _, _) in keyed.OrderBy(k => k.Key).ThenBy(k => k.Tie))
        {
            layer[slot++] = node;
        }
    }

    private static void UpdatePositions(List<List<int>> layerNodes, int[] position)
    {
        foreach (List<int> layer in layerNodes)
        {
            for (int i = 0; i < layer.Count; i++)
            {
                position[layer[i]] = i;
            }
        }
    }

    private static void AssignCoordinates(
        FlowDirection direction,
        List<LayoutNode> nodes,
        List<List<int>> layerNodes,
        List<(int From, int To)> segments,
        LayoutMetrics metrics)
    {
        bool topDown = direction == FlowDirection.TopDown;
        var cross = new double[nodes.Count];
        var main = new double[nodes.Count];

        double CrossExtent(LayoutNode node) => topDown ? node.Width : node.Height;
        double MainExtent(LayoutNode node) => topDown ? node.Height : node.Width;

        // Cross axis: pack each layer, then centre the layers against the widest one.
        double widest = 0;
        var layerExtents = new double[layerNodes.Count];
        for (int layer = 0; layer < layerNodes.Count; layer++)
        {
            double running = 0;
            foreach (int nodeIndex in layerNodes[layer])
            {
                double extent = CrossExtent(nodes[nodeIndex]);
                cross[nodeIndex] = running + (extent / 2);
                running += extent + metrics.NodeGap;
            }

            layerExtents[layer] = Math.Max(0, running - metrics.NodeGap);
            widest = Math.Max(widest, layerExtents[layer]);
        }

        for (int layer = 0; layer < layerNodes.Count; layer++)
        {
            double shift = (widest - layerExtents[layer]) / 2;
            foreach (int nodeIndex in layerNodes[layer])
            {
                cross[nodeIndex] += shift;
            }
        }

        // Main axis: layer origins are the running sum of layer extents plus a fixed gap.
        double offset = 0;
        for (int layer = 0; layer < layerNodes.Count; layer++)
        {
            double extent = 0;
            foreach (int nodeIndex in layerNodes[layer])
            {
                extent = Math.Max(extent, MainExtent(nodes[nodeIndex]));
            }

            foreach (int nodeIndex in layerNodes[layer])
            {
                main[nodeIndex] = offset + (extent / 2);
            }

            offset += extent + metrics.LayerGap;
        }

        StraightenChains(nodes, layerNodes, segments, cross, CrossExtent, metrics.NodeGap);

        foreach (List<int> layer in layerNodes)
        {
            foreach (int nodeIndex in layer)
            {
                LayoutNode node = nodes[nodeIndex];
                node.CenterX = topDown ? cross[nodeIndex] : main[nodeIndex];
                node.CenterY = topDown ? main[nodeIndex] : cross[nodeIndex];
            }
        }
    }

    /// <summary>
    /// Nudges each node toward the average cross coordinate of its neighbours, clamped by its
    /// in-layer neighbours' current positions so no overlap can be introduced.
    /// </summary>
    private static void StraightenChains(
        List<LayoutNode> nodes,
        List<List<int>> layerNodes,
        List<(int From, int To)> segments,
        double[] cross,
        Func<LayoutNode, double> crossExtent,
        double gap)
    {
        var predecessors = new List<int>[nodes.Count];
        var successors = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            predecessors[i] = [];
            successors[i] = [];
        }

        foreach ((int from, int to) in segments)
        {
            successors[from].Add(to);
            predecessors[to].Add(from);
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool downward = pass == 0;
            for (int step = 0; step < layerNodes.Count; step++)
            {
                int layerIndex = downward ? step : layerNodes.Count - 1 - step;
                List<int> layer = layerNodes[layerIndex];
                for (int i = 0; i < layer.Count; i++)
                {
                    int nodeIndex = layer[i];
                    List<int> adjacent = downward
                        ? predecessors[nodeIndex]
                        : successors[nodeIndex];
                    if (adjacent.Count == 0)
                    {
                        continue;
                    }

                    double sum = 0;
                    foreach (int neighbour in adjacent)
                    {
                        sum += cross[neighbour];
                    }

                    double desired = sum / adjacent.Count;
                    double half = crossExtent(nodes[nodeIndex]) / 2;
                    double lower = double.NegativeInfinity;
                    double upper = double.PositiveInfinity;
                    if (i > 0)
                    {
                        int previous = layer[i - 1];
                        lower = cross[previous] + (crossExtent(nodes[previous]) / 2) + gap + half;
                    }

                    if (i < layer.Count - 1)
                    {
                        int next = layer[i + 1];
                        upper = cross[next] - (crossExtent(nodes[next]) / 2) - gap - half;
                    }

                    if (lower > upper)
                    {
                        continue;
                    }

                    cross[nodeIndex] = Math.Clamp(desired, lower, upper);
                }
            }
        }
    }

    private static (double Width, double Height) Normalize(
        List<LayoutNode> nodes,
        List<LayoutEdge> edges,
        double margin)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach (LayoutNode node in nodes)
        {
            minX = Math.Min(minX, node.CenterX - (node.Width / 2));
            minY = Math.Min(minY, node.CenterY - (node.Height / 2));
            maxX = Math.Max(maxX, node.CenterX + (node.Width / 2));
            maxY = Math.Max(maxY, node.CenterY + (node.Height / 2));
        }

        if (double.IsInfinity(minX))
        {
            return (margin * 2, margin * 2);
        }

        double shiftX = margin - minX;
        double shiftY = margin - minY;

        foreach (LayoutNode node in nodes)
        {
            node.CenterX += shiftX;
            node.CenterY += shiftY;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            LayoutEdge edge = edges[i];
            var moved = new List<LayoutPoint>(edge.Points.Count);
            foreach (LayoutPoint point in edge.Points)
            {
                moved.Add(new LayoutPoint(point.X + shiftX, point.Y + shiftY));
            }

            edges[i] = edge with { Points = moved };
        }

        return (maxX - minX + (margin * 2), maxY - minY + (margin * 2));
    }

    private readonly record struct OrientedEdge(int Index, int Source, int Target, bool IsSelfLoop)
    {
        public bool Reversed { get; init; }

        /// <summary>The source used for ranking and routing, after cycle breaking.</summary>
        public int LayoutSource => Reversed ? Target : Source;

        /// <summary>The target used for ranking and routing, after cycle breaking.</summary>
        public int LayoutTarget => Reversed ? Source : Target;
    }
}

/// <summary>A node's box size in CSS pixels.</summary>
/// <param name="Width">Box width.</param>
/// <param name="Height">Box height.</param>
public readonly record struct NodeSize(double Width, double Height);
