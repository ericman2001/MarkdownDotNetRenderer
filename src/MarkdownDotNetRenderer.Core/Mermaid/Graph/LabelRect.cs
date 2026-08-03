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

using MarkdownDotNetRenderer.Mermaid.Flowchart;

namespace MarkdownDotNetRenderer.Mermaid.Graph;

/// <summary>
/// An axis-aligned rectangle a label has to stay off: either a box or a label already placed
/// (<see cref="GraphEdgePainter.LabelAnchors(GraphPlacement, GraphEdgePaint, double)"/>).
/// </summary>
/// <param name="Left">Left edge in diagram coordinates.</param>
/// <param name="Top">Top edge in diagram coordinates.</param>
/// <param name="Width">Width in CSS pixels.</param>
/// <param name="Height">Height in CSS pixels.</param>
public readonly record struct LabelRect(double Left, double Top, double Width, double Height)
{
    /// <summary>The rectangle a placed box occupies.</summary>
    /// <param name="node">The placed box.</param>
    /// <returns>Its rectangle.</returns>
    public static LabelRect Of(PlacedNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new LabelRect(node.Left, node.Top, node.Width, node.Height);
    }

    /// <summary>The rectangle a label of the given size centred on a point occupies.</summary>
    /// <param name="center">The label's anchor.</param>
    /// <param name="size">The label's backing-rect size.</param>
    /// <returns>Its rectangle.</returns>
    public static LabelRect Around(LayoutPoint center, NodeSize size) => new(
        center.X - (size.Width / 2),
        center.Y - (size.Height / 2),
        size.Width,
        size.Height);
}
