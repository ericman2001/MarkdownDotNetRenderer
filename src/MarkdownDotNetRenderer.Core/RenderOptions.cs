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
/// Immutable options controlling a single render operation. The full behaviour is defined in
/// docs/03-core-api.md; this phase-0 stub carries the shape only.
/// </summary>
public sealed class RenderOptions
{
    /// <summary>The document format to produce.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Html;

    /// <summary>Value for the HTML &lt;title&gt; / document core-properties title.</summary>
    public string? DocumentTitle { get; init; }

    /// <summary>Extra CSS injected into the HTML &lt;style&gt; block. HTML only.</summary>
    public string? AdditionalCss { get; init; }

    /// <summary>Emit the built-in minimal stylesheet. HTML only.</summary>
    public bool IncludeDefaultCss { get; init; } = true;

    /// <summary>Base font family used for SVG diagram labels and office-document body text.</summary>
    public string FontFamily { get; init; } = "Segoe UI, Arial, sans-serif";

    /// <summary>Base font size, in points, for diagram labels.</summary>
    public double DiagramFontSize { get; init; } = 12;

    /// <summary>Max diagram width in CSS pixels; layout wraps/scales to fit.</summary>
    public double MaxDiagramWidth { get; init; } = 900;

    /// <summary>Preset options producing self-contained HTML.</summary>
    public static RenderOptions Html { get; } = new() { Format = OutputFormat.Html };

    /// <summary>Preset options producing an ODT package. Writer arrives in phase 2.</summary>
    public static RenderOptions Odt { get; } = new() { Format = OutputFormat.Odt };

    /// <summary>Preset options producing a DOCX package. Writer arrives in phase 5.</summary>
    public static RenderOptions Docx { get; } = new() { Format = OutputFormat.Docx };
}
