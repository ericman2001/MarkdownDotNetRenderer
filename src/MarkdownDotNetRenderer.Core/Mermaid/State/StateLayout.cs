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

namespace MarkdownDotNetRenderer.Mermaid.State;

/// <summary>A measured and placed state box.</summary>
/// <param name="Node">The parsed vertex.</param>
/// <param name="Lines">The wrapped label lines; empty for pseudo-states.</param>
/// <param name="Box">Where the vertex sits.</param>
public sealed record StateBox(StateNode Node, IReadOnlyList<string> Lines, PlacedNode Box);

/// <summary>A laid-out state diagram.</summary>
/// <param name="Boxes">Placed vertices in source order.</param>
/// <param name="Placement">The underlying placement, used to route transitions.</param>
public sealed record StateDiagramLayout(
    IReadOnlyList<StateBox> Boxes,
    GraphPlacement Placement)
{
    /// <summary>Total width including margins.</summary>
    public double Width => Placement.Width;

    /// <summary>Total height including margins.</summary>
    public double Height => Placement.Height;
}

/// <summary>The outcome of laying out a state diagram.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Layout">The layout, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record StateLayoutResult(
    bool Success,
    StateDiagramLayout? Layout,
    string? FailureMessage);

/// <summary>
/// Measures state boxes from their labels and hands them to the shared layered layout: no solver,
/// no iteration beyond the layered layout's own fixed sweeps, so a source always lays out to the
/// same coordinates.
/// </summary>
public static class StateLayoutEngine
{
    /// <summary>Lays out a parsed state diagram.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="theme">Box metrics and wrap width.</param>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <returns>The layout, or a failure when a guard tripped.</returns>
    public static StateLayoutResult Compute(
        StateDiagramModel model,
        double fontSize,
        StateTheme theme,
        LayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(metrics);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        var lines = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var specs = new List<GraphNodeSpec>(model.States.Count);

        foreach (StateNode state in model.States)
        {
            if (state.Kind is StateKind.Start or StateKind.End)
            {
                lines[state.Id] = [];
                double diameter = 2 * (theme.PseudoRadius +
                    (state.Kind == StateKind.End ? theme.EndRingGap : 0));
                specs.Add(new GraphNodeSpec(state.Id, diameter, diameter, ClipShape.Ellipse));
                continue;
            }

            IReadOnlyList<string> wrapped = TextMetrics.WrapLabel(
                state.Label.Length == 0 ? state.Id : state.Label, theme.LabelWrapChars);
            lines[state.Id] = wrapped;

            double textWidth = 0;
            foreach (string line in wrapped)
            {
                textWidth = Math.Max(textWidth, TextMetrics.MeasureWidth(line, fontSize));
            }

            double width = Math.Max(
                theme.MinWidth, textWidth + (2 * theme.HorizontalPadding));
            double height = Math.Max(
                theme.MinHeight, (wrapped.Count * lineHeight) + (2 * theme.VerticalPadding));
            specs.Add(new GraphNodeSpec(state.Id, width, height, ClipShape.Box));
        }

        var edges = new List<GraphEdgeSpec>(model.Transitions.Count);
        foreach (StateTransition transition in model.Transitions)
        {
            edges.Add(new GraphEdgeSpec(
                transition.SourceId,
                transition.TargetId,
                transition.Label,
                transition.IsNoteLink ? GraphLineStyle.Dashed : GraphLineStyle.Solid,
                StartMarker: null,
                EndMarker: transition.IsNoteLink ? null : GraphMarker.Arrow));
        }

        GraphPlacementResult placement = GraphLayoutAdapter.Compute(
            GraphCanvas.WithSelfLoopReserves(specs, edges, theme.Edge, fontSize),
            edges,
            model.Direction,
            GraphCanvas.WithLabelledLayerGap(
                metrics, edges, model.Direction, theme.Edge, fontSize));
        if (!placement.Success || placement.Placement is null)
        {
            return new StateLayoutResult(false, null, placement.FailureMessage);
        }

        var boxes = new List<StateBox>(model.States.Count);
        foreach (StateNode state in model.States)
        {
            if (placement.Placement.NodesById.TryGetValue(state.Id, out PlacedNode? placed))
            {
                boxes.Add(new StateBox(state, lines[state.Id], placed));
            }
        }

        return new StateLayoutResult(
            true, new StateDiagramLayout(boxes, placement.Placement), null);
    }
}
