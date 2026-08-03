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

namespace MarkdownDotNetRenderer.Mermaid.State;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>stateDiagram</c> and <c>stateDiagram-v2</c>: states as
/// stadiums, <c>[*]</c> as filled circles, transitions routed by the shared layered layout.
/// </summary>
public sealed class StateRenderer : IDiagramRenderer
{
    private readonly LayoutMetrics _metrics;
    private readonly StateTheme _theme;

    /// <summary>Creates a renderer with the default geometry and theme.</summary>
    public StateRenderer()
        : this(new LayoutMetrics(), StateTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and visual theme.</summary>
    /// <param name="metrics">Layered-layout geometry and guards.</param>
    /// <param name="theme">Colours, paddings, and stroke widths.</param>
    public StateRenderer(LayoutMetrics metrics, StateTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } =
        [StateParser.HeaderKeyword, StateParser.HeaderKeywordV2];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        StateParseResult parsed;
        try
        {
            parsed = StateParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The state-diagram source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The state-diagram source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        StateDiagramModel model = parsed.Model;
        double fontSize = DiagramDefaults.ResolveFontSize(options);
        string idPrefix = DiagramIds.ForSource(mermaidSource);

        StateLayoutResult layout =
            StateLayoutEngine.Compute(model, fontSize, _theme, _metrics, idPrefix);
        if (!layout.Success || layout.Layout is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramTooLarge,
                layout.FailureMessage ?? "The state diagram could not be laid out.",
                parsed.Diagnostics);
        }

        string altText =
            $"state diagram with {model.States.Count} states and {model.Transitions.Count} transitions";
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
        StateDiagramLayout layout,
        GraphCanvas canvas,
        RenderOptions options,
        double fontSize,
        string idPrefix,
        string altText)
    {
        var svg = new SvgBuilder();
        DiagramSvg.StartRoot(svg, canvas.Width, canvas.Height, options, altText, "mdnr-state");

        GraphMarkers.EmitDefs(
            svg,
            idPrefix,
            [GraphMarker.Arrow],
            _theme.Edge.Stroke,
            _theme.Edge.StrokeWidth,
            _theme.StateFill,
            _theme.MarkerSize);

        canvas.StartShift(svg);

        svg.StartElement("g").Attribute("class", "mdnr-transitions");
        foreach (PlacedEdge edge in layout.Placement.Edges)
        {
            GraphEdgePainter.EmitEdge(svg, edge, layout.Placement, _theme.Edge);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-states");
        foreach (StateBox box in layout.Boxes)
        {
            EmitBox(svg, box, options, fontSize);
        }

        svg.EndElement();

        // Labels last so their opaque backing rects sit above the lines they interrupt.
        svg.StartElement("g").Attribute("class", "mdnr-transition-labels");
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

    private void EmitBox(SvgBuilder svg, StateBox box, RenderOptions options, double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", $"mdnr-state-node mdnr-state-{CssKind(box.Node.Kind)}")
            .Attribute("data-id", box.Node.Id);

        switch (box.Node.Kind)
        {
            case StateKind.Start:
                svg.StartElement("circle")
                    .Attribute("cx", box.Box.CenterX)
                    .Attribute("cy", box.Box.CenterY)
                    .Attribute("r", _theme.PseudoRadius)
                    .Attribute("fill", _theme.PseudoFill)
                    .EndElement();
                break;

            case StateKind.End:
                svg.StartElement("circle")
                    .Attribute("cx", box.Box.CenterX)
                    .Attribute("cy", box.Box.CenterY)
                    .Attribute("r", _theme.PseudoRadius + _theme.EndRingGap)
                    .Attribute("fill", _theme.StateFill)
                    .Attribute("stroke", _theme.PseudoFill)
                    .Attribute("stroke-width", _theme.StateStrokeWidth)
                    .EndElement();
                svg.StartElement("circle")
                    .Attribute("cx", box.Box.CenterX)
                    .Attribute("cy", box.Box.CenterY)
                    .Attribute("r", _theme.PseudoRadius - _theme.EndRingGap)
                    .Attribute("fill", _theme.PseudoFill)
                    .EndElement();
                break;

            case StateKind.Note:
                EmitRect(svg, box, _theme.NoteFill, _theme.NoteStroke, _theme.NoteDashArray);
                break;

            case StateKind.Normal:
            default:
                EmitRect(svg, box, _theme.StateFill, _theme.StateStroke, dashArray: null);
                break;
        }

        if (box.Lines.Count > 0)
        {
            SvgText.EmitCentered(
                svg,
                box.Lines,
                box.Box.CenterX,
                box.Box.CenterY,
                "middle",
                options,
                fontSize,
                _theme.TextFill);
        }

        svg.EndElement();
    }

    private void EmitRect(
        SvgBuilder svg,
        StateBox box,
        string fill,
        string stroke,
        string? dashArray)
    {
        // A stadium is a rectangle whose corner radius is half its height, which is exactly how the
        // flowchart draws its stadium nodes; notes use the same box with a dashed border.
        svg.StartElement("rect")
            .Attribute("x", box.Box.Left)
            .Attribute("y", box.Box.Top)
            .Attribute("width", box.Box.Width)
            .Attribute("height", box.Box.Height)
            .Attribute("rx", dashArray is null ? box.Box.Height / 2 : 0)
            .Attribute("ry", dashArray is null ? box.Box.Height / 2 : 0)
            .Attribute("fill", fill)
            .Attribute("stroke", stroke)
            .Attribute("stroke-width", _theme.StateStrokeWidth);

        if (dashArray is { Length: > 0 })
        {
            svg.Attribute("stroke-dasharray", dashArray);
        }

        svg.EndElement();
    }

    private static string CssKind(StateKind kind) => kind switch
    {
        StateKind.Start => "start",
        StateKind.End => "end",
        StateKind.Note => "note",
        _ => "normal",
    };
}
