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
/// Where each of an edge's labels is centred, or <see langword="null"/> when the edge does not carry
/// that label. Shared by the painter and by canvas measurement so both agree on the geometry.
/// </summary>
/// <param name="Mid">The mid-edge label's anchor.</param>
/// <param name="Start">The source-end label's anchor.</param>
/// <param name="End">The target-end label's anchor.</param>
public sealed record EdgeLabelAnchors(
    LayoutPoint? Mid,
    LayoutPoint? Start,
    LayoutPoint? End);
