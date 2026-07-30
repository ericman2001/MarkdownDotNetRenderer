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

/// <summary>
/// Area 7 of docs/07-testing-strategy.md: the kitchen-sink golden file. The expected HTML is
/// committed with <c>\n</c> endings and compared byte-for-byte, which is what proves output is
/// deterministic and platform-independent. Regenerate with
/// <c>dotnet run --project src/MarkdownDotNetRenderer.Cli -- -i samples/kitchen-sink.md -o samples/expected/kitchen-sink.html</c>.
/// </summary>
public sealed class GoldenFileTests
{
    [Fact]
    public async Task Kitchen_Sink_Matches_The_Golden_Html_Byte_For_Byte()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));
        byte[] expected = await File.ReadAllBytesAsync(TestFiles.Expected("kitchen-sink.html"));

        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, RenderOptions.Html);

        // Compare as text first: a mismatch then reports a readable diff rather than byte offsets.
        Assert.Equal(
            Encoding.UTF8.GetString(expected),
            Encoding.UTF8.GetString(result.Content.Span),
            ignoreLineEndingDifferences: false);
        Assert.Equal(expected, result.Content.ToArray());
    }

    [Fact]
    public async Task Kitchen_Sink_Reports_The_Expected_Diagnostics()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md"));

        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, RenderOptions.Html);

        Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.UnsupportedDiagramType);
        Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
        Assert.All(result.Diagnostics, diagnostic => Assert.NotNull(diagnostic.SourceLine));
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public async Task The_Golden_Html_Is_Self_Contained()
    {
        string html = await File.ReadAllTextAsync(TestFiles.Expected("kitchen-sink.html"));

        SelfContainment.Assert(html);
    }

    [Fact]
    public async Task The_Flowchart_Demo_Sample_Renders_Without_Diagnostics()
    {
        string markdown = await File.ReadAllTextAsync(TestFiles.Sample("flowchart-demo.md"));

        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, html.Split("<figure class=\"mermaid-figure\">").Length - 1);
        SelfContainment.Assert(html);
    }
}
