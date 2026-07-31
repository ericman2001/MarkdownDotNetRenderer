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

namespace MarkdownDotNetRenderer.Writers.Odt;

/// <summary>
/// The fixed vocabulary of the OpenDocument format: namespace URIs and prefixes, package entry
/// names, media types, and the ODF version the package declares. These are spec constants, not
/// tunables — a caller who changed them could only produce an invalid package — so they are
/// <c>const</c> fields here rather than options (docs/09-conventions.md section 3).
/// </summary>
internal static class OdfNames
{
    /// <summary>ODF version declared by every part and by the manifest.</summary>
    internal const string Version = "1.3";

    /// <summary>Media type of an OpenDocument Text package.</summary>
    internal const string TextMediaType = "application/vnd.oasis.opendocument.text";

    /// <summary>Media type used for the XML parts listed in the manifest.</summary>
    internal const string XmlMediaType = "text/xml";

    /// <summary>Media type of an embedded SVG picture.</summary>
    internal const string SvgMediaType = "image/svg+xml";

    /// <summary>The uncompressed first entry identifying the package type.</summary>
    internal const string MimetypeEntry = "mimetype";

    /// <summary>The document flow part.</summary>
    internal const string ContentEntry = "content.xml";

    /// <summary>The named-style and page-layout part.</summary>
    internal const string StylesEntry = "styles.xml";

    /// <summary>The document metadata part.</summary>
    internal const string MetaEntry = "meta.xml";

    /// <summary>The package manifest part.</summary>
    internal const string ManifestEntry = "META-INF/manifest.xml";

    /// <summary>Folder holding picture parts; ODF package paths always use <c>/</c>.</summary>
    internal const string PicturesFolder = "Pictures/";

    /// <summary>Sequential name template for a diagram picture part.</summary>
    internal const string DiagramPictureNameFormat = PicturesFolder + "diagram-{0}.svg";

    /// <summary>Manifest path of the package root.</summary>
    internal const string RootEntryPath = "/";

    /// <summary>Prolog prepended to every SVG picture part.</summary>
    internal const string XmlProlog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    /// <summary>Line ending used inside every generated part, on every platform.</summary>
    internal const string Newline = "\n";

    internal const string OfficePrefix = "office";
    internal const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    internal const string TextPrefix = "text";
    internal const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    internal const string StylePrefix = "style";
    internal const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    internal const string TablePrefix = "table";
    internal const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    internal const string DrawPrefix = "draw";
    internal const string DrawNs = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    internal const string FoPrefix = "fo";
    internal const string FoNs = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    internal const string SvgPrefix = "svg";
    internal const string SvgNs = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    internal const string XlinkPrefix = "xlink";
    internal const string XlinkNs = "http://www.w3.org/1999/xlink";

    internal const string MetaPrefix = "meta";
    internal const string MetaNs = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

    internal const string DcPrefix = "dc";
    internal const string DcNs = "http://purl.org/dc/elements/1.1/";

    internal const string ManifestPrefix = "manifest";
    internal const string ManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
}
