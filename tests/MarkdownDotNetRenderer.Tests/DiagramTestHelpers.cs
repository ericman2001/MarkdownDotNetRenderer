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

namespace MarkdownDotNetRenderer.Tests;

internal static class DiagramTestHelpers
{
    internal static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    internal static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    internal static readonly XNamespace Office =
        "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    internal static readonly XNamespace Draw =
        "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    internal static XElement RenderSvg(DiagramRenderResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.SvgFragment);
        return XElement.Parse(result.SvgFragment);
    }

    internal static XElement ParseSvg(string svgFragment)
    {
        ArgumentNullException.ThrowIfNull(svgFragment);
        return XElement.Parse(svgFragment);
    }

    internal static double Number(XElement element, string name) =>
        double.Parse(
            element.Attribute(name)!.Value,
            CultureInfo.InvariantCulture);
}
