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

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 1 and area 6 of docs/07-testing-strategy.md: Markdown to HTML conversion.</summary>
public sealed class MarkdownConversionTests
{
    private static async Task<string> RenderAsync(string markdown, RenderOptions? options = null)
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, options ?? RenderOptions.Html);
        return Encoding.UTF8.GetString(result.Content.Span);
    }

    [Theory]
    [InlineData("# Title", "<h1 id=\"title\">Title</h1>")]
    [InlineData("Some **bold** text.", "<strong>bold</strong>")]
    [InlineData("~~gone~~", "<del>gone</del>")]
    [InlineData("- [x] done", "type=\"checkbox\"")]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |", "<table>")]
    [InlineData("> quoted", "<blockquote>")]
    [InlineData("`code`", "<code>code</code>")]
    [InlineData("```text\nplain\n```", "<pre><code class=\"language-text\">plain")]
    public async Task Gfm_Constructs_Render_Through_Markdig(string markdown, string expected)
    {
        string html = await RenderAsync(markdown);
        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_Is_A_Complete_Html5_Document()
    {
        string html = await RenderAsync("# Hello\n\nWorld.\n");

        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>\n", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_Is_Utf8_Without_A_Bom_And_Uses_Lf_Endings()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync("# Héllo ✓\n", RenderOptions.Html);
        byte[] bytes = result.Content.ToArray();

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        string html = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Héllo ✓", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_Comes_From_Options_Then_First_H1_Then_Fallback()
    {
        string explicitTitle = await RenderAsync(
            "# Heading\n", new RenderOptions { DocumentTitle = "Chosen" });
        Assert.Contains("<title>Chosen</title>", explicitTitle, StringComparison.Ordinal);

        string fromHeading = await RenderAsync("Intro.\n\n# Heading & more\n");
        Assert.Contains("<title>Heading &amp; more</title>", fromHeading, StringComparison.Ordinal);

        string fallback = await RenderAsync("Just prose.\n");
        Assert.Contains("<title>Document</title>", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Css_Options_Are_Honoured()
    {
        string withDefault = await RenderAsync("Text.\n");
        Assert.Contains(".markdown-body", withDefault, StringComparison.Ordinal);

        string withoutCss = await RenderAsync(
            "Text.\n", new RenderOptions { IncludeDefaultCss = false });
        Assert.DoesNotContain("<style>", withoutCss, StringComparison.Ordinal);

        string withExtra = await RenderAsync(
            "Text.\n",
            new RenderOptions { IncludeDefaultCss = false, AdditionalCss = "body { color: red; }" });
        Assert.Contains("body { color: red; }", withExtra, StringComparison.Ordinal);

        string withFont = await RenderAsync("Text.\n", new RenderOptions { FontFamily = "Iosevka" });
        Assert.Contains("font-family: Iosevka;", withFont, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagram_Is_Inlined_In_A_Figure_And_Document_Is_Self_Contained()
    {
        string html = await RenderAsync("""
            # Doc

            Before.

            ```mermaid
            flowchart TD
                A[One] --> B[Two]
            ```

            After.
            """);

        Assert.Contains("<figure class=\"mermaid-figure\">", html, StringComparison.Ordinal);
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", html, StringComparison.Ordinal);
        SelfContainment.Assert(html);
    }

    [Fact]
    public async Task Angle_Brackets_In_Text_And_In_Mermaid_Fallbacks_Are_Escaped()
    {
        string prose = await RenderAsync("A 1 < 2 comparison and a<b too.\n");
        Assert.Contains("1 &lt; 2", prose, StringComparison.Ordinal);

        // The mermaid fallback is escaped by us rather than by Markdig, so cover it here too.
        string fallback = await RenderAsync(
            "```mermaid\nmindmap\n    root((<b>bold</b>))\n```\n");
        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", fallback, StringComparison.Ordinal);
        SelfContainment.Assert(fallback);
    }

    [Theory]
    [InlineData(OutputFormat.Docx, "phase 5")]
    public async Task Unimplemented_Formats_Fail_Clearly(OutputFormat format, string phase)
    {
        var renderer = new MarkdownRenderer();
        NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(
            () => renderer.RenderAsync("# Doc\n", new RenderOptions { Format = format }));

        Assert.Contains("not implemented", error.Message, StringComparison.Ordinal);
        Assert.Contains(phase, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderFileAsync_Writes_The_Output_File()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mdnr-{Guid.NewGuid():N}");
        string input = Path.Combine(directory, "input.md");
        string output = Path.Combine(directory, "nested", "output.html");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(input, "# File render\n\n```mermaid\nflowchart LR\n  A --> B\n```\n");

        try
        {
            var renderer = new MarkdownRenderer();
            RenderResult result = await renderer.RenderFileAsync(input, output, RenderOptions.Html);

            Assert.True(File.Exists(output));
            byte[] written = await File.ReadAllBytesAsync(output);
            Assert.Equal(result.Content.ToArray(), written);
            SelfContainment.Assert(Encoding.UTF8.GetString(written));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderFileAsync_Refuses_Unimplemented_Formats_Before_Reading_Input()
    {
        var renderer = new MarkdownRenderer();
        await Assert.ThrowsAsync<NotSupportedException>(() => renderer.RenderFileAsync(
            "does-not-exist.md", "out.docx", RenderOptions.Docx));
    }

    [Fact]
    public async Task Cancellation_Is_Observed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var renderer = new MarkdownRenderer();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => renderer.RenderAsync("# Doc\n", RenderOptions.Html, cancellation.Token));
    }

    [Fact]
    public async Task Rendering_The_Same_Markdown_Twice_Produces_Identical_Bytes()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));
        string first = await RenderAsync(markdown);
        string second = await RenderAsync(markdown);

        Assert.Equal(first, second, ignoreLineEndingDifferences: false);
    }
}
