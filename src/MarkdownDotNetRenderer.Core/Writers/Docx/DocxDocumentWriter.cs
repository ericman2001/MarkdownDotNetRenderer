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
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownDotNetRenderer.Svg;
using MarkdownDotNetRenderer.Writers.Odt;
// Markdig and WordprocessingML both define Table, TableRow and TableCell; the unprefixed names
// here are the OOXML ones, and the AST types are aliased.
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using MdTableRow = Markdig.Extensions.Tables.TableRow;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// Assembles one WordprocessingML package: prose mapped from the Markdig AST to OOXML elements,
/// diagrams embedded as SVG image parts referenced from an inline <c>Drawing</c>, and unsupported
/// diagrams kept as verbatim code. Every OpenXml type used lives in this namespace — the public
/// surface exchanges only <see cref="DocumentContent"/>, <see cref="Stream"/> and
/// <see cref="RenderOptions"/> (docs/06-aot-and-dependencies.md). Timestamps, drawing ids,
/// numbering ids, and relationship ids are fixed or sequential, so repeated renders produce an
/// identical <c>document.xml</c>.
/// </summary>
public sealed class DocxDocumentWriter : IDocumentWriter, IDiagnosticReportingWriter
{
    /// <summary>Fixed creation/modification timestamp, so two renders agree byte for byte.</summary>
    private static readonly DateTime FixedTimestamp =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Ordered-list start value OOXML assumes, so it is only overridden when different.</summary>
    private const int DefaultListStart = DocxStyles.DefaultListStart;

    /// <summary>Table width expressed in fiftieths of a percent: the full text column.</summary>
    private const string FullWidthPercent = "5000";

    /// <summary>Space between a paragraph border and its text, in points.</summary>
    private const uint BorderSpace = 4;

    /// <summary>Scheme prepended to an email autolink so Word opens a mail client.</summary>
    private const string MailToScheme = "mailto:";

    /// <summary>Namespace of the OPC core properties part.</summary>
    private const string CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    private const string DublinCoreTermsNamespace = "http://purl.org/dc/terms/";
    private const string XmlSchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>W3CDTF layout the core properties timestamps must use.</summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>
    /// Prefix of every relationship id the writer assigns. OpenXml would otherwise mint a fresh
    /// GUID per relationship, which two renders of the same input could not agree on.
    /// </summary>
    private const string RelationshipIdPrefix = "rId";

    private const string DocumentRelationshipId = RelationshipIdPrefix + "1";
    private const string CorePropertiesRelationshipId = RelationshipIdPrefix + "2";
    private const string StylesRelationshipId = RelationshipIdPrefix + "1";
    private const string NumberingRelationshipId = RelationshipIdPrefix + "2";

    /// <summary>First id handed out for the images and hyperlinks the body discovers.</summary>
    private const int FirstContentRelationshipNumber = 3;

    private readonly DocxTheme _theme;
    private readonly List<RenderDiagnostic> _diagnostics = [];

    /// <summary>Creates a writer using the default visual theme.</summary>
    public DocxDocumentWriter()
        : this(DocxTheme.Default)
    {
    }

    /// <summary>Creates a writer with a specific visual theme.</summary>
    /// <param name="theme">Visual values for the emitted styles.</param>
    public DocxDocumentWriter(DocxTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
    }

    /// <inheritdoc />
    public string FileExtension => ".docx";

    /// <inheritdoc />
    public string ContentType => OoxmlNames.DocumentContentType;

    /// <inheritdoc />
    public IReadOnlyList<RenderDiagnostic> Diagnostics => _diagnostics;

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

        _diagnostics.Clear();

        // The package is built over a buffer rather than the caller's stream: OpenXml's package
        // takes ownership of the stream it is given, and IDocumentWriter promises to leave the
        // destination open.
        using var buffer = new MemoryStream();
        WritePackage(content, buffer, options, cancellationToken);

        buffer.Position = 0;
        DocxPackageWriter.CopyWithFixedTimestamps(buffer, destination, FixedTimestamp);
        return Task.CompletedTask;
    }

    private void WritePackage(
        DocumentContent content,
        Stream buffer,
        RenderOptions options,
        CancellationToken cancellationToken)
    {
        using WordprocessingDocument document = WordprocessingDocument.Create(
            buffer,
            WordprocessingDocumentType.Document);

        MainDocumentPart main = document.AddNewPart<MainDocumentPart>(
            OoxmlNames.MainDocumentPartContentType,
            DocumentRelationshipId);
        WriteCoreProperties(document, DocumentTitle.Resolve(content, options));

        var body = new Body();
        main.Document = new Document(body);

        StyleDefinitionsPart stylesPart =
            main.AddNewPart<StyleDefinitionsPart>(StylesRelationshipId);
        stylesPart.Styles = DocxStyles.BuildStyles(_theme, options.FontFamily);

        // The numbering part is added before the body is walked so its relationship id does not
        // depend on how many images the document has, and filled afterwards because the ordered
        // lists it must declare are only known once they have been visited.
        NumberingDefinitionsPart numberingPart =
            main.AddNewPart<NumberingDefinitionsPart>(NumberingRelationshipId);

        var context = new BodyContext(main, _diagnostics);
        WriteBlocks(context, body, content, cancellationToken);
        body.Append(BuildSectionProperties());

        numberingPart.Numbering = DocxStyles.BuildNumbering(_theme, context.OrderedListStarts);
    }

    /// <summary>
    /// Writes the core properties as a typed part rather than through
    /// <c>PackageProperties</c>: the packaging layer would name that part after a fresh GUID, which
    /// would make two renders of the same input differ.
    /// </summary>
    private static void WriteCoreProperties(WordprocessingDocument document, string title)
    {
        CoreFilePropertiesPart part =
            document.AddNewPart<CoreFilePropertiesPart>(CorePropertiesRelationshipId);
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        using var writer = XmlWriter.Create(
            stream,
            new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                CloseOutput = false,
            });

        writer.WriteStartDocument(standalone: true);
        writer.WriteStartElement("cp", "coreProperties", CorePropertiesNamespace);
        writer.WriteAttributeString("xmlns", "dc", null, DublinCoreNamespace);
        writer.WriteAttributeString("xmlns", "dcterms", null, DublinCoreTermsNamespace);
        writer.WriteAttributeString("xmlns", "xsi", null, XmlSchemaInstanceNamespace);

        writer.WriteElementString("dc", "title", DublinCoreNamespace, OdtXml.SanitizeText(title));
        writer.WriteElementString(
            "dc",
            "creator",
            DublinCoreNamespace,
            MarkdownRenderer.ProductName);
        writer.WriteElementString(
            "cp",
            "lastModifiedBy",
            CorePropertiesNamespace,
            MarkdownRenderer.ProductName);

        foreach (string element in new[] { "created", "modified" })
        {
            writer.WriteStartElement("dcterms", element, DublinCoreTermsNamespace);
            writer.WriteAttributeString(
                "xsi",
                "type",
                XmlSchemaInstanceNamespace,
                "dcterms:W3CDTF");
            writer.WriteString(FixedTimestamp.ToString(
                TimestampFormat,
                CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private void WriteBlocks(
        BodyContext context,
        Body body,
        DocumentContent content,
        CancellationToken cancellationToken)
    {
        foreach (DocumentBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (block)
            {
                case ProseBlock prose:
                    foreach (Block node in prose.Nodes)
                    {
                        WriteBlock(context, body, node, ParagraphContext.Body);
                    }

                    break;

                case DiagramBlock diagram:
                    WriteDiagram(context, body, diagram);
                    break;

                case Writers.CodeBlock code:
                    WriteCodeBlock(body, SplitLines(code.Text));
                    break;

                default:
                    throw new NotSupportedException(
                        $"Document block type '{block.GetType().Name}' is not supported by the "
                        + "DOCX writer.");
            }
        }
    }

    private void WriteBlock(
        BodyContext context,
        OpenXmlCompositeElement parent,
        Block block,
        ParagraphContext paragraphContext)
    {
        switch (block)
        {
            case HeadingBlock heading:
                parent.Append(BuildParagraph(
                    context,
                    heading.Inline,
                    paragraphContext with
                    {
                        StyleId = DocxStyles.HeadingStyleId(heading.Level),
                    }));
                break;

            case ParagraphBlock paragraph:
                parent.Append(BuildParagraph(context, paragraph.Inline, paragraphContext));
                break;

            case QuoteBlock quote:
                foreach (Block child in quote)
                {
                    WriteBlock(
                        context,
                        parent,
                        child,
                        paragraphContext with { StyleId = DocxStyles.QuoteStyleId });
                }

                break;

            case ListBlock list:
                WriteList(context, parent, list, paragraphContext);
                break;

            case ThematicBreakBlock:
                parent.Append(BuildThematicBreak(paragraphContext));
                break;

            case MdTable table:
                WriteTable(context, parent, table, paragraphContext);
                break;

            case Markdig.Syntax.CodeBlock code:
                WriteCodeBlock(parent, ReadLines(code.Lines));
                break;

            case HtmlBlock html:
                // Raw HTML has no WordprocessingML equivalent; keep its text so no content is lost.
                context.Report("Raw HTML block rendered as plain text.", html.Line);
                foreach (string line in ReadLines(html.Lines))
                {
                    parent.Append(BuildTextParagraph(line, paragraphContext));
                }

                break;

            case LinkReferenceDefinitionGroup:
                break;

            case ContainerBlock container:
                foreach (Block child in container)
                {
                    WriteBlock(context, parent, child, paragraphContext);
                }

                break;

            case LeafBlock leaf when leaf.Inline is not null:
                parent.Append(BuildParagraph(context, leaf.Inline, paragraphContext));
                break;

            case LeafBlock leaf when leaf.Lines.Count > 0:
                // An unmapped leaf block (a math block from an extension, say) still owns source
                // lines; keep them verbatim rather than dropping the block.
                context.Report(
                    $"Markdown block '{leaf.GetType().Name}' rendered as verbatim text.",
                    leaf.Line);
                WriteCodeBlock(parent, ReadLines(leaf.Lines));
                break;

            default:
                break;
        }
    }

    private void WriteList(
        BodyContext context,
        OpenXmlCompositeElement parent,
        ListBlock list,
        ParagraphContext paragraphContext)
    {
        int level = Math.Min(paragraphContext.ListLevel, DocxStyles.MaxListLevel);

        // Bullets carry no counter, so one shared numbering instance is enough; every ordered list
        // gets its own instance, otherwise Word continues the previous list's numbering.
        int numberingId = list.IsOrdered
            ? context.AddOrderedList(ListStart(list))
            : DocxStyles.BulletNumberingId;

        foreach (Block item in list)
        {
            IEnumerable<Block> children = item is ListItemBlock listItem ? listItem : [item];
            bool numbered = false;
            foreach (Block child in children)
            {
                ParagraphContext childContext = child is ListBlock
                    ? paragraphContext with { ListLevel = level + 1, NumberingId = null }
                    : numbered
                        // A continuation paragraph must not repeat the item's label, so it is
                        // indented to the level instead of carrying numbering properties.
                        ? paragraphContext with
                        {
                            NumberingId = null,
                            IndentInches = DocxStyles.ListLevelIndent(_theme, level),
                        }
                        : paragraphContext with { NumberingId = numberingId, ListLevel = level };

                numbered |= child is not ListBlock;
                WriteBlock(context, parent, child, childContext);
            }
        }
    }

    private static int ListStart(ListBlock list) => int.TryParse(
        list.OrderedStart,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out int parsed)
        ? parsed
        : DefaultListStart;

    private void WriteTable(
        BodyContext context,
        OpenXmlCompositeElement parent,
        MdTable table,
        ParagraphContext paragraphContext)
    {
        int columnCount = Math.Max(1, table.ColumnDefinitions.Count);
        var element = new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = FullWidthPercent },
                BuildTableBorders(),
                new TableLayout { Type = TableLayoutValues.Autofit }),
            BuildTableGrid(columnCount));

        int[] rowSpans = new int[columnCount];
        foreach (Block rowBlock in table)
        {
            if (rowBlock is MdTableRow row)
            {
                element.Append(BuildTableRow(context, table, row, rowSpans, paragraphContext));
            }
        }

        parent.Append(element);
    }

    /// <summary>
    /// Builds one row. A horizontal merge is expressed on the cell itself with <c>gridSpan</c>,
    /// while a vertical merge needs a continuation cell in every row it covers, or Word reads the
    /// row as short and shifts the remaining cells left.
    /// </summary>
    private TableRow BuildTableRow(
        BodyContext context,
        MdTable table,
        MdTableRow row,
        int[] rowSpans,
        ParagraphContext paragraphContext)
    {
        var element = new TableRow();
        if (row.IsHeader)
        {
            element.Append(new TableRowProperties(new TableHeader()));
        }

        int column = 0;
        foreach (Block cellBlock in row)
        {
            if (cellBlock is not MdTableCell cell)
            {
                continue;
            }

            column = AppendMergedCells(element, rowSpans, column);

            int columnSpan = Math.Max(1, cell.ColumnSpan);
            int rowSpan = Math.Max(1, cell.RowSpan);
            element.Append(BuildTableCell(
                context,
                cell,
                row.IsHeader,
                columnSpan,
                rowSpan > 1,
                ColumnAlignment(table, column),
                paragraphContext));

            for (int offset = 0; offset < columnSpan && column + offset < rowSpans.Length; offset++)
            {
                rowSpans[column + offset] = rowSpan - 1;
            }

            column += columnSpan;
        }

        _ = AppendMergedCells(element, rowSpans, column);
        return element;
    }

    private TableCell BuildTableCell(
        BodyContext context,
        MdTableCell cell,
        bool isHeader,
        int columnSpan,
        bool startsVerticalMerge,
        JustificationValues? alignment,
        ParagraphContext paragraphContext)
    {
        var properties = new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Auto });
        if (columnSpan > 1)
        {
            properties.Append(new GridSpan { Val = columnSpan });
        }

        if (startsVerticalMerge)
        {
            properties.Append(new VerticalMerge { Val = MergedCellValues.Restart });
        }

        if (isHeader)
        {
            properties.Append(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = _theme.TableHeaderBackgroundColor,
            });
        }

        var element = new TableCell(properties);
        ParagraphContext cellContext = paragraphContext with
        {
            StyleId = DocxStyles.NormalStyleId,
            NumberingId = null,
            IndentInches = null,
            Alignment = alignment,
            BoldRuns = isHeader,
        };

        foreach (Block child in cell)
        {
            WriteBlock(context, element, child, cellContext);
        }

        // A table cell must end with a block-level child; an empty Markdown cell has none.
        if (!element.Elements<Paragraph>().Any() && !element.Elements<Table>().Any())
        {
            element.Append(BuildTextParagraph(string.Empty, cellContext));
        }

        return element;
    }

    /// <summary>Appends continuation cells for the columns a previous row's merge still covers.</summary>
    private static int AppendMergedCells(TableRow row, int[] rowSpans, int column)
    {
        while (column < rowSpans.Length && rowSpans[column] > 0)
        {
            rowSpans[column]--;
            row.Append(new TableCell(
                new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Auto },
                    new VerticalMerge { Val = MergedCellValues.Continue }),
                new Paragraph()));
            column++;
        }

        return column;
    }

    private TableBorders BuildTableBorders()
    {
        uint size = DocxUnits.EighthPoints(_theme.TableBorderWidth);
        string color = _theme.TableBorderColor;
        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Color = color, Size = size },
            new LeftBorder { Val = BorderValues.Single, Color = color, Size = size },
            new BottomBorder { Val = BorderValues.Single, Color = color, Size = size },
            new RightBorder { Val = BorderValues.Single, Color = color, Size = size },
            new InsideHorizontalBorder { Val = BorderValues.Single, Color = color, Size = size },
            new InsideVerticalBorder { Val = BorderValues.Single, Color = color, Size = size });
    }

    private TableGrid BuildTableGrid(int columnCount)
    {
        var grid = new TableGrid();
        string width = DocxUnits.Twips(_theme.ContentWidth / columnCount);
        for (int column = 0; column < columnCount; column++)
        {
            grid.Append(new GridColumn { Width = width });
        }

        return grid;
    }

    private static JustificationValues? ColumnAlignment(MdTable table, int column)
    {
        if (column >= table.ColumnDefinitions.Count)
        {
            return null;
        }

        return table.ColumnDefinitions[column].Alignment switch
        {
            MdTableColumnAlign.Left => JustificationValues.Left,
            MdTableColumnAlign.Center => JustificationValues.Center,
            MdTableColumnAlign.Right => JustificationValues.Right,
            _ => null,
        };
    }

    /// <summary>
    /// Writes verbatim source as a single-cell shaded table: one paragraph per line, so long lines
    /// wrap inside the block instead of running past the margin.
    /// </summary>
    private void WriteCodeBlock(
        OpenXmlCompositeElement parent,
        IReadOnlyList<string> lines)
    {
        var cell = new TableCell(
            new TableCellProperties(
                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = FullWidthPercent },
                new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = _theme.CodeBackgroundColor,
                }));

        foreach (string line in lines)
        {
            cell.Append(BuildCodeLine(line));
        }

        if (lines.Count == 0)
        {
            cell.Append(BuildCodeLine(string.Empty));
        }

        parent.Append(new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = FullWidthPercent },
                new TableLayout { Type = TableLayoutValues.Fixed }),
            BuildTableGrid(1),
            new TableRow(cell)));
    }

    private Paragraph BuildCodeLine(string line)
    {
        var paragraph = new Paragraph(new ParagraphProperties(
            new ParagraphStyleId { Val = DocxStyles.NormalStyleId },
            new SpacingBetweenLines { After = "0", Before = "0" }));

        if (line.Length > 0)
        {
            paragraph.Append(BuildRun(line, new SpanFormat(Code: true)));
        }

        return paragraph;
    }

    private void WriteDiagram(
        BodyContext context,
        OpenXmlCompositeElement parent,
        DiagramBlock diagram)
    {
        double widthInches = CssUnits.PixelsToInches(diagram.Width);
        double heightInches = CssUnits.PixelsToInches(diagram.Height);
        if (widthInches > _theme.ContentWidth && widthInches > 0)
        {
            double scale = _theme.ContentWidth / widthInches;
            widthInches *= scale;
            heightInches *= scale;
        }

        string relationshipId = context.AddSvgPart(
            OoxmlNames.XmlProlog + OoxmlNames.Newline + diagram.SvgFragment + OoxmlNames.Newline);

        parent.Append(new Paragraph(new Run(DocxDrawing.BuildInlineImage(
            context.Main,
            relationshipId,
            relationshipId,
            DocxUnits.InchesToEmus(widthInches),
            DocxUnits.InchesToEmus(heightInches),
            context.NextDrawingId(),
            diagram.AltText))));
    }

    private Paragraph BuildThematicBreak(ParagraphContext paragraphContext)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = paragraphContext.StyleId },
            new ParagraphBorders(new BottomBorder
            {
                Val = BorderValues.Single,
                Color = _theme.ThematicBreakBorderColor,
                Size = DocxUnits.EighthPoints(_theme.ThematicBreakBorderWidth),
                Space = BorderSpace,
            }));

        return new Paragraph(properties);
    }

    private Paragraph BuildTextParagraph(string text, ParagraphContext paragraphContext)
    {
        var paragraph = new Paragraph(BuildParagraphProperties(paragraphContext));
        if (text.Length > 0)
        {
            paragraph.Append(BuildRun(text, new SpanFormat(Bold: paragraphContext.BoldRuns)));
        }

        return paragraph;
    }

    private Paragraph BuildParagraph(
        BodyContext context,
        ContainerInline? inlines,
        ParagraphContext paragraphContext)
    {
        var paragraph = new Paragraph(BuildParagraphProperties(paragraphContext));
        WriteInlines(
            context,
            paragraph,
            inlines,
            new SpanFormat(Bold: paragraphContext.BoldRuns));
        return paragraph;
    }

    /// <summary>
    /// Builds a paragraph's properties. The child order follows the WordprocessingML schema
    /// (<c>pStyle</c>, <c>numPr</c>, <c>ind</c>, <c>jc</c>); a different order is a validation error.
    /// </summary>
    private static ParagraphProperties BuildParagraphProperties(ParagraphContext paragraphContext)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = paragraphContext.StyleId });

        if (paragraphContext.NumberingId is int numberingId)
        {
            properties.Append(new NumberingProperties(
                new NumberingLevelReference { Val = paragraphContext.ListLevel },
                new NumberingId { Val = numberingId }));
        }

        if (paragraphContext.IndentInches is double indent)
        {
            properties.Append(new Indentation { Left = DocxUnits.Twips(indent) });
        }

        if (paragraphContext.Alignment is { } alignment)
        {
            properties.Append(new Justification { Val = alignment });
        }

        return properties;
    }

    private void WriteInlines(
        BodyContext context,
        OpenXmlCompositeElement parent,
        ContainerInline? container,
        SpanFormat format)
    {
        if (container is null)
        {
            return;
        }

        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    parent.Append(BuildRun(literal.Content.ToString(), format));
                    break;

                case CodeInline code:
                    parent.Append(BuildRun(code.Content ?? string.Empty, format.WithCode()));
                    break;

                case EmphasisInline emphasis:
                    WriteInlines(context, parent, emphasis, format.With(emphasis));
                    break;

                case TaskList task:
                    parent.Append(BuildRun(
                        task.Checked
                            ? _theme.TaskListCheckedMarker
                            : _theme.TaskListUncheckedMarker,
                        format));
                    break;

                case LinkInline { IsImage: true } image:
                    WriteImage(context, parent, image, format);
                    break;

                case LinkInline link:
                    WriteHyperlink(context, parent, link, link.Url, format);
                    break;

                case AutolinkInline autolink:
                    WriteAutolink(context, parent, autolink, format);
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        parent.Append(new Run(new Break()));
                    }
                    else
                    {
                        parent.Append(BuildRun(" ", format));
                    }

                    break;

                case HtmlEntityInline entity:
                    parent.Append(BuildRun(entity.Transcoded.ToString(), format));
                    break;

                case HtmlInline html:
                    // Raw inline HTML: emit its text so nothing is silently dropped.
                    context.Report("Raw inline HTML rendered as plain text.", html.Line);
                    parent.Append(BuildRun(html.Tag, format));
                    break;

                case MathInline math:
                    // No WordprocessingML equivalent for TeX; keep the source, delimiters included.
                    context.Report("Inline maths rendered as verbatim text.", math.Line);
                    string delimiter = new(math.Delimiter, math.DelimiterCount);
                    parent.Append(BuildRun(
                        delimiter + math.Content.ToString() + delimiter,
                        format.WithCode()));
                    break;

                case AbbreviationInline abbreviation:
                    parent.Append(BuildRun(
                        abbreviation.Abbreviation.Label ?? string.Empty,
                        format));
                    break;

                case FootnoteLink { IsBackLink: false } footnote:
                    context.Report("Footnote reference rendered as a plain marker.", footnote.Line);
                    parent.Append(BuildRun(FootnoteMarker(footnote), format));
                    break;

                case ContainerInline nested:
                    WriteInlines(context, parent, nested, format);
                    break;

                default:
                    // A leaf inline with no text of its own (a footnote back-link, say); there is
                    // nothing to keep.
                    break;
            }
        }
    }

    private void WriteAutolink(
        BodyContext context,
        OpenXmlCompositeElement parent,
        AutolinkInline autolink,
        SpanFormat format)
    {
        string url = autolink.IsEmail ? MailToScheme + autolink.Url : autolink.Url;
        if (!TryAppendHyperlink(context, parent, url, out Hyperlink hyperlink))
        {
            parent.Append(BuildRun(autolink.Url, format));
            return;
        }

        hyperlink.Append(BuildRun(autolink.Url, format.WithHyperlink()));
    }

    private void WriteHyperlink(
        BodyContext context,
        OpenXmlCompositeElement parent,
        ContainerInline children,
        string? url,
        SpanFormat format)
    {
        // OOXML has no nested hyperlink, so a link inside a link keeps only the outer relationship.
        if (format.Hyperlink
            || !TryAppendHyperlink(context, parent, url, out Hyperlink hyperlink))
        {
            WriteInlines(context, parent, children, format);
            return;
        }

        WriteInlines(context, hyperlink, children, format.WithHyperlink());
    }

    private static bool TryAppendHyperlink(
        BodyContext context,
        OpenXmlCompositeElement parent,
        string? url,
        out Hyperlink hyperlink)
    {
        string? relationshipId = context.AddHyperlink(url);
        if (relationshipId is null)
        {
            hyperlink = null!;
            return false;
        }

        hyperlink = new Hyperlink { Id = relationshipId };
        parent.Append(hyperlink);
        return true;
    }

    /// <summary>
    /// Embeds a local image, or keeps its alt text and reports <c>WRITER001</c> when the target is
    /// not a readable local file of a type whose intrinsic size can be read.
    /// </summary>
    private void WriteImage(
        BodyContext context,
        OpenXmlCompositeElement parent,
        LinkInline image,
        SpanFormat format)
    {
        DocxImages.ResolvedImage? resolved = DocxImages.Resolve(image.Url);
        if (resolved is null)
        {
            context.Report(
                $"Image '{image.Url}' is not an embeddable local file; its alt text was kept.",
                image.Line);
            WriteImageFallback(context, parent, image, format);
            return;
        }

        double widthInches = CssUnits.PixelsToInches(resolved.Width);
        double heightInches = CssUnits.PixelsToInches(resolved.Height);
        if (widthInches > _theme.ContentWidth && widthInches > 0)
        {
            double scale = _theme.ContentWidth / widthInches;
            widthInches *= scale;
            heightInches *= scale;
        }

        string relationshipId = context.AddImagePart(resolved);
        bool isSvg = string.Equals(
            resolved.ContentType,
            OoxmlNames.SvgContentType,
            StringComparison.Ordinal);

        parent.Append(new Run(DocxDrawing.BuildInlineImage(
            context.Main,
            relationshipId,
            isSvg ? relationshipId : null,
            DocxUnits.InchesToEmus(widthInches),
            DocxUnits.InchesToEmus(heightInches),
            context.NextDrawingId(),
            AltText(image))));
    }

    private void WriteImageFallback(
        BodyContext context,
        OpenXmlCompositeElement parent,
        LinkInline image,
        SpanFormat format)
    {
        if (image.FirstChild is not null)
        {
            WriteInlines(context, parent, image, format);
            return;
        }

        parent.Append(BuildRun(image.Url ?? string.Empty, format));
    }

    private static string? AltText(LinkInline image)
    {
        var text = new StringBuilder();
        foreach (Inline inline in image)
        {
            if (inline is LiteralInline literal)
            {
                text.Append(literal.Content.AsSpan());
            }
        }

        return text.Length > 0 ? text.ToString() : null;
    }

    /// <summary>
    /// Builds one run. The property order follows the WordprocessingML schema
    /// (<c>rStyle</c>, <c>b</c>, <c>i</c>, <c>strike</c>, <c>color</c>, <c>u</c>).
    /// </summary>
    private Run BuildRun(string text, SpanFormat format)
    {
        var properties = new RunProperties();
        if (format.Code)
        {
            properties.Append(new RunStyle { Val = DocxStyles.CodeCharStyleId });
        }

        if (format.Bold)
        {
            properties.Append(new Bold());
        }

        if (format.Italic)
        {
            properties.Append(new Italic());
        }

        if (format.Strikethrough)
        {
            properties.Append(new Strike());
        }

        if (format.Hyperlink)
        {
            properties.Append(new Color { Val = _theme.HyperlinkColor });
        }

        if (format.Hyperlink)
        {
            properties.Append(new Underline { Val = UnderlineValues.Single });
        }

        // Markdown is arbitrary input and may carry characters XML 1.0 forbids (a form feed inside
        // a code block, say); those must not turn a render into an exception from the XML writer.
        var run = new Run(properties);
        run.Append(new Text(OdtXml.SanitizeText(text))
        {
            // Word trims whitespace at a run boundary unless it is explicitly preserved, which
            // would silently reflow code indentation and text around inline formatting.
            Space = SpaceProcessingModeValues.Preserve,
        });

        return run;
    }

    private SectionProperties BuildSectionProperties() => new(
        new PageSize
        {
            Width = (uint)DocxUnits.InchesToTwips(_theme.PageWidth),
            Height = (uint)DocxUnits.InchesToTwips(_theme.PageHeight),
        },
        new PageMargin
        {
            Top = DocxUnits.InchesToTwips(_theme.PageMargin),
            Bottom = DocxUnits.InchesToTwips(_theme.PageMargin),
            Left = (uint)DocxUnits.InchesToTwips(_theme.PageMargin),
            Right = (uint)DocxUnits.InchesToTwips(_theme.PageMargin),
            Header = 0,
            Footer = 0,
            Gutter = 0,
        });

    private static string FootnoteMarker(FootnoteLink link) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{(link.Footnote.Order > 0 ? link.Footnote.Order : link.Index)}]");

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
            .Replace("\r\n", OoxmlNames.Newline, StringComparison.Ordinal)
            .Replace("\r", OoxmlNames.Newline, StringComparison.Ordinal)
            .Split('\n');

        // A trailing newline is a terminator, not an empty last line of the code block.
        return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    /// <summary>Writer state threaded through the block and inline visitors.</summary>
    private sealed class BodyContext(MainDocumentPart main, List<RenderDiagnostic> diagnostics)
    {
        private uint _drawingId;
        private int _relationshipNumber = FirstContentRelationshipNumber;

        internal MainDocumentPart Main { get; } = main;

        /// <summary>Start value of each ordered list, in the order the lists were visited.</summary>
        internal List<int> OrderedListStarts { get; } = [];

        /// <summary>Ids are per document and start at 1; Word rejects a zero <c>docPr</c> id.</summary>
        internal uint NextDrawingId() => ++_drawingId;

        internal int AddOrderedList(int start)
        {
            OrderedListStarts.Add(start);
            return DocxStyles.FirstOrderedNumberingId + OrderedListStarts.Count - 1;
        }

        internal string AddSvgPart(string svg)
        {
            string relationshipId = NextRelationshipId();
            ImagePart part = Main.AddImagePart(OoxmlNames.SvgContentType, relationshipId);
            part.FeedData(new MemoryStream(Encoding.UTF8.GetBytes(svg)));
            return relationshipId;
        }

        internal string AddImagePart(DocxImages.ResolvedImage image)
        {
            string relationshipId = NextRelationshipId();
            ImagePart part = Main.AddImagePart(image.ContentType, relationshipId);
            using FileStream file = File.OpenRead(image.Path);
            part.FeedData(file);
            return relationshipId;
        }

        /// <summary>
        /// Adds a hyperlink relationship, returning its id, or <see langword="null"/> when the
        /// target is not a usable URI and the link text must stand on its own.
        /// </summary>
        internal string? AddHyperlink(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? target))
            {
                return null;
            }

            return Main
                .AddHyperlinkRelationship(target, isExternal: true, NextRelationshipId())
                .Id;
        }

        private string NextRelationshipId() =>
            RelationshipIdPrefix + DocxUnits.Integer(_relationshipNumber++);

        internal void Report(string message, int line) => diagnostics.Add(new RenderDiagnostic(
            DiagnosticSeverity.Warning,
            RenderDiagnostic.WriterUnsupportedConstruct,
            message,
            line + 1));
    }

    /// <summary>The paragraph-level context of a block being written.</summary>
    /// <param name="StyleId">Paragraph style id to reference.</param>
    private sealed record ParagraphContext(string StyleId)
    {
        /// <summary>Body paragraph context: the default style, no list, no alignment.</summary>
        internal static ParagraphContext Body { get; } = new(DocxStyles.NormalStyleId);

        /// <summary>Numbering instance of the list this paragraph is an item of, if any.</summary>
        internal int? NumberingId { get; init; }

        /// <summary>Zero-based list nesting level.</summary>
        internal int ListLevel { get; init; }

        /// <summary>Explicit left indent in inches, for a list item's continuation paragraphs.</summary>
        internal double? IndentInches { get; init; }

        /// <summary>Paragraph alignment, from a table column definition.</summary>
        internal JustificationValues? Alignment { get; init; }

        /// <summary>Whether runs default to bold, as in a table header cell.</summary>
        internal bool BoldRuns { get; init; }
    }

    /// <summary>The inline formatting active at a point in the AST walk.</summary>
    private readonly record struct SpanFormat(
        bool Bold = false,
        bool Italic = false,
        bool Strikethrough = false,
        bool Code = false,
        bool Hyperlink = false)
    {
        /// <summary>Markdig's delimiter count at which emphasis means bold rather than italic.</summary>
        private const int StrongDelimiterCount = 2;

        internal SpanFormat WithCode() => this with { Code = true };

        internal SpanFormat WithHyperlink() => this with { Hyperlink = true };

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
