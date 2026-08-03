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

namespace MarkdownDotNetRenderer.Mermaid.Er;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>erDiagram</c>: two-compartment entity boxes joined by
/// relationships whose crow's-foot markers show the cardinality at both ends.
/// </summary>
public sealed class ErRenderer : IDiagramRenderer
{
    private readonly LayoutMetrics _metrics;
    private readonly ErTheme _theme;

    /// <summary>Creates a renderer with the default geometry and theme.</summary>
    public ErRenderer()
        : this(new LayoutMetrics(), ErTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and visual theme.</summary>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <param name="theme">Colours, paddings, and stroke widths.</param>
    public ErRenderer(LayoutMetrics metrics, ErTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } = [ErParser.HeaderKeyword];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        ErParseResult parsed;
        try
        {
            parsed = ErParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The ER-diagram source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The ER-diagram source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        ErDiagramModel model = parsed.Model;
        double fontSize = DiagramDefaults.ResolveFontSize(options);

        ErLayoutResult layout = ErLayoutEngine.Compute(
            model, fontSize, _theme, _metrics);
        if (!layout.Success || layout.Layout is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramTooLarge,
                layout.FailureMessage ?? "The ER diagram could not be laid out.",
                parsed.Diagnostics);
        }

        string altText = $"entity relationship diagram with {model.Entities.Count} entities and " +
            $"{model.Relationships.Count} relationships";
        GraphCanvas canvas = GraphCanvas.Measure(
            layout.Layout.Placement, _theme.Edge, fontSize, _metrics.Margin);
        string svg = Emit(layout.Layout, canvas, options, fontSize, altText);

        return new DiagramRenderResult(
            true,
            svg,
            canvas.Width,
            canvas.Height,
            altText,
            parsed.Diagnostics);
    }

    private string Emit(
        ErDiagramLayout layout,
        GraphCanvas canvas,
        RenderOptions options,
        double fontSize,
        string altText)
    {
        var svg = new SvgBuilder();
        DiagramSvg.StartRoot(svg, canvas.Width, canvas.Height, options, altText, "mdnr-er");

        canvas.StartShift(svg);

        svg.StartElement("g").Attribute("class", "mdnr-relationships");
        foreach (PlacedEdge edge in layout.Placement.Edges)
        {
            GraphEdgePainter.EmitEdge(svg, edge, layout.Placement, _theme.Edge);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-entities");
        foreach (ErBoxLayout box in layout.Boxes)
        {
            EmitBox(svg, box, options, fontSize);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-relationship-labels");
        GraphEdgePainter.EmitLabels(svg, layout.Placement, _theme.Edge, options, fontSize);

        svg.EndElement();

        canvas.EndShift(svg);

        svg.EndElement();
        return svg.ToString();
    }

    private void EmitBox(SvgBuilder svg, ErBoxLayout box, RenderOptions options, double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", "mdnr-entity")
            .Attribute("data-id", box.Entity.Name);

        svg.StartElement("rect")
            .Attribute("x", box.Box.Left)
            .Attribute("y", box.Box.Top)
            .Attribute("width", box.Box.Width)
            .Attribute("height", box.Box.Height)
            .Attribute("fill", _theme.BoxFill)
            .Attribute("stroke", _theme.BoxStroke)
            .Attribute("stroke-width", _theme.BoxStrokeWidth)
            .EndElement();

        svg.StartElement("rect")
            .Attribute("x", box.Box.Left)
            .Attribute("y", box.Box.Top)
            .Attribute("width", box.Box.Width)
            .Attribute("height", box.HeaderHeight)
            .Attribute("fill", _theme.HeaderFill)
            .Attribute("stroke", _theme.BoxStroke)
            .Attribute("stroke-width", _theme.BoxStrokeWidth)
            .EndElement();

        SvgText.EmitLine(
            svg,
            box.Entity.Name,
            box.Box.CenterX,
            box.Box.Top + (box.HeaderHeight / 2),
            "middle",
            options,
            fontSize,
            _theme.TextFill,
            _theme.NameWeight);

        if (box.AttributeLines.Count > 0)
        {
            SvgText.EmitFromTop(
                svg,
                box.AttributeLines,
                box.Box.Left + _theme.HorizontalPadding,
                box.Box.Top + box.HeaderHeight + _theme.CompartmentPadding,
                "start",
                options,
                fontSize,
                _theme.TextFill);
        }

        svg.EndElement();
    }
}
