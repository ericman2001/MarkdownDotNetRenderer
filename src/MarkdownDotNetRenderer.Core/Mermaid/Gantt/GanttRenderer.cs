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
using MarkdownDotNetRenderer.Mermaid.Graph;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Gantt;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>gantt</c> charts: a date axis, one bar per task grouped
/// under its section, and diamonds for milestones. Dates are mapped to x positions by a single
/// linear scale, so no solver and no iteration are involved.
/// </summary>
public sealed class GanttRenderer : IDiagramRenderer
{
    private readonly GanttMetrics _metrics;
    private readonly GanttTheme _theme;

    /// <summary>Creates a renderer with the default geometry and theme.</summary>
    public GanttRenderer()
        : this(GanttMetrics.Default, GanttTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit geometry and visual theme.</summary>
    /// <param name="metrics">Geometry to use.</param>
    /// <param name="theme">Colours and stroke widths to use.</param>
    public GanttRenderer(GanttMetrics metrics, GanttTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } = [GanttParser.HeaderKeyword];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        GanttParseResult parsed;
        try
        {
            parsed = GanttParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The Gantt source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The Gantt source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        GanttModel model = parsed.Model;
        double fontSize = DiagramDefaults.ResolveFontSize(options);
        GanttChartLayout layout = GanttLayoutEngine.Compute(
            model, fontSize, _metrics, _metrics.TitleWrapChars, fontSize * _theme.AxisFontScale);

        string altText = string.Create(
            CultureInfo.InvariantCulture,
            $"gantt chart with {model.Tasks.Count()} tasks from " +
            $"{model.Start.ToString(GanttParser.IsoDateFormat, CultureInfo.InvariantCulture)} to " +
            $"{model.End.ToString(GanttParser.IsoDateFormat, CultureInfo.InvariantCulture)}");
        string svg = Emit(layout, options, fontSize, altText);

        return new DiagramRenderResult(
            true, svg, layout.Width, layout.Height, altText, parsed.Diagnostics);
    }

    private string Emit(
        GanttChartLayout layout,
        RenderOptions options,
        double fontSize,
        string altText)
    {
        var svg = new SvgBuilder();
        DiagramSvg.StartRoot(svg, layout.Width, layout.Height, options, altText, "mdnr-gantt");

        if (layout.Title.Count > 0)
        {
            svg.StartElement("g").Attribute("class", "mdnr-gantt-title");
            SvgText.EmitCentered(
                svg,
                layout.Title,
                layout.TitleCenterX,
                layout.TitleCenterY,
                "middle",
                options,
                fontSize,
                _theme.TextFill,
                _theme.TitleWeight);
            svg.EndElement();
        }

        EmitAxis(svg, layout, options, fontSize);

        svg.StartElement("g").Attribute("class", "mdnr-gantt-sections");
        foreach (GanttSectionRow section in layout.Sections)
        {
            SvgText.EmitLine(
                svg,
                section.Name,
                layout.LabelLeft,
                section.CenterY,
                "start",
                options,
                fontSize,
                _theme.TextFill,
                _theme.SectionWeight);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-gantt-tasks");
        foreach (GanttBarRow row in layout.Bars)
        {
            EmitTask(svg, layout, row, options, fontSize);
        }

        svg.EndElement();

        svg.EndElement();
        return svg.ToString();
    }

    private void EmitAxis(
        SvgBuilder svg,
        GanttChartLayout layout,
        RenderOptions options,
        double fontSize)
    {
        double axisFontSize = fontSize * _theme.AxisFontScale;

        svg.StartElement("g").Attribute("class", "mdnr-gantt-axis");
        svg.StartElement("line")
            .Attribute("x1", layout.ChartLeft)
            .Attribute("y1", layout.AxisY)
            .Attribute("x2", layout.ChartRight)
            .Attribute("y2", layout.AxisY)
            .Attribute("stroke", _theme.GridStroke)
            .Attribute("stroke-width", _theme.GridStrokeWidth)
            .EndElement();

        foreach (GanttTick tick in layout.Ticks)
        {
            svg.StartElement("line")
                .Attribute("x1", tick.X)
                .Attribute("y1", layout.AxisY)
                .Attribute("x2", tick.X)
                .Attribute("y2", layout.RowsBottom)
                .Attribute("stroke", _theme.GridStroke)
                .Attribute("stroke-width", _theme.GridStrokeWidth)
                .EndElement();

            SvgText.EmitLine(
                svg,
                tick.Label,
                tick.X,
                layout.AxisY - (TextMetrics.LineHeight(axisFontSize) / 2),
                "middle",
                options,
                axisFontSize,
                _theme.TextFill);
        }

        svg.EndElement();
    }

    private void EmitTask(
        SvgBuilder svg,
        GanttChartLayout layout,
        GanttBarRow row,
        RenderOptions options,
        double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", $"mdnr-gantt-task mdnr-gantt-{CssState(row.Task)}")
            .Attribute("data-task", row.Task.Name);

        SvgText.EmitLine(
            svg,
            row.Task.Name,
            layout.LabelLeft,
            row.CenterY,
            "start",
            options,
            fontSize,
            _theme.TextFill);

        if (row.Task.IsMilestone)
        {
            double half = _metrics.MilestoneSize / 2;
            svg.StartElement("polygon")
                .Attribute(
                    "points",
                    string.Join(
                        ' ',
                        GraphGeometry.PointPair(row.BarLeft, row.CenterY - half),
                        GraphGeometry.PointPair(row.BarLeft + half, row.CenterY),
                        GraphGeometry.PointPair(row.BarLeft, row.CenterY + half),
                        GraphGeometry.PointPair(row.BarLeft - half, row.CenterY)))
                .Attribute("fill", _theme.MilestoneFill)
                .Attribute("stroke", _theme.Stroke(row.Task))
                .Attribute("stroke-width", _theme.StrokeWidth(row.Task))
                .EndElement();
        }
        else
        {
            svg.StartElement("rect")
                .Attribute("x", row.BarLeft)
                .Attribute("y", row.CenterY - (_metrics.BarHeight / 2))
                .Attribute("width", row.BarWidth)
                .Attribute("height", _metrics.BarHeight)
                .Attribute("rx", _theme.BarStrokeWidth * 2)
                .Attribute("ry", _theme.BarStrokeWidth * 2)
                .Attribute("fill", _theme.Fill(row.Task.State))
                .Attribute("stroke", _theme.Stroke(row.Task))
                .Attribute("stroke-width", _theme.StrokeWidth(row.Task))
                .EndElement();
        }

        svg.EndElement();
    }

    private static string CssState(GanttTask task)
    {
        if (task.IsMilestone)
        {
            return "milestone";
        }

        return task.State switch
        {
            GanttTaskState.Done => "done",
            GanttTaskState.Active => "active",
            _ => task.IsCritical ? "crit" : "planned",
        };
    }
}
