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

using System.Globalization;

namespace MarkdownDotNetRenderer.Mermaid;

/// <summary>
/// Builds the element-id prefix a diagram uses for its <c>&lt;defs&gt;</c> entries. Several inline
/// SVGs share one HTML document's id space, so ids are derived from the diagram source: unique per
/// diagram, and identical on every repeated render (docs/04-mermaid-engine.md).
/// </summary>
public static class DiagramIds
{
    /// <summary>Prefix every generated id starts with.</summary>
    public const string Prefix = "mdnr";

    /// <summary>Returns the id prefix for one diagram source.</summary>
    /// <param name="mermaidSource">The verbatim mermaid source.</param>
    /// <returns>A prefix of the form <c>mdnr-1a2b3c4d</c>.</returns>
    public static string ForSource(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        return $"{Prefix}-{Fingerprint(mermaidSource)}";
    }

    /// <summary>A short deterministic FNV-1a fingerprint of a diagram source.</summary>
    /// <param name="source">The source to fingerprint.</param>
    /// <returns>Eight lowercase hex digits.</returns>
    public static string Fingerprint(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        uint hash = offsetBasis;
        foreach (char c in MermaidRenderer.Normalize(source))
        {
            hash ^= c;
            hash *= prime;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
