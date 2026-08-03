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

/// <summary>Values every diagram renderer needs before it has a theme to consult.</summary>
public static class DiagramDefaults
{
    /// <summary>Label font size used when <see cref="RenderOptions.DiagramFontSize"/> is unusable.</summary>
    public const double FontSize = 12;

    /// <summary>The label font size to lay out with.</summary>
    /// <param name="options">The render options.</param>
    /// <returns>The requested size, or <see cref="FontSize"/> when it is not positive.</returns>
    public static double ResolveFontSize(RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DiagramFontSize <= 0 ? FontSize : options.DiagramFontSize;
    }
}
