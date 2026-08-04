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
/// Area 7 of docs/07-testing-strategy.md for phase 3: the same sequence diagram reaches both the
/// HTML and the ODT writer through the format-agnostic diagram seam, with no writer involvement in
/// the diagram type itself.
/// </summary>
public sealed class SequenceDocumentTests
{
    private static Task<string> SampleAsync() =>
        File.ReadAllTextAsync(TestFiles.Sample("sequence-demo.md"));

    [Fact]
    public async Task The_Sample_Renders_To_Html_With_Inline_Sequence_Svg()
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            await SampleAsync(), RenderOptions.Html);

        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("mdnr-sequence", html, StringComparison.Ordinal);
        Assert.Contains("mdnr-lifeline", html, StringComparison.Ordinal);
        Assert.Contains("sequence diagram with", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        // Every diagram in the sample was rendered, not dropped to a code-block fallback.
        Assert.Equal(4, CountOccurrences(html, "<svg "));
        Assert.DoesNotContain("sequenceDiagram", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Sample_Renders_To_Odt_With_One_Svg_Picture_Part_Per_Diagram()
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            await SampleAsync(), RenderOptions.Odt);

        using var archive = new ZipArchive(
            new MemoryStream(result.Content.ToArray()), ZipArchiveMode.Read);

        List<string> pictures = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("Pictures/", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(4, pictures.Count);

        foreach (string picture in pictures)
        {
            string svg = Read(archive, picture);
            XDocument parsed = XDocument.Parse(svg);
            Assert.Equal("mdnr-sequence", parsed.Root!.Attribute("class")!.Value);
        }

        XElement body = XDocument.Parse(Read(archive, "content.xml"))
            .Root!
            .Element(Office + "body")!
            .Element(Office + "text")!;
        List<XElement> frames = body.Descendants(Draw + "frame").ToList();

        Assert.Equal(4, frames.Count);
        Assert.All(frames, frame => Assert.Contains(
            pictures,
            picture => picture == frame.Element(Draw + "image")!.Attribute(Xlink + "href")!.Value));
    }

    [Fact]
    public async Task A_Malformed_Sequence_Block_Falls_Back_To_Its_Verbatim_Source()
    {
        const string markdown = "```mermaid\nsequenceDiagram\n```\n";

        RenderResult result = await new MarkdownRenderer().RenderAsync(markdown, RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("<pre", html, StringComparison.Ordinal);
        Assert.Contains("sequenceDiagram", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.DiagramParseFailure, diagnostic.Code);
    }

    [Fact]
    public async Task Deferred_Constructs_Report_One_Info_Per_Construct_Without_Losing_Messages()
    {
        RenderResult result = await new MarkdownRenderer().RenderAsync(
            await SampleAsync(), RenderOptions.Html);

        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("loop", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Renders_Of_The_Sample_Are_Byte_Identical(bool odt)
    {
        string markdown = await SampleAsync();
        RenderOptions options = odt ? RenderOptions.Odt : RenderOptions.Html;
        var renderer = new MarkdownRenderer();

        RenderResult first = await renderer.RenderAsync(markdown, options);
        RenderResult second = await renderer.RenderAsync(markdown, options);

        Assert.Equal(first.Content.ToArray(), second.Content.ToArray());
    }

    private static string Read(ZipArchive archive, string entryName)
    {
        using Stream stream = archive.GetEntry(entryName)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
