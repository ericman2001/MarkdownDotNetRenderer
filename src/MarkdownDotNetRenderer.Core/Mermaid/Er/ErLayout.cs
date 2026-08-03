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
using MarkdownDotNetRenderer.Mermaid.Graph;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Er;

/// <summary>A measured and placed entity box.</summary>
/// <param name="Entity">The parsed entity.</param>
/// <param name="Box">Where the entity sits.</param>
/// <param name="AttributeLines">The attribute compartment's rows.</param>
/// <param name="HeaderHeight">Height of the name compartment.</param>
public sealed record ErBoxLayout(
    ErEntity Entity,
    PlacedNode Box,
    IReadOnlyList<string> AttributeLines,
    double HeaderHeight);

/// <summary>A laid-out ER diagram.</summary>
/// <param name="Boxes">Placed entities in source order.</param>
/// <param name="Placement">The underlying placement, used to route relationships.</param>
/// <param name="Markers">The crow's-foot glyphs the diagram references.</param>
public sealed record ErDiagramLayout(
    IReadOnlyList<ErBoxLayout> Boxes,
    GraphPlacement Placement,
    IReadOnlyCollection<GraphMarker> Markers)
{
    /// <summary>Total width including margins.</summary>
    public double Width => Placement.Width;

    /// <summary>Total height including margins.</summary>
    public double Height => Placement.Height;
}

/// <summary>The outcome of laying out an ER diagram.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Layout">The layout, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record ErLayoutResult(
    bool Success,
    ErDiagramLayout? Layout,
    string? FailureMessage);

/// <summary>
/// Sizes each entity box from its widest attribute row and hands the boxes to the shared layered
/// layout. Both ends of a relationship carry a marker, which is why the shared edge painter
/// supports <c>marker-start</c> as well as <c>marker-end</c>.
/// </summary>
public static class ErLayoutEngine
{
    /// <summary>Lays out a parsed ER diagram.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="theme">Box metrics.</param>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <param name="idPrefix">The diagram's element-id prefix, which names its markers.</param>
    /// <returns>The layout, or a failure when a guard tripped.</returns>
    public static ErLayoutResult Compute(
        ErDiagramModel model,
        double fontSize,
        ErTheme theme,
        LayoutMetrics metrics,
        string idPrefix)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        double headerHeight = (2 * theme.CompartmentPadding) + lineHeight;
        var rows = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var specs = new List<GraphNodeSpec>(model.Entities.Count);

        foreach (ErEntity entity in model.Entities)
        {
            List<string> lines = entity.Attributes.Select(attribute => attribute.Text).ToList();
            rows[entity.Name] = lines;

            double widest = TextMetrics.MeasureWidth(entity.Name, fontSize);
            foreach (string line in lines)
            {
                widest = Math.Max(widest, TextMetrics.MeasureWidth(line, fontSize));
            }

            double width = Math.Max(theme.MinWidth, widest + (2 * theme.HorizontalPadding));
            double bodyHeight = lines.Count == 0
                ? theme.EmptyCompartmentHeight
                : (lines.Count * lineHeight) + (2 * theme.CompartmentPadding);
            specs.Add(new GraphNodeSpec(
                entity.Name, width, headerHeight + bodyHeight, ClipShape.Box));
        }

        var markers = new HashSet<GraphMarker>();
        var edges = new List<GraphEdgeSpec>(model.Relationships.Count);
        foreach (ErRelationship relationship in model.Relationships)
        {
            GraphMarker start = Marker(relationship.LeftCardinality);
            GraphMarker end = Marker(relationship.RightCardinality);
            markers.Add(start);
            markers.Add(end);

            edges.Add(new GraphEdgeSpec(
                relationship.LeftId,
                relationship.RightId,
                relationship.Label,
                relationship.Identifying ? GraphLineStyle.Solid : GraphLineStyle.Dashed,
                GraphMarkers.Id(idPrefix, start),
                GraphMarkers.Id(idPrefix, end),
                StartLabel: null,
                EndLabel: null,
                // Crow's-foot glyphs sit behind their endpoint, so the line stops short of the box
                // by the glyph's length instead of being drawn underneath it.
                GraphMarkers.EndpointInset(start, theme.MarkerSize, theme.Edge.StrokeWidth),
                GraphMarkers.EndpointInset(end, theme.MarkerSize, theme.Edge.StrokeWidth)));
        }

        GraphPlacementResult placement = GraphLayoutAdapter.Compute(
            GraphCanvas.WithSelfLoopReserves(specs, edges, theme.Edge, fontSize),
            edges,
            model.Direction,
            GraphCanvas.WithLabelledLayerGap(
                metrics, edges, model.Direction, theme.Edge, fontSize));
        if (!placement.Success || placement.Placement is null)
        {
            return new ErLayoutResult(false, null, placement.FailureMessage);
        }

        var boxes = new List<ErBoxLayout>(model.Entities.Count);
        foreach (ErEntity entity in model.Entities)
        {
            if (placement.Placement.NodesById.TryGetValue(entity.Name, out PlacedNode? placed))
            {
                boxes.Add(new ErBoxLayout(entity, placed, rows[entity.Name], headerHeight));
            }
        }

        return new ErLayoutResult(
            true, new ErDiagramLayout(boxes, placement.Placement, markers), null);
    }

    /// <summary>The crow's-foot glyph for one cardinality.</summary>
    /// <param name="cardinality">The cardinality.</param>
    /// <returns>The glyph to draw at that end.</returns>
    public static GraphMarker Marker(ErCardinality cardinality) => cardinality switch
    {
        ErCardinality.ZeroOrOne => GraphMarker.ErZeroOrOne,
        ErCardinality.OneOrMany => GraphMarker.ErOneOrMany,
        ErCardinality.ZeroOrMany => GraphMarker.ErZeroOrMany,
        _ => GraphMarker.ErExactlyOne,
    };
}
