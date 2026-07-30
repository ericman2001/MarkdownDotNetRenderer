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
using System.Xml;

namespace MarkdownDotNetRenderer.Writers.Odt;

/// <summary>
/// Hand-written XML plumbing shared by the ODF parts: a deterministic <see cref="XmlWriter"/>
/// configuration, prefix-qualified element/attribute shorthands, and length formatting. Every
/// part is written element by element with explicit namespace prefixes — no serializer, no
/// reflection (docs/06-aot-and-dependencies.md).
/// </summary>
internal static class OdtXml
{
    /// <summary>Decimal places used for physical lengths; enough for sub-typographic precision.</summary>
    private const int LengthDecimals = 4;

    /// <summary>ODF length suffix for inches.</summary>
    private const string InchSuffix = "in";

    /// <summary>ODF length suffix for points.</summary>
    private const string PointSuffix = "pt";

    /// <summary>UTF-8 without a byte-order mark, as every ODF part requires.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes an XML part to a UTF-8 byte array using the deterministic settings.</summary>
    /// <param name="write">Callback writing the part's root element.</param>
    /// <returns>The encoded part.</returns>
    internal static byte[] WritePart(Action<XmlWriter> write)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            // No indentation: whitespace between a paragraph's children is content in ODF, so an
            // indented text:p containing only a draw:frame would render with stray spaces. This is
            // also what LibreOffice itself writes.
            Indent = false,
            NewLineChars = OdfNames.Newline,
            NewLineHandling = NewLineHandling.Replace,
            CloseOutput = false,
        };

        using var buffer = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartDocument();
            write(writer);
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    /// <summary>Declares every ODF prefix used by this writer on the element being opened.</summary>
    /// <param name="writer">The writer positioned on a root element's start tag.</param>
    internal static void WriteNamespaceDeclarations(this XmlWriter writer)
    {
        Declare(writer, OdfNames.OfficePrefix, OdfNames.OfficeNs);
        Declare(writer, OdfNames.StylePrefix, OdfNames.StyleNs);
        Declare(writer, OdfNames.TextPrefix, OdfNames.TextNs);
        Declare(writer, OdfNames.TablePrefix, OdfNames.TableNs);
        Declare(writer, OdfNames.DrawPrefix, OdfNames.DrawNs);
        Declare(writer, OdfNames.FoPrefix, OdfNames.FoNs);
        Declare(writer, OdfNames.SvgPrefix, OdfNames.SvgNs);
        Declare(writer, OdfNames.XlinkPrefix, OdfNames.XlinkNs);
        Declare(writer, OdfNames.MetaPrefix, OdfNames.MetaNs);
        Declare(writer, OdfNames.DcPrefix, OdfNames.DcNs);
    }

    /// <summary>Opens an element in the <c>office</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartOffice(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.OfficePrefix, localName, OdfNames.OfficeNs);

    /// <summary>Opens an element in the <c>text</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartText(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.TextPrefix, localName, OdfNames.TextNs);

    /// <summary>Opens an element in the <c>style</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartStyle(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.StylePrefix, localName, OdfNames.StyleNs);

    /// <summary>Opens an element in the <c>table</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartTable(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.TablePrefix, localName, OdfNames.TableNs);

    /// <summary>Opens an element in the <c>draw</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartDraw(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.DrawPrefix, localName, OdfNames.DrawNs);

    /// <summary>Opens an element in the ODF <c>svg</c>-compatible namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The element's local name.</param>
    internal static void StartSvg(this XmlWriter writer, string localName) =>
        writer.WriteStartElement(OdfNames.SvgPrefix, localName, OdfNames.SvgNs);

    /// <summary>Writes an attribute in the <c>office</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void OfficeAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.OfficePrefix, localName, OdfNames.OfficeNs, value);

    /// <summary>Writes an attribute in the <c>text</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void TextAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.TextPrefix, localName, OdfNames.TextNs, value);

    /// <summary>Writes an attribute in the <c>style</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void StyleAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.StylePrefix, localName, OdfNames.StyleNs, value);

    /// <summary>Writes an attribute in the <c>table</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void TableAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.TablePrefix, localName, OdfNames.TableNs, value);

    /// <summary>Writes an attribute in the <c>draw</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void DrawAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.DrawPrefix, localName, OdfNames.DrawNs, value);

    /// <summary>Writes an attribute in the <c>fo</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void FoAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.FoPrefix, localName, OdfNames.FoNs, value);

    /// <summary>Writes an attribute in the ODF <c>svg</c>-compatible namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void SvgAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.SvgPrefix, localName, OdfNames.SvgNs, value);

    /// <summary>Writes an attribute in the <c>xlink</c> namespace.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="value">The attribute value.</param>
    internal static void XlinkAttribute(this XmlWriter writer, string localName, string value) =>
        writer.WriteAttributeString(OdfNames.XlinkPrefix, localName, OdfNames.XlinkNs, value);

    /// <summary>Formats a length in inches, e.g. <c>6.9291in</c>.</summary>
    /// <param name="inches">The length in inches.</param>
    /// <returns>The ODF length literal.</returns>
    internal static string Inches(double inches) => Length(inches, InchSuffix);

    /// <summary>Formats a length in points, e.g. <c>11pt</c>.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The ODF length literal.</returns>
    internal static string Points(double points) => Length(points, PointSuffix);

    /// <summary>Formats an integer, culture-independently.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant decimal representation.</returns>
    internal static string Integer(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Length(double value, string suffix) =>
        Math.Round(value, LengthDecimals).ToString("0.####", CultureInfo.InvariantCulture) + suffix;

    private static void Declare(XmlWriter writer, string prefix, string ns) =>
        writer.WriteAttributeString("xmlns", prefix, null, ns);
}
