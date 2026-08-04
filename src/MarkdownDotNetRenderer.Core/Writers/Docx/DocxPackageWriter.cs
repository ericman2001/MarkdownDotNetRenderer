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

using System.IO.Compression;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// Copies a finished OPC package to its destination with fixed entry timestamps. The OOXML parts
/// are already deterministic, but the packaging layer stamps every zip entry with the current
/// time, so the same input would otherwise produce files that differ byte for byte. Entry order
/// and content are copied unchanged.
/// </summary>
internal static class DocxPackageWriter
{
    /// <summary>Copies a package, replacing per-entry timestamps with a fixed one.</summary>
    /// <param name="package">The package to copy; read from its current position.</param>
    /// <param name="destination">The stream to write to; left open.</param>
    /// <param name="timestamp">Timestamp stamped on every entry.</param>
    internal static void CopyWithFixedTimestamps(
        Stream package,
        Stream destination,
        DateTimeOffset timestamp)
    {
        using var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (ZipArchiveEntry entry in source.Entries)
        {
            ZipArchiveEntry copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            copy.LastWriteTime = timestamp;

            using Stream input = entry.Open();
            using Stream output = copy.Open();
            input.CopyTo(output);
        }
    }
}
