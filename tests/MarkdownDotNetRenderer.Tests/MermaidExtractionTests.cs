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

using Markdig.Syntax;
using MarkdownDotNetRenderer.Markdown;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 2 of docs/07-testing-strategy.md: mermaid block extraction.</summary>
public sealed class MermaidExtractionTests
{
    private static IReadOnlyList<ExtractedBlock> Extract(string markdown) =>
        MermaidBlockExtractor.Extract(
            Markdig.Markdown.Parse(markdown, MarkdownPipelineFactory.Default));

    [Fact]
    public void Prose_And_Diagrams_Keep_Document_Order()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("""
            # One

            ```mermaid
            flowchart TD
                A --> B
            ```

            Middle.

            ```mermaid
            graph LR
                C --> D
            ```

            End.
            """);

        Assert.Collection(
            blocks,
            block => Assert.IsType<ProseRun>(block),
            block => Assert.Equal("flowchart TD\n    A --> B\n", Assert.IsType<MermaidDiagramSource>(block).Source),
            block => Assert.IsType<ProseRun>(block),
            block => Assert.Equal("graph LR\n    C --> D\n", Assert.IsType<MermaidDiagramSource>(block).Source),
            block => Assert.IsType<ProseRun>(block));
    }

    [Fact]
    public void Source_Line_Is_The_One_Based_Opening_Fence_Line()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("Line 1\n\n```mermaid\nflowchart TD\n  A\n```\n");

        MermaidDiagramSource diagram = Assert.IsType<MermaidDiagramSource>(blocks[1]);
        Assert.Equal(3, diagram.SourceLine);
    }

    [Theory]
    [InlineData("mermaid", true)]
    [InlineData("MERMAID", true)]
    [InlineData("Mermaid title=Flow", true)]
    [InlineData("mermaidish", false)]
    [InlineData("csharp", false)]
    [InlineData("", false)]
    public void Only_Mermaid_Fences_Are_Extracted(string info, bool expected)
    {
        var document = Markdig.Markdown.Parse(
            $"```{info}\nflowchart TD\n  A\n```\n", MarkdownPipelineFactory.Default);
        FencedCodeBlock fence = document.Descendants<FencedCodeBlock>().Single();

        Assert.Equal(expected, MermaidBlockExtractor.IsMermaidFence(fence));
    }

    [Fact]
    public void Indented_Code_Blocks_Are_Never_Diagrams()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("    flowchart TD\n      A --> B\n");

        Assert.Single(blocks);
        Assert.IsType<ProseRun>(blocks[0]);
    }

    [Fact]
    public void Info_String_Arguments_Are_Preserved()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("```mermaid title=Flow\nflowchart TD\n  A\n```\n");

        MermaidDiagramSource diagram = Assert.IsType<MermaidDiagramSource>(blocks[0]);
        Assert.Equal("mermaid title=Flow", diagram.InfoString);
    }

    [Fact]
    public void Diagrams_Nested_In_Containers_Are_Found()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("""
            - Item with a diagram:

              ```mermaid
              flowchart TD
                  A --> B
              ```
            """);

        Assert.Contains(blocks, block => block is MermaidDiagramSource);
    }

    [Fact]
    public void A_Document_Without_Diagrams_Is_One_Prose_Run()
    {
        IReadOnlyList<ExtractedBlock> blocks = Extract("# Title\n\nText.\n\n- a\n- b\n");

        ProseRun prose = Assert.IsType<ProseRun>(Assert.Single(blocks));
        Assert.Equal(3, prose.Nodes.Count);
    }
}
