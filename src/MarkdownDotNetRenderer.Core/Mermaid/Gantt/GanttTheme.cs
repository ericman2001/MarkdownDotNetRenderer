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

/// <summary>
/// Paint for <see cref="GanttRenderer"/>; <see cref="GanttMetrics"/> holds the geometry
/// (docs/09-conventions.md). Lengths are CSS pixels, colours are CSS colour literals.
/// </summary>
/// <param name="BarFill">Fill of a planned task bar.</param>
/// <param name="DoneFill">Fill of a bar tagged <c>done</c>.</param>
/// <param name="ActiveFill">Fill of a bar tagged <c>active</c>.</param>
/// <param name="CriticalStroke">Stroke of a bar tagged <c>crit</c>.</param>
/// <param name="CriticalStrokeWidth">Stroke width of a bar tagged <c>crit</c>. Wider than a plain
/// bar's so the outline survives being rasterized at a document's scale, where a hairline
/// disappeared.</param>
/// <param name="BarStroke">Stroke of an untagged bar.</param>
/// <param name="BarStrokeWidth">Stroke width of a bar.</param>
/// <param name="MilestoneFill">Fill of a milestone diamond.</param>
/// <param name="GridStroke">Stroke of the axis line and its grid lines.</param>
/// <param name="GridStrokeWidth">Stroke width of the axis line and its grid lines.</param>
/// <param name="TextFill">Fill of task, section, axis, and title text.</param>
/// <param name="TitleWeight">Font weight of the chart title.</param>
/// <param name="SectionWeight">Font weight of a section heading.</param>
/// <param name="AxisFontScale">Axis-label font size relative to the label font size.</param>
public sealed record GanttTheme(
    string BarFill = "#8fb3d9",
    string DoneFill = "#b8c2cc",
    string ActiveFill = "#4477aa",
    string CriticalStroke = "#c0392b",
    double CriticalStrokeWidth = 2.5,
    string BarStroke = "#33415a",
    double BarStrokeWidth = 1,
    string MilestoneFill = "#33415a",
    string GridStroke = "#c7ced9",
    double GridStrokeWidth = 1,
    string TextFill = "#111827",
    string TitleWeight = "600",
    string SectionWeight = "600",
    double AxisFontScale = 0.85)
{
    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static GanttTheme Default { get; } = new();

    /// <summary>The stroke a task's bar or milestone is outlined with.</summary>
    /// <param name="task">The task.</param>
    /// <returns>A CSS colour literal.</returns>
    public string Stroke(GanttTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.IsCritical
            ? CriticalStroke
            : task.IsMilestone ? MilestoneFill : BarStroke;
    }

    /// <summary>The stroke width a task's bar or milestone is outlined with.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The width in CSS pixels.</returns>
    public double StrokeWidth(GanttTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.IsCritical ? CriticalStrokeWidth : BarStrokeWidth;
    }

    /// <summary>The bar fill for one task state.</summary>
    /// <param name="state">The task's progress state.</param>
    /// <returns>A CSS colour literal.</returns>
    public string Fill(GanttTaskState state) => state switch
    {
        GanttTaskState.Done => DoneFill,
        GanttTaskState.Active => ActiveFill,
        _ => BarFill,
    };
}
