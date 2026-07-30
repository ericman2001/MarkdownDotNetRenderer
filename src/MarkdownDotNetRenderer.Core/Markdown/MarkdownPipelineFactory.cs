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

using Markdig;

namespace MarkdownDotNetRenderer.Markdown;

/// <summary>
/// Supplies the one <see cref="MarkdownPipeline"/> used for both parsing and HTML rendering, so
/// extension behaviour cannot drift between the two.
/// </summary>
public static class MarkdownPipelineFactory
{
    /// <summary>The shared GFM/advanced pipeline. Markdig pipelines are immutable and thread-safe.</summary>
    public static MarkdownPipeline Default { get; } = Create();

    /// <summary>Builds a fresh pipeline with the same configuration as <see cref="Default"/>.</summary>
    /// <returns>A new pipeline instance.</returns>
    public static MarkdownPipeline Create() =>
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
}
