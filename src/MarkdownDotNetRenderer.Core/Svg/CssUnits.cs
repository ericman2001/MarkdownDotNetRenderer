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
/// The CSS-pixel unit definitions shared by every sizing path. Diagram geometry is produced in
/// CSS pixels (<see cref="SvgBuilder"/>), while office formats need physical lengths, so the
/// conversion factor lives here once rather than in each writer.
/// </summary>
public static class CssUnits
{
    /// <summary>CSS pixels per inch, as fixed by the CSS specification.</summary>
    public const double PixelsPerInch = 96;

    /// <summary>Centimetres per inch.</summary>
    public const double CentimetersPerInch = 2.54;

    /// <summary>Converts a CSS-pixel length to inches.</summary>
    /// <param name="pixels">The length in CSS pixels.</param>
    /// <returns>The same length in inches.</returns>
    public static double PixelsToInches(double pixels) => pixels / PixelsPerInch;

    /// <summary>Converts a CSS-pixel length to centimetres.</summary>
    /// <param name="pixels">The length in CSS pixels.</param>
    /// <returns>The same length in centimetres.</returns>
    public static double PixelsToCentimeters(double pixels) =>
        PixelsToInches(pixels) * CentimetersPerInch;
}
