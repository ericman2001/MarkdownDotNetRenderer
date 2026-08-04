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

using System.Globalization;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// The unit conversions and string formatting WordprocessingML attributes need. Every value is
/// formatted with <see cref="CultureInfo.InvariantCulture"/>, so a comma-decimal machine cannot
/// produce an unreadable package — the same rule the SVG and ODT paths follow.
/// </summary>
internal static class DocxUnits
{
    /// <summary>Converts a CSS-pixel length to English Metric Units.</summary>
    /// <param name="pixels">The length in CSS pixels.</param>
    /// <returns>The length in EMU, rounded to the nearest unit and never negative.</returns>
    internal static long PixelsToEmus(double pixels) =>
        Math.Max(0, (long)Math.Round(pixels * OoxmlNames.EmusPerPixel, MidpointRounding.AwayFromZero));

    /// <summary>Converts an inch length to English Metric Units.</summary>
    /// <param name="inches">The length in inches.</param>
    /// <returns>The length in EMU.</returns>
    internal static long InchesToEmus(double inches) =>
        Math.Max(0, (long)Math.Round(inches * OoxmlNames.EmusPerInch, MidpointRounding.AwayFromZero));

    /// <summary>Converts an inch length to twentieths of a point.</summary>
    /// <param name="inches">The length in inches.</param>
    /// <returns>The length in twips.</returns>
    internal static int InchesToTwips(double inches) =>
        (int)Math.Round(inches * OoxmlNames.TwipsPerInch, MidpointRounding.AwayFromZero);

    /// <summary>Formats a point size as the half-points <c>w:sz</c> expects.</summary>
    /// <param name="points">The size in points.</param>
    /// <returns>The size in half-points, as a string.</returns>
    internal static string HalfPoints(double points) => Integer(
        (int)Math.Round(points * OoxmlNames.HalfPointsPerPoint, MidpointRounding.AwayFromZero));

    /// <summary>Formats a point width as the eighths of a point border sizes expect.</summary>
    /// <param name="points">The width in points.</param>
    /// <returns>The width in eighths of a point.</returns>
    internal static uint EighthPoints(double points) => (uint)Math.Max(
        1,
        (int)Math.Round(points * OoxmlNames.EighthPointsPerPoint, MidpointRounding.AwayFromZero));

    /// <summary>Formats a point length as the twips string an attribute expects.</summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in twips, as a string.</returns>
    internal static string TwipsOfPoints(double points) => Integer(
        (int)Math.Round(points * OoxmlNames.TwipsPerPoint, MidpointRounding.AwayFromZero));

    /// <summary>Formats an inch length as the twips string an attribute expects.</summary>
    /// <param name="inches">The length in inches.</param>
    /// <returns>The length in twips, as a string.</returns>
    internal static string Twips(double inches) => Integer(InchesToTwips(inches));

    /// <summary>Formats an integer invariantly.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The decimal representation.</returns>
    internal static string Integer(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats an EMU length invariantly.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The decimal representation.</returns>
    internal static string Integer(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reduces a CSS font stack to the single family name <c>w:rFonts</c> accepts: OOXML names one
    /// font per attribute, so <c>"Segoe UI, Arial, sans-serif"</c> becomes <c>Segoe UI</c> and Word
    /// substitutes on its own when the font is missing.
    /// </summary>
    /// <param name="fontFamily">A CSS font stack or a single family name.</param>
    /// <param name="fallback">Family used when the stack yields nothing usable.</param>
    /// <returns>One font family name.</returns>
    internal static string PrimaryFontFamily(string fontFamily, string fallback)
    {
        foreach (string candidate in fontFamily.Split(','))
        {
            string trimmed = candidate.Trim().Trim('\'', '"').Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return fallback;
    }
}
