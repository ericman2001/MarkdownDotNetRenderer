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
using Markdig.Syntax.Inlines;

namespace MarkdownDotNetRenderer.Writers;

/// <summary>Resolves document titles consistently across document formats.</summary>
internal static class DocumentTitle
{
    private const string DefaultTitle = "Document";

    /// <summary>
    /// Uses the explicit title, the first non-empty level-one heading, or the default title.
    /// </summary>
    /// <param name="content">The document blocks to inspect.</param>
    /// <param name="options">Options supplying an optional explicit title.</param>
    /// <returns>The title to emit.</returns>
    public static string Resolve(DocumentContent content, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.DocumentTitle))
        {
            return options.DocumentTitle;
        }

        foreach (DocumentBlock block in content.Blocks)
        {
            if (block is not ProseBlock prose)
            {
                continue;
            }

            foreach (Block node in prose.Nodes)
            {
                if (node is HeadingBlock { Level: 1 } heading)
                {
                    string text = ExtractText(heading.Inline);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        return DefaultTitle;
    }

    private static string ExtractText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content.AsSpan());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case ContainerInline nested:
                    text.Append(ExtractText(nested));
                    break;
                default:
                    break;
            }
        }

        return text.ToString();
    }
}
