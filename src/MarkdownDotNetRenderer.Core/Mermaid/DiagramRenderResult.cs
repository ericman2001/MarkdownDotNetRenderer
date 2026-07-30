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

namespace MarkdownDotNetRenderer.Mermaid;

/// <summary>The outcome of rendering one mermaid block.</summary>
/// <param name="Success">Whether an SVG fragment was produced.</param>
/// <param name="SvgFragment">The <c>&lt;svg&gt;</c> fragment, or <see langword="null"/> on failure.</param>
/// <param name="Width">Intrinsic width in CSS pixels.</param>
/// <param name="Height">Intrinsic height in CSS pixels.</param>
/// <param name="AltText">Accessible description, e.g. "flowchart with 6 nodes and 7 edges".</param>
/// <param name="Diagnostics">Diagnostics produced while rendering.</param>
public sealed record DiagramRenderResult(
    bool Success,
    string? SvgFragment,
    double Width,
    double Height,
    string? AltText,
    IReadOnlyList<RenderDiagnostic> Diagnostics)
{
    /// <summary>Creates a failure result carrying a single warning diagnostic.</summary>
    /// <param name="code">Diagnostic code, e.g. <see cref="RenderDiagnostic.DiagramParseFailure"/>.</param>
    /// <param name="message">Human-readable description.</param>
    /// <returns>A failed result.</returns>
    public static DiagramRenderResult Failed(string code, string message) =>
        Failed(code, message, []);

    /// <summary>Creates a failure result, appending its warning to earlier diagnostics.</summary>
    /// <param name="code">Diagnostic code.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="earlier">Diagnostics collected before the failure.</param>
    /// <returns>A failed result.</returns>
    public static DiagramRenderResult Failed(
        string code,
        string message,
        IReadOnlyList<RenderDiagnostic> earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);

        var diagnostics = new List<RenderDiagnostic>(earlier.Count + 1);
        diagnostics.AddRange(earlier);
        diagnostics.Add(new RenderDiagnostic(DiagnosticSeverity.Warning, code, message));
        return new DiagramRenderResult(false, null, 0, 0, null, diagnostics);
    }
}
