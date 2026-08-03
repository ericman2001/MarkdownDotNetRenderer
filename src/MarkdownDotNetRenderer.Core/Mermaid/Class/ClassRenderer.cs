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

namespace MarkdownDotNetRenderer.Mermaid.Class;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>classDiagram</c>: three-compartment boxes — name,
/// attributes, operations — joined by relations whose markers distinguish inheritance, composition,
/// aggregation, association, dependency, and realization.
/// </summary>
public sealed class ClassRenderer : IDiagramRenderer
{
    private readonly LayoutMetrics _metrics;
    private readonly ClassTheme _theme;

    /// <summary>Creates a renderer with the default geometry and theme.</summary>
    public ClassRenderer()
        : this(new LayoutMetrics(), ClassTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and visual theme.</summary>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <param name="theme">Colours, paddings, and stroke widths.</param>
    public ClassRenderer(LayoutMetrics metrics, ClassTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } =
        [ClassParser.HeaderKeyword, ClassParser.HeaderKeywordV2];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        ClassParseResult parsed;
        try
        {
            parsed = ClassParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The class-diagram source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The class-diagram source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        ClassDiagramModel model = parsed.Model;
        double fontSize = DiagramDefaults.ResolveFontSize(options);
        string idPrefix = DiagramIds.ForSource(mermaidSource);

        ClassLayoutResult layout =
            ClassLayoutEngine.Compute(model, fontSize, _theme, _metrics, idPrefix);
        if (!layout.Success || layout.Layout is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramTooLarge,
                layout.FailureMessage ?? "The class diagram could not be laid out.",
                parsed.Diagnostics);
        }

        string altText =
            $"class diagram with {model.Classes.Count} classes and {model.Relations.Count} relations";
        GraphCanvas canvas = GraphCanvas.Measure(
            layout.Layout.Placement, _theme.Edge, fontSize, _metrics.Margin);
        string svg = Emit(layout.Layout, canvas, options, fontSize, idPrefix, altText);

        return new DiagramRenderResult(
            true,
            svg,
            canvas.Width,
            canvas.Height,
            altText,
            parsed.Diagnostics);
    }

    private string Emit(
        ClassDiagramLayout layout,
        GraphCanvas canvas,
        RenderOptions options,
        double fontSize,
        string idPrefix,
        string altText)
    {
        var svg = new SvgBuilder();
        DiagramSvg.StartRoot(svg, canvas.Width, canvas.Height, options, altText, "mdnr-class");

        GraphMarkers.EmitDefs(
            svg,
            idPrefix,
            layout.Markers,
            _theme.Edge.Stroke,
            _theme.Edge.StrokeWidth,
            _theme.BoxFill,
            _theme.MarkerSize);

        canvas.StartShift(svg);

        svg.StartElement("g").Attribute("class", "mdnr-relations");
        foreach (PlacedEdge edge in layout.Placement.Edges)
        {
            GraphEdgePainter.EmitEdge(svg, edge, layout.Placement, _theme.Edge);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-classes");
        foreach (ClassBoxLayout box in layout.Boxes)
        {
            EmitBox(svg, box, options, fontSize);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-relation-labels");
        foreach (PlacedEdge edge in layout.Placement.Edges)
        {
            GraphEdgePainter.EmitLabels(
                svg, edge, layout.Placement, _theme.Edge, options, fontSize);
        }

        svg.EndElement();

        canvas.EndShift(svg);

        svg.EndElement();
        return svg.ToString();
    }

    private void EmitBox(
        SvgBuilder svg,
        ClassBoxLayout box,
        RenderOptions options,
        double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", "mdnr-class-node")
            .Attribute("data-id", box.Definition.Name);

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

        double attributesTop = box.Box.Top + box.HeaderHeight;
        double operationsTop = attributesTop + box.AttributesHeight;
        EmitDivider(svg, box, operationsTop);

        SvgText.EmitCentered(
            svg,
            box.HeaderLines,
            box.Box.CenterX,
            box.Box.Top + (box.HeaderHeight / 2),
            "middle",
            options,
            fontSize,
            _theme.TextFill,
            _theme.NameWeight);

        EmitMembers(svg, box, box.AttributeLines, attributesTop, options, fontSize);
        EmitMembers(svg, box, box.OperationLines, operationsTop, options, fontSize);

        svg.EndElement();
    }

    private void EmitDivider(SvgBuilder svg, ClassBoxLayout box, double y) =>
        svg.StartElement("line")
            .Attribute("x1", box.Box.Left)
            .Attribute("y1", y)
            .Attribute("x2", box.Box.Left + box.Box.Width)
            .Attribute("y2", y)
            .Attribute("stroke", _theme.BoxStroke)
            .Attribute("stroke-width", _theme.BoxStrokeWidth)
            .EndElement();

    private void EmitMembers(
        SvgBuilder svg,
        ClassBoxLayout box,
        IReadOnlyList<string> lines,
        double top,
        RenderOptions options,
        double fontSize)
    {
        if (lines.Count == 0)
        {
            return;
        }

        SvgText.EmitFromTop(
            svg,
            lines,
            box.Box.Left + _theme.HorizontalPadding,
            top + _theme.CompartmentPadding,
            "start",
            options,
            fontSize,
            _theme.TextFill);
    }
}
