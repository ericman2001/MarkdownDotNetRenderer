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

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// Tunable visual values of the DOCX output — the WordprocessingML counterpart to
/// <c>OdtTheme</c>. Lengths are points or inches; colours are OOXML hex triplets without the
/// leading <c>#</c>. Nothing here is mandated by OOXML, so it is an injectable record with a
/// <see cref="Default"/> singleton rather than scattered constants
/// (docs/09-conventions.md section 1).
/// </summary>
/// <param name="MonospaceFontFamily">Font family for code spans, code blocks, and fallbacks.</param>
/// <param name="FallbackBodyFontFamily">Body family used when the options supply an empty stack.</param>
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
/// <param name="CodeBackgroundColor">Shading of code spans, code blocks, and their table cell.</param>
/// <param name="HyperlinkColor">Colour of link text.</param>
/// <param name="QuoteIndent">Left indent of a block quote, in inches.</param>
/// <param name="QuoteBorderWidth">Width of a block quote's left border, in points.</param>
/// <param name="QuoteBorderColor">Colour of a block quote's left border.</param>
/// <param name="TableBorderWidth">Width of table cell borders, in points.</param>
/// <param name="TableBorderColor">Colour of table cell borders.</param>
/// <param name="TableHeaderBackgroundColor">Shading of header-row cells.</param>
/// <param name="ThematicBreakBorderWidth">Width of a thematic break's rule, in points.</param>
/// <param name="ThematicBreakBorderColor">Colour of a thematic break's rule.</param>
/// <param name="PageWidth">Page width, in inches.</param>
/// <param name="PageHeight">Page height, in inches.</param>
/// <param name="PageMargin">Page margin on all four sides, in inches.</param>
/// <param name="ListLevelIndent">Indent added per list nesting level, in inches.</param>
/// <param name="ListHangingIndent">Hanging indent of a list label, in inches.</param>
/// <param name="TaskListCheckedMarker">Glyph standing in for a ticked task-list checkbox.</param>
/// <param name="TaskListUncheckedMarker">Glyph standing in for an empty task-list checkbox.</param>
public sealed record DocxTheme(
    string MonospaceFontFamily = "Consolas",
    string FallbackBodyFontFamily = "Calibri",
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
    string CodeBackgroundColor = "F5F5F5",
    string HyperlinkColor = "0563C1",
    double QuoteIndent = 0.4,
    double QuoteBorderWidth = 2,
    string QuoteBorderColor = "CCCCCC",
    double TableBorderWidth = 0.5,
    string TableBorderColor = "B0B0B0",
    string TableHeaderBackgroundColor = "EEEEEE",
    double ThematicBreakBorderWidth = 0.5,
    string ThematicBreakBorderColor = "808080",
    double PageWidth = 8.5,
    double PageHeight = 11,
    double PageMargin = 1,
    double ListLevelIndent = 0.25,
    double ListHangingIndent = 0.25,
    string TaskListCheckedMarker = "\u2612",
    string TaskListUncheckedMarker = "\u2610")
{
    /// <summary>The defaults documented in docs/05-output-writers.md.</summary>
    public static DocxTheme Default { get; } = new();

    /// <summary>
    /// Bullet glyphs cycled through as list nesting deepens; the deepest levels reuse them from
    /// the start. Never empty — an empty list falls back to the first default glyph.
    /// </summary>
    public IReadOnlyList<string> BulletCharacters { get; init; } =
        ["\u2022", "\u25e6", "\u25aa"];

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
