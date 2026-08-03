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

namespace MarkdownDotNetRenderer.Mermaid.Class;

/// <summary>A measured and placed class box, with the height of each of its three compartments.</summary>
/// <param name="Definition">The parsed class.</param>
/// <param name="Box">Where the class sits.</param>
/// <param name="HeaderLines">The annotation line, if any, followed by the class name.</param>
/// <param name="AttributeLines">The attribute compartment's lines.</param>
/// <param name="OperationLines">The operation compartment's lines.</param>
/// <param name="HeaderHeight">Height of the name compartment.</param>
/// <param name="AttributesHeight">Height of the attribute compartment.</param>
/// <param name="OperationsHeight">Height of the operation compartment.</param>
public sealed record ClassBoxLayout(
    ClassDefinition Definition,
    PlacedNode Box,
    IReadOnlyList<string> HeaderLines,
    IReadOnlyList<string> AttributeLines,
    IReadOnlyList<string> OperationLines,
    double HeaderHeight,
    double AttributesHeight,
    double OperationsHeight);

/// <summary>A laid-out class diagram.</summary>
/// <param name="Boxes">Placed classes in source order.</param>
/// <param name="Placement">The underlying placement, used to route relations.</param>
/// <param name="Markers">The marker glyphs the diagram references.</param>
public sealed record ClassDiagramLayout(
    IReadOnlyList<ClassBoxLayout> Boxes,
    GraphPlacement Placement,
    IReadOnlyCollection<GraphMarker> Markers)
{
    /// <summary>Total width including margins.</summary>
    public double Width => Placement.Width;

    /// <summary>Total height including margins.</summary>
    public double Height => Placement.Height;
}

/// <summary>The outcome of laying out a class diagram.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Layout">The layout, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record ClassLayoutResult(
    bool Success,
    ClassDiagramLayout? Layout,
    string? FailureMessage);

/// <summary>
/// Sizes each class box from its widest member and hands the boxes to the shared layered layout,
/// which ranks a relation's marker-carrying end — the parent, the whole — above the other end.
/// Solver-free and therefore deterministic.
/// </summary>
public static class ClassLayoutEngine
{
    /// <summary>Lays out a parsed class diagram.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="theme">Box metrics.</param>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <param name="idPrefix">The diagram's element-id prefix, which names its markers.</param>
    /// <returns>The layout, or a failure when a guard tripped.</returns>
    public static ClassLayoutResult Compute(
        ClassDiagramModel model,
        double fontSize,
        ClassTheme theme,
        LayoutMetrics metrics,
        string idPrefix)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        var measured = new List<(ClassDefinition Definition, List<string> Header,
            List<string> Attributes, List<string> Operations, double HeaderHeight,
            double AttributesHeight, double OperationsHeight, double Width)>(model.Classes.Count);
        var specs = new List<GraphNodeSpec>(model.Classes.Count);

        foreach (ClassDefinition definition in model.Classes)
        {
            var header = new List<string>(2);
            if (definition.Annotation is { Length: > 0 })
            {
                header.Add(definition.Annotation);
            }

            header.Add(definition.Name);

            List<string> attributes = definition.Attributes.Select(member => member.Text).ToList();
            List<string> operations = definition.Operations.Select(member => member.Text).ToList();

            double widest = 0;
            foreach (string line in header.Concat(attributes).Concat(operations))
            {
                widest = Math.Max(widest, TextMetrics.MeasureWidth(line, fontSize));
            }

            double width = Math.Max(theme.MinWidth, widest + (2 * theme.HorizontalPadding));
            double headerHeight = Compartment(header.Count);
            double attributesHeight = Compartment(attributes.Count);
            double operationsHeight = Compartment(operations.Count);

            measured.Add((
                definition,
                header,
                attributes,
                operations,
                headerHeight,
                attributesHeight,
                operationsHeight,
                width));
            specs.Add(new GraphNodeSpec(
                definition.Name,
                width,
                headerHeight + attributesHeight + operationsHeight,
                ClipShape.Box));
        }

        var markers = new HashSet<GraphMarker>();
        var edges = new List<GraphEdgeSpec>(model.Relations.Count);
        foreach (ClassRelation relation in model.Relations)
        {
            GraphMarker? startMarker = StartMarker(relation.Kind);
            GraphMarker? endMarker = EndMarker(relation.Kind);
            if (startMarker is { } start)
            {
                markers.Add(start);
            }

            if (endMarker is { } end)
            {
                markers.Add(end);
            }

            edges.Add(new GraphEdgeSpec(
                relation.SourceId,
                relation.TargetId,
                relation.Label,
                IsDashed(relation.Kind) ? GraphLineStyle.Dashed : GraphLineStyle.Solid,
                startMarker is { } startId ? GraphMarkers.Id(idPrefix, startId) : null,
                endMarker is { } endId ? GraphMarkers.Id(idPrefix, endId) : null,
                relation.SourceCardinality,
                relation.TargetCardinality,
                Inset(startMarker, theme),
                Inset(endMarker, theme)));
        }

        GraphPlacementResult placement = GraphLayoutAdapter.Compute(
            specs,
            edges,
            model.Direction,
            GraphCanvas.WithLabelledLayerGap(
                metrics, edges, model.Direction, theme.Edge, fontSize));
        if (!placement.Success || placement.Placement is null)
        {
            return new ClassLayoutResult(false, null, placement.FailureMessage);
        }

        var boxes = new List<ClassBoxLayout>(measured.Count);
        foreach (var entry in measured)
        {
            if (placement.Placement.NodesById.TryGetValue(
                entry.Definition.Name, out PlacedNode? placed))
            {
                boxes.Add(new ClassBoxLayout(
                    entry.Definition,
                    placed,
                    entry.Header,
                    entry.Attributes,
                    entry.Operations,
                    entry.HeaderHeight,
                    entry.AttributesHeight,
                    entry.OperationsHeight));
            }
        }

        return new ClassLayoutResult(
            true, new ClassDiagramLayout(boxes, placement.Placement, markers), null);

        double Compartment(int lineCount) => lineCount == 0
            ? theme.EmptyCompartmentHeight
            : (lineCount * lineHeight) + (2 * theme.CompartmentPadding);
    }

    /// <summary>The glyph drawn at a relation's source (marker-carrying) end.</summary>
    /// <param name="kind">The relation kind.</param>
    /// <returns>The glyph, or <see langword="null"/> when that end is plain.</returns>
    public static GraphMarker? StartMarker(ClassRelationKind kind) => kind switch
    {
        ClassRelationKind.Inheritance or ClassRelationKind.Realization =>
            GraphMarker.HollowTriangle,
        ClassRelationKind.Composition => GraphMarker.FilledDiamond,
        ClassRelationKind.Aggregation => GraphMarker.HollowDiamond,
        _ => null,
    };

    /// <summary>The glyph drawn at a relation's target end.</summary>
    /// <param name="kind">The relation kind.</param>
    /// <returns>The glyph, or <see langword="null"/> when that end is plain.</returns>
    public static GraphMarker? EndMarker(ClassRelationKind kind) => kind switch
    {
        ClassRelationKind.Association or ClassRelationKind.Dependency => GraphMarker.Arrow,
        _ => null,
    };

    /// <summary>Clearance an end needs so its glyph is drawn beside the box, not under it.</summary>
    private static double Inset(GraphMarker? marker, ClassTheme theme) => marker is { } glyph
        ? GraphMarkers.EndpointInset(glyph, theme.MarkerSize, theme.Edge.StrokeWidth)
        : 0;

    /// <summary>Whether a relation kind is drawn with a dashed line.</summary>
    /// <param name="kind">The relation kind.</param>
    /// <returns>Whether the line is dashed.</returns>
    public static bool IsDashed(ClassRelationKind kind) =>
        kind is ClassRelationKind.Dependency or ClassRelationKind.Realization;
}
