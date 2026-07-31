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

namespace MarkdownDotNetRenderer.Mermaid.Sequence;

/// <summary>
/// The <see cref="IDiagramRenderer"/> for <c>sequenceDiagram</c>: parse, lay out with
/// <see cref="SequenceLayoutEngine"/>, and emit a self-contained SVG fragment whose visual
/// attributes are all inline, so it renders identically inlined in HTML or stored as a standalone
/// SVG part in an office package. No writer knows this diagram type exists.
/// </summary>
public sealed class SequenceRenderer : IDiagramRenderer
{
    private readonly SequenceMetrics _metrics;
    private readonly SequenceTheme _theme;

    /// <summary>Creates a renderer with the default layout geometry and theme.</summary>
    public SequenceRenderer()
        : this(SequenceMetrics.Default, SequenceTheme.Default)
    {
    }

    /// <summary>Creates a renderer with explicit layout geometry and visual theme.</summary>
    /// <param name="metrics">Geometry and guards to use.</param>
    /// <param name="theme">Colours, stroke widths, and box geometry to use.</param>
    public SequenceRenderer(SequenceMetrics metrics, SequenceTheme theme)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(theme);
        _metrics = metrics;
        _theme = theme;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DiagramTypes { get; } = [SequenceParser.HeaderKeyword];

    /// <inheritdoc />
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        SequenceParseResult parsed;
        try
        {
            parsed = SequenceParser.Parse(mermaidSource);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The sequence diagram source could not be parsed: {ex.Message}");
        }

        if (!parsed.Success || parsed.Model is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"The sequence diagram source could not be parsed: {parsed.FailureMessage}",
                parsed.Diagnostics);
        }

        SequenceModel model = parsed.Model;
        double fontSize = options.DiagramFontSize <= 0 ? 12 : options.DiagramFontSize;

        SequenceLayoutResult layout = SequenceLayoutEngine.Compute(
            model, fontSize, _metrics, _theme.LabelWrapChars);
        if (!layout.Success || layout.Layout is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramTooLarge,
                $"The sequence diagram was not laid out: {layout.FailureMessage}",
                parsed.Diagnostics);
        }

        int messageCount = model.Events.Count(item => item is SequenceMessage);
        string altText =
            $"sequence diagram with {model.Actors.Count} participants and {messageCount} messages";
        string svg = Emit(
            layout.Layout, options, fontSize, DiagramIds.ForSource(mermaidSource), altText);

        return new DiagramRenderResult(
            true,
            svg,
            layout.Layout.Width,
            layout.Layout.Height,
            altText,
            parsed.Diagnostics);
    }

    private string Emit(
        SequenceLayout layout,
        RenderOptions options,
        double fontSize,
        string idPrefix,
        string altText)
    {
        var svg = new SvgBuilder();

        double width = layout.Width;
        double height = layout.Height;
        double renderWidth = width;
        double renderHeight = height;
        if (options.MaxDiagramWidth > 0 && width > options.MaxDiagramWidth)
        {
            // Keep the viewBox intrinsic and clamp the presented size, so the consumer scales the
            // diagram down instead of clipping it.
            renderWidth = options.MaxDiagramWidth;
            renderHeight = height * (options.MaxDiagramWidth / width);
        }

        svg.StartElement("svg")
            .Attribute("xmlns", SvgBuilder.SvgNamespace)
            .Attribute("width", renderWidth)
            .Attribute("height", renderHeight)
            .Attribute("viewBox", $"0 0 {SvgBuilder.Number(width)} {SvgBuilder.Number(height)}")
            .Attribute("role", "img")
            .Attribute("aria-label", altText)
            .Attribute("class", "mdnr-sequence");

        EmitDefs(svg, idPrefix);

        svg.StartElement("g").Attribute("class", "mdnr-lifelines");
        foreach (ActorColumn column in layout.Actors)
        {
            svg.StartElement("line")
                .Attribute("class", "mdnr-lifeline")
                .Attribute("data-id", column.Actor.Id)
                .Attribute("x1", column.CenterX)
                .Attribute("y1", layout.LifelineTop)
                .Attribute("x2", column.CenterX)
                .Attribute("y2", layout.LifelineBottom)
                .Attribute("stroke", _theme.LifelineStroke)
                .Attribute("stroke-width", _theme.LifelineStrokeWidth)
                .Attribute("stroke-dasharray", _theme.LifelineDashArray)
                .EndElement();
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-actors");
        foreach (ActorColumn column in layout.Actors)
        {
            EmitActor(svg, column, options, fontSize);
        }

        svg.EndElement();

        svg.StartElement("g").Attribute("class", "mdnr-messages");
        foreach (SequenceRow row in layout.Rows)
        {
            switch (row)
            {
                case MessageRow message:
                    EmitMessage(svg, message, options, fontSize, idPrefix);
                    break;
                case SelfMessageRow self:
                    EmitSelfMessage(svg, self, options, fontSize, idPrefix);
                    break;
                case NoteRow note:
                    EmitNote(svg, note, options, fontSize);
                    break;
                default:
                    break;
            }
        }

        svg.EndElement();

        svg.EndElement();
        return svg.ToString();
    }

    /// <summary>Defines the three arrowhead markers once, with ids unique to this diagram.</summary>
    private void EmitDefs(SvgBuilder svg, string idPrefix)
    {
        svg.StartElement("defs");

        StartMarker(svg, MarkerId(idPrefix, SequenceArrowHead.Filled));
        svg.StartElement("path")
            .Attribute("d", "M 0 0 L 10 5 L 0 10 z")
            .Attribute("fill", _theme.MessageStroke)
            .EndElement();
        svg.EndElement();

        StartMarker(svg, MarkerId(idPrefix, SequenceArrowHead.Open));
        svg.StartElement("path")
            .Attribute("d", "M 0 0 L 10 5 L 0 10")
            .Attribute("fill", "none")
            .Attribute("stroke", _theme.MessageStroke)
            .Attribute("stroke-width", _theme.MessageStrokeWidth)
            .EndElement();
        svg.EndElement();

        // The async arrow is the open chevron's lower half, as mermaid draws '-)'.
        StartMarker(svg, MarkerId(idPrefix, SequenceArrowHead.Async));
        svg.StartElement("path")
            .Attribute("d", "M 0 10 L 10 5")
            .Attribute("fill", "none")
            .Attribute("stroke", _theme.MessageStroke)
            .Attribute("stroke-width", _theme.MessageStrokeWidth)
            .EndElement();
        svg.EndElement();

        StartMarker(svg, MarkerId(idPrefix, SequenceArrowHead.Cross));
        svg.StartElement("path")
            .Attribute("d", "M 1 1 L 9 9 M 9 1 L 1 9")
            .Attribute("fill", "none")
            .Attribute("stroke", _theme.MessageStroke)
            .Attribute("stroke-width", _theme.MessageStrokeWidth)
            .EndElement();
        svg.EndElement();

        svg.EndElement();
    }

    private void StartMarker(SvgBuilder svg, string id) =>
        svg.StartElement("marker")
            .Attribute("id", id)
            .Attribute("viewBox", "0 0 10 10")
            .Attribute("refX", 9.0)
            .Attribute("refY", 5.0)
            .Attribute("markerWidth", _theme.MarkerSize)
            .Attribute("markerHeight", _theme.MarkerSize)
            .Attribute("orient", "auto-start-reverse");

    /// <summary>The marker id for one arrowhead style within one diagram.</summary>
    /// <param name="idPrefix">The diagram's id prefix.</param>
    /// <param name="head">The arrowhead style.</param>
    /// <returns>The marker id.</returns>
    public static string MarkerId(string idPrefix, SequenceArrowHead head)
    {
        ArgumentException.ThrowIfNullOrEmpty(idPrefix);

        string suffix = head switch
        {
            SequenceArrowHead.Filled => "arrow-filled",
            SequenceArrowHead.Open => "arrow-open",
            SequenceArrowHead.Cross => "arrow-cross",
            SequenceArrowHead.Async => "arrow-async",
            _ => "arrow-filled",
        };

        return $"{idPrefix}-{suffix}";
    }

    private void EmitActor(
        SvgBuilder svg,
        ActorColumn column,
        RenderOptions options,
        double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", "mdnr-actor")
            .Attribute("data-id", column.Actor.Id);

        svg.StartElement("rect")
            .Attribute("x", column.CenterX - (column.Width / 2))
            .Attribute("y", column.Top)
            .Attribute("width", column.Width)
            .Attribute("height", column.Height)
            .Attribute("rx", _theme.ActorCornerRadius)
            .Attribute("ry", _theme.ActorCornerRadius)
            .Attribute("fill", _theme.ActorFill)
            .Attribute("stroke", _theme.ActorStroke)
            .Attribute("stroke-width", _theme.ActorStrokeWidth)
            .EndElement();

        EmitTextBlock(
            svg,
            column.LabelLines,
            column.CenterX,
            column.Top + ((column.Height - (column.LabelLines.Count *
                TextMetrics.LineHeight(fontSize))) / 2),
            "middle",
            options,
            fontSize);

        svg.EndElement();
    }

    private void EmitMessage(
        SvgBuilder svg,
        MessageRow row,
        RenderOptions options,
        double fontSize,
        string idPrefix)
    {
        SequenceMessage message = row.Message;

        svg.StartElement("g")
            .Attribute("class", "mdnr-message")
            .Attribute("data-source", message.SourceId)
            .Attribute("data-target", message.TargetId);

        svg.StartElement("line")
            .Attribute("x1", row.StartX)
            .Attribute("y1", row.LineY)
            .Attribute("x2", row.EndX)
            .Attribute("y2", row.LineY)
            .Attribute("stroke", _theme.MessageStroke)
            .Attribute("stroke-width", _theme.MessageStrokeWidth)
            .Attribute("fill", "none")
            .Attribute("marker-end", $"url(#{MarkerId(idPrefix, message.Head)})");
        if (message.Line == SequenceLineStyle.Dashed)
        {
            svg.Attribute("stroke-dasharray", _theme.MessageDashArray);
        }

        svg.EndElement();

        if (message.DisplayLabel.Length > 0)
        {
            double lineHeight = TextMetrics.LineHeight(fontSize);
            double textHeight = row.LabelLines.Count * lineHeight;
            double top = row.LineY - _metrics.MessageLabelGap - textHeight;
            double centerX = (row.StartX + row.EndX) / 2;

            svg.StartElement("rect")
                .Attribute("x", centerX - (row.LabelWidth / 2) - _theme.LabelPaddingX)
                .Attribute("y", top - _theme.LabelPaddingY)
                .Attribute("width", row.LabelWidth + (2 * _theme.LabelPaddingX))
                .Attribute("height", textHeight + (2 * _theme.LabelPaddingY))
                .Attribute("fill", _theme.LabelBackground)
                .Attribute("stroke", "none")
                .EndElement();

            EmitTextBlock(svg, row.LabelLines, centerX, top, "middle", options, fontSize);
        }

        svg.EndElement();
    }

    private void EmitSelfMessage(
        SvgBuilder svg,
        SelfMessageRow row,
        RenderOptions options,
        double fontSize,
        string idPrefix)
    {
        SequenceMessage message = row.Message;

        svg.StartElement("g")
            .Attribute("class", "mdnr-message mdnr-self-message")
            .Attribute("data-source", message.SourceId)
            .Attribute("data-target", message.TargetId);

        string path = string.Create(
            CultureInfo.InvariantCulture,
            $"M {SvgBuilder.Number(row.LifelineX)} {SvgBuilder.Number(row.LoopTop)} " +
            $"L {SvgBuilder.Number(row.LifelineX + row.LoopWidth)} {SvgBuilder.Number(row.LoopTop)} " +
            $"L {SvgBuilder.Number(row.LifelineX + row.LoopWidth)} " +
            $"{SvgBuilder.Number(row.LoopTop + row.LoopHeight)} " +
            $"L {SvgBuilder.Number(row.LifelineX)} {SvgBuilder.Number(row.LoopTop + row.LoopHeight)}");

        svg.StartElement("path")
            .Attribute("d", path)
            .Attribute("stroke", _theme.MessageStroke)
            .Attribute("stroke-width", _theme.MessageStrokeWidth)
            .Attribute("fill", "none")
            .Attribute("marker-end", $"url(#{MarkerId(idPrefix, message.Head)})");
        if (message.Line == SequenceLineStyle.Dashed)
        {
            svg.Attribute("stroke-dasharray", _theme.MessageDashArray);
        }

        svg.EndElement();

        if (message.DisplayLabel.Length > 0)
        {
            double textHeight = row.LabelLines.Count * TextMetrics.LineHeight(fontSize);
            double top = row.LoopTop + (row.LoopHeight / 2) - (textHeight / 2);
            EmitTextBlock(
                svg,
                row.LabelLines,
                row.LifelineX + row.LoopWidth + _metrics.SelfLoopLabelGap,
                top,
                "start",
                options,
                fontSize);
        }

        svg.EndElement();
    }

    private void EmitNote(SvgBuilder svg, NoteRow row, RenderOptions options, double fontSize)
    {
        svg.StartElement("g")
            .Attribute("class", "mdnr-note")
            .Attribute("data-actors", string.Join(',', row.Note.ActorIds));

        svg.StartElement("rect")
            .Attribute("x", row.BoxLeft)
            .Attribute("y", row.Top)
            .Attribute("width", row.BoxWidth)
            .Attribute("height", row.BoxHeight)
            .Attribute("rx", _theme.NoteCornerRadius)
            .Attribute("ry", _theme.NoteCornerRadius)
            .Attribute("fill", _theme.NoteFill)
            .Attribute("stroke", _theme.NoteStroke)
            .Attribute("stroke-width", _theme.NoteStrokeWidth)
            .EndElement();

        EmitTextBlock(
            svg,
            row.Lines,
            row.BoxLeft + (row.BoxWidth / 2),
            row.Top + _metrics.NotePaddingY,
            "middle",
            options,
            fontSize);

        svg.EndElement();
    }

    /// <summary>
    /// Writes one <c>&lt;text&gt;</c> with a <c>&lt;tspan&gt;</c> per line, the block's first line
    /// centred on <paramref name="top"/> plus half a line.
    /// </summary>
    private void EmitTextBlock(
        SvgBuilder svg,
        IReadOnlyList<string> lines,
        double anchorX,
        double top,
        string anchor,
        RenderOptions options,
        double fontSize)
    {
        double lineHeight = TextMetrics.LineHeight(fontSize);

        svg.StartElement("text")
            .Attribute("x", anchorX)
            .Attribute("y", top)
            .Attribute("text-anchor", anchor)
            .Attribute("dominant-baseline", "middle")
            .Attribute("font-family", options.FontFamily)
            .Attribute("font-size", fontSize)
            .Attribute("fill", _theme.TextFill);

        for (int i = 0; i < lines.Count; i++)
        {
            svg.StartElement("tspan")
                .Attribute("x", anchorX)
                .Attribute("dy", i == 0 ? lineHeight / 2 : lineHeight)
                .Text(lines[i])
                .EndElement();
        }

        svg.EndElement();
    }
}
