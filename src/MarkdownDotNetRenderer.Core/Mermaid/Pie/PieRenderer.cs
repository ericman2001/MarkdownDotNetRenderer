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

using MarkdownDotNetRenderer.Mermaid.Graph;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Pie;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>pie</c> charts: parse, compute wedge angles with
/// <see cref="PieLayoutEngine"/>, and emit a self-contained SVG fragment. Needs no layout solver
/// and no writer changes.
/// </summary>
public sealed class PieRenderer : IDiagramRenderer
{
    private readonly PieMetrics _metrics;
    private readonly PieTheme _theme;

    /// <summary>Creates a renderer with the default geometry and theme.</summary>
    public PieRenderer()
        : this(PieMetrics.Default, PieTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit geometry and visual theme.</summary>
    /// <param name="metrics">Geometry to use.</param>
    /// <param name="theme">Colours and stroke widths to use.</param>
    public PieRenderer(PieMetrics metrics, PieTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } = [PieParser.HeaderKeyword];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        PieParseResult parsed;
        try
        {
            parsed = PieParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The pie-chart source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The pie-chart source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        PieModel model = parsed.Model;
        double fontSize = DiagramDefaults.ResolveFontSize(options);
        PieChartLayout layout =
            PieLayoutEngine.Compute(model, fontSize, _metrics, _theme.LabelWrapChars);

        string altText = $"pie chart with {model.Slices.Count} slices";
        string svg = Emit(layout, options, fontSize, altText);

        return new DiagramRenderResult(
            true, svg, layout.Width, layout.Height, altText, parsed.Diagnostics);
    }

    private string Emit(
        PieChartLayout layout,
        RenderOptions options,
        double fontSize,
        string altText)
    {
        var svg = new SvgBuilder();
        DiagramSvg.StartRoot(svg, layout.Width, layout.Height, options, altText, "mdnr-pie");

        if (layout.Title.Count > 0)
        {
            svg.StartElement("g").Attribute("class", "mdnr-pie-title");
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

        svg.StartElement("g").Attribute("class", "mdnr-pie-slices");
        foreach (PieSliceLayout slice in layout.Slices)
        {
            svg.StartElement("g")
                .Attribute("class", "mdnr-pie-slice")
                .Attribute("data-label", slice.Slice.Label);

            if (slice.IsFullCircle)
            {
                svg.StartElement("circle")
                    .Attribute("cx", layout.CenterX)
                    .Attribute("cy", layout.CenterY)
                    .Attribute("r", layout.Radius);
            }
            else
            {
                svg.StartElement("path")
                    .Attribute("d", PieLayoutEngine.WedgePath(
                        slice, layout.CenterX, layout.CenterY, layout.Radius));
            }

            svg.Attribute("fill", slice.Colour)
                .Attribute("stroke", _theme.SliceStroke)
                .Attribute("stroke-width", _theme.SliceStrokeWidth)
                .EndElement();

            svg.EndElement();
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-pie-legend");
        foreach (PieLegendRow row in layout.Legend)
        {
            svg.StartElement("g").Attribute("class", "mdnr-pie-legend-row");
            svg.StartElement("rect")
                .Attribute("x", row.SwatchLeft)
                .Attribute("y", row.SwatchTop)
                .Attribute("width", _metrics.SwatchSize)
                .Attribute("height", _metrics.SwatchSize)
                .Attribute("fill", row.Colour)
                .Attribute("stroke", _theme.SliceStroke)
                .Attribute("stroke-width", _theme.SliceStrokeWidth)
                .EndElement();

            SvgText.EmitLine(
                svg,
                row.Text,
                row.TextLeft,
                row.CenterY,
                "start",
                options,
                fontSize,
                _theme.TextFill);
            svg.EndElement();
        }

        svg.EndElement();

        svg.EndElement();
        return svg.ToString();
    }
}
