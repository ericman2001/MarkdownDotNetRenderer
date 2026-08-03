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
using System.Text;
using MarkdownDotNetRenderer.Mermaid.Flowchart;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Graph;

/// <summary>The boundary shape an edge endpoint is clipped to.</summary>
public enum ClipShape
{
    /// <summary>An axis-aligned rectangle (also used for stadiums and rounded boxes).</summary>
    Box,

    /// <summary>A diamond inscribed in the node's box.</summary>
    Diamond,

    /// <summary>An ellipse inscribed in the node's box.</summary>
    Ellipse,
}

/// <summary>
/// Edge geometry shared by every graph-shaped diagram type — flowcharts, state, class, and ER
/// diagrams all clip centre-to-centre routes to their node boxes and draw polylines with rounded
/// bends the same way, so the maths lives here once rather than being forked per renderer
/// (docs/phases/phase-4-additional-diagrams.md, standing rule "reuse, don't fork").
/// </summary>
public static class GraphGeometry
{
    /// <summary>Clips a ray leaving a node's centre to that node's boundary.</summary>
    /// <param name="centerX">Node centre on the x axis.</param>
    /// <param name="centerY">Node centre on the y axis.</param>
    /// <param name="width">Node box width.</param>
    /// <param name="height">Node box height.</param>
    /// <param name="shape">The boundary shape to clip to.</param>
    /// <param name="toward">A point the ray passes through.</param>
    /// <returns>The point where the ray crosses the boundary.</returns>
    public static LayoutPoint Clip(
        double centerX,
        double centerY,
        double width,
        double height,
        ClipShape shape,
        LayoutPoint toward)
    {
        double dx = toward.X - centerX;
        double dy = toward.Y - centerY;
        if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
        {
            return new LayoutPoint(centerX, centerY);
        }

        double halfWidth = width / 2;
        double halfHeight = height / 2;
        double scale;

        switch (shape)
        {
            case ClipShape.Diamond:
                // |x| / halfWidth + |y| / halfHeight = 1 on a diamond boundary.
                scale = 1 / ((Math.Abs(dx) / halfWidth) + (Math.Abs(dy) / halfHeight));
                break;

            case ClipShape.Ellipse:
                double normalizedX = dx / halfWidth;
                double normalizedY = dy / halfHeight;
                scale = 1 / Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
                break;

            case ClipShape.Box:
            default:
                double scaleX = Math.Abs(dx) < Epsilon
                    ? double.PositiveInfinity
                    : halfWidth / Math.Abs(dx);
                double scaleY = Math.Abs(dy) < Epsilon
                    ? double.PositiveInfinity
                    : halfHeight / Math.Abs(dy);
                scale = Math.Min(scaleX, scaleY);
                break;
        }

        return new LayoutPoint(centerX + (dx * scale), centerY + (dy * scale));
    }

    /// <summary>Steps from one point toward another by at most <paramref name="distance"/>.</summary>
    /// <param name="from">The point to step from.</param>
    /// <param name="toward">The point to step toward.</param>
    /// <param name="distance">How far to step; capped at half the separation.</param>
    /// <returns>The stepped point.</returns>
    public static LayoutPoint Along(LayoutPoint from, LayoutPoint toward, double distance)
    {
        double dx = toward.X - from.X;
        double dy = toward.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= double.Epsilon)
        {
            return from;
        }

        double step = Math.Min(distance, length / 2);
        return new LayoutPoint(from.X + (dx / length * step), from.Y + (dy / length * step));
    }

    /// <summary>Builds a polyline path whose interior bends are rounded quadratic corners.</summary>
    /// <param name="points">At least two points, in draw order.</param>
    /// <param name="cornerRadius">Bend rounding radius.</param>
    /// <returns>An SVG path <c>d</c> value.</returns>
    public static string BuildRoundedPath(IReadOnlyList<LayoutPoint> points, double cornerRadius)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new ArgumentException("A path needs at least two points.", nameof(points));
        }

        var path = new StringBuilder();
        path.Append("M ").Append(SvgBuilder.Number(points[0].X)).Append(' ')
            .Append(SvgBuilder.Number(points[0].Y));

        for (int i = 1; i < points.Count - 1; i++)
        {
            LayoutPoint previous = points[i - 1];
            LayoutPoint current = points[i];
            LayoutPoint next = points[i + 1];

            LayoutPoint approach = Along(current, previous, cornerRadius);
            LayoutPoint leave = Along(current, next, cornerRadius);

            path.Append(" L ").Append(SvgBuilder.Number(approach.X)).Append(' ')
                .Append(SvgBuilder.Number(approach.Y));
            path.Append(" Q ").Append(SvgBuilder.Number(current.X)).Append(' ')
                .Append(SvgBuilder.Number(current.Y)).Append(' ')
                .Append(SvgBuilder.Number(leave.X)).Append(' ')
                .Append(SvgBuilder.Number(leave.Y));
        }

        path.Append(" L ").Append(SvgBuilder.Number(points[^1].X)).Append(' ')
            .Append(SvgBuilder.Number(points[^1].Y));
        return path.ToString();
    }

    /// <summary>The midpoint of a route, used as its label anchor.</summary>
    /// <param name="points">The route; at least one point.</param>
    /// <returns>The anchor point.</returns>
    public static LayoutPoint MidPoint(IReadOnlyList<LayoutPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 1)
        {
            return points[0];
        }

        if (points.Count == 2)
        {
            return new LayoutPoint(
                (points[0].X + points[1].X) / 2,
                (points[0].Y + points[1].Y) / 2);
        }

        return points[points.Count / 2];
    }

    /// <summary>Formats a point as an SVG <c>points</c>-list entry.</summary>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    /// <returns>The <c>x,y</c> pair, invariantly formatted.</returns>
    public static string PointPair(double x, double y) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{SvgBuilder.Number(x)},{SvgBuilder.Number(y)}");

    private const double Epsilon = 1e-9;
}
