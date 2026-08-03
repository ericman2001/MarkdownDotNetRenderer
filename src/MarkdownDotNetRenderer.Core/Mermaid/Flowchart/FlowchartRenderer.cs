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

using System.Globalization;
using System.Text;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Flowchart;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>flowchart</c> and <c>graph</c> diagrams: parse, lay
/// out with <see cref="LayeredLayout"/>, and emit a self-contained SVG fragment whose visual
/// attributes are all inline, so it renders identically inlined in HTML or stored as a standalone
/// SVG part.
/// </summary>
public sealed class FlowchartRenderer : IDiagramRenderer
{
    private readonly LayoutMetrics _metrics;
    private readonly DiagramTheme _theme;

    /// <summary>Creates a renderer with the default layout geometry and theme.</summary>
    public FlowchartRenderer()
        : this(LayoutMetrics.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and guards.</summary>
    /// <param name="metrics">Geometry and guards to use.</param>
    public FlowchartRenderer(LayoutMetrics metrics)
        : this(metrics, DiagramTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and visual theme.</summary>
    /// <param name="metrics">Geometry and guards to use.</param>
    /// <param name="theme">Colours, stroke widths, and box geometry to use.</param>
    public FlowchartRenderer(LayoutMetrics metrics, DiagramTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } = ["flowchart", "graph"];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        FlowchartParseResult parsed;
        try
        {
            parsed = FlowchartParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The flowchart source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The flowchart source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        FlowchartModel model = parsed.Model;
        double fontSize = options.DiagramFontSize <= 0 ? 12 : options.DiagramFontSize;
        Dictionary<string, NodeSize> sizes = MeasureNodes(model, fontSize, _theme);

        LayoutResult layout = LayeredLayout.Compute(model, sizes, _metrics);
        if (!layout.Success || layout.Layout is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramTooLarge,
                $"The flowchart was not laid out: {layout.FailureMessage}",
                parsed.Diagnostics);
        }

        string idPrefix = DiagramIds.ForSource(mermaidSource);
        string altText =
            $"flowchart with {model.Nodes.Count} nodes and {model.Edges.Count} edges";
        // Self-loops are drawn beside their node, which the node-box-based layout cannot know about.
        double width = Math.Max(
            layout.Layout.Width,
            SelfLoopContentRight(layout.Layout, fontSize) + _metrics.Margin);
        string svg = Emit(layout.Layout, model, options, fontSize, idPrefix, altText, width);

        return new DiagramRenderResult(
            true,
            svg,
            width,
            layout.Layout.Height,
            altText,
            parsed.Diagnostics);
    }

    /// <summary>Estimates each node's box size from its wrapped label.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="theme">Padding, minimum box size, and label wrapping; defaults to
    /// <see cref="DiagramTheme.Default"/>.</param>
    /// <returns>Box sizes keyed by node id.</returns>
    public static Dictionary<string, NodeSize> MeasureNodes(
        FlowchartModel model,
        double fontSize,
        DiagramTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        theme ??= DiagramTheme.Default;

        var sizes = new Dictionary<string, NodeSize>(StringComparer.Ordinal);
        foreach (FlowNode node in model.Nodes)
        {
            IReadOnlyList<string> lines = TextMetrics.WrapLabel(node.Label, theme.LabelWrapChars);
            double textWidth = 0;
            foreach (string line in lines)
            {
                textWidth = Math.Max(textWidth, TextMetrics.MeasureWidth(line, fontSize));
            }

            double width = Math.Max(theme.MinNodeWidth, textWidth + theme.HorizontalPadding);
            double height = Math.Max(
                theme.MinNodeHeight,
                (lines.Count * TextMetrics.LineHeight(fontSize)) + theme.VerticalPadding);

            switch (node.Shape)
            {
                case FlowNodeShape.Rhombus:
                    // A diamond only contains its inscribed rectangle, so it needs to be bigger.
                    width *= 1.5;
                    height *= 1.6;
                    break;
                case FlowNodeShape.Stadium:
                    width += height / 2;
                    break;
                case FlowNodeShape.Rectangle:
                case FlowNodeShape.Rounded:
                default:
                    break;
            }

            sizes[node.Id] = new NodeSize(width, height);
        }

        return sizes;
    }

    /// <summary>
    /// The rightmost x any self-loop curve or self-loop label reaches, or 0 without self-loops.
    /// </summary>
    private double SelfLoopContentRight(FlowchartLayout layout, double fontSize)
    {
        double right = 0;
        foreach (LayoutEdge edge in layout.Edges)
        {
            if (!edge.IsSelfLoop)
            {
                continue;
            }

            LayoutNode? source = FindNode(layout, edge.Edge.SourceId);
            if (source is null)
            {
                continue;
            }

            right = Math.Max(right, SelfLoopRight(source));
            if (!string.IsNullOrEmpty(edge.Edge.Label))
            {
                double boxWidth = LabelBoxWidth(edge.Edge.Label, fontSize);
                right = Math.Max(right, SelfLoopLabelAnchorX(source, boxWidth) + (boxWidth / 2));
            }
        }

        return right;
    }

    private static LayoutNode? FindNode(FlowchartLayout layout, string id)
    {
        foreach (LayoutNode node in layout.Nodes)
        {
            if (!node.IsVirtual && string.Equals(node.Id, id, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>
    /// The rightmost x of a self-loop curve. Both control points sit at <c>bulge</c> past the node,
    /// and a cubic with equal control x reaches three quarters of that offset at its midpoint.
    /// </summary>
    private double SelfLoopRight(LayoutNode source) =>
        source.Left + source.Width + (_theme.SelfLoopBulge * 0.75);

    private double SelfLoopLabelAnchorX(LayoutNode source, double labelBoxWidth) =>
        SelfLoopRight(source) + _theme.SelfLoopLabelGap + (labelBoxWidth / 2);

    private static double LabelBoxWidth(string label, double fontSize) =>
        TextMetrics.MeasureWidth(label, fontSize) + 8;

    private string Emit(
        FlowchartLayout layout,
        FlowchartModel model,
        RenderOptions options,
        double fontSize,
        string idPrefix,
        string altText,
        double width)
    {
        string arrowId = $"{idPrefix}-arrow";
        var svg = new SvgBuilder();

        double height = layout.Height;
        double renderWidth = width;
        double renderHeight = height;
        if (options.MaxDiagramWidth > 0 && width > options.MaxDiagramWidth)
        {
            // Keep the viewBox intrinsic and clamp the presented size, so the consumer scales the
            // diagram down instead of clipping it.
            renderWidth = options.MaxDiagramWidth;
            renderHeight = height * (options.MaxDiagramWidth / width);
        }

        svg.StartElement("svg")
            .Attribute("xmlns", SvgBuilder.SvgNamespace)
            .Attribute("width", renderWidth)
            .Attribute("height", renderHeight)
            .Attribute("viewBox", $"0 0 {SvgBuilder.Number(width)} {SvgBuilder.Number(height)}")
            .Attribute("role", "img")
            .Attribute("aria-label", altText)
            .Attribute("class", "mdnr-flowchart");

        EmitDefs(svg, arrowId);

        var nodesById = new Dictionary<string, LayoutNode>(StringComparer.Ordinal);
        foreach (LayoutNode node in layout.Nodes)
        {
            if (!node.IsVirtual)
            {
                nodesById[node.Id] = node;
            }
        }

        // Edges first, then nodes (opaque fills cover grazing edges), then labels on top.
        svg.StartElement("g").Attribute("class", "mdnr-edges");
        foreach (LayoutEdge edge in layout.Edges)
        {
            EmitEdge(svg, edge, nodesById, arrowId);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-nodes");
        foreach (FlowNode node in model.Nodes)
        {
            if (nodesById.TryGetValue(node.Id, out LayoutNode? placed))
            {
                EmitNode(svg, node, placed, options, fontSize);
            }
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-edge-labels");
        foreach (LayoutEdge edge in layout.Edges)
        {
            EmitEdgeLabel(svg, edge, nodesById, options, fontSize);
        }

        svg.EndElement();

        svg.EndElement();
        return svg.ToString();
    }

    private void EmitDefs(SvgBuilder svg, string arrowId)
    {
        svg.StartElement("defs");
        svg.StartElement("marker")
            .Attribute("id", arrowId)
            .Attribute("viewBox", "0 0 10 10")
            .Attribute("refX", 9.0)
            .Attribute("refY", 5.0)
            .Attribute("markerWidth", 7.0)
            .Attribute("markerHeight", 7.0)
            .Attribute("orient", "auto-start-reverse");
        svg.StartElement("path")
            .Attribute("d", "M 0 0 L 10 5 L 0 10 z")
            .Attribute("fill", _theme.EdgeStroke)
            .EndElement();
        svg.EndElement();
        svg.EndElement();
    }

    private void EmitNode(
        SvgBuilder svg,
        FlowNode node,
        LayoutNode placed,
        RenderOptions options,
        double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", "mdnr-node")
            .Attribute("data-id", node.Id);

        switch (node.Shape)
        {
            case FlowNodeShape.Rhombus:
                svg.StartElement("polygon")
                    .Attribute("points", string.Join(
                        ' ',
                        Point(placed.CenterX, placed.Top),
                        Point(placed.Left + placed.Width, placed.CenterY),
                        Point(placed.CenterX, placed.Top + placed.Height),
                        Point(placed.Left, placed.CenterY)))
                    .Attribute("fill", _theme.NodeFill)
                    .Attribute("stroke", _theme.NodeStroke)
                    .Attribute("stroke-width", _theme.NodeStrokeWidth)
                    .EndElement();
                break;

            case FlowNodeShape.Rectangle:
            case FlowNodeShape.Rounded:
            case FlowNodeShape.Stadium:
            default:
                double radius = node.Shape switch
                {
                    FlowNodeShape.Stadium => placed.Height / 2,
                    FlowNodeShape.Rounded => 8,
                    _ => 2,
                };

                svg.StartElement("rect")
                    .Attribute("x", placed.Left)
                    .Attribute("y", placed.Top)
                    .Attribute("width", placed.Width)
                    .Attribute("height", placed.Height)
                    .Attribute("rx", radius)
                    .Attribute("ry", radius)
                    .Attribute("fill", _theme.NodeFill)
                    .Attribute("stroke", _theme.NodeStroke)
                    .Attribute("stroke-width", _theme.NodeStrokeWidth)
                    .EndElement();
                break;
        }

        EmitLabel(
            svg,
            node.Label,
            placed.CenterX,
            placed.CenterY,
            options,
            fontSize,
            _theme.TextFill);
        svg.EndElement();
    }

    private void EmitLabel(
        SvgBuilder svg,
        string label,
        double centerX,
        double centerY,
        RenderOptions options,
        double fontSize,
        string fill)
    {
        IReadOnlyList<string> lines = TextMetrics.WrapLabel(label, _theme.LabelWrapChars);
        double lineHeight = TextMetrics.LineHeight(fontSize);

        svg.StartElement("text")
            .Attribute("x", centerX)
            .Attribute("y", centerY)
            .Attribute("text-anchor", "middle")
            .Attribute("dominant-baseline", "middle")
            .Attribute("font-family", options.FontFamily)
            .Attribute("font-size", fontSize)
            .Attribute("fill", fill);

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

    private void EmitEdge(
        SvgBuilder svg,
        LayoutEdge edge,
        Dictionary<string, LayoutNode> nodesById,
        string arrowId)
    {
        if (!nodesById.TryGetValue(edge.Edge.SourceId, out LayoutNode? source) ||
            !nodesById.TryGetValue(edge.Edge.TargetId, out LayoutNode? target))
        {
            return;
        }

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge")
            .Attribute("data-source", edge.Edge.SourceId)
            .Attribute("data-target", edge.Edge.TargetId);

        if (edge.IsSelfLoop)
        {
            // Control points are offset horizontally only, so the loop stays within the node's own
            // vertical band and can never overflow the canvas top or bottom.
            double loopX = source.Left + source.Width;
            double loopStartY = source.CenterY - (source.Height / 4);
            double loopEndY = source.CenterY + (source.Height / 4);
            double controlX = loopX + _theme.SelfLoopBulge;
            string path =
                $"M {SvgBuilder.Number(loopX)} {SvgBuilder.Number(loopStartY)} " +
                $"C {SvgBuilder.Number(controlX)} {SvgBuilder.Number(loopStartY)} " +
                $"{SvgBuilder.Number(controlX)} {SvgBuilder.Number(loopEndY)} " +
                $"{SvgBuilder.Number(loopX)} {SvgBuilder.Number(loopEndY)}";
            EmitEdgeGeometry(svg, path, edge.Edge.Directed, arrowId);
            svg.EndElement();
            return;
        }

        List<LayoutPoint> points = Route(edge, source, target);

        if (points.Count == 2)
        {
            svg.StartElement("line")
                .Attribute("x1", points[0].X)
                .Attribute("y1", points[0].Y)
                .Attribute("x2", points[1].X)
                .Attribute("y2", points[1].Y)
                .Attribute("stroke", _theme.EdgeStroke)
                .Attribute("stroke-width", _theme.EdgeStrokeWidth)
                .Attribute("fill", "none");
            if (edge.Edge.Directed)
            {
                svg.Attribute("marker-end", $"url(#{arrowId})");
            }

            svg.EndElement();
        }
        else
        {
            EmitEdgeGeometry(svg, BuildPath(points), edge.Edge.Directed, arrowId);
        }

        svg.EndElement();
    }

    private void EmitEdgeGeometry(SvgBuilder svg, string path, bool directed, string arrowId)
    {
        svg.StartElement("path")
            .Attribute("d", path)
            .Attribute("stroke", _theme.EdgeStroke)
            .Attribute("stroke-width", _theme.EdgeStrokeWidth)
            .Attribute("fill", "none");
        if (directed)
        {
            svg.Attribute("marker-end", $"url(#{arrowId})");
        }

        svg.EndElement();
    }

    private void EmitEdgeLabel(
        SvgBuilder svg,
        LayoutEdge edge,
        Dictionary<string, LayoutNode> nodesById,
        RenderOptions options,
        double fontSize)
    {
        if (string.IsNullOrEmpty(edge.Edge.Label) ||
            !nodesById.TryGetValue(edge.Edge.SourceId, out LayoutNode? source) ||
            !nodesById.TryGetValue(edge.Edge.TargetId, out LayoutNode? target))
        {
            return;
        }

        double boxWidth = LabelBoxWidth(edge.Edge.Label, fontSize);
        double boxHeight = TextMetrics.LineHeight(fontSize) + 4;

        // Anchor on the clipped route so a short edge's label cannot land inside a node box.
        LayoutPoint anchor = edge.IsSelfLoop
            ? new LayoutPoint(SelfLoopLabelAnchorX(source, boxWidth), source.CenterY)
            : LabelAnchor(Route(edge, source, target));

        svg.StartElement("g")
            .Attribute("class", "mdnr-edge-label")
            .Attribute("data-source", edge.Edge.SourceId)
            .Attribute("data-target", edge.Edge.TargetId);

        svg.StartElement("rect")
            .Attribute("x", anchor.X - (boxWidth / 2))
            .Attribute("y", anchor.Y - (boxHeight / 2))
            .Attribute("width", boxWidth)
            .Attribute("height", boxHeight)
            .Attribute("rx", 3.0)
            .Attribute("ry", 3.0)
            .Attribute("fill", _theme.NodeFill)
            .Attribute("stroke", "none")
            .EndElement();

        svg.StartElement("text")
            .Attribute("x", anchor.X)
            .Attribute("y", anchor.Y)
            .Attribute("text-anchor", "middle")
            .Attribute("dominant-baseline", "middle")
            .Attribute("font-family", options.FontFamily)
            .Attribute("font-size", fontSize)
            .Attribute("fill", _theme.TextFill)
            .Text(edge.Edge.Label)
            .EndElement();

        svg.EndElement();
    }

    /// <summary>Clips an edge's centre-to-centre route to the two shapes it connects.</summary>
    private List<LayoutPoint> Route(LayoutEdge edge, LayoutNode source, LayoutNode target)
    {
        var points = new List<LayoutPoint>(edge.Points);
        points[0] = ClipToShape(source, points[1]);
        LayoutPoint lastInner = points[^2];
        LayoutPoint endpoint = ClipToShape(target, lastInner);
        if (edge.Edge.Directed)
        {
            endpoint = PullBack(endpoint, lastInner, _theme.ArrowInset);
        }

        points[^1] = endpoint;
        return points;
    }

    private static LayoutPoint LabelAnchor(IReadOnlyList<LayoutPoint> points)
    {
        if (points.Count == 1)
        {
            return points[0];
        }

        if (points.Count == 2)
        {
            return new LayoutPoint(
                (points[0].X + points[1].X) / 2,
                (points[0].Y + points[1].Y) / 2);
        }

        return points[points.Count / 2];
    }

    private string BuildPath(IReadOnlyList<LayoutPoint> points)
    {
        var path = new StringBuilder();
        path.Append("M ").Append(SvgBuilder.Number(points[0].X)).Append(' ')
            .Append(SvgBuilder.Number(points[0].Y));

        for (int i = 1; i < points.Count - 1; i++)
        {
            LayoutPoint previous = points[i - 1];
            LayoutPoint current = points[i];
            LayoutPoint next = points[i + 1];

            LayoutPoint approach = Along(current, previous, _theme.CornerRadius);
            LayoutPoint leave = Along(current, next, _theme.CornerRadius);

            path.Append(" L ").Append(SvgBuilder.Number(approach.X)).Append(' ')
                .Append(SvgBuilder.Number(approach.Y));
            path.Append(" Q ").Append(SvgBuilder.Number(current.X)).Append(' ')
                .Append(SvgBuilder.Number(current.Y)).Append(' ')
                .Append(SvgBuilder.Number(leave.X)).Append(' ')
                .Append(SvgBuilder.Number(leave.Y));
        }

        path.Append(" L ").Append(SvgBuilder.Number(points[^1].X)).Append(' ')
            .Append(SvgBuilder.Number(points[^1].Y));
        return path.ToString();
    }

    private static LayoutPoint Along(LayoutPoint from, LayoutPoint toward, double distance)
    {
        double dx = toward.X - from.X;
        double dy = toward.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= double.Epsilon)
        {
            return from;
        }

        double step = Math.Min(distance, length / 2);
        return new LayoutPoint(from.X + (dx / length * step), from.Y + (dy / length * step));
    }

    private static LayoutPoint PullBack(LayoutPoint point, LayoutPoint toward, double distance) =>
        Along(point, toward, distance);

    /// <summary>Clips a centre-to-centre ray to the boundary of a node's shape.</summary>
    private static LayoutPoint ClipToShape(LayoutNode node, LayoutPoint toward)
    {
        double dx = toward.X - node.CenterX;
        double dy = toward.Y - node.CenterY;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            return new LayoutPoint(node.CenterX, node.CenterY);
        }

        double halfWidth = node.Width / 2;
        double halfHeight = node.Height / 2;
        double scale;

        if (node.Node?.Shape == FlowNodeShape.Rhombus)
        {
            // |x| / halfWidth + |y| / halfHeight = 1 on a diamond boundary.
            scale = 1 / ((Math.Abs(dx) / halfWidth) + (Math.Abs(dy) / halfHeight));
        }
        else
        {
            double scaleX = Math.Abs(dx) < 1e-9 ? double.PositiveInfinity : halfWidth / Math.Abs(dx);
            double scaleY = Math.Abs(dy) < 1e-9 ? double.PositiveInfinity : halfHeight / Math.Abs(dy);
            scale = Math.Min(scaleX, scaleY);
        }

        return new LayoutPoint(node.CenterX + (dx * scale), node.CenterY + (dy * scale));
    }

    private static string Point(double x, double y) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{SvgBuilder.Number(x)},{SvgBuilder.Number(y)}");
}
