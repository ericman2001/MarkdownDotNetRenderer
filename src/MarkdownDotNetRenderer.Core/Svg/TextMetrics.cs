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

namespace MarkdownDotNetRenderer.Svg;

/// <summary>
/// Font-free text measurement. Widths are estimated from a static per-character advance table
/// for a representative sans-serif, because no font rasterizer may be linked in (see
/// docs/06-aot-and-dependencies.md). Estimates are intentionally slightly generous: node padding
/// absorbs the error, and the fidelity target is "readable", not "pixel-perfect".
/// </summary>
public static class TextMetrics
{
    /// <summary>Multiplier applied to the summed advances, as a safety margin.</summary>
    public const double SafetyFactor = 1.06;

    /// <summary>Line height as a multiple of the font size.</summary>
    public const double LineHeightFactor = 1.25;

    /// <summary>Advance width, in em, used for characters missing from the table.</summary>
    public const double DefaultAdvance = 0.55;

    /// <summary>Estimates the rendered width of <paramref name="text"/> in CSS pixels.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>The estimated width in CSS pixels.</returns>
    public static double MeasureWidth(string text, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);

        double advances = 0;
        foreach (char c in text)
        {
            advances += Advance(c);
        }

        return advances * fontSize * SafetyFactor;
    }

    /// <summary>Line height for a given font size, in CSS pixels.</summary>
    /// <param name="fontSize">Font size in CSS pixels.</param>
    /// <returns>The line height in CSS pixels.</returns>
    public static double LineHeight(double fontSize) => fontSize * LineHeightFactor;

    /// <summary>
    /// Wraps <paramref name="text"/> at word boundaries so that no line exceeds
    /// <paramref name="maxChars"/> characters. Words longer than the budget are hard-split so a
    /// pathological label still produces bounded lines.
    /// </summary>
    /// <param name="text">The label text; may be empty.</param>
    /// <param name="maxChars">Maximum characters per line; must be positive.</param>
    /// <returns>One entry per rendered line; never empty.</returns>
    public static IReadOnlyList<string> WrapLabel(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChars);

        var lines = new List<string>();
        string collapsed = text.Trim();
        if (collapsed.Length == 0)
        {
            lines.Add(string.Empty);
            return lines;
        }

        var current = new System.Text.StringBuilder();
        foreach (string word in collapsed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string remaining = word;
            while (remaining.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                lines.Add(remaining[..maxChars]);
                remaining = remaining[maxChars..];
            }

            if (current.Length == 0)
            {
                current.Append(remaining);
            }
            else if (current.Length + 1 + remaining.Length <= maxChars)
            {
                current.Append(' ').Append(remaining);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(remaining);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    /// <summary>Advance width of one character, in em.</summary>
    /// <param name="c">The character.</param>
    /// <returns>The advance width in em.</returns>
    public static double Advance(char c) => c switch
    {
        ' ' => 0.28,
        'i' or 'j' or 'l' or 'I' or '|' or '!' or '.' or ',' or ':' or ';' or '\'' or '`' => 0.26,
        'f' or 'r' or 't' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' => 0.34,
        'J' or '"' or '*' => 0.40,
        'm' or 'w' or 'M' or 'W' or '@' => 0.88,
        'A' or 'B' or 'C' or 'D' or 'E' or 'F' or 'G' or 'H' or 'K' or 'L' or 'N' or 'O'
            or 'P' or 'Q' or 'R' or 'S' or 'T' or 'U' or 'V' or 'X' or 'Y' or 'Z' => 0.68,
        >= '0' and <= '9' => 0.56,
        >= 'a' and <= 'z' => 0.55,
        // Wide by default: CJK and other non-Latin scripts are typically full-width.
        > '\u02ff' => 1.0,
        _ => DefaultAdvance,
    };
}
