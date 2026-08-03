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

namespace MarkdownDotNetRenderer.Mermaid.Sequence;

/// <summary>How a message line is stroked.</summary>
public enum SequenceLineStyle
{
    /// <summary>A continuous line, from <c>-&gt;</c> and <c>-&gt;&gt;</c>.</summary>
    Solid,

    /// <summary>A dashed line, from <c>--&gt;</c> and <c>--&gt;&gt;</c>.</summary>
    Dashed,
}

/// <summary>What a message line ends in.</summary>
public enum SequenceArrowHead
{
    /// <summary>A filled triangle, from <c>-&gt;&gt;</c> and <c>--&gt;&gt;</c>.</summary>
    Filled,

    /// <summary>An open chevron, from <c>-&gt;</c> and <c>--&gt;</c>.</summary>
    Open,

    /// <summary>A cross, from <c>-x</c> and <c>--x</c>.</summary>
    Cross,

    /// <summary>A half-open chevron for the async arrows <c>-)</c> and <c>--)</c>.</summary>
    Async,
}

/// <summary>Where a note sits relative to the actors it names.</summary>
public enum SequenceNotePlacement
{
    /// <summary><c>Note left of A</c>.</summary>
    LeftOf,

    /// <summary><c>Note right of A</c>.</summary>
    RightOf,

    /// <summary><c>Note over A</c> or <c>Note over A,B</c>.</summary>
    Over,
}

/// <summary>A participant or actor lifeline.</summary>
/// <param name="Id">The mermaid identifier used in messages.</param>
/// <param name="Label">The display label; defaults to the id.</param>
/// <param name="Order">0-based index of first mention, which fixes the column order.</param>
public sealed record SequenceActor(string Id, string Label, int Order);

/// <summary>Base type for the ordered rows of a sequence diagram.</summary>
/// <param name="Order">0-based index in source order.</param>
public abstract record SequenceEvent(int Order);

/// <summary>A message between two lifelines; a self-message has equal ids.</summary>
/// <param name="Order">0-based index in source order.</param>
/// <param name="SourceId">Id of the sending actor.</param>
/// <param name="TargetId">Id of the receiving actor.</param>
/// <param name="Label">The message text; may be empty.</param>
/// <param name="Line">How the line is stroked.</param>
/// <param name="Head">What the line ends in.</param>
/// <param name="Number">The <c>autonumber</c> sequence number, or <see langword="null"/>.</param>
public sealed record SequenceMessage(
    int Order,
    string SourceId,
    string TargetId,
    string Label,
    SequenceLineStyle Line,
    SequenceArrowHead Head,
    int? Number) : SequenceEvent(Order)
{
    /// <summary>Whether the message loops back to its own lifeline.</summary>
    public bool IsSelfMessage => string.Equals(SourceId, TargetId, StringComparison.Ordinal);

    /// <summary>The rendered label, including the <c>autonumber</c> prefix when there is one.</summary>
    public string DisplayLabel => Number is null ? Label : $"{Number.Value}. {Label}";
}

/// <summary>A note box attached to one lifeline or spanning several.</summary>
/// <param name="Order">0-based index in source order.</param>
/// <param name="Placement">Where the box sits.</param>
/// <param name="ActorIds">The named actors, in source order; at least one.</param>
/// <param name="Text">The note text.</param>
public sealed record SequenceNote(
    int Order,
    SequenceNotePlacement Placement,
    IReadOnlyList<string> ActorIds,
    string Text) : SequenceEvent(Order);

/// <summary>A parsed sequence diagram: ordered actors plus ordered events.</summary>
/// <param name="Actors">Actors in first-mention order.</param>
/// <param name="Events">Messages and notes in source order.</param>
public sealed record SequenceModel(
    IReadOnlyList<SequenceActor> Actors,
    IReadOnlyList<SequenceEvent> Events);

/// <summary>The outcome of parsing a sequence-diagram source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record SequenceParseResult(
    bool Success,
    SequenceModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
