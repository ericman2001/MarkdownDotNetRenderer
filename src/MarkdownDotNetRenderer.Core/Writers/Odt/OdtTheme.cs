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

namespace MarkdownDotNetRenderer.Writers.Odt;

/// <summary>
/// Tunable visual values of the ODT output — the office-document counterpart to
/// <c>DiagramTheme</c> and <c>Writers/default.css</c>. Lengths are points or inches and colours
/// are hex literals; nothing here is mandated by ODF, so it is an injectable record with a
/// <see cref="Default"/> singleton rather than scattered constants
/// (docs/09-conventions.md section 1).
/// </summary>
/// <param name="MonospaceFontFamily">Font family for code spans, code blocks, and preformatted text.</param>
/// <param name="BodyFontSize">Body text size, in points.</param>
/// <param name="CodeFontSize">Code text size, in points.</param>
/// <param name="Heading1FontSize">Level 1 heading size, in points.</param>
/// <param name="Heading2FontSize">Level 2 heading size, in points.</param>
/// <param name="Heading3FontSize">Level 3 heading size, in points.</param>
/// <param name="Heading4FontSize">Level 4 heading size, in points.</param>
/// <param name="Heading5FontSize">Level 5 heading size, in points.</param>
/// <param name="Heading6FontSize">Level 6 heading size, in points.</param>
/// <param name="ParagraphSpaceBelow">Space after a body paragraph, in points.</param>
/// <param name="HeadingSpaceAbove">Space before a heading, in points.</param>
/// <param name="HeadingSpaceBelow">Space after a heading, in points.</param>
/// <param name="CodeBackgroundColor">Background of code spans and code paragraphs.</param>
/// <param name="QuoteIndent">Left and right indent of a block quote, in inches.</param>
/// <param name="QuoteBorderWidth">Width of a block quote's left border, in points.</param>
/// <param name="QuoteBorderColor">Colour of a block quote's left border.</param>
/// <param name="TableBorderWidth">Width of table cell borders, in points.</param>
/// <param name="TableBorderColor">Colour of table cell borders.</param>
/// <param name="TableHeaderBackgroundColor">Background of header-row cells.</param>
/// <param name="TableCellPadding">Padding inside a table cell, in inches.</param>
/// <param name="ThematicBreakBorderWidth">Width of a thematic break's rule, in points.</param>
/// <param name="ThematicBreakBorderColor">Colour of a thematic break's rule.</param>
/// <param name="PageWidth">Page width, in inches.</param>
/// <param name="PageHeight">Page height, in inches.</param>
/// <param name="PageMargin">Page margin on all four sides, in inches.</param>
/// <param name="ListLevelIndent">Indent added per list nesting level, in inches.</param>
public sealed record OdtTheme(
    string MonospaceFontFamily = "DejaVu Sans Mono, Consolas, monospace",
    double BodyFontSize = 11,
    double CodeFontSize = 10,
    double Heading1FontSize = 20,
    double Heading2FontSize = 17,
    double Heading3FontSize = 15,
    double Heading4FontSize = 13,
    double Heading5FontSize = 12,
    double Heading6FontSize = 11,
    double ParagraphSpaceBelow = 6,
    double HeadingSpaceAbove = 12,
    double HeadingSpaceBelow = 6,
    string CodeBackgroundColor = "#f5f5f5",
    double QuoteIndent = 0.4,
    double QuoteBorderWidth = 2,
    string QuoteBorderColor = "#cccccc",
    double TableBorderWidth = 0.5,
    string TableBorderColor = "#b0b0b0",
    string TableHeaderBackgroundColor = "#eeeeee",
    double TableCellPadding = 0.04,
    double ThematicBreakBorderWidth = 0.5,
    string ThematicBreakBorderColor = "#808080",
    double PageWidth = 8.5,
    double PageHeight = 11,
    double PageMargin = 0.8,
    double ListLevelIndent = 0.3)
{
    /// <summary>The defaults documented in docs/05-output-writers.md.</summary>
    public static OdtTheme Default { get; } = new();

    /// <summary>Width available to body content between the page margins, in inches.</summary>
    public double ContentWidth => PageWidth - (2 * PageMargin);

    /// <summary>Font size of a heading at <paramref name="level"/>, in points.</summary>
    /// <param name="level">Heading level, 1–6.</param>
    /// <returns>The size in points; levels beyond 6 use the level 6 size.</returns>
    public double HeadingFontSize(int level) => level switch
    {
        1 => Heading1FontSize,
        2 => Heading2FontSize,
        3 => Heading3FontSize,
        4 => Heading4FontSize,
        5 => Heading5FontSize,
        _ => Heading6FontSize,
    };
}
