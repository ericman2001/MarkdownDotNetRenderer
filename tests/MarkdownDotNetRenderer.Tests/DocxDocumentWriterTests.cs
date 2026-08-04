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

using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Area 9 of docs/07-testing-strategy.md: the DOCX package opens, validates against the OOXML
/// schema, maps Markdown to the documented WordprocessingML structures, embeds diagrams as SVG
/// images Word 2016+ can display, and renders deterministically.
/// </summary>
public sealed class DocxDocumentWriterTests
{
    private const string SvgMediaType = "image/svg+xml";
    private const string SvgExtensionUri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";
    private const string SvgNamespace = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
    private const long EmusPerPixel = 9525;

    [Fact]
    public void Docx_Extension_And_Content_Type_Come_From_The_Writer()
    {
        Assert.Equal(".docx", MarkdownRenderer.GetFileExtension(OutputFormat.Docx));
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            MarkdownRenderer.CreateWriter(OutputFormat.Docx).ContentType);
    }

    [Theory]
    [InlineData("kitchen-sink.md")]
    [InlineData("flowchart-demo.md")]
    [InlineData("sequence-demo.md")]
    [InlineData("diagram-gallery.md")]
    public async Task Every_Sample_Opens_And_Reports_No_Validation_Errors(string sample)
    {
        byte[] package = await RenderAsync(
            await File.ReadAllTextAsync(TestFiles.Sample(sample)));

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);

        Assert.NotNull(document.MainDocumentPart?.Document?.Body);

        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        List<ValidationErrorInfo> errors = validator.Validate(document).ToList();

        Assert.Empty(errors.Select(error => $"{error.Path?.XPath}: {error.Description}"));
    }

    [Fact]
    public async Task Headings_Use_The_Heading_Style_Ids()
    {
        Body body = await RenderBodyAsync(
            "# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six\n");

        List<string?> styles = body.Elements<Paragraph>()
            .Select(paragraph => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
            .Where(style => style is not null and not "Normal")
            .ToList();

        Assert.Equal(
            ["Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6"],
            styles);
    }

    [Fact]
    public async Task Headings_Are_Declared_As_Styles_Derived_From_The_Options_Font()
    {
        byte[] package = await RenderAsync(
            "# Title\n",
            new RenderOptions { Format = OutputFormat.Docx, FontFamily = "Georgia, serif" });

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        Styles styles = document.MainDocumentPart!.StyleDefinitionsPart!.Styles!;

        List<string> ids = styles.Elements<Style>()
            .Select(style => style.StyleId!.Value!)
            .ToList();

        Assert.Contains("Normal", ids);
        Assert.Contains("Quote", ids);
        Assert.Contains("CodeChar", ids);
        for (int level = 1; level <= 6; level++)
        {
            Assert.Contains($"Heading{level}", ids);
        }

        // Only the first family of the CSS stack fits w:rFonts, which names a single font.
        RunFonts fonts = styles.DocDefaults!.RunPropertiesDefault!
            .RunPropertiesBaseStyle!.RunFonts!;
        Assert.Equal("Georgia", fonts.Ascii?.Value);
    }

    [Fact]
    public async Task Inline_Formatting_Sets_The_Matching_Run_Properties()
    {
        Body body = await RenderBodyAsync(
            "Plain **bold** *italic* ~~struck~~ `code`.\n");

        List<Run> runs = body.Descendants<Run>().ToList();

        Assert.Contains(runs, run => run.RunProperties?.Bold is not null && Text(run) == "bold");
        Assert.Contains(runs, run => run.RunProperties?.Italic is not null && Text(run) == "italic");
        Assert.Contains(runs, run => run.RunProperties?.Strike is not null && Text(run) == "struck");
        Assert.Contains(
            runs,
            run => run.RunProperties?.RunStyle?.Val == "CodeChar" && Text(run) == "code");
        Assert.Contains(
            runs,
            run => Text(run) == "Plain "
                && run.RunProperties?.Bold is null
                && run.RunProperties?.Italic is null);
    }

    [Fact]
    public async Task Links_Become_Hyperlinks_With_A_Resolvable_Relationship()
    {
        byte[] package = await RenderAsync("See [the docs](https://example.com/docs).\n");

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        MainDocumentPart main = document.MainDocumentPart!;

        Hyperlink hyperlink = Assert.Single(main.Document!.Body!.Descendants<Hyperlink>());
        HyperlinkRelationship relationship = Assert.Single(main.HyperlinkRelationships);

        Assert.Equal(relationship.Id, hyperlink.Id?.Value);
        Assert.Equal("https://example.com/docs", relationship.Uri.ToString());
        Assert.True(relationship.IsExternal);
        Assert.Equal("the docs", string.Concat(hyperlink.Descendants<Text>().Select(t => t.Text)));
    }

    [Fact]
    public async Task Nested_Lists_Carry_Numbering_Properties_With_The_Nesting_Depth()
    {
        Body body = await RenderBodyAsync(
            "1. First\n2. Second\n   - Inner\n   - Also inner\n3. Third\n\n- Separate bullet\n");

        List<(int Level, int NumberingId, string Text)> items = body.Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .Select(paragraph =>
            {
                NumberingProperties numbering = paragraph.ParagraphProperties!.NumberingProperties!;
                return (
                    numbering.NumberingLevelReference!.Val!.Value,
                    numbering.NumberingId!.Val!.Value,
                    string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)));
            })
            .ToList();

        Assert.Equal(
            ["First", "Second", "Inner", "Also inner", "Third", "Separate bullet"],
            items.Select(item => item.Text));
        Assert.Equal([0, 0, 1, 1, 0, 0], items.Select(item => item.Level));

        // The ordered list has its own numbering instance; bullets share the first one.
        Assert.Equal(1, items[^1].NumberingId);
        Assert.Equal(items[0].NumberingId, items[4].NumberingId);
        Assert.NotEqual(items[0].NumberingId, items[2].NumberingId);
    }

    [Fact]
    public async Task Numbering_Declares_Bullet_And_Decimal_Levels_Zero_To_Four()
    {
        byte[] package = await RenderAsync("5. Five\n6. Six\n");

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        Numbering numbering = document.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;

        List<AbstractNum> abstractNumbering = numbering.Elements<AbstractNum>().ToList();
        Assert.Equal(2, abstractNumbering.Count);
        foreach (AbstractNum definition in abstractNumbering)
        {
            Assert.Equal(
                [0, 1, 2, 3, 4],
                definition.Elements<Level>().Select(level => level.LevelIndex!.Value));
        }

        Assert.Contains(
            abstractNumbering,
            definition => definition.Elements<Level>()
                .All(level => level.NumberingFormat!.Val! == NumberFormatValues.Bullet));

        // An ordered list starting at 5 needs a start override, or Word renumbers it from 1.
        NumberingInstance ordered = numbering.Elements<NumberingInstance>()
            .Single(instance => instance.NumberID!.Value != 1);
        LevelOverride first = ordered.Elements<LevelOverride>()
            .Single(level => level.LevelIndex!.Value == 0);
        Assert.Equal(5, first.StartOverrideNumberingValue!.Val!.Value);
    }

    [Fact]
    public async Task Task_List_Items_Are_Prefixed_With_A_Checkbox_Glyph()
    {
        Body body = await RenderBodyAsync("- [x] Done\n- [ ] Pending\n");

        List<string> texts = body.Elements<Paragraph>()
            .Select(paragraph => string.Concat(
                paragraph.Descendants<Text>().Select(text => text.Text)))
            .ToList();

        Assert.Equal(["\u2612 Done", "\u2610 Pending"], texts);
    }

    [Fact]
    public async Task Tables_Have_A_Header_Row_Bold_Headers_And_Column_Alignment()
    {
        Body body = await RenderBodyAsync(
            "| Left | Middle | Right |\n|:-----|:------:|------:|\n| a | b | c |\n| d | e | f |\n");

        Table table = Assert.Single(body.Elements<Table>());
        List<TableRow> rows = table.Elements<TableRow>().ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(3, row.Elements<TableCell>().Count()));
        Assert.Equal(3, table.Elements<TableGrid>().Single().Elements<GridColumn>().Count());

        Assert.NotNull(rows[0].TableRowProperties?.GetFirstChild<TableHeader>());
        Assert.Null(rows[1].TableRowProperties?.GetFirstChild<TableHeader>());
        Assert.All(
            rows[0].Descendants<Run>(),
            run => Assert.NotNull(run.RunProperties?.Bold));

        List<JustificationValues?> alignments = rows[1].Elements<TableCell>()
            .Select(cell => cell.Elements<Paragraph>().Single()
                .ParagraphProperties?.Justification?.Val?.Value)
            .ToList();
        Assert.Equal(
            [JustificationValues.Left, JustificationValues.Center, JustificationValues.Right],
            alignments);

        Assert.NotNull(table.GetFirstChild<TableProperties>()?.TableBorders);
        Assert.Equal(
            TableLayoutValues.Autofit,
            table.GetFirstChild<TableProperties>()!.TableLayout!.Type!.Value);
    }

    [Fact]
    public async Task Code_Blocks_Become_A_Shaded_Single_Cell_Table_With_A_Paragraph_Per_Line()
    {
        Body body = await RenderBodyAsync("```csharp\nvar x = 1;\n    indented();\n```\n");

        Table table = Assert.Single(body.Elements<Table>());
        TableCell cell = Assert.Single(Assert.Single(table.Elements<TableRow>()).Elements<TableCell>());

        Assert.Equal(
            "F5F5F5",
            cell.TableCellProperties!.Shading!.Fill!.Value);

        List<Paragraph> lines = cell.Elements<Paragraph>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("var x = 1;", Text(lines[0].Descendants<Run>().Single()));

        Run indented = lines[1].Descendants<Run>().Single();
        Assert.Equal("    indented();", Text(indented));
        Assert.Equal("CodeChar", indented.RunProperties?.RunStyle?.Val?.Value);
        Assert.Equal(
            SpaceProcessingModeValues.Preserve,
            indented.GetFirstChild<Text>()!.Space!.Value);
    }

    [Fact]
    public async Task Block_Quotes_Use_The_Quote_Style_And_Thematic_Breaks_A_Bottom_Border()
    {
        Body body = await RenderBodyAsync("> Quoted line.\n\n---\n");

        List<Paragraph> paragraphs = body.Elements<Paragraph>().ToList();

        Assert.Equal("Quote", paragraphs[0].ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        Assert.NotNull(
            paragraphs[1].ParagraphProperties?.ParagraphBorders?.BottomBorder);
    }

    [Fact]
    public async Task Diagrams_Embed_An_Svg_Image_Part_Referenced_By_An_Inline_Drawing()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("flowchart-demo.md"));
        byte[] package = await RenderAsync(markdown);

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        MainDocumentPart main = document.MainDocumentPart!;

        Assert.All(main.ImageParts, part => Assert.Equal(SvgMediaType, part.ContentType));
        ImagePart image = main.ImageParts.First();

        string svg = ReadPart(image);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg, StringComparison.Ordinal);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);

        WP.Inline inline = main.Document!.Body!.Descendants<WP.Inline>().First();
        A.Blip blip = Assert.Single(inline.Descendants<A.Blip>());
        Assert.Equal(main.GetIdOfPart(image), blip.Embed?.Value);

        Assert.Equal(main.ImageParts.Count(), main.Document.Body.Descendants<Drawing>().Count());

        // The extent is the diagram's CSS-pixel size scaled to the text column, in EMU.
        Assert.True(inline.Extent!.Cx!.Value > 0);
        Assert.True(inline.Extent!.Cy!.Value > 0);
        A.Extents extents = Assert.Single(inline.Descendants<A.Extents>());
        Assert.Equal(inline.Extent.Cx.Value, extents.Cx!.Value);
        Assert.Equal(inline.Extent.Cy.Value, extents.Cy!.Value);

        Assert.Equal(1u, inline.DocProperties!.Id!.Value);
        Assert.False(string.IsNullOrWhiteSpace(inline.DocProperties.Description?.Value));

        A.BlipExtension extension = Assert.Single(inline.Descendants<A.BlipExtension>());
        Assert.Equal(SvgExtensionUri, extension.Uri?.Value);

        OpenXmlElement svgBlip = Assert.Single(extension.ChildElements);
        Assert.Equal("svgBlip", svgBlip.LocalName);
        Assert.Equal(SvgNamespace, svgBlip.NamespaceUri);
        Assert.Equal(
            main.GetIdOfPart(image),
            svgBlip.GetAttributes().Single(attribute => attribute.LocalName == "embed").Value);
    }

    [Fact]
    public async Task Diagram_Extents_Are_The_Pixel_Size_In_Emu_When_It_Fits_The_Text_Column()
    {
        // A two-node flowchart is narrower than the 6.5 inch text column, so it is not scaled.
        byte[] package = await RenderAsync("```mermaid\nflowchart LR\n    A --> B\n```\n");

        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        MainDocumentPart main = document.MainDocumentPart!;

        var renderer = new MarkdownRenderer();
        RenderResult html = await renderer.RenderAsync(
            "```mermaid\nflowchart LR\n    A --> B\n```\n",
            RenderOptions.Html);
        Assert.Empty(html.Diagnostics);

        WP.Extent extent = Assert.Single(main.Document!.Body!.Descendants<WP.Extent>());
        Assert.Equal(0, extent.Cx!.Value % EmusPerPixel);
        Assert.Equal(0, extent.Cy!.Value % EmusPerPixel);
    }

    [Fact]
    public async Task Unsupported_Diagrams_Keep_Their_Verbatim_Source_As_Code()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "```mermaid\nmindmap\n    root((Later))\n```\n",
            RenderOptions.Docx);

        Body body = BodyOf(result.Content.ToArray());
        Table table = Assert.Single(body.Elements<Table>());

        List<string> lines = table.Descendants<Paragraph>()
            .Select(paragraph => string.Concat(
                paragraph.Descendants<Text>().Select(text => text.Text)))
            .ToList();

        Assert.Equal(["mindmap", "    root((Later))"], lines);
        Assert.Empty(body.Descendants<Drawing>());
        Assert.Equal(
            RenderDiagnostic.UnsupportedDiagramType,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Malformed_Diagrams_Do_Not_Throw_And_Keep_Their_Source()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "```mermaid\nflowchart TD\n    A[Unterminated --> B\n    -->\n```\n",
            RenderOptions.Docx);

        Body body = BodyOf(result.Content.ToArray());
        List<string> lines = body.Descendants<Table>().Single().Descendants<Paragraph>()
            .Select(paragraph => string.Concat(
                paragraph.Descendants<Text>().Select(text => text.Text)))
            .ToList();

        Assert.Equal(["flowchart TD", "    A[Unterminated --> B", "    -->"], lines);
        Assert.Equal(
            RenderDiagnostic.DiagramParseFailure,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Raw_Html_Is_Kept_As_Plain_Text_And_Reports_Writer001()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "<div class=\"note\">Kept</div>\n\nProse with <b>inline</b> markup.\n",
            RenderOptions.Docx);

        Body body = BodyOf(result.Content.ToArray());
        string text = string.Concat(body.Descendants<Text>().Select(t => t.Text));

        Assert.Contains("<div class=\"note\">Kept</div>", text, StringComparison.Ordinal);
        Assert.Contains("<b>", text, StringComparison.Ordinal);
        Assert.All(
            result.Diagnostics,
            diagnostic => Assert.Equal(
                RenderDiagnostic.WriterUnsupportedConstruct,
                diagnostic.Code));
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task An_Unresolvable_Image_Keeps_Its_Alt_Text_And_Reports_Writer001()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "![A missing diagram](https://example.com/missing.png)\n",
            RenderOptions.Docx);

        Body body = BodyOf(result.Content.ToArray());

        Assert.Empty(body.Descendants<Drawing>());
        Assert.Equal(
            "A missing diagram",
            string.Concat(body.Descendants<Text>().Select(text => text.Text)));

        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.WriterUnsupportedConstruct, diagnostic.Code);
        Assert.Equal(1, diagnostic.SourceLine);
    }

    [Fact]
    public async Task A_Local_Image_Is_Embedded_At_Its_Intrinsic_Size()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"mdrender-{Guid.NewGuid():N}.svg");
        await File.WriteAllTextAsync(
            path,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"60\"></svg>");

        try
        {
            byte[] package = await RenderAsync($"![Local]({path})\n");

            using var stream = new MemoryStream(package);
            using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
            MainDocumentPart main = document.MainDocumentPart!;

            ImagePart image = Assert.Single(main.ImageParts);
            Assert.Equal(SvgMediaType, image.ContentType);

            WP.Inline inline = Assert.Single(main.Document!.Body!.Descendants<WP.Inline>());
            Assert.Equal(120 * EmusPerPixel, inline.Extent!.Cx!.Value);
            Assert.Equal(60 * EmusPerPixel, inline.Extent!.Cy!.Value);
            Assert.Equal("Local", inline.DocProperties!.Description?.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Two_Renders_Of_The_Same_Input_Produce_An_Identical_Document_Part()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));

        byte[] first = await RenderAsync(markdown);
        byte[] second = await RenderAsync(markdown);

        Assert.Equal(DocumentPartBytes(first), DocumentPartBytes(second));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task The_Section_Ends_The_Body_With_Letter_Page_Size_And_One_Inch_Margins()
    {
        Body body = await RenderBodyAsync("Prose.\n");

        SectionProperties section = Assert.IsType<SectionProperties>(body.LastChild);
        Assert.Equal(12240u, section.GetFirstChild<PageSize>()!.Width!.Value);
        Assert.Equal(15840u, section.GetFirstChild<PageSize>()!.Height!.Value);

        PageMargin margin = section.GetFirstChild<PageMargin>()!;
        Assert.Equal(1440, margin.Top!.Value);
        Assert.Equal(1440u, margin.Left!.Value);
    }

    private static string Text(Run run) =>
        string.Concat(run.Descendants<Text>().Select(text => text.Text));

    private static string ReadPart(OpenXmlPart part)
    {
        using var reader = new StreamReader(part.GetStream(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] DocumentPartBytes(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);
        using var buffer = new MemoryStream();
        using (Stream part = document.MainDocumentPart!.GetStream())
        {
            part.CopyTo(buffer);
        }

        return buffer.ToArray();
    }

    private static Body BodyOf(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, false);

        // The body is detached so it outlives the package the assertions no longer need.
        return (Body)document.MainDocumentPart!.Document!.Body!.CloneNode(deep: true);
    }

    private static async Task<Body> RenderBodyAsync(string markdown) =>
        BodyOf(await RenderAsync(markdown));

    private static Task<byte[]> RenderAsync(string markdown) =>
        RenderAsync(markdown, RenderOptions.Docx);

    private static async Task<byte[]> RenderAsync(string markdown, RenderOptions options)
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, options);
        return result.Content.ToArray();
    }
}
