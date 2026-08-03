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
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Gantt;

/// <summary>Tunable geometry for <see cref="GanttLayoutEngine"/>. All values are CSS pixels.</summary>
/// <param name="Margin">Margin around the whole chart.</param>
/// <param name="ChartWidth">Width of the dated area, which fixes the date scale.</param>
/// <param name="LabelGap">Gap between the task-name column and the dated area.</param>
/// <param name="TitleGap">Gap between the title and the axis.</param>
/// <param name="AxisHeight">Height reserved for the date axis and its labels.</param>
/// <param name="RowHeight">Height of one task row.</param>
/// <param name="SectionHeaderHeight">Height of a section heading row.</param>
/// <param name="BarHeight">Height of a task bar.</param>
/// <param name="MilestoneSize">Diagonal of a milestone diamond.</param>
/// <param name="MinTickSpacing">Smallest gap between two axis ticks, which fixes the tick step.</param>
public sealed record GanttMetrics(
    double Margin = 16,
    double ChartWidth = 560,
    double LabelGap = 12,
    double TitleGap = 12,
    double AxisHeight = 26,
    double RowHeight = 24,
    double SectionHeaderHeight = 22,
    double BarHeight = 14,
    double MilestoneSize = 14,
    double MinTickSpacing = 76)
{
    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static GanttMetrics Default { get; } = new();
}

/// <summary>One axis tick.</summary>
/// <param name="X">Horizontal position of the tick.</param>
/// <param name="Label">The tick's ISO date label.</param>
public sealed record GanttTick(double X, string Label);

/// <summary>A placed section heading.</summary>
/// <param name="Name">The section name.</param>
/// <param name="CenterY">Vertical centre of the heading row.</param>
public sealed record GanttSectionRow(string Name, double CenterY);

/// <summary>A placed task row: its name on the left, its bar in the dated area.</summary>
/// <param name="Task">The parsed task.</param>
/// <param name="CenterY">Vertical centre of the row.</param>
/// <param name="BarLeft">Left edge of the bar, or the diamond's centre for a milestone.</param>
/// <param name="BarWidth">Width of the bar; zero for a milestone.</param>
public sealed record GanttBarRow(
    GanttTask Task,
    double CenterY,
    double BarLeft,
    double BarWidth);

/// <summary>The laid-out Gantt chart.</summary>
/// <param name="Title">The wrapped title lines; empty when the chart has no title.</param>
/// <param name="TitleCenterX">Horizontal centre of the title block.</param>
/// <param name="TitleCenterY">Vertical centre of the title block.</param>
/// <param name="LabelLeft">Left edge of the task-name column.</param>
/// <param name="ChartLeft">Left edge of the dated area.</param>
/// <param name="ChartRight">Right edge of the dated area.</param>
/// <param name="AxisY">Vertical position of the axis line.</param>
/// <param name="RowsBottom">Bottom of the last row, where grid lines stop.</param>
/// <param name="Ticks">Axis ticks, left to right.</param>
/// <param name="Sections">Placed section headings in source order.</param>
/// <param name="Bars">Placed task rows in source order.</param>
/// <param name="Width">Total chart width including margins.</param>
/// <param name="Height">Total chart height including margins.</param>
public sealed record GanttChartLayout(
    IReadOnlyList<string> Title,
    double TitleCenterX,
    double TitleCenterY,
    double LabelLeft,
    double ChartLeft,
    double ChartRight,
    double AxisY,
    double RowsBottom,
    IReadOnlyList<GanttTick> Ticks,
    IReadOnlyList<GanttSectionRow> Sections,
    IReadOnlyList<GanttBarRow> Bars,
    double Width,
    double Height);

/// <summary>
/// The solver-free Gantt layout: one row per task under its section heading, and a single linear
/// date scale from the chart's earliest start to its latest end. The tick step is chosen from a
/// fixed candidate list — the first that keeps ticks at least
/// <see cref="GanttMetrics.MinTickSpacing"/> apart — so no iteration is unbounded and the output is
/// a pure function of the model.
/// </summary>
public static class GanttLayoutEngine
{
    /// <summary>Tick steps in days, tried in order.</summary>
    public static IReadOnlyList<int> TickStepDays { get; } = [1, 2, 7, 14, 28, 56, 112, 364];

    /// <summary>Lays out a parsed Gantt chart.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="metrics">Geometry to use.</param>
    /// <param name="wrapChars">Soft wrap width, in characters, for the title.</param>
    /// <returns>The laid-out chart.</returns>
    public static GanttChartLayout Compute(
        GanttModel model,
        double fontSize,
        GanttMetrics metrics,
        int wrapChars)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wrapChars);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        DateOnly start = model.Start;
        DateOnly end = model.End;
        int span = Math.Max(1, end.DayNumber - start.DayNumber);

        double labelWidth = 0;
        foreach (GanttSection section in model.Sections)
        {
            labelWidth = Math.Max(labelWidth, TextMetrics.MeasureWidth(section.Name, fontSize));
            foreach (GanttTask task in section.Tasks)
            {
                labelWidth = Math.Max(labelWidth, TextMetrics.MeasureWidth(task.Name, fontSize));
            }
        }

        IReadOnlyList<string> titleLines = model.Title is { Length: > 0 }
            ? TextMetrics.WrapLabel(model.Title, wrapChars)
            : [];
        double titleHeight = titleLines.Count == 0
            ? 0
            : (titleLines.Count * lineHeight) + metrics.TitleGap;

        double labelLeft = metrics.Margin;
        double chartLeft = labelLeft + labelWidth + metrics.LabelGap;
        double chartRight = chartLeft + metrics.ChartWidth;
        double axisY = metrics.Margin + titleHeight + metrics.AxisHeight;

        var sections = new List<GanttSectionRow>();
        var bars = new List<GanttBarRow>();
        double y = axisY;
        foreach (GanttSection section in model.Sections)
        {
            if (section.Name.Length > 0)
            {
                sections.Add(new GanttSectionRow(
                    section.Name, y + (metrics.SectionHeaderHeight / 2)));
                y += metrics.SectionHeaderHeight;
            }

            foreach (GanttTask task in section.Tasks)
            {
                double left = X(task.Start);
                double right = X(task.End);
                bars.Add(new GanttBarRow(
                    task,
                    y + (metrics.RowHeight / 2),
                    left,
                    task.IsMilestone ? 0 : Math.Max(MinBarWidth, right - left)));
                y += metrics.RowHeight;
            }
        }

        var ticks = new List<GanttTick>();
        int step = TickStep(span, metrics);
        for (int offset = 0; offset <= span; offset += step)
        {
            DateOnly date = start.AddDays(offset);
            ticks.Add(new GanttTick(
                X(date), date.ToString(GanttParser.IsoDateFormat, CultureInfo.InvariantCulture)));
        }

        double width = chartRight + metrics.Margin;
        double height = y + metrics.Margin;

        return new GanttChartLayout(
            titleLines,
            width / 2,
            metrics.Margin + (titleLines.Count * lineHeight / 2),
            labelLeft,
            chartLeft,
            chartRight,
            axisY,
            y,
            ticks,
            sections,
            bars,
            width,
            height);

        double X(DateOnly date) =>
            chartLeft + ((date.DayNumber - start.DayNumber) / (double)span * metrics.ChartWidth);
    }

    /// <summary>The smallest candidate step that keeps ticks readable at this scale.</summary>
    /// <param name="spanDays">The chart's overall span in days.</param>
    /// <param name="metrics">Geometry supplying the chart width and minimum tick spacing.</param>
    /// <returns>The tick step in days.</returns>
    public static int TickStep(int spanDays, GanttMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spanDays);

        double perDay = metrics.ChartWidth / spanDays;
        foreach (int candidate in TickStepDays)
        {
            if (candidate * perDay >= metrics.MinTickSpacing)
            {
                return candidate;
            }
        }

        return spanDays;
    }

    /// <summary>Narrowest bar drawn, so a one-day task on a long chart stays visible.</summary>
    public const double MinBarWidth = 3;
}
