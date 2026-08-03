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

using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Graph;

/// <summary>The fragment envelope every diagram renderer emits (docs/04-mermaid-engine.md).</summary>
public static class DiagramSvg
{
    /// <summary>
    /// Opens the root <c>&lt;svg&gt;</c> element. The <c>viewBox</c> always stays at intrinsic
    /// size while <c>width</c>/<c>height</c> are clamped to
    /// <see cref="RenderOptions.MaxDiagramWidth"/>, so an oversized diagram is scaled down by the
    /// consumer rather than clipped. The caller closes the element.
    /// </summary>
    /// <param name="svg">The builder to write to.</param>
    /// <param name="width">Intrinsic width in CSS pixels.</param>
    /// <param name="height">Intrinsic height in CSS pixels.</param>
    /// <param name="options">Options supplying the maximum presented width.</param>
    /// <param name="altText">Accessible description written to <c>aria-label</c>.</param>
    /// <param name="cssClass">Diagram-type class, e.g. <c>mdnr-pie</c>.</param>
    public static void StartRoot(
        SvgBuilder svg,
        double width,
        double height,
        RenderOptions options,
        string altText,
        string cssClass)
    {
        ArgumentNullException.ThrowIfNull(svg);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(altText);
        ArgumentException.ThrowIfNullOrEmpty(cssClass);

        double renderWidth = width;
        double renderHeight = height;
        if (options.MaxDiagramWidth > 0 && width > options.MaxDiagramWidth)
        {
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
            .Attribute("class", cssClass);
    }
}
