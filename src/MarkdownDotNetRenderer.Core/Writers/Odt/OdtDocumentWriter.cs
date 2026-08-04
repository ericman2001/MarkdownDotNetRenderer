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
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Writers.Odt;

/// <summary>
/// Assembles one OpenDocument Text package: prose mapped from the Markdig AST to ODF elements,
/// diagrams written as SVG picture parts and referenced from sized <c>draw:frame</c>s, and
/// unsupported diagrams kept as preformatted source. Every part is hand-written with
/// <see cref="XmlWriter"/> over <see cref="OdtPackageWriter"/>'s <c>ZipArchive</c>, so the path
/// needs no dependency and stays AOT-clean. Timestamps, picture names, and automatic-style names
/// are fixed or sequential, so repeated renders are byte-identical.
/// </summary>
public sealed class OdtDocumentWriter : IDocumentWriter
{
    /// <summary>Frame anchoring used for diagram frames, shared with the graphic style.</summary>
    internal const string AsCharAnchor = "as-char";

    /// <summary>Generator recorded in <c>meta.xml</c>; deliberately version-free for determinism.</summary>
    private const string Generator = MarkdownRenderer.ProductName;

    /// <summary>Fixed creation/modification timestamp, so two renders agree byte for byte.</summary>
    private const string FixedTimestamp = "2026-01-01T00:00:00";

    /// <summary>Sequential name template for the <c>draw:frame</c> of a diagram.</summary>
    private const string FrameNameFormat = "diagram{0}";

    /// <summary>Sequential name template for a table.</summary>
    private const string TableNameFormat = "Table{0}";

    /// <summary>Ordered-list start value ODF assumes, so it is only written when overridden.</summary>
    private const int DefaultListStart = 1;

    private readonly OdtTheme _theme;

    /// <summary>Creates a writer using the default visual theme.</summary>
    public OdtDocumentWriter()
        : this(OdtTheme.Default)
    {
    }

    /// <summary>Creates a writer with a specific visual theme.</summary>
    /// <param name="theme">Visual values for the emitted styles.</param>
    public OdtDocumentWriter(OdtTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <inheritdoc />
    public string FileExtension => ".odt";

    /// <inheritdoc />
    public string ContentType => OdfNames.TextMediaType;

    /// <inheritdoc />
    public Task WriteAsync(
        DocumentContent content,
        Stream destination,
        RenderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Picture> pictures = AssignPictures(content);
        var styles = new OdtStyles.OdtAutomaticStyles(_theme);

        // Automatic styles must precede the body in content.xml but are only discovered while
        // writing it, so the body is written twice: once to fill the style cache, once for real.
        // The cache is keyed by property set, so the second pass adds nothing and names match.
        _ = OdtXml.WritePart(writer => WriteContentDocument(
            writer, content, options, styles, pictures, includeStyles: false, cancellationToken));
        int discoveredStyles = styles.Count;
        byte[] contentPart = OdtXml.WritePart(writer => WriteContentDocument(
            writer, content, options, styles, pictures, includeStyles: true, cancellationToken));

        // A style first requested in the second pass would be referenced but never declared, so
        // fail loudly rather than emit a document whose style names do not resolve.
        if (styles.Count != discoveredStyles)
        {
            throw new InvalidOperationException(
                "The ODT body requested automatic styles that the first pass did not discover; "
                + "every style request must be a pure function of the document.");
        }

        var package = new OdtPackageWriter();
        package.AddPart(OdfNames.ContentEntry, OdfNames.XmlMediaType, contentPart);
        package.AddPart(
            OdfNames.StylesEntry,
            OdfNames.XmlMediaType,
            OdtStyles.BuildStylesPart(_theme, options.FontFamily));
        package.AddPart(
            OdfNames.MetaEntry,
            OdfNames.XmlMediaType,
            BuildMetaPart(DocumentTitle.Resolve(content, options)));

        foreach (Picture picture in pictures)
        {
            package.AddPart(
                picture.EntryName,
                OdfNames.SvgMediaType,
                Encoding.UTF8.GetBytes(
                    OdfNames.XmlProlog + OdfNames.Newline + picture.Block.SvgFragment
                    + OdfNames.Newline));
        }

        package.WriteTo(destination);
        return Task.CompletedTask;
    }

    private static IReadOnlyList<Picture> AssignPictures(DocumentContent content)
    {
        var pictures = new List<Picture>();
        foreach (DocumentBlock block in content.Blocks)
        {
            if (block is DiagramBlock diagram)
            {
                int index = pictures.Count + 1;
                pictures.Add(new Picture(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        OdfNames.DiagramPictureNameFormat,
                        index),
                    string.Format(CultureInfo.InvariantCulture, FrameNameFormat, index),
                    diagram));
            }
        }

        return pictures;
    }

    private static byte[] BuildMetaPart(string title) => OdtXml.WritePart(writer =>
    {
        writer.StartOffice("document-meta");
        writer.WriteNamespaceDeclarations();
        writer.OfficeAttribute("version", OdfNames.Version);

        writer.StartOffice("meta");
        writer.WriteStartElement(OdfNames.MetaPrefix, "generator", OdfNames.MetaNs);
        writer.WriteString(Generator);
        writer.WriteEndElement();
        writer.WriteStartElement(OdfNames.DcPrefix, "title", OdfNames.DcNs);
        writer.WriteText(title);
        writer.WriteEndElement();
        writer.WriteStartElement(OdfNames.MetaPrefix, "creation-date", OdfNames.MetaNs);
        writer.WriteString(FixedTimestamp);
        writer.WriteEndElement();
        writer.WriteStartElement(OdfNames.DcPrefix, "date", OdfNames.DcNs);
        writer.WriteString(FixedTimestamp);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteEndElement();
    });

    private void WriteContentDocument(
        XmlWriter writer,
        DocumentContent content,
        RenderOptions options,
        OdtStyles.OdtAutomaticStyles styles,
        IReadOnlyList<Picture> pictures,
        bool includeStyles,
        CancellationToken cancellationToken)
    {
        writer.StartOffice("document-content");
        writer.WriteNamespaceDeclarations();
        writer.OfficeAttribute("version", OdfNames.Version);

        if (includeStyles)
        {
            OdtStyles.WriteFontFaceDeclarations(writer, _theme, options.FontFamily);
            styles.Write(writer);
        }

        writer.StartOffice("body");
        writer.StartOffice("text");

        var context = new BodyContext(writer, styles);
        int pictureIndex = 0;
        foreach (DocumentBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (block)
            {
                case ProseBlock prose:
                    foreach (Block node in prose.Nodes)
                    {
                        WriteBlock(context, node, OdtStyles.StandardParagraph);
                    }

                    break;

                case DiagramBlock:
                    WriteDiagram(context, pictures[pictureIndex++]);
                    break;

                case CodeBlock code:
                    WriteCodeLines(context, SplitLines(code.Text));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Document block type '{block.GetType().Name}' is not supported by the " +
                        "ODT writer.");
            }
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private void WriteBlock(BodyContext context, Block block, string paragraphStyle)
    {
        XmlWriter writer = context.Writer;
        switch (block)
        {
            case HeadingBlock heading:
                writer.StartText("h");
                writer.TextAttribute(
                    "style-name",
                    OdtStyles.HeadingStyle(heading.Level));
                writer.TextAttribute(
                    "outline-level",
                    OdtXml.Integer(Math.Clamp(heading.Level, 1, OdtStyles.MaxHeadingLevel)));
                WriteInlines(context, heading.Inline, default);
                writer.WriteEndElement();
                break;

            case ParagraphBlock paragraph:
                writer.StartText("p");
                writer.TextAttribute("style-name", paragraphStyle);
                WriteInlines(context, paragraph.Inline, default);
                writer.WriteEndElement();
                break;

            case QuoteBlock quote:
                foreach (Block child in quote)
                {
                    WriteBlock(context, child, OdtStyles.Quotations);
                }

                break;

            case ListBlock list:
                WriteList(context, list, paragraphStyle);
                break;

            case ThematicBreakBlock:
                writer.StartText("p");
                writer.TextAttribute("style-name", OdtStyles.HorizontalLine);
                writer.WriteEndElement();
                break;

            case Table table:
                WriteTable(context, table);
                break;

            case Markdig.Syntax.CodeBlock code:
                WriteCodeLines(context, ReadLines(code.Lines));
                break;

            case HtmlBlock html:
                // Raw HTML has no ODF equivalent; keep its text so no content is lost.
                foreach (string line in ReadLines(html.Lines))
                {
                    writer.StartText("p");
                    writer.TextAttribute("style-name", paragraphStyle);
                    writer.WriteText(line);
                    writer.WriteEndElement();
                }

                break;

            case LinkReferenceDefinitionGroup:
                break;

            case ContainerBlock container:
                foreach (Block child in container)
                {
                    WriteBlock(context, child, paragraphStyle);
                }

                break;

            case LeafBlock leaf when leaf.Inline is not null:
                writer.StartText("p");
                writer.TextAttribute("style-name", paragraphStyle);
                WriteInlines(context, leaf.Inline, default);
                writer.WriteEndElement();
                break;

            case LeafBlock leaf when leaf.Lines.Count > 0:
                // An unmapped leaf block (a math block from an extension, say) still owns source
                // lines; keep them verbatim rather than dropping the block.
                WriteCodeLines(context, ReadLines(leaf.Lines));
                break;

            default:
                break;
        }
    }

    private void WriteList(BodyContext context, ListBlock list, string paragraphStyle)
    {
        XmlWriter writer = context.Writer;
        writer.StartText("list");
        writer.TextAttribute(
            "style-name",
            list.IsOrdered ? OdtStyles.NumberedList : OdtStyles.BulletList);

        bool firstItem = true;
        foreach (Block item in list)
        {
            writer.StartText("list-item");
            if (firstItem && list.IsOrdered && TryGetListStart(list, out int start))
            {
                writer.TextAttribute("start-value", OdtXml.Integer(start));
            }

            firstItem = false;
            if (item is ListItemBlock listItem)
            {
                foreach (Block child in listItem)
                {
                    WriteBlock(context, child, paragraphStyle);
                }
            }
            else
            {
                WriteBlock(context, item, paragraphStyle);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static bool TryGetListStart(ListBlock list, out int start)
    {
        if (int.TryParse(
                list.OrderedStart,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            && parsed != DefaultListStart)
        {
            start = parsed;
            return true;
        }

        start = DefaultListStart;
        return false;
    }

    private void WriteTable(BodyContext context, Table table)
    {
        XmlWriter writer = context.Writer;
        int columnCount = Math.Max(1, table.ColumnDefinitions.Count);

        writer.StartTable("table");
        writer.TableAttribute(
            "name",
            string.Format(CultureInfo.InvariantCulture, TableNameFormat, context.NextTableIndex()));
        writer.TableAttribute("style-name", context.Styles.Table());

        writer.StartTable("table-column");
        writer.TableAttribute("style-name", context.Styles.TableColumn());
        if (columnCount > 1)
        {
            writer.TableAttribute("number-columns-repeated", OdtXml.Integer(columnCount));
        }

        writer.WriteEndElement();

        // ODF allows one table:table-header-rows group, before the body rows. Markdig only ever
        // produces leading header rows; a stray later one is written as a body row rather than
        // opening a second, invalid group.
        bool headerOpen = false;
        bool headerClosed = false;
        int[] rowSpans = new int[columnCount];
        foreach (Block rowBlock in table)
        {
            if (rowBlock is not TableRow row)
            {
                continue;
            }

            if (row.IsHeader && !headerOpen && !headerClosed)
            {
                writer.StartTable("table-header-rows");
                headerOpen = true;
            }
            else if (!row.IsHeader && headerOpen)
            {
                writer.WriteEndElement();
                headerOpen = false;
                headerClosed = true;
            }

            WriteTableRow(context, table, row, rowSpans);
        }

        if (headerOpen)
        {
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>
    /// Writes one row. A merged cell must be followed by <c>table:covered-table-cell</c>
    /// placeholders for every grid position it hides, horizontally in this row and vertically in
    /// the rows below, or consumers read the row as short and shift the remaining values left.
    /// </summary>
    /// <param name="context">The writer state.</param>
    /// <param name="table">The table being written, for its column alignments.</param>
    /// <param name="row">The row to write.</param>
    /// <param name="rowSpans">Per-column count of rows still covered by an earlier cell.</param>
    private void WriteTableRow(BodyContext context, Table table, TableRow row, int[] rowSpans)
    {
        XmlWriter writer = context.Writer;
        writer.StartTable("table-row");

        int column = 0;
        foreach (Block cellBlock in row)
        {
            if (cellBlock is not TableCell cell)
            {
                continue;
            }

            column = WriteCoveredCells(writer, rowSpans, column);

            int columnSpan = Math.Max(1, cell.ColumnSpan);
            int rowSpan = Math.Max(1, cell.RowSpan);
            string? alignment = ColumnAlignment(table, column);
            string paragraphStyle = context.Styles.CellParagraph(row.IsHeader, alignment);

            writer.StartTable("table-cell");
            writer.TableAttribute("style-name", context.Styles.TableCell(row.IsHeader));
            writer.OfficeAttribute("value-type", "string");
            if (columnSpan > 1)
            {
                writer.TableAttribute("number-columns-spanned", OdtXml.Integer(columnSpan));
            }

            if (rowSpan > 1)
            {
                writer.TableAttribute("number-rows-spanned", OdtXml.Integer(rowSpan));
            }

            foreach (Block child in cell)
            {
                WriteBlock(context, child, paragraphStyle);
            }

            writer.WriteEndElement();

            for (int covered = 1; covered < columnSpan; covered++)
            {
                WriteCoveredCell(writer);
            }

            for (int offset = 0; offset < columnSpan && column + offset < rowSpans.Length; offset++)
            {
                rowSpans[column + offset] = rowSpan - 1;
            }

            column += columnSpan;
        }

        _ = WriteCoveredCells(writer, rowSpans, column);

        writer.WriteEndElement();
    }

    /// <summary>Emits placeholders for the columns a previous row's vertical span still covers.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="rowSpans">Per-column count of rows still covered; decremented as consumed.</param>
    /// <param name="column">The grid column the next cell would occupy.</param>
    /// <returns>The first grid column not covered by an earlier cell.</returns>
    private static int WriteCoveredCells(XmlWriter writer, int[] rowSpans, int column)
    {
        while (column < rowSpans.Length && rowSpans[column] > 0)
        {
            rowSpans[column]--;
            WriteCoveredCell(writer);
            column++;
        }

        return column;
    }

    private static void WriteCoveredCell(XmlWriter writer)
    {
        writer.StartTable("covered-table-cell");
        writer.WriteEndElement();
    }

    private static string? ColumnAlignment(Table table, int column)
    {
        if (column >= table.ColumnDefinitions.Count)
        {
            return null;
        }

        return table.ColumnDefinitions[column].Alignment switch
        {
            TableColumnAlign.Left => "start",
            TableColumnAlign.Center => "center",
            TableColumnAlign.Right => "end",
            _ => null,
        };
    }

    private void WriteDiagram(BodyContext context, Picture picture)
    {
        XmlWriter writer = context.Writer;
        DiagramBlock diagram = picture.Block;

        double widthInches = CssUnits.PixelsToInches(diagram.Width);
        double heightInches = CssUnits.PixelsToInches(diagram.Height);
        if (widthInches > _theme.ContentWidth && widthInches > 0)
        {
            double scale = _theme.ContentWidth / widthInches;
            widthInches *= scale;
            heightInches *= scale;
        }

        writer.StartText("p");
        writer.TextAttribute("style-name", OdtStyles.StandardParagraph);

        writer.StartDraw("frame");
        writer.DrawAttribute("style-name", OdtStyles.Graphics);
        writer.DrawAttribute("name", picture.FrameName);
        writer.TextAttribute("anchor-type", AsCharAnchor);
        writer.SvgAttribute("width", OdtXml.Inches(widthInches));
        writer.SvgAttribute("height", OdtXml.Inches(heightInches));

        writer.StartDraw("image");
        writer.XlinkAttribute("href", picture.EntryName);
        writer.XlinkAttribute("type", "simple");
        writer.XlinkAttribute("show", "embed");
        writer.XlinkAttribute("actuate", "onLoad");
        writer.DrawAttribute("mime-type", OdfNames.SvgMediaType);
        writer.WriteEndElement();

        if (!string.IsNullOrWhiteSpace(diagram.AltText))
        {
            writer.StartSvg("title");
            writer.WriteText(diagram.AltText);
            writer.WriteEndElement();
            writer.StartSvg("desc");
            writer.WriteText(diagram.AltText);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCodeLines(BodyContext context, IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            context.Writer.StartText("p");
            context.Writer.TextAttribute("style-name", OdtStyles.PreformattedText);
            WritePreformattedText(context.Writer, line);
            context.Writer.WriteEndElement();
        }
    }

    /// <summary>
    /// Writes code text preserving indentation: ODF collapses whitespace in a paragraph, so runs
    /// of spaces become <c>text:s</c> and tabs become <c>text:tab</c>.
    /// </summary>
    private static void WritePreformattedText(XmlWriter writer, string line)
    {
        int index = 0;
        while (index < line.Length)
        {
            char current = line[index];
            if (current == '\t')
            {
                writer.StartText("tab");
                writer.WriteEndElement();
                index++;
                continue;
            }

            if (current == ' ')
            {
                int run = 0;
                while (index + run < line.Length && line[index + run] == ' ')
                {
                    run++;
                }

                // A single space between words needs no markup; only runs and leading spaces do.
                if (run == 1 && index > 0)
                {
                    writer.WriteString(" ");
                }
                else
                {
                    writer.StartText("s");
                    if (run > 1)
                    {
                        writer.TextAttribute("c", OdtXml.Integer(run));
                    }

                    writer.WriteEndElement();
                }

                index += run;
                continue;
            }

            int textStart = index;
            while (index < line.Length && line[index] is not ' ' and not '\t')
            {
                index++;
            }

            writer.WriteText(line[textStart..index]);
        }
    }

    private void WriteInlines(BodyContext context, ContainerInline? container, SpanFormat format)
    {
        if (container is null)
        {
            return;
        }

        XmlWriter writer = context.Writer;
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    writer.WriteText(literal.Content.ToString());
                    break;

                case CodeInline code:
                    WriteSpan(context, format.WithCode(), () => writer.WriteText(code.Content));
                    break;

                case EmphasisInline emphasis:
                    SpanFormat inner = format.With(emphasis);
                    if (inner.Equals(format))
                    {
                        WriteInlines(context, emphasis, inner);
                    }
                    else
                    {
                        WriteSpan(context, inner, () => WriteInlines(context, emphasis, inner));
                    }

                    break;

                case TaskList task:
                    writer.WriteText(task.Checked
                        ? _theme.TaskListCheckedMarker
                        : _theme.TaskListUncheckedMarker);
                    break;

                case LinkInline { IsImage: true } image:
                    // Images from the Markdown source are not embedded: the writer has no base
                    // directory to resolve them against. The alt text keeps the meaning.
                    WriteImageFallback(context, image, format);
                    break;

                case LinkInline link:
                    writer.StartText("a");
                    writer.XlinkAttribute("type", "simple");
                    writer.XlinkAttribute("href", link.Url ?? string.Empty);
                    WriteInlines(context, link, format);
                    writer.WriteEndElement();
                    break;

                case AutolinkInline autolink:
                    writer.StartText("a");
                    writer.XlinkAttribute("type", "simple");
                    writer.XlinkAttribute("href", autolink.Url);
                    writer.WriteText(autolink.Url);
                    writer.WriteEndElement();
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        writer.StartText("line-break");
                        writer.WriteEndElement();
                    }
                    else
                    {
                        writer.WriteString(" ");
                    }

                    break;

                case HtmlEntityInline entity:
                    writer.WriteText(entity.Transcoded.ToString());
                    break;

                case HtmlInline html:
                    // Raw inline HTML: emit its text so nothing is silently dropped.
                    writer.WriteText(html.Tag);
                    break;

                case MathInline math:
                    // No ODF equivalent for TeX; keep the source, delimiters included.
                    WriteSpan(context, format.WithCode(), () =>
                    {
                        string delimiter = new(math.Delimiter, math.DelimiterCount);
                        writer.WriteText(delimiter + math.Content.ToString() + delimiter);
                    });
                    break;

                case AbbreviationInline abbreviation:
                    writer.WriteText(abbreviation.Abbreviation.Label ?? string.Empty);
                    break;

                case FootnoteLink { IsBackLink: false } footnote:
                    writer.WriteText(FootnoteMarker(footnote));
                    break;

                case ContainerInline nested:
                    WriteInlines(context, nested, format);
                    break;

                default:
                    // A leaf inline with no text of its own (a footnote back-link, say); there is
                    // nothing to keep.
                    break;
            }
        }
    }

    private void WriteImageFallback(BodyContext context, LinkInline image, SpanFormat format)
    {
        if (image.FirstChild is not null)
        {
            WriteInlines(context, image, format);
            return;
        }

        context.Writer.WriteText(image.Url ?? string.Empty);
    }

    private static string FootnoteMarker(FootnoteLink link) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{(link.Footnote.Order > 0 ? link.Footnote.Order : link.Index)}]");

    private static void WriteSpan(BodyContext context, SpanFormat format, Action writeContent)
    {
        context.Writer.StartText("span");
        context.Writer.TextAttribute("style-name", context.Styles.TextSpan(
            format.Bold, format.Italic, format.Strikethrough, format.Code));
        writeContent();
        context.Writer.WriteEndElement();
    }

    private static IReadOnlyList<string> ReadLines(StringLineGroup lines)
    {
        var result = new List<string>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            result.Add(lines.Lines[i].Slice.ToString());
        }

        return result;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        string[] lines = text
            .Replace("\r\n", OdfNames.Newline, StringComparison.Ordinal)
            .Replace("\r", OdfNames.Newline, StringComparison.Ordinal)
            .Split('\n');

        // A trailing newline is a terminator, not an empty last line of the code block.
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Writer state threaded through the block and inline visitors.</summary>
    private sealed class BodyContext(XmlWriter writer, OdtStyles.OdtAutomaticStyles styles)
    {
        private int _tableCount;

        internal XmlWriter Writer { get; } = writer;

        internal OdtStyles.OdtAutomaticStyles Styles { get; } = styles;

        internal int NextTableIndex() => ++_tableCount;
    }

    /// <summary>A diagram's picture part and the frame that references it.</summary>
    private sealed record Picture(string EntryName, string FrameName, DiagramBlock Block);

    /// <summary>The inline formatting active at a point in the AST walk.</summary>
    private readonly record struct SpanFormat(bool Bold, bool Italic, bool Strikethrough, bool Code)
    {
        /// <summary>Markdig's delimiter count at which emphasis means bold rather than italic.</summary>
        private const int StrongDelimiterCount = 2;

        internal SpanFormat WithCode() => this with { Code = true };

        internal SpanFormat With(EmphasisInline emphasis) => emphasis.DelimiterChar switch
        {
            '~' when emphasis.DelimiterCount >= StrongDelimiterCount =>
                this with { Strikethrough = true },
            '*' or '_' when emphasis.DelimiterCount >= StrongDelimiterCount =>
                this with { Bold = true },
            '*' or '_' => this with { Italic = true },
            _ => this,
        };
    }
}
