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

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// The fixed vocabulary of WordprocessingML and DrawingML: content types, the namespaces and
/// extension URI that carry an SVG image, and the unit factors the formats mandate. Like
/// <c>OdfNames</c> for ODF, these are spec constants rather than tunables — a caller who changed
/// them could only produce a package Word refuses — so they are <c>const</c> fields here
/// (docs/09-conventions.md section 3).
/// </summary>
internal static class OoxmlNames
{
    /// <summary>Content type of a WordprocessingML document package.</summary>
    internal const string DocumentContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Content type of the main document part inside that package — distinct from
    /// <see cref="DocumentContentType"/>, which names the package as a whole.
    /// </summary>
    internal const string MainDocumentPartContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    /// <summary>Content type of an embedded SVG image part.</summary>
    internal const string SvgContentType = "image/svg+xml";

    /// <summary>
    /// URI identifying the blip extension that carries an SVG reference. Fixed by Microsoft's
    /// SVG extension; Word matches on this exact GUID.
    /// </summary>
    internal const string SvgBlipExtensionUri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";

    /// <summary>Namespace of the <c>svgBlip</c> element inside that extension.</summary>
    internal const string SvgBlipNamespace =
        "http://schemas.microsoft.com/office/drawing/2016/SVG/main";

    /// <summary>Prefix bound to <see cref="SvgBlipNamespace"/> in the emitted extension.</summary>
    internal const string SvgBlipPrefix = "asvg";

    /// <summary>Local name of the element referencing the SVG part.</summary>
    internal const string SvgBlipElement = "svgBlip";

    /// <summary>The relationships namespace, so <c>r:embed</c> resolves on the unknown element.</summary>
    internal const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Prefix bound to <see cref="RelationshipsNamespace"/>.</summary>
    internal const string RelationshipsPrefix = "r";

    /// <summary>Attribute on <c>svgBlip</c> naming the part it embeds.</summary>
    internal const string EmbedAttribute = "embed";

    /// <summary>Graphic-data URI selecting the DrawingML picture part.</summary>
    internal const string PictureGraphicDataUri =
        "http://schemas.openxmlformats.org/drawingml/2006/picture";

    /// <summary>English Metric Units per CSS pixel, i.e. 914400 EMU per inch / 96 px per inch.</summary>
    internal const long EmusPerPixel = 9525;

    /// <summary>English Metric Units per inch, as fixed by DrawingML.</summary>
    internal const long EmusPerInch = 914400;

    /// <summary>Twentieths of a point per inch, the unit of page geometry and indents.</summary>
    internal const double TwipsPerInch = 1440;

    /// <summary>Twentieths of a point per point, the same unit expressed per point.</summary>
    internal const double TwipsPerPoint = 20;

    /// <summary>Half-points per point, the unit of <c>w:sz</c> font sizes.</summary>
    internal const double HalfPointsPerPoint = 2;

    /// <summary>Eighths of a point per point, the unit of border widths.</summary>
    internal const double EighthPointsPerPoint = 8;

    /// <summary>Prolog prepended to every SVG image part, matching the ODT writer's pictures.</summary>
    internal const string XmlProlog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    /// <summary>Line ending used inside generated parts, on every platform.</summary>
    internal const string Newline = "\n";
}
