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

namespace MarkdownDotNetRenderer.Mermaid.Pie;

/// <summary>Tunable geometry for <see cref="PieLayoutEngine"/>. All values are CSS pixels.</summary>
/// <param name="Margin">Margin around the whole chart.</param>
/// <param name="Radius">Radius of the pie itself.</param>
/// <param name="TitleGap">Gap between the title and the pie.</param>
/// <param name="LegendGap">Gap between the pie and the legend column.</param>
/// <param name="SwatchSize">Side length of a legend colour swatch.</param>
/// <param name="SwatchTextGap">Gap between a swatch and its legend text.</param>
/// <param name="LegendRowGap">Vertical gap between legend rows.</param>
public sealed record PieMetrics(
    double Margin = 16,
    double Radius = 110,
    double TitleGap = 12,
    double LegendGap = 28,
    double SwatchSize = 12,
    double SwatchTextGap = 8,
    double LegendRowGap = 6)
{
    /// <summary>The defaults documented in docs/phases/phase-4-additional-diagrams.md.</summary>
    public static PieMetrics Default { get; } = new();
}

/// <summary>One placed slice: its wedge angles plus the share it represents.</summary>
/// <param name="Slice">The parsed slice.</param>
/// <param name="StartAngle">Wedge start, in degrees clockwise from twelve o'clock.</param>
/// <param name="SweepAngle">Wedge sweep, in degrees.</param>
/// <param name="Fraction">The slice's share of the total, in <c>[0, 1]</c>.</param>
/// <param name="Colour">The palette colour assigned to this slice.</param>
public sealed record PieSliceLayout(
    PieSlice Slice,
    double StartAngle,
    double SweepAngle,
    double Fraction,
    string Colour)
{
    /// <summary>Whether this slice is the whole pie, which is drawn as a circle, not an arc.</summary>
    public bool IsFullCircle => SweepAngle >= PieLayoutEngine.FullTurnDegrees;
}

/// <summary>One legend row: a swatch, and the text describing its slice.</summary>
/// <param name="SwatchLeft">Left edge of the colour swatch.</param>
/// <param name="SwatchTop">Top edge of the colour swatch.</param>
/// <param name="Colour">The swatch colour.</param>
/// <param name="TextLeft">Left edge of the row's text.</param>
/// <param name="CenterY">Vertical centre of the row.</param>
/// <param name="Text">The rendered row text.</param>
public sealed record PieLegendRow(
    double SwatchLeft,
    double SwatchTop,
    string Colour,
    double TextLeft,
    double CenterY,
    string Text);

/// <summary>The laid-out pie chart.</summary>
/// <param name="Title">The wrapped title lines; empty when the chart has no title.</param>
/// <param name="TitleCenterX">Horizontal centre of the title block.</param>
/// <param name="TitleCenterY">Vertical centre of the title block.</param>
/// <param name="CenterX">Pie centre on the x axis.</param>
/// <param name="CenterY">Pie centre on the y axis.</param>
/// <param name="Radius">Pie radius.</param>
/// <param name="Slices">Placed slices in source order.</param>
/// <param name="Legend">Legend rows in source order.</param>
/// <param name="Width">Total chart width including margins.</param>
/// <param name="Height">Total chart height including margins.</param>
public sealed record PieChartLayout(
    IReadOnlyList<string> Title,
    double TitleCenterX,
    double TitleCenterY,
    double CenterX,
    double CenterY,
    double Radius,
    IReadOnlyList<PieSliceLayout> Slices,
    IReadOnlyList<PieLegendRow> Legend,
    double Width,
    double Height);

/// <summary>
/// The solver-free pie layout: sum the values, turn each into a wedge angle, and stack a legend
/// column beside the circle. Every coordinate is a fixed function of the model, the font size, and
/// <see cref="PieMetrics"/>, so repeated renders are byte-identical.
/// </summary>
public static class PieLayoutEngine
{
    /// <summary>Degrees in a full turn.</summary>
    public const double FullTurnDegrees = 360;

    /// <summary>Where the first wedge starts: twelve o'clock, in degrees from the +x axis.</summary>
    public const double StartAngleDegrees = -90;

    /// <summary>Lays out a parsed pie chart.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="metrics">Geometry to use.</param>
    /// <param name="wrapChars">Soft wrap width, in characters, for the title.</param>
    /// <returns>The laid-out chart.</returns>
    public static PieChartLayout Compute(
        PieModel model,
        double fontSize,
        PieMetrics metrics,
        int wrapChars)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wrapChars);

        double lineHeight = TextMetrics.LineHeight(fontSize);
        double total = model.Total;

        IReadOnlyList<string> titleLines = model.Title is { Length: > 0 }
            ? TextMetrics.WrapLabel(model.Title, wrapChars)
            : [];
        double titleHeight = titleLines.Count == 0
            ? 0
            : (titleLines.Count * lineHeight) + metrics.TitleGap;

        // Legend rows first: their widest text sets the chart's overall width.
        var rowTexts = new List<string>(model.Slices.Count);
        double legendTextWidth = 0;
        foreach (PieSlice slice in model.Slices)
        {
            string text = LegendText(slice, slice.Value / total, model.ShowData);
            rowTexts.Add(text);
            legendTextWidth = Math.Max(legendTextWidth, TextMetrics.MeasureWidth(text, fontSize));
        }

        double rowHeight = Math.Max(lineHeight, metrics.SwatchSize);
        double legendHeight = (rowTexts.Count * rowHeight) +
            ((rowTexts.Count - 1) * metrics.LegendRowGap);
        double legendWidth = metrics.SwatchSize + metrics.SwatchTextGap + legendTextWidth;

        double diameter = metrics.Radius * 2;
        double contentHeight = Math.Max(diameter, legendHeight);
        double width = (metrics.Margin * 2) + diameter + metrics.LegendGap + legendWidth;
        double height = (metrics.Margin * 2) + titleHeight + contentHeight;

        double contentTop = metrics.Margin + titleHeight;
        double centerX = metrics.Margin + metrics.Radius;
        double centerY = contentTop + (contentHeight / 2);

        var slices = new List<PieSliceLayout>(model.Slices.Count);
        double angle = StartAngleDegrees;
        for (int i = 0; i < model.Slices.Count; i++)
        {
            PieSlice slice = model.Slices[i];
            double fraction = slice.Value / total;
            // The last wedge takes the remainder, so rounding can never leave a hairline gap.
            double sweep = i == model.Slices.Count - 1
                ? StartAngleDegrees + FullTurnDegrees - angle
                : fraction * FullTurnDegrees;
            slices.Add(new PieSliceLayout(
                slice, angle, sweep, fraction, PieTheme.Colour(slice.Order)));
            angle += sweep;
        }

        double legendLeft = metrics.Margin + diameter + metrics.LegendGap;
        double rowTop = contentTop + ((contentHeight - legendHeight) / 2);
        var legend = new List<PieLegendRow>(rowTexts.Count);
        for (int i = 0; i < rowTexts.Count; i++)
        {
            double rowCenterY = rowTop + (rowHeight / 2);
            legend.Add(new PieLegendRow(
                legendLeft,
                rowCenterY - (metrics.SwatchSize / 2),
                PieTheme.Colour(model.Slices[i].Order),
                legendLeft + metrics.SwatchSize + metrics.SwatchTextGap,
                rowCenterY,
                rowTexts[i]));
            rowTop += rowHeight + metrics.LegendRowGap;
        }

        return new PieChartLayout(
            titleLines,
            width / 2,
            metrics.Margin + (titleLines.Count * lineHeight / 2),
            centerX,
            centerY,
            metrics.Radius,
            slices,
            legend,
            width,
            height);
    }

    /// <summary>
    /// The legend row text for a slice: <c>Label — pct%</c>, or <c>Label — value (pct%)</c> when the
    /// chart asked for <c>showData</c>.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <param name="fraction">The slice's share of the total.</param>
    /// <param name="showData">Whether the raw value is shown alongside the share.</param>
    /// <returns>The row text.</returns>
    public static string LegendText(PieSlice slice, double fraction, bool showData)
    {
        ArgumentNullException.ThrowIfNull(slice);

        string percent = (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture);
        if (!showData)
        {
            return $"{slice.Label} — {percent}%";
        }

        string value = slice.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{slice.Label} — {value} ({percent}%)";
    }

    /// <summary>The SVG <c>path</c> data for one wedge.</summary>
    /// <param name="slice">The placed slice; must not be a full circle.</param>
    /// <param name="centerX">Pie centre on the x axis.</param>
    /// <param name="centerY">Pie centre on the y axis.</param>
    /// <param name="radius">Pie radius.</param>
    /// <returns>The wedge's <c>d</c> value.</returns>
    public static string WedgePath(
        PieSliceLayout slice,
        double centerX,
        double centerY,
        double radius)
    {
        ArgumentNullException.ThrowIfNull(slice);

        (double startX, double startY) = OnCircle(centerX, centerY, radius, slice.StartAngle);
        (double endX, double endY) =
            OnCircle(centerX, centerY, radius, slice.StartAngle + slice.SweepAngle);
        int largeArc = slice.SweepAngle > FullTurnDegrees / 2 ? 1 : 0;

        return $"M {SvgBuilder.Number(centerX)} {SvgBuilder.Number(centerY)} " +
            $"L {SvgBuilder.Number(startX)} {SvgBuilder.Number(startY)} " +
            $"A {SvgBuilder.Number(radius)} {SvgBuilder.Number(radius)} 0 " +
            $"{largeArc.ToString(CultureInfo.InvariantCulture)} 1 " +
            $"{SvgBuilder.Number(endX)} {SvgBuilder.Number(endY)} Z";
    }

    /// <summary>A point on the pie's circumference at an angle measured clockwise.</summary>
    /// <param name="centerX">Circle centre on the x axis.</param>
    /// <param name="centerY">Circle centre on the y axis.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="degrees">Angle in degrees, clockwise, zero at three o'clock.</param>
    /// <returns>The point.</returns>
    public static (double X, double Y) OnCircle(
        double centerX,
        double centerY,
        double radius,
        double degrees)
    {
        double radians = degrees * Math.PI / (FullTurnDegrees / 2);
        return (centerX + (radius * Math.Cos(radians)), centerY + (radius * Math.Sin(radians)));
    }
}
