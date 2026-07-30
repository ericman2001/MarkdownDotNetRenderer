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

/// <summary>How serious a <see cref="RenderDiagnostic"/> is. Rendering never fails outright.</summary>
public enum DiagnosticSeverity
{
    /// <summary>A recognized construct was ignored; the document is otherwise complete.</summary>
    Info,

    /// <summary>Content degraded (for example a diagram fell back to a code block).</summary>
    Warning,
}

/// <summary>
/// A non-fatal issue encountered while rendering. See docs/03-core-api.md for the code table.
/// </summary>
/// <param name="Severity">How serious the issue is.</param>
/// <param name="Code">Stable diagnostic code, e.g. <c>MERMAID001</c>.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="SourceLine">1-based line in the input Markdown, when known.</param>
public sealed record RenderDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    int? SourceLine = null)
{
    /// <summary>Unsupported diagram type; the raw mermaid source is emitted as a code block.</summary>
    public const string UnsupportedDiagramType = "MERMAID001";

    /// <summary>Diagram type recognized but its source failed to parse.</summary>
    public const string DiagramParseFailure = "MERMAID002";

    /// <summary>A recognized directive or feature was ignored.</summary>
    public const string IgnoredDiagramFeature = "MERMAID003";

    /// <summary>The diagram exceeded a layout guard.</summary>
    public const string DiagramTooLarge = "MERMAID004";

    /// <summary>A Markdown construct is unsupported by the selected writer.</summary>
    public const string WriterUnsupportedConstruct = "WRITER001";

    /// <summary>Returns a copy of this diagnostic carrying the given source line.</summary>
    /// <param name="line">The 1-based line, or <see langword="null"/> to clear it.</param>
    /// <returns>A diagnostic with <see cref="SourceLine"/> set.</returns>
    public RenderDiagnostic WithSourceLine(int? line) => this with { SourceLine = line };
}

/// <summary>The outcome of a render operation: the document bytes plus any diagnostics.</summary>
public sealed class RenderResult
{
    /// <summary>Rendered document bytes: UTF-8 (no BOM) HTML, or an ODT/DOCX package.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Non-fatal issues encountered while rendering, in document order.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; }

    /// <summary>Whether any diagnostic was reported.</summary>
    public bool HasWarnings => Diagnostics.Count > 0;
}
