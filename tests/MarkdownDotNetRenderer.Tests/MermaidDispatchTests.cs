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
using MarkdownDotNetRenderer.Mermaid;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 4 of docs/07-testing-strategy.md: diagram-type detection and dispatch.</summary>
public sealed class MermaidDispatchTests
{
    [Theory]
    [InlineData("flowchart TD\n  A --> B\n", "flowchart")]
    [InlineData("graph LR\n  A --> B\n", "graph")]
    [InlineData("  \n%% a comment\nflowchart TD\n", "flowchart")]
    [InlineData("sequenceDiagram\n  A->>B: hi\n", "sequenceDiagram")]
    [InlineData("flowchart;\n", "flowchart")]
    [InlineData("pie title Pets\n", "pie")]
    public void Diagram_Type_Is_The_First_Token_Of_The_First_Meaningful_Line(
        string source,
        string expected)
    {
        var diagnostics = new List<RenderDiagnostic>();

        Assert.Equal(expected, MermaidRenderer.DetectDiagramType(source, diagnostics));
    }

    [Fact]
    public void An_Empty_Block_Has_No_Diagram_Type()
    {
        var diagnostics = new List<RenderDiagnostic>();

        Assert.Null(MermaidRenderer.DetectDiagramType("\n\n%% only a comment\n", diagnostics));
    }

    [Fact]
    public void A_Directive_Block_Is_Skipped_And_Reported_Once()
    {
        var diagnostics = new List<RenderDiagnostic>();

        string? type = MermaidRenderer.DetectDiagramType(
            "%%{init: {'theme': 'dark'}}%%\nflowchart TD\n  A --> B\n", diagnostics);

        Assert.Equal("flowchart", type);
        RenderDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }

    [Fact]
    public void A_Byte_Order_Mark_And_Crlf_Endings_Are_Normalized()
    {
        var diagnostics = new List<RenderDiagnostic>();

        Assert.Equal(
            "flowchart",
            MermaidRenderer.DetectDiagramType("\uFEFFflowchart TD\r\n  A --> B\r\n", diagnostics));
    }

    [Fact]
    public void Unsupported_Types_Report_Exactly_One_Mermaid001()
    {
        var renderer = new MermaidRenderer();

        DiagramRenderResult result = renderer.Render(
            "sequenceDiagram\n  Alice->>Bob: hi\n", RenderOptions.Html);

        Assert.False(result.Success);
        Assert.Null(result.SvgFragment);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.UnsupportedDiagramType, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void A_Renderer_That_Throws_Degrades_To_Mermaid002()
    {
        var renderer = new MermaidRenderer([new ThrowingRenderer()]);

        DiagramRenderResult result = renderer.Render("boom\n", RenderOptions.Html);

        Assert.False(result.Success);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.DiagramParseFailure, diagnostic.Code);
        Assert.Contains("deliberate", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Registry_Is_Explicit_And_Covers_Only_This_Phase()
    {
        var renderer = new MermaidRenderer();

        Assert.Equal(
            ["flowchart", "graph"],
            renderer.SupportedDiagramTypes.OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_Unsupported_Diagram_Falls_Back_To_An_Escaped_Code_Block()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "```mermaid\nsequenceDiagram\n  Alice->>Bob: <hi>\n```\n", RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("<pre><code class=\"language-mermaid\">", html, StringComparison.Ordinal);
        Assert.Contains("Alice-&gt;&gt;Bob: &lt;hi&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);

        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.UnsupportedDiagramType, diagnostic.Code);
        Assert.Equal(1, diagnostic.SourceLine);
    }

    [Fact]
    public async Task A_Malformed_Flowchart_Falls_Back_With_Mermaid002()
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(
            "```mermaid\nflowchart TD\n    -->\n```\n", RenderOptions.Html);
        string html = Encoding.UTF8.GetString(result.Content.Span);

        Assert.Contains("<pre><code class=\"language-mermaid\">", html, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
    }

    private sealed class ThrowingRenderer : IDiagramRenderer
    {
        public IReadOnlyCollection<string> DiagramTypes { get; } = ["boom"];

        public DiagramRenderResult Render(string mermaidSource, RenderOptions options) =>
            throw new InvalidOperationException("deliberate test failure");
    }
}
