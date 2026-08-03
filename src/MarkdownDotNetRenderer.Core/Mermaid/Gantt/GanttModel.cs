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

namespace MarkdownDotNetRenderer.Mermaid.Gantt;

/// <summary>The progress states a task tag can set.</summary>
public enum GanttTaskState
{
    /// <summary>No progress tag; the bar uses the plain fill.</summary>
    Planned,

    /// <summary>Tagged <c>done</c>.</summary>
    Done,

    /// <summary>Tagged <c>active</c>.</summary>
    Active,
}

/// <summary>One task row.</summary>
/// <param name="Name">The task's display name.</param>
/// <param name="Id">The task id, used by <c>after &lt;id&gt;</c>; empty when the source gave none.</param>
/// <param name="Start">Inclusive start date.</param>
/// <param name="End">Exclusive end date; equal to <paramref name="Start"/> for a milestone.</param>
/// <param name="State">The progress state from the task's tags.</param>
/// <param name="IsCritical">Whether the task is tagged <c>crit</c>.</param>
/// <param name="IsMilestone">Whether the task is tagged <c>milestone</c> and drawn as a diamond.</param>
/// <param name="Order">0-based index in source order.</param>
public sealed record GanttTask(
    string Name,
    string Id,
    DateOnly Start,
    DateOnly End,
    GanttTaskState State,
    bool IsCritical,
    bool IsMilestone,
    int Order);

/// <summary>A group of tasks under one <c>section</c> heading.</summary>
/// <param name="Name">The section name; empty for tasks declared before any section.</param>
/// <param name="Tasks">The section's tasks in source order.</param>
public sealed record GanttSection(string Name, IReadOnlyList<GanttTask> Tasks);

/// <summary>A parsed Gantt chart.</summary>
/// <param name="Title">The chart title, or <see langword="null"/>.</param>
/// <param name="Sections">Sections in source order; at least one with at least one task.</param>
public sealed record GanttModel(string? Title, IReadOnlyList<GanttSection> Sections)
{
    /// <summary>Every task, in source order.</summary>
    public IEnumerable<GanttTask> Tasks => Sections.SelectMany(section => section.Tasks);

    /// <summary>The earliest start date in the chart.</summary>
    public DateOnly Start => Tasks.Min(task => task.Start);

    /// <summary>The latest end date in the chart.</summary>
    public DateOnly End => Tasks.Max(task => task.End);
}

/// <summary>The outcome of parsing a Gantt source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record GanttParseResult(
    bool Success,
    GanttModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
