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
/// The named styles of an ODT package: the <c>styles.xml</c> part plus the style names
/// <c>content.xml</c> refers to. Named styles are what keep the document restyleable in
/// LibreOffice; anything span-local becomes an automatic style
/// (<see cref="OdtAutomaticStyles"/>).
/// </summary>
internal static class OdtStyles
{
    /// <summary>Body paragraph style; also the parent of every other paragraph style.</summary>
    internal const string StandardParagraph = "Standard";

    /// <summary>Prefix of the heading paragraph styles; the level is appended.</summary>
    internal const string HeadingPrefix = "Heading_20_";

    /// <summary>Style of a code-block line.</summary>
    internal const string PreformattedText = "Preformatted_20_Text";

    /// <summary>Style of a block-quote paragraph.</summary>
    internal const string Quotations = "Quotations";

    /// <summary>Style of the empty paragraph standing in for a thematic break.</summary>
    internal const string HorizontalLine = "Horizontal_20_Line";

    /// <summary>Style of a paragraph inside a table body cell.</summary>
    internal const string TableContents = "Table_20_Contents";

    /// <summary>Style of a paragraph inside a table header cell.</summary>
    internal const string TableHeading = "Table_20_Heading";

    /// <summary>Graphic style of a diagram frame.</summary>
    internal const string Graphics = "Graphics";

    /// <summary>List style declaring bullet levels.</summary>
    internal const string BulletList = "Bullet_20_List";

    /// <summary>List style declaring numbered levels.</summary>
    internal const string NumberedList = "Numbered_20_List";

    /// <summary>Declared font face used for body text.</summary>
    internal const string BodyFontName = "BodyFont";

    /// <summary>Declared font face used for code and preformatted text.</summary>
    internal const string MonospaceFontName = "MonoFont";

    /// <summary>Name of the single page layout.</summary>
    internal const string PageLayoutName = "pm1";

    /// <summary>Name of the single master page.</summary>
    internal const string MasterPageName = "Standard";

    /// <summary>Deepest list nesting level the list styles declare.</summary>
    internal const int ListLevels = 10;

    /// <summary>Highest heading level ODF outlines and Markdown both support.</summary>
    internal const int MaxHeadingLevel = 6;

    private const string ParagraphFamily = "paragraph";
    private const string TextFamily = "text";
    private const string TableFamily = "table";
    private const string TableColumnFamily = "table-column";
    private const string TableCellFamily = "table-cell";
    private const string GraphicFamily = "graphic";

    private const string ParagraphProperties = "paragraph-properties";
    private const string TextProperties = "text-properties";
    private const string TableProperties = "table-properties";
    private const string TableColumnProperties = "table-column-properties";
    private const string TableCellProperties = "table-cell-properties";
    private const string GraphicProperties = "graphic-properties";

    private const string NoBorder = "none";
    private const string SolidBorder = "solid";
    private const string BoldWeight = "bold";
    private const string ItalicPosture = "italic";
    private const string LineThroughStyle = "solid";
    private const string FixedPitch = "fixed";
    private const string PortraitOrientation = "portrait";
    private const string KeepWithNext = "always";

    /// <summary>Bullet glyph used when a theme supplies none.</summary>
    private const string FallbackBulletCharacter = "\u2022";

    /// <summary>The paragraph style name for a heading level.</summary>
    /// <param name="level">Heading level, 1–6.</param>
    /// <returns>The style name, e.g. <c>Heading_20_2</c>.</returns>
    internal static string HeadingStyle(int level) =>
        HeadingPrefix + OdtXml.Integer(Math.Clamp(level, 1, MaxHeadingLevel));

    /// <summary>Composes an ODF border literal, e.g. <c>0.5pt solid #b0b0b0</c>.</summary>
    /// <param name="width">Border width, in points.</param>
    /// <param name="color">Border colour.</param>
    /// <returns>The border literal.</returns>
    internal static string Border(double width, string color) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{OdtXml.Points(width)} {SolidBorder} {color}");

    /// <summary>Writes the <c>styles.xml</c> part.</summary>
    /// <param name="theme">Visual values to emit.</param>
    /// <param name="bodyFontFamily">Body font family from <see cref="RenderOptions.FontFamily"/>.</param>
    /// <returns>The encoded part.</returns>
    internal static byte[] BuildStylesPart(OdtTheme theme, string bodyFontFamily) =>
        OdtXml.WritePart(writer =>
        {
            writer.StartOffice("document-styles");
            writer.WriteNamespaceDeclarations();
            writer.OfficeAttribute("version", OdfNames.Version);

            WriteFontFaceDeclarations(writer, theme, bodyFontFamily);

            writer.StartOffice("styles");
            WriteDefaultStyle(writer, theme);
            WriteStandardStyle(writer, theme);
            for (int level = 1; level <= MaxHeadingLevel; level++)
            {
                WriteHeadingStyle(writer, theme, level);
            }

            WritePreformattedStyle(writer, theme);
            WriteQuotationsStyle(writer, theme);
            WriteHorizontalLineStyle(writer, theme);
            WriteTableParagraphStyles(writer);
            WriteGraphicsStyle(writer);
            WriteListStyle(writer, theme, BulletList, numbered: false);
            WriteListStyle(writer, theme, NumberedList, numbered: true);
            writer.WriteEndElement();

            writer.StartOffice("automatic-styles");
            WritePageLayout(writer, theme);
            writer.WriteEndElement();

            writer.StartOffice("master-styles");
            writer.StartStyle("master-page");
            writer.StyleAttribute("name", MasterPageName);
            writer.StyleAttribute("page-layout-name", PageLayoutName);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement();
        });

    /// <summary>Writes the font-face declarations shared by <c>styles.xml</c> and <c>content.xml</c>.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="theme">Visual values to emit.</param>
    /// <param name="bodyFontFamily">Body font family from <see cref="RenderOptions.FontFamily"/>.</param>
    internal static void WriteFontFaceDeclarations(
        XmlWriter writer,
        OdtTheme theme,
        string bodyFontFamily)
    {
        writer.StartOffice("font-face-decls");

        writer.StartStyle("font-face");
        writer.StyleAttribute("name", BodyFontName);
        writer.SvgAttribute("font-family", bodyFontFamily);
        writer.WriteEndElement();

        writer.StartStyle("font-face");
        writer.StyleAttribute("name", MonospaceFontName);
        writer.SvgAttribute("font-family", theme.MonospaceFontFamily);
        writer.StyleAttribute("font-pitch", FixedPitch);
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WriteDefaultStyle(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("default-style");
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StartStyle(TextProperties);
        writer.StyleAttribute("font-name", BodyFontName);
        writer.FoAttribute("font-size", OdtXml.Points(theme.BodyFontSize));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteStandardStyle(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", StandardParagraph);
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("margin-bottom", OdtXml.Points(theme.ParagraphSpaceBelow));
        writer.WriteEndElement();
        writer.StartStyle(TextProperties);
        writer.StyleAttribute("font-name", BodyFontName);
        writer.FoAttribute("font-size", OdtXml.Points(theme.BodyFontSize));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteHeadingStyle(XmlWriter writer, OdtTheme theme, int level)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", HeadingStyle(level));
        writer.StyleAttribute("display-name", "Heading " + OdtXml.Integer(level));
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", StandardParagraph);
        writer.StyleAttribute("next-style-name", StandardParagraph);
        writer.StyleAttribute("default-outline-level", OdtXml.Integer(level));

        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("margin-top", OdtXml.Points(theme.HeadingSpaceAbove));
        writer.FoAttribute("margin-bottom", OdtXml.Points(theme.HeadingSpaceBelow));
        writer.FoAttribute("keep-with-next", KeepWithNext);
        writer.WriteEndElement();

        writer.StartStyle(TextProperties);
        writer.FoAttribute("font-size", OdtXml.Points(theme.HeadingFontSize(level)));
        writer.FoAttribute("font-weight", BoldWeight);
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WritePreformattedStyle(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", PreformattedText);
        writer.StyleAttribute("display-name", "Preformatted Text");
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", StandardParagraph);

        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("margin-bottom", OdtXml.Points(0));
        writer.FoAttribute("background-color", theme.CodeBackgroundColor);
        writer.WriteEndElement();

        writer.StartStyle(TextProperties);
        writer.StyleAttribute("font-name", MonospaceFontName);
        writer.FoAttribute("font-size", OdtXml.Points(theme.CodeFontSize));
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WriteQuotationsStyle(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", Quotations);
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", StandardParagraph);

        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("margin-left", OdtXml.Inches(theme.QuoteIndent));
        writer.FoAttribute("margin-right", OdtXml.Inches(theme.QuoteIndent));
        writer.FoAttribute(
            "border-left",
            Border(theme.QuoteBorderWidth, theme.QuoteBorderColor));
        writer.FoAttribute("padding-left", OdtXml.Inches(theme.TableCellPadding));
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WriteHorizontalLineStyle(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", HorizontalLine);
        writer.StyleAttribute("display-name", "Horizontal Line");
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", StandardParagraph);

        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("border-top", NoBorder);
        writer.FoAttribute("border-left", NoBorder);
        writer.FoAttribute("border-right", NoBorder);
        writer.FoAttribute(
            "border-bottom",
            Border(theme.ThematicBreakBorderWidth, theme.ThematicBreakBorderColor));
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static void WriteTableParagraphStyles(XmlWriter writer)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", TableContents);
        writer.StyleAttribute("display-name", "Table Contents");
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", StandardParagraph);
        writer.StartStyle(ParagraphProperties);
        writer.FoAttribute("margin-bottom", OdtXml.Points(0));
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.StartStyle("style");
        writer.StyleAttribute("name", TableHeading);
        writer.StyleAttribute("display-name", "Table Heading");
        writer.StyleAttribute("family", ParagraphFamily);
        writer.StyleAttribute("parent-style-name", TableContents);
        writer.StartStyle(TextProperties);
        writer.FoAttribute("font-weight", BoldWeight);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteGraphicsStyle(XmlWriter writer)
    {
        writer.StartStyle("style");
        writer.StyleAttribute("name", Graphics);
        writer.StyleAttribute("family", GraphicFamily);
        writer.StartStyle(GraphicProperties);
        writer.TextAttribute("anchor-type", OdtDocumentWriter.AsCharAnchor);
        writer.StyleAttribute("wrap", "none");
        writer.StyleAttribute("vertical-pos", "middle");
        writer.StyleAttribute("vertical-rel", "text");
        writer.FoAttribute("border", NoBorder);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteListStyle(
        XmlWriter writer,
        OdtTheme theme,
        string name,
        bool numbered)
    {
        writer.StartText("list-style");
        writer.StyleAttribute("name", name);

        for (int level = 1; level <= ListLevels; level++)
        {
            if (numbered)
            {
                writer.StartText("list-level-style-number");
                writer.TextAttribute("level", OdtXml.Integer(level));
                writer.StyleAttribute("num-suffix", ".");
                writer.StyleAttribute("num-format", "1");
            }
            else
            {
                writer.StartText("list-level-style-bullet");
                writer.TextAttribute("level", OdtXml.Integer(level));
                writer.TextAttribute("bullet-char", BulletCharacter(theme, level));
            }

            double indent = theme.ListLevelIndent * level;
            writer.StartStyle("list-level-properties");
            writer.TextAttribute("list-level-position-and-space-mode", "label-alignment");
            writer.StartStyle("list-level-label-alignment");
            writer.TextAttribute("label-followed-by", "listtab");
            writer.TextAttribute("list-tab-stop-position", OdtXml.Inches(indent));
            writer.FoAttribute("text-indent", OdtXml.Inches(-theme.ListLevelIndent));
            writer.FoAttribute("margin-left", OdtXml.Inches(indent));
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string BulletCharacter(OdtTheme theme, int level)
    {
        IReadOnlyList<string> bullets = theme.BulletCharacters;
        return bullets.Count == 0
            ? FallbackBulletCharacter
            : bullets[(level - 1) % bullets.Count];
    }

    private static void WritePageLayout(XmlWriter writer, OdtTheme theme)
    {
        writer.StartStyle("page-layout");
        writer.StyleAttribute("name", PageLayoutName);
        writer.StartStyle("page-layout-properties");
        writer.FoAttribute("page-width", OdtXml.Inches(theme.PageWidth));
        writer.FoAttribute("page-height", OdtXml.Inches(theme.PageHeight));
        writer.StyleAttribute("print-orientation", PortraitOrientation);
        writer.FoAttribute("margin-top", OdtXml.Inches(theme.PageMargin));
        writer.FoAttribute("margin-bottom", OdtXml.Inches(theme.PageMargin));
        writer.FoAttribute("margin-left", OdtXml.Inches(theme.PageMargin));
        writer.FoAttribute("margin-right", OdtXml.Inches(theme.PageMargin));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    /// <summary>One attribute of an automatic style's property element.</summary>
    /// <param name="Prefix">Namespace prefix.</param>
    /// <param name="Namespace">Namespace URI matching <paramref name="Prefix"/>.</param>
    /// <param name="LocalName">Attribute local name.</param>
    /// <param name="Value">Attribute value.</param>
    internal readonly record struct StyleAttribute(
        string Prefix,
        string Namespace,
        string LocalName,
        string Value)
    {
        /// <summary>An attribute in the <c>fo</c> namespace.</summary>
        /// <param name="localName">Attribute local name.</param>
        /// <param name="value">Attribute value.</param>
        /// <returns>The attribute.</returns>
        internal static StyleAttribute Fo(string localName, string value) =>
            new(OdfNames.FoPrefix, OdfNames.FoNs, localName, value);

        /// <summary>An attribute in the <c>style</c> namespace.</summary>
        /// <param name="localName">Attribute local name.</param>
        /// <param name="value">Attribute value.</param>
        /// <returns>The attribute.</returns>
        internal static StyleAttribute Style(string localName, string value) =>
            new(OdfNames.StylePrefix, OdfNames.StyleNs, localName, value);

        /// <summary>An attribute in the <c>table</c> namespace.</summary>
        /// <param name="localName">Attribute local name.</param>
        /// <param name="value">Attribute value.</param>
        /// <returns>The attribute.</returns>
        internal static StyleAttribute Table(string localName, string value) =>
            new(OdfNames.TablePrefix, OdfNames.TableNs, localName, value);
    }

    /// <summary>One property element of an automatic style, e.g. <c>style:text-properties</c>.</summary>
    /// <param name="ElementLocalName">Local name of the property element.</param>
    /// <param name="Attributes">The properties it carries.</param>
    internal sealed record StylePropertySet(
        string ElementLocalName,
        IReadOnlyList<StyleAttribute> Attributes);

    /// <summary>An automatic style, named on first use and emitted once.</summary>
    /// <param name="Name">Generated style name.</param>
    /// <param name="Family">ODF style family.</param>
    /// <param name="ParentStyleName">Named style it inherits from, if any.</param>
    /// <param name="PropertySets">Property elements to emit, in order.</param>
    internal sealed record AutomaticStyle(
        string Name,
        string Family,
        string? ParentStyleName,
        IReadOnlyList<StylePropertySet> PropertySets);

    /// <summary>
    /// The automatic styles of one document, deduplicated by property set: N bold spans share one
    /// style, and names are handed out in first-use order, so output is deterministic.
    /// </summary>
    internal sealed class OdtAutomaticStyles
    {
        private const string TextNamePrefix = "T";
        private const string ParagraphNamePrefix = "P";
        private const string TableNamePrefix = "Ta";
        private const string TableColumnNamePrefix = "Tc";
        private const string TableCellNamePrefix = "Tk";

        private readonly Dictionary<string, string> _namesByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _countsByPrefix = new(StringComparer.Ordinal);
        private readonly List<AutomaticStyle> _styles = [];
        private readonly OdtTheme _theme;

        /// <summary>Creates an empty collection over a theme.</summary>
        /// <param name="theme">Visual values the styles are derived from.</param>
        internal OdtAutomaticStyles(OdtTheme theme) => _theme = theme;

        /// <summary>How many distinct styles have been requested so far.</summary>
        internal int Count => _styles.Count;

        /// <summary>The style for a span carrying the given inline formatting.</summary>
        /// <param name="bold">Emit bold weight.</param>
        /// <param name="italic">Emit italic posture.</param>
        /// <param name="strikethrough">Emit a line-through.</param>
        /// <param name="code">Emit the monospace code appearance.</param>
        /// <returns>The automatic style name.</returns>
        internal string TextSpan(bool bold, bool italic, bool strikethrough, bool code)
        {
            var attributes = new List<StyleAttribute>();
            if (bold)
            {
                attributes.Add(StyleAttribute.Fo("font-weight", BoldWeight));
            }

            if (italic)
            {
                attributes.Add(StyleAttribute.Fo("font-style", ItalicPosture));
            }

            if (strikethrough)
            {
                attributes.Add(StyleAttribute.Style("text-line-through-style", LineThroughStyle));
            }

            if (code)
            {
                attributes.Add(StyleAttribute.Style("font-name", MonospaceFontName));
                attributes.Add(StyleAttribute.Fo("font-size", OdtXml.Points(_theme.CodeFontSize)));
                attributes.Add(
                    StyleAttribute.Fo("background-color", _theme.CodeBackgroundColor));
            }

            return GetOrAdd(
                TextFamily,
                parentStyleName: null,
                TextNamePrefix,
                new StylePropertySet(TextProperties, attributes));
        }

        /// <summary>The paragraph style for a table cell's content at a column alignment.</summary>
        /// <param name="header">Whether the cell belongs to the header row.</param>
        /// <param name="textAlign">ODF <c>fo:text-align</c> value, or <see langword="null"/> to inherit.</param>
        /// <returns>The automatic style name.</returns>
        internal string CellParagraph(bool header, string? textAlign)
        {
            var attributes = new List<StyleAttribute>();
            if (textAlign is not null)
            {
                attributes.Add(StyleAttribute.Fo("text-align", textAlign));
            }

            return GetOrAdd(
                ParagraphFamily,
                header ? TableHeading : TableContents,
                ParagraphNamePrefix,
                new StylePropertySet(ParagraphProperties, attributes));
        }

        /// <summary>The style of a table, sized to the text column.</summary>
        /// <returns>The automatic style name.</returns>
        internal string Table() => GetOrAdd(
            TableFamily,
            parentStyleName: null,
            TableNamePrefix,
            new StylePropertySet(
                TableProperties,
                [
                    StyleAttribute.Style("width", OdtXml.Inches(_theme.ContentWidth)),
                    StyleAttribute.Table("align", "margins"),
                ]));

        /// <summary>The style of a table column; all columns share one relative width.</summary>
        /// <returns>The automatic style name.</returns>
        internal string TableColumn() => GetOrAdd(
            TableColumnFamily,
            parentStyleName: null,
            TableColumnNamePrefix,
            new StylePropertySet(
                TableColumnProperties,
                [StyleAttribute.Style("rel-column-width", "1*")]));

        /// <summary>The style of a table cell.</summary>
        /// <param name="header">Whether the cell belongs to the header row.</param>
        /// <returns>The automatic style name.</returns>
        internal string TableCell(bool header)
        {
            var attributes = new List<StyleAttribute>
            {
                StyleAttribute.Fo(
                    "border",
                    Border(_theme.TableBorderWidth, _theme.TableBorderColor)),
                StyleAttribute.Fo("padding", OdtXml.Inches(_theme.TableCellPadding)),
            };

            if (header)
            {
                attributes.Add(
                    StyleAttribute.Fo("background-color", _theme.TableHeaderBackgroundColor));
            }

            return GetOrAdd(
                TableCellFamily,
                parentStyleName: null,
                TableCellNamePrefix,
                new StylePropertySet(TableCellProperties, attributes));
        }

        /// <summary>Writes the <c>office:automatic-styles</c> element and its styles.</summary>
        /// <param name="writer">The target writer.</param>
        internal void Write(XmlWriter writer)
        {
            writer.StartOffice("automatic-styles");
            foreach (AutomaticStyle style in _styles)
            {
                writer.StartStyle("style");
                writer.StyleAttribute("name", style.Name);
                writer.StyleAttribute("family", style.Family);
                if (style.ParentStyleName is not null)
                {
                    writer.StyleAttribute("parent-style-name", style.ParentStyleName);
                }

                foreach (StylePropertySet set in style.PropertySets)
                {
                    if (set.Attributes.Count == 0)
                    {
                        continue;
                    }

                    writer.StartStyle(set.ElementLocalName);
                    foreach (StyleAttribute attribute in set.Attributes)
                    {
                        writer.WriteAttributeString(
                            attribute.Prefix,
                            attribute.LocalName,
                            attribute.Namespace,
                            attribute.Value);
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        private string GetOrAdd(
            string family,
            string? parentStyleName,
            string namePrefix,
            params StylePropertySet[] propertySets)
        {
            string key = BuildKey(family, parentStyleName, propertySets);
            if (_namesByKey.TryGetValue(key, out string? existing))
            {
                return existing;
            }

            _countsByPrefix.TryGetValue(namePrefix, out int count);
            count++;
            _countsByPrefix[namePrefix] = count;

            string name = namePrefix + OdtXml.Integer(count);
            _namesByKey[key] = name;
            _styles.Add(new AutomaticStyle(name, family, parentStyleName, propertySets));
            return name;
        }

        private static string BuildKey(
            string family,
            string? parentStyleName,
            IReadOnlyList<StylePropertySet> propertySets)
        {
            var key = new StringBuilder(family).Append('|').Append(parentStyleName);
            foreach (StylePropertySet set in propertySets)
            {
                key.Append('|').Append(set.ElementLocalName);
                foreach (StyleAttribute attribute in set.Attributes)
                {
                    key.Append(';')
                        .Append(attribute.Prefix)
                        .Append(':')
                        .Append(attribute.LocalName)
                        .Append('=')
                        .Append(attribute.Value);
                }
            }

            return key.ToString();
        }
    }
}
