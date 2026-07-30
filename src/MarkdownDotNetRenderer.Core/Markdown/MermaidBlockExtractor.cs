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
using Markdig.Syntax;

namespace MarkdownDotNetRenderer.Markdown;

/// <summary>One item of the extractor's output: either a prose run or a mermaid diagram source.</summary>
public abstract record ExtractedBlock;

/// <summary>Consecutive non-mermaid blocks, to be rendered by the writer as prose.</summary>
/// <param name="Nodes">References into the Markdig AST, in document order.</param>
public sealed record ProseRun(IReadOnlyList<Block> Nodes) : ExtractedBlock;

/// <summary>A mermaid fenced code block awaiting diagram rendering.</summary>
/// <param name="Source">The fence's verbatim content, with <c>\n</c> line endings.</param>
/// <param name="InfoString">The fence info string, e.g. <c>mermaid title=Flow</c>.</param>
/// <param name="SourceLine">1-based line of the opening fence in the Markdown source.</param>
public sealed record MermaidDiagramSource(string Source, string InfoString, int SourceLine)
    : ExtractedBlock;

/// <summary>
/// Splits a parsed <see cref="MarkdownDocument"/> into an ordered list of prose runs and mermaid
/// diagram sources. The AST is only read, never mutated (docs/02-architecture.md).
/// </summary>
public static class MermaidBlockExtractor
{
    /// <summary>The fence info-string token that marks a mermaid diagram.</summary>
    public const string MermaidInfo = "mermaid";

    /// <summary>Extracts the ordered block list from a parsed document.</summary>
    /// <param name="document">The parsed Markdown document.</param>
    /// <returns>Prose runs and mermaid sources in document order.</returns>
    public static IReadOnlyList<ExtractedBlock> Extract(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = new List<ExtractedBlock>();
        var prose = new List<Block>();
        Walk(document, result, prose);
        Flush(result, prose);
        return result;
    }

    /// <summary>Whether a fenced code block's info string marks it as mermaid.</summary>
    /// <param name="fence">The fenced code block.</param>
    /// <returns><see langword="true"/> when the info string's first token is <c>mermaid</c>.</returns>
    public static bool IsMermaidFence(FencedCodeBlock fence)
    {
        ArgumentNullException.ThrowIfNull(fence);

        string info = fence.Info ?? string.Empty;
        int end = info.IndexOfAny([' ', '\t']);
        string firstToken = end < 0 ? info : info[..end];
        return firstToken.Equals(MermaidInfo, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads a leaf block's raw lines as text with <c>\n</c> endings.</summary>
    /// <param name="block">The leaf block, e.g. a fenced code block.</param>
    /// <returns>The verbatim content.</returns>
    public static string GetText(LeafBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var text = new StringBuilder();
        for (int i = 0; i < block.Lines.Count; i++)
        {
            text.Append(block.Lines.Lines[i].Slice.AsSpan());
            text.Append('\n');
        }

        return text.ToString();
    }

    private static void Walk(ContainerBlock container, List<ExtractedBlock> result, List<Block> prose)
    {
        foreach (Block child in container)
        {
            if (child is FencedCodeBlock fence && IsMermaidFence(fence))
            {
                Flush(result, prose);
                string info = fence.Info ?? string.Empty;
                string arguments = fence.Arguments ?? string.Empty;
                string infoString = arguments.Length == 0 ? info : $"{info} {arguments}";
                result.Add(new MermaidDiagramSource(GetText(fence), infoString, fence.Line + 1));
            }
            else if (child is ListItemBlock item)
            {
                // Reached only while flattening a list that holds a diagram: emit the item's own
                // blocks so the list's other content keeps its position and stays renderable.
                Walk(item, result, prose);
            }
            else if (child is ContainerBlock nested && ContainsMermaid(nested))
            {
                Walk(nested, result, prose);
            }
            else
            {
                prose.Add(child);
            }
        }
    }

    private static bool ContainsMermaid(ContainerBlock container)
    {
        foreach (Block child in container)
        {
            if (child is FencedCodeBlock fence && IsMermaidFence(fence))
            {
                return true;
            }

            if (child is ContainerBlock nested && ContainsMermaid(nested))
            {
                return true;
            }
        }

        return false;
    }

    private static void Flush(List<ExtractedBlock> result, List<Block> prose)
    {
        if (prose.Count == 0)
        {
            return;
        }

        result.Add(new ProseRun(prose.ToArray()));
        prose.Clear();
    }
}
