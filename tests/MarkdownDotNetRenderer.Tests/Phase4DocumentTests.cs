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
/// Area 7 of docs/07-testing-strategy.md for phase 4: one sample holding every newly supported
/// diagram type reaches both writers through the format-agnostic diagram seam, which is the
/// design-validation point that adding diagram types needs no writer change.
/// </summary>
public sealed class Phase4DocumentTests
{
    private static readonly XNamespace Draw =
        "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    private static readonly XNamespace Office =
        "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    private const int DiagramCount = 5;

    private static Task<string> SampleAsync() =>
        File.ReadAllTextAsync(TestFiles.Sample("diagram-gallery.md"));

    [Fact]
    public async Task The_Gallery_Renders_To_Html_With_One_Inline_Svg_Per_Diagram()
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            await SampleAsync(), RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(DiagramCount, html.Split("<figure class=\"mermaid-figure\">").Length - 1);
        foreach (string cssClass in
            new[] { "mdnr-pie", "mdnr-state", "mdnr-class", "mdnr-er", "mdnr-gantt" })
        {
            Assert.Contains($"class=\"{cssClass}\"", html, StringComparison.Ordinal);
        }

        // Verbatim mermaid source only survives when a diagram fell back to a code block.
        Assert.DoesNotContain("<pre", html, StringComparison.Ordinal);
        foreach (string keyword in
            new[] { "pie showData", "stateDiagram-v2", "classDiagram", "erDiagram", "dateFormat" })
        {
            Assert.DoesNotContain(keyword, html, StringComparison.Ordinal);
        }

        SelfContainment.Assert(html);
    }

    [Fact]
    public async Task The_Gallery_Renders_To_Odt_With_One_Svg_Picture_Part_Per_Diagram()
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            await SampleAsync(), RenderOptions.Odt);

        using var archive = new ZipArchive(
            new MemoryStream(result.Content.ToArray()), ZipArchiveMode.Read);

        List<string> pictures = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("Pictures/", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(DiagramCount, pictures.Count);

        var classes = new List<string>();
        foreach (string picture in pictures)
        {
            using var reader = new StreamReader(archive.GetEntry(picture)!.Open());
            XDocument parsed = XDocument.Parse(await reader.ReadToEndAsync());
            classes.Add(parsed.Root!.Attribute("class")!.Value);
        }

        Assert.Equal(
            ["mdnr-pie", "mdnr-state", "mdnr-class", "mdnr-er", "mdnr-gantt"],
            classes);

        using var contentReader = new StreamReader(archive.GetEntry("content.xml")!.Open());
        XElement body = XDocument.Parse(await contentReader.ReadToEndAsync())
            .Root!
            .Element(Office + "body")!
            .Element(Office + "text")!;

        List<XElement> frames = body.Descendants(Draw + "frame").ToList();
        Assert.Equal(DiagramCount, frames.Count);
        Assert.All(frames, frame => Assert.Contains(
            pictures,
            picture => picture == frame.Element(Draw + "image")!.Attribute(Xlink + "href")!.Value));
    }

    [Fact]
    public async Task The_Gallery_Renders_Byte_Identically_Every_Time()
    {
        string markdown = await SampleAsync();
        var renderer = new MarkdownRenderer();

        RenderResult first = await renderer.RenderAsync(markdown, RenderOptions.Html);
        RenderResult second = await renderer.RenderAsync(markdown, RenderOptions.Html);

        Assert.Equal(first.Content.ToArray(), second.Content.ToArray());
    }

    [Theory]
    [InlineData("gitGraph")]
    [InlineData("journey")]
    [InlineData("mindmap")]
    [InlineData("quadrantChart")]
    [InlineData("xychart-beta")]
    [InlineData("timeline")]
    [InlineData("sankey-beta")]
    [InlineData("C4Context")]
    public async Task Diagram_Types_Deferred_Past_This_Phase_Still_Fall_Back_With_Mermaid001(
        string keyword)
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            $"```mermaid\n{keyword}\n    whatever\n```\n", RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("<pre", html, StringComparison.Ordinal);
        Assert.Contains(keyword, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.UnsupportedDiagramType, diagnostic.Code);
    }

    [Theory]
    [InlineData("pie\n")]
    [InlineData("stateDiagram\n")]
    [InlineData("classDiagram\n")]
    [InlineData("erDiagram\n")]
    [InlineData("gantt\n")]
    public async Task A_Malformed_Diagram_Of_Any_New_Type_Falls_Back_To_Its_Verbatim_Source(
        string source)
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            $"```mermaid\n{source}```\n", RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("<pre", html, StringComparison.Ordinal);
        Assert.Contains(source.Trim(), html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.DiagramParseFailure, diagnostic.Code);
    }
}
