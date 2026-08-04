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
using System.Xml.Linq;
using MarkdownDotNetRenderer.Mermaid;
using MarkdownDotNetRenderer.Mermaid.Gantt;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 2, 3, and 5 of docs/07-testing-strategy.md for the phase-4 Gantt chart: task-line parsing,
/// the linear date scale, tags, milestones, and deterministic output.
/// </summary>
public sealed class GanttDiagramTests
{
    private const string Simple = """
        gantt
            title Delivery
            dateFormat YYYY-MM-DD
            section Build
            Design      :done, d1, 2026-01-05, 5d
            Implement   :active, crit, d2, after d1, 2w
            section Ship
            Release     :milestone, m1, after d2, 0d
        """;

    private static DiagramRenderResult Render(string source) =>
        new GanttRenderer().Render(source, RenderOptions.Html);

    private static XElement RenderSvg(string source)
    {
        DiagramRenderResult result = Render(source);

        return DiagramTestHelpers.RenderSvg(result);
    }

    [Fact]
    public void The_Parser_Reads_Title_Sections_Tags_Durations_And_Dependencies()
    {
        GanttParseResult parsed = GanttParser.Parse(Simple);

        Assert.True(parsed.Success);
        GanttModel model = parsed.Model!;

        Assert.Equal("Delivery", model.Title);
        Assert.Equal(["Build", "Ship"], model.Sections.Select(section => section.Name));

        List<GanttTask> tasks = model.Tasks.ToList();
        Assert.Equal(["Design", "Implement", "Release"], tasks.Select(task => task.Name));
        Assert.Equal(new DateOnly(2026, 1, 5), tasks[0].Start);
        Assert.Equal(new DateOnly(2026, 1, 10), tasks[0].End);
        Assert.Equal(GanttTaskState.Done, tasks[0].State);

        // 'after d1' starts where the previous task ended, and 2w is fourteen days.
        Assert.Equal(tasks[0].End, tasks[1].Start);
        Assert.Equal(new DateOnly(2026, 1, 24), tasks[1].End);
        Assert.Equal(GanttTaskState.Active, tasks[1].State);
        Assert.True(tasks[1].IsCritical);

        Assert.True(tasks[2].IsMilestone);
        Assert.Equal(tasks[2].Start, tasks[2].End);
        Assert.Equal(new DateOnly(2026, 1, 5), model.Start);
        Assert.Equal(new DateOnly(2026, 1, 24), model.End);
    }

    [Theory]
    [InlineData("3d", 3)]
    [InlineData("2w", 14)]
    [InlineData("0d", 0)]
    public void Durations_Are_Read_In_Whole_Days(string field, int days)
    {
        Assert.True(GanttParser.TryParseDuration(field, out int parsed));

        Assert.Equal(days, parsed);
    }

    [Theory]
    [InlineData("3h")]
    [InlineData("d")]
    [InlineData("-2d")]
    public void Unsupported_Durations_Are_Refused(string field) =>
        Assert.False(GanttParser.TryParseDuration(field, out _));

    [Fact]
    public void An_Unsupported_Date_Format_Reports_Mermaid003_And_Iso_Dates_Are_Still_Read()
    {
        DiagramRenderResult result = Render(
            "gantt\n    dateFormat DD-MM-YYYY\n    A :2026-03-01, 1d\n");

        Assert.True(result.Success);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
        Assert.Contains("YYYY-MM-DD", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gantt\n")]
    [InlineData("gantt\n    title Nothing dated\n")]
    [InlineData("flowchart TD\n    A --> B\n")]
    public void Charts_Without_Dated_Tasks_Degrade_To_Mermaid002(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.False(result.Success);
        Assert.Null(result.SvgFragment);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
    }

    [Fact]
    public void The_Date_Scale_Is_Linear_And_Spans_The_Whole_Chart()
    {
        GanttChartLayout layout = GanttLayoutEngine.Compute(
            GanttParser.Parse(Simple).Model!,
            12,
            GanttMetrics.Default,
            wrapChars: 48,
            axisFontSize: 12 * GanttTheme.Default.AxisFontScale);

        Assert.Equal(layout.ChartLeft, layout.Bars[0].BarLeft, precision: 9);
        Assert.Equal(layout.ChartRight, layout.Bars[^1].BarLeft, precision: 9);
        Assert.Equal(
            layout.ChartRight - layout.ChartLeft, GanttMetrics.Default.ChartWidth, precision: 9);

        // Equal-length spans map to equal widths, which is what 'linear' means here.
        double perDay = (layout.Bars[0].BarWidth) / 5;
        Assert.Equal(perDay * 14, layout.Bars[1].BarWidth, precision: 6);
        Assert.Equal(0, layout.Bars[^1].BarWidth);
    }

    [Fact]
    public void Ticks_Stay_At_Least_The_Minimum_Spacing_Apart()
    {
        GanttChartLayout layout = GanttLayoutEngine.Compute(
            GanttParser.Parse(Simple).Model!,
            12,
            GanttMetrics.Default,
            wrapChars: 48,
            axisFontSize: 12 * GanttTheme.Default.AxisFontScale);

        Assert.NotEmpty(layout.Ticks);
        for (int i = 1; i < layout.Ticks.Count; i++)
        {
            Assert.True(
                layout.Ticks[i].X - layout.Ticks[i - 1].X >=
                    GanttMetrics.Default.MinTickSpacing - 1e-9,
                "Axis ticks are closer together than the configured minimum.");
        }

        Assert.All(layout.Ticks, tick => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", tick.Label));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(60, 14)]
    [InlineData(1000, 364)]
    public void The_Tick_Step_Grows_With_The_Span(int spanDays, int expected) =>
        Assert.Equal(expected, GanttLayoutEngine.TickStep(spanDays, GanttMetrics.Default));

    [Fact]
    public void Every_Task_Gets_A_Row_And_Every_Section_A_Heading()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> tasks = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value.StartsWith(
                "mdnr-gantt-task ", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(
            ["Design", "Implement", "Release"],
            tasks.Select(task => task.Attribute("data-task")!.Value));

        // A bar per dated task, a diamond for the milestone.
        Assert.Equal(2, svg.Descendants(DiagramTestHelpers.Svg + "rect").Count());
        Assert.Single(svg.Descendants(DiagramTestHelpers.Svg + "polygon"));

        string text = string.Join(
            '\n',
            svg.Descendants(DiagramTestHelpers.Svg + "text").Select(t => t.Value));
        foreach (string expected in new[] { "Delivery", "Build", "Ship", "Design", "2026-01-05" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tags_Choose_The_Bar_Fill_And_Critical_Stroke()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> bars = svg.Descendants(DiagramTestHelpers.Svg + "rect").ToList();
        Assert.Equal(GanttTheme.Default.DoneFill, bars[0].Attribute("fill")!.Value);
        Assert.Equal(GanttTheme.Default.ActiveFill, bars[1].Attribute("fill")!.Value);
        Assert.Equal(GanttTheme.Default.CriticalStroke, bars[1].Attribute("stroke")!.Value);
    }

    [Fact]
    public void A_Critical_Bar_Is_Outlined_More_Heavily_Than_A_Plain_One()
    {
        XElement svg = RenderSvg(Simple);

        // A hairline outline disappeared once the diagram was rasterized into a document, so the
        // crit outline is drawn wider than a plain bar's.
        List<XElement> bars = svg.Descendants(DiagramTestHelpers.Svg + "rect").ToList();
        double plain = double.Parse(
            bars[0].Attribute("stroke-width")!.Value, CultureInfo.InvariantCulture);
        double critical = double.Parse(
            bars[1].Attribute("stroke-width")!.Value, CultureInfo.InvariantCulture);

        Assert.Equal(GanttTheme.Default.BarStrokeWidth, plain);
        Assert.Equal(GanttTheme.Default.CriticalStrokeWidth, critical);
        Assert.True(critical > plain);
    }

    [Fact]
    public void Everything_Stays_Inside_The_Canvas()
    {
        GanttChartLayout layout = GanttLayoutEngine.Compute(
            GanttParser.Parse(Simple).Model!,
            12,
            GanttMetrics.Default,
            wrapChars: 48,
            axisFontSize: 12 * GanttTheme.Default.AxisFontScale);

        Assert.True(layout.ChartRight <= layout.Width);
        Assert.True(layout.RowsBottom <= layout.Height);
        Assert.All(layout.Bars, bar =>
        {
            Assert.True(bar.BarLeft >= layout.ChartLeft);
            Assert.True(bar.BarLeft + bar.BarWidth <= layout.Width);
            Assert.True(bar.CenterY <= layout.Height);
        });
    }

    [Theory]
    [InlineData("2026-01-05", "2d")]
    [InlineData("2026-01-05", "8w")]
    public void The_Outermost_Axis_Labels_Stay_Inside_The_Canvas(string start, string duration)
    {
        double axisFontSize = 12 * GanttTheme.Default.AxisFontScale;
        GanttChartLayout layout = GanttLayoutEngine.Compute(
            GanttParser.Parse($"""
                gantt
                    dateFormat YYYY-MM-DD
                    section Only
                    Task :t1, {start}, {duration}
                """).Model!,
            12,
            GanttMetrics.Default,
            wrapChars: 48,
            axisFontSize);

        double reach = TextMetrics.MeasureWidth(layout.Ticks[0].Label, axisFontSize) / 2;
        Assert.True(layout.Ticks[0].X - reach >= 0);
        Assert.True(layout.Ticks[^1].X + reach <= layout.Width);
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new GanttRenderer();

        Assert.Equal(
            renderer.Render(Simple, RenderOptions.Html).SvgFragment,
            renderer.Render(Simple, RenderOptions.Html).SvgFragment);
    }
}
