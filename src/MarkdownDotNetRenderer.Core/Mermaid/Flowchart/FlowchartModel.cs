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

/// <summary>The axis along which flowchart layers advance.</summary>
public enum FlowDirection
{
    /// <summary>Layers advance downwards; the cross axis is horizontal. Mermaid <c>TD</c>/<c>TB</c>.</summary>
    TopDown,

    /// <summary>Layers advance rightwards; the cross axis is vertical. Mermaid <c>LR</c>.</summary>
    LeftRight,
}

/// <summary>The node shapes supported in this phase.</summary>
public enum FlowNodeShape
{
    /// <summary><c>A[Label]</c>.</summary>
    Rectangle,

    /// <summary><c>A(Label)</c>.</summary>
    Rounded,

    /// <summary><c>A([Label])</c>.</summary>
    Stadium,

    /// <summary><c>A{Label}</c>.</summary>
    Rhombus,
}

/// <summary>A flowchart node.</summary>
/// <param name="Id">The mermaid node id.</param>
/// <param name="Label">The display label; defaults to the id.</param>
/// <param name="Shape">The node shape.</param>
/// <param name="Order">0-based index of first mention, which fixes layout tie-breaking.</param>
public sealed record FlowNode(string Id, string Label, FlowNodeShape Shape, int Order);

/// <summary>A flowchart edge.</summary>
/// <param name="SourceId">Id of the source node.</param>
/// <param name="TargetId">Id of the target node.</param>
/// <param name="Label">Edge label, or <see langword="null"/>.</param>
/// <param name="Directed">Whether an arrowhead is drawn at the target end.</param>
public sealed record FlowEdge(string SourceId, string TargetId, string? Label, bool Directed);

/// <summary>A parsed flowchart: its direction plus nodes and edges in source order.</summary>
/// <param name="Direction">The layout direction.</param>
/// <param name="Nodes">Nodes in first-mention order.</param>
/// <param name="Edges">Edges in source order.</param>
public sealed record FlowchartModel(
    FlowDirection Direction,
    IReadOnlyList<FlowNode> Nodes,
    IReadOnlyList<FlowEdge> Edges);

/// <summary>The outcome of parsing a flowchart source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record FlowchartParseResult(
    bool Success,
    FlowchartModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
