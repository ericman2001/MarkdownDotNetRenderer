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

namespace MarkdownDotNetRenderer.Mermaid.State;

/// <summary>What a state-diagram vertex represents.</summary>
public enum StateKind
{
    /// <summary>An ordinary state, drawn as a stadium.</summary>
    Normal,

    /// <summary>The <c>[*]</c> start pseudo-state, drawn as a filled circle.</summary>
    Start,

    /// <summary>The <c>[*]</c> end pseudo-state, drawn as a ringed filled circle.</summary>
    End,

    /// <summary>A <c>note</c>, drawn as a dashed box linked to its state.</summary>
    Note,
}

/// <summary>A state-diagram vertex.</summary>
/// <param name="Id">Unique id; pseudo-states and notes get synthetic ids.</param>
/// <param name="Label">Display label; empty for pseudo-states.</param>
/// <param name="Kind">What the vertex represents.</param>
/// <param name="Order">0-based index of first mention, which fixes layout tie-breaking.</param>
public sealed record StateNode(string Id, string Label, StateKind Kind, int Order);

/// <summary>A transition between two vertices.</summary>
/// <param name="SourceId">Id of the source vertex.</param>
/// <param name="TargetId">Id of the target vertex.</param>
/// <param name="Label">Transition label, or <see langword="null"/>.</param>
/// <param name="IsNoteLink">Whether this links a note to its state rather than being a transition.</param>
public sealed record StateTransition(
    string SourceId,
    string TargetId,
    string? Label,
    bool IsNoteLink = false);

/// <summary>A parsed state diagram.</summary>
/// <param name="Direction">The layout direction; <c>TD</c> unless the source says otherwise.</param>
/// <param name="States">Vertices in first-mention order.</param>
/// <param name="Transitions">Transitions in source order.</param>
public sealed record StateDiagramModel(
    FlowDirection Direction,
    IReadOnlyList<StateNode> States,
    IReadOnlyList<StateTransition> Transitions);

/// <summary>The outcome of parsing a state-diagram source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record StateParseResult(
    bool Success,
    StateDiagramModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
