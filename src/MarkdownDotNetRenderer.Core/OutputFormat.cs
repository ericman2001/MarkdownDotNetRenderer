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
/// The document format produced by a render operation.
/// </summary>
public enum OutputFormat
{
    /// <summary>Self-contained HTML with inline SVG diagrams.</summary>
    Html,

    /// <summary>OpenDocument Text for LibreOffice/OpenOffice. Added in phase 2.</summary>
    Odt,

    /// <summary>OOXML WordprocessingML for Microsoft Word. Added in phase 5.</summary>
    Docx,
}
