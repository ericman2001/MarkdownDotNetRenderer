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

namespace MarkdownDotNetRenderer;

/// <summary>
/// Entry point for rendering Markdown to a configured <see cref="OutputFormat"/>.
/// This is a phase-0 placeholder: the parse/walk/dispatch/write pipeline described in
/// docs/02-architecture.md is implemented in later phases.
/// </summary>
public sealed class MarkdownRenderer
{
    /// <summary>Product name, exposed so the CLI and tests share a single source of truth.</summary>
    public const string ProductName = "MarkdownDotNetRenderer";

    /// <summary>Creates a renderer with the default diagram-renderer registry.</summary>
    public MarkdownRenderer()
    {
    }
}
