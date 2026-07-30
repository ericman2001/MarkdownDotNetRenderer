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

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Area 8 of docs/07-testing-strategy.md: the ODT package's zip invariants, part well-formedness,
/// the Markdown → ODF structural mapping, diagram picture parts, and determinism.
/// </summary>
public sealed class OdtDocumentWriterTests
{
    private const string OdtMediaType = "application/vnd.oasis.opendocument.text";
    private const string SvgMediaType = "image/svg+xml";
    private const string XmlMediaType = "text/xml";

    private static readonly XNamespace Office =
        "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    private static readonly XNamespace Text =
        "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    private static readonly XNamespace Style =
        "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    private static readonly XNamespace Table =
        "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    private static readonly XNamespace Draw =
        "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    private static readonly XNamespace Fo =
        "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    private static readonly XNamespace Svg =
        "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    private static readonly XNamespace Manifest =
        "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    [Fact]
    public async Task Mimetype_Is_The_First_Entry_Stored_Uncompressed()
    {
        byte[] package = await RenderAsync("# Title\n\nBody.\n");

        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        ZipArchiveEntry first = archive.Entries[0];

        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);

        using var reader = new StreamReader(first.Open(), Encoding.UTF8);
        Assert.Equal(OdtMediaType, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Every_Xml_Part_Is_Present_And_Well_Formed()
    {
        var document = await OdtPackage.RenderAsync(KitchenSinkAsync);

        foreach (string part in new[] { "content.xml", "styles.xml", "meta.xml", "META-INF/manifest.xml" })
        {
            XDocument parsed = document.Xml(part);
            Assert.NotNull(parsed.Root);
        }

        Assert.Equal(Office + "document-content", document.Xml("content.xml").Root!.Name);
        Assert.Equal(Office + "document-styles", document.Xml("styles.xml").Root!.Name);
        Assert.Equal(Office + "document-meta", document.Xml("meta.xml").Root!.Name);
    }

    [Fact]
    public async Task Manifest_Lists_Exactly_The_Entries_In_The_Package()
    {
        var document = await OdtPackage.RenderAsync(KitchenSinkAsync);

        // Per ODF, mimetype and the manifest itself are not listed; "/" stands for the document.
        List<string> expected =
        [
            "/",
            .. document.EntryNames.Where(name =>
                name is not "mimetype" and not "META-INF/manifest.xml"),
        ];

        XDocument manifest = document.Xml("META-INF/manifest.xml");
        List<string> listed = manifest.Root!
            .Elements(Manifest + "file-entry")
            .Select(entry => entry.Attribute(Manifest + "full-path")!.Value)
            .ToList();

        Assert.Equal(expected, listed);

        Dictionary<string, string> mediaTypes = manifest.Root!
            .Elements(Manifest + "file-entry")
            .ToDictionary(
                entry => entry.Attribute(Manifest + "full-path")!.Value,
                entry => entry.Attribute(Manifest + "media-type")!.Value,
                StringComparer.Ordinal);

        Assert.Equal(OdtMediaType, mediaTypes["/"]);
        Assert.Equal(XmlMediaType, mediaTypes["content.xml"]);
        Assert.Equal(XmlMediaType, mediaTypes["styles.xml"]);
        Assert.Equal(XmlMediaType, mediaTypes["meta.xml"]);
        foreach (string picture in document.EntryNames.Where(name => name.StartsWith("Pictures/", StringComparison.Ordinal)))
        {
            Assert.Equal(SvgMediaType, mediaTypes[picture]);
        }
    }

    [Fact]
    public async Task Headings_Become_Text_H_With_Matching_Outline_Levels()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six\n"));

        List<XElement> headings = document.Body().Elements(Text + "h").ToList();
        Assert.Equal(6, headings.Count);

        for (int level = 1; level <= 6; level++)
        {
            XElement heading = headings[level - 1];
            Assert.Equal(
                level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                heading.Attribute(Text + "outline-level")!.Value);
            Assert.Equal(
                $"Heading_20_{level}",
                heading.Attribute(Text + "style-name")!.Value);
        }
    }

    [Fact]
    public async Task Nested_Lists_Become_Nested_Text_List_Elements()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "1. First\n2. Second\n   - Inner\n   - Also inner\n3. Third\n"));

        XElement outer = Assert.Single(document.Body().Elements(Text + "list"));
        Assert.Equal("Numbered_20_List", outer.Attribute(Text + "style-name")!.Value);

        List<XElement> items = outer.Elements(Text + "list-item").ToList();
        Assert.Equal(3, items.Count);

        XElement inner = Assert.Single(items[1].Elements(Text + "list"));
        Assert.Equal("Bullet_20_List", inner.Attribute(Text + "style-name")!.Value);
        Assert.Equal(2, inner.Elements(Text + "list-item").Count());
    }

    [Fact]
    public async Task Task_List_Items_Carry_A_Checkbox_Marker()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "- [x] Done\n- [ ] Pending\n"));

        List<string> texts = document.Body()
            .Descendants(Text + "list-item")
            .Select(item => item.Value)
            .ToList();

        Assert.Equal(["\u2612 Done", "\u2610 Pending"], texts);
    }

    [Fact]
    public async Task Gfm_Table_Becomes_A_Table_With_A_Header_Row_And_Matching_Cell_Counts()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "| A | B | C |\n|---|:-:|--:|\n| 1 | 2 | 3 |\n| 4 | 5 | 6 |\n"));

        XElement table = Assert.Single(document.Body().Elements(Table + "table"));
        XElement column = Assert.Single(table.Elements(Table + "table-column"));
        Assert.Equal("3", column.Attribute(Table + "number-columns-repeated")!.Value);

        XElement headerRows = Assert.Single(table.Elements(Table + "table-header-rows"));
        XElement headerRow = Assert.Single(headerRows.Elements(Table + "table-row"));
        Assert.Equal(3, headerRow.Elements(Table + "table-cell").Count());

        List<XElement> bodyRows = table.Elements(Table + "table-row").ToList();
        Assert.Equal(2, bodyRows.Count);
        Assert.All(bodyRows, row => Assert.Equal(3, row.Elements(Table + "table-cell").Count()));

        // The GFM alignment row drives fo:text-align on the cell paragraph styles.
        string centeredStyle = bodyRows[0]
            .Elements(Table + "table-cell")
            .ElementAt(1)
            .Element(Text + "p")!
            .Attribute(Text + "style-name")!
            .Value;
        Assert.Equal("center", document.ParagraphProperty(centeredStyle, Fo + "text-align"));

        string rightStyle = bodyRows[0]
            .Elements(Table + "table-cell")
            .ElementAt(2)
            .Element(Text + "p")!
            .Attribute(Text + "style-name")!
            .Value;
        Assert.Equal("end", document.ParagraphProperty(rightStyle, Fo + "text-align"));
    }

    [Fact]
    public async Task Inline_Formatting_Becomes_Spans_With_The_Expected_Text_Properties()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "**bold** *italic* ~~struck~~ `code`\n"));

        XElement paragraph = Assert.Single(document.Body().Elements(Text + "p"));
        List<XElement> spans = paragraph.Elements(Text + "span").ToList();
        Assert.Equal(4, spans.Count);

        Assert.Equal("bold", document.TextProperty(StyleOf(spans[0]), Fo + "font-weight"));
        Assert.Equal("italic", document.TextProperty(StyleOf(spans[1]), Fo + "font-style"));
        Assert.Equal(
            "solid",
            document.TextProperty(StyleOf(spans[2]), Style + "text-line-through-style"));
        Assert.Equal("MonoFont", document.TextProperty(StyleOf(spans[3]), Style + "font-name"));
    }

    [Fact]
    public async Task Identical_Spans_Share_One_Automatic_Style()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "**one** and **two** and **three**\n"));

        List<string> styleNames = document.Body()
            .Descendants(Text + "span")
            .Select(StyleOf)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Single(styleNames);
    }

    [Fact]
    public async Task Code_Blocks_Use_Preformatted_Text_And_Preserve_Indentation()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "```csharp\nif (x)\n    return 1;\n```\n"));

        List<XElement> paragraphs = document.Body().Elements(Text + "p").ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.All(
            paragraphs,
            paragraph => Assert.Equal(
                "Preformatted_20_Text",
                paragraph.Attribute(Text + "style-name")!.Value));

        XElement spaces = Assert.Single(paragraphs[1].Elements(Text + "s"));
        Assert.Equal("4", spaces.Attribute(Text + "c")!.Value);
        Assert.Equal("return 1;", paragraphs[1].Value);
    }

    [Fact]
    public async Task Block_Quotes_And_Thematic_Breaks_Use_Their_Named_Styles()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "> Quoted line\n\n---\n"));

        List<XElement> paragraphs = document.Body().Elements(Text + "p").ToList();
        Assert.Equal("Quotations", paragraphs[0].Attribute(Text + "style-name")!.Value);
        Assert.Equal("Quoted line", paragraphs[0].Value);
        Assert.Equal("Horizontal_20_Line", paragraphs[1].Attribute(Text + "style-name")!.Value);
        Assert.Equal(string.Empty, paragraphs[1].Value);
    }

    [Fact]
    public async Task Links_Become_Text_A_With_An_Xlink_Href()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "See [the docs](https://example.com/docs).\n"));

        XElement anchor = Assert.Single(document.Body().Descendants(Text + "a"));
        Assert.Equal("https://example.com/docs", anchor.Attribute(Xlink + "href")!.Value);
        Assert.Equal("the docs", anchor.Value);
    }

    [Fact]
    public async Task Diagram_Frames_Reference_A_Picture_Part_Holding_The_Fragment()
    {
        var document = await OdtPackage.RenderAsync(KitchenSinkAsync);

        List<XElement> frames = document.Body().Descendants(Draw + "frame").ToList();
        Assert.NotEmpty(frames);

        foreach (XElement frame in frames)
        {
            XElement image = Assert.Single(frame.Elements(Draw + "image"));
            string href = image.Attribute(Xlink + "href")!.Value;

            Assert.StartsWith("Pictures/", href, StringComparison.Ordinal);
            Assert.EndsWith(".svg", href, StringComparison.Ordinal);
            Assert.Contains(href, document.EntryNames);
            Assert.Equal(SvgMediaType, image.Attribute(Draw + "mime-type")!.Value);

            string svg = document.Text(href);
            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg, StringComparison.Ordinal);
            Assert.Contains("<svg ", svg, StringComparison.Ordinal);
            XDocument.Parse(svg);

            // Physical units, not pixels: ODF sizes are inches here (px / 96).
            Assert.EndsWith("in", frame.Attribute(Svg + "width")!.Value, StringComparison.Ordinal);
            Assert.EndsWith("in", frame.Attribute(Svg + "height")!.Value, StringComparison.Ordinal);

            // Alt text travels into the accessibility elements.
            Assert.NotEmpty(frame.Element(Svg + "title")!.Value);
            Assert.NotEmpty(frame.Element(Svg + "desc")!.Value);
        }
    }

    [Fact]
    public async Task Diagram_Frame_Size_Matches_The_Fragment_Scaled_To_The_Text_Column()
    {
        var document = await OdtPackage.RenderAsync(() => Task.FromResult(
            "```mermaid\nflowchart LR\n    A --> B\n```\n"));

        XElement frame = Assert.Single(document.Body().Descendants(Draw + "frame"));
        string href = frame.Element(Draw + "image")!.Attribute(Xlink + "href")!.Value;
        XDocument svg = XDocument.Parse(document.Text(href));

        double pixels = double.Parse(
            svg.Root!.Attribute("width")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        double inches = double.Parse(
            frame.Attribute(Svg + "width")!.Value.Replace("in", string.Empty, StringComparison.Ordinal),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(pixels / 96, inches, precision: 3);
    }

    [Fact]
    public async Task Unsupported_Diagrams_Fall_Back_To_Verbatim_Preformatted_Source()
    {
        const string source = "sequenceDiagram\n    Alice->>Bob: Later\n";
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            $"```mermaid\n{source}```\n",
            RenderOptions.Odt);

        var document = OdtPackage.Open(result.Content.ToArray());
        List<XElement> paragraphs = document.Body()
            .Elements(Text + "p")
            .Where(paragraph =>
                paragraph.Attribute(Text + "style-name")!.Value == "Preformatted_20_Text")
            .ToList();

        Assert.Equal(2, paragraphs.Count);
        Assert.Equal("sequenceDiagram", paragraphs[0].Value);
        Assert.Equal("Alice->>Bob: Later", paragraphs[1].Value);
        Assert.Empty(document.Body().Descendants(Draw + "frame"));

        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.UnsupportedDiagramType, diagnostic.Code);
    }

    [Fact]
    public async Task Malformed_Diagrams_Do_Not_Throw_And_Keep_Their_Source()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "```mermaid\nflowchart TD\n    A[Unterminated --> B\n    -->\n```\n",
            RenderOptions.Odt);

        var document = OdtPackage.Open(result.Content.ToArray());
        List<string> lines = document.Body()
            .Elements(Text + "p")
            .Where(paragraph =>
                paragraph.Attribute(Text + "style-name")!.Value == "Preformatted_20_Text")
            .Select(paragraph => paragraph.Value)
            .ToList();

        Assert.Equal(["flowchart TD", "A[Unterminated --> B", "-->"], lines);
        Assert.Equal(
            RenderDiagnostic.DiagramParseFailure,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Meta_Takes_Its_Title_From_Options_Then_From_The_First_Heading()
    {
        var renderer = new MarkdownRenderer();

        RenderResult fromHeading = await renderer.RenderAsync("# From heading\n", RenderOptions.Odt);
        Assert.Equal("From heading", TitleOf(fromHeading));

        RenderResult fromOptions = await renderer.RenderAsync(
            "# From heading\n",
            new RenderOptions { Format = OutputFormat.Odt, DocumentTitle = "From options" });
        Assert.Equal("From options", TitleOf(fromOptions));

        RenderResult fallback = await renderer.RenderAsync("Just prose.\n", RenderOptions.Odt);
        Assert.Equal("Document", TitleOf(fallback));

        static string TitleOf(RenderResult result) => OdtPackage
            .Open(result.Content.ToArray())
            .Xml("meta.xml")
            .Descendants(XNamespace.Get("http://purl.org/dc/elements/1.1/") + "title")
            .Single()
            .Value;
    }

    [Fact]
    public async Task Two_Renders_Of_The_Same_Input_Are_Byte_Identical()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));
        var renderer = new MarkdownRenderer();

        RenderResult first = await renderer.RenderAsync(markdown, RenderOptions.Odt);
        RenderResult second = await renderer.RenderAsync(markdown, RenderOptions.Odt);

        Assert.Equal(first.Content.ToArray(), second.Content.ToArray());
    }

    [Fact]
    public async Task Render_Produces_A_Zip_With_The_Expected_Magic_Bytes()
    {
        byte[] package = await RenderAsync("# Doc\n");

        Assert.Equal((byte)'P', package[0]);
        Assert.Equal((byte)'K', package[1]);
        Assert.Equal(0x03, package[2]);
        Assert.Equal(0x04, package[3]);
    }

    private static string StyleOf(XElement span) => span.Attribute(Text + "style-name")!.Value;

    private static Task<string> KitchenSinkAsync() =>
        File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));

    private static async Task<byte[]> RenderAsync(string markdown)
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, RenderOptions.Odt);
        return result.Content.ToArray();
    }

    /// <summary>Reads an ODT package's entries for structural assertions.</summary>
    private sealed class OdtPackage
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        private readonly List<string> _entryNames = [];

        private OdtPackage(byte[] package)
        {
            using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                using var buffer = new MemoryStream();
                using (Stream stream = entry.Open())
                {
                    stream.CopyTo(buffer);
                }

                _entryNames.Add(entry.FullName);
                _entries[entry.FullName] = buffer.ToArray();
            }
        }

        internal IReadOnlyList<string> EntryNames => _entryNames;

        internal static OdtPackage Open(byte[] package) => new(package);

        internal static async Task<OdtPackage> RenderAsync(Func<Task<string>> markdown) =>
            Open(await OdtDocumentWriterTests.RenderAsync(await markdown()));

        internal string Text(string entryName) => Encoding.UTF8.GetString(_entries[entryName]);

        internal XDocument Xml(string entryName) => XDocument.Parse(Text(entryName));

        internal XElement Body() => Xml("content.xml")
            .Root!
            .Element(Office + "body")!
            .Element(Office + "text")!;

        internal string? TextProperty(string styleName, XName property) =>
            AutomaticStyleProperty(styleName, Style + "text-properties", property);

        internal string? ParagraphProperty(string styleName, XName property) =>
            AutomaticStyleProperty(styleName, Style + "paragraph-properties", property);

        private string? AutomaticStyleProperty(
            string styleName,
            XName propertyElement,
            XName property) =>
            Xml("content.xml")
                .Root!
                .Element(Office + "automatic-styles")!
                .Elements(Style + "style")
                .Single(style => style.Attribute(Style + "name")!.Value == styleName)
                .Element(propertyElement)
                ?.Attribute(property)
                ?.Value;
    }
}
