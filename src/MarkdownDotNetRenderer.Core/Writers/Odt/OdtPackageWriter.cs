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
using System.Text;

namespace MarkdownDotNetRenderer.Writers.Odt;

/// <summary>
/// The zip mechanics of an ODF package: an uncompressed <c>mimetype</c> first entry, the added
/// parts in the order they were added, and a <c>META-INF/manifest.xml</c> generated from the
/// actual entry list. Built on <see cref="ZipArchive"/> alone — no dependency, AOT-clean
/// (docs/06-aot-and-dependencies.md).
/// </summary>
internal sealed class OdtPackageWriter
{
    /// <summary>
    /// Zip entry timestamp. Fixed so two renders of the same input are byte-identical; the DOS
    /// timestamp of a zip entry cannot be omitted, only pinned.
    /// </summary>
    private static readonly DateTimeOffset EntryTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<Part> _parts = [];

    /// <summary>Adds a part to the package, after any part already added.</summary>
    /// <param name="entryName">Package-relative entry name; always <c>/</c>-separated.</param>
    /// <param name="mediaType">Media type recorded in the manifest.</param>
    /// <param name="content">The part's bytes.</param>
    internal void AddPart(string entryName, string mediaType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentException.ThrowIfNullOrEmpty(mediaType);
        ArgumentNullException.ThrowIfNull(content);

        _parts.Add(new Part(entryName, mediaType, content));
    }

    /// <summary>Writes the package to a stream, which is left open.</summary>
    /// <param name="destination">The stream to write the zip to.</param>
    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        // The mimetype entry must be first and stored, or LibreOffice may refuse the package.
        WriteEntry(
            archive,
            OdfNames.MimetypeEntry,
            Utf8NoBom.GetBytes(OdfNames.TextMediaType),
            CompressionLevel.NoCompression);

        foreach (Part part in _parts)
        {
            WriteEntry(archive, part.EntryName, part.Content, CompressionLevel.Optimal);
        }

        WriteEntry(
            archive,
            OdfNames.ManifestEntry,
            BuildManifest(),
            CompressionLevel.Optimal);
    }

    private static void WriteEntry(
        ZipArchive archive,
        string entryName,
        byte[] content,
        CompressionLevel compressionLevel)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, compressionLevel);
        entry.LastWriteTime = EntryTimestamp;
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    /// <summary>
    /// Builds the manifest from the parts actually added. Per ODF, <c>mimetype</c> and the
    /// manifest itself are not listed; everything else is, with the media type it was added with.
    /// </summary>
    private byte[] BuildManifest() => OdtXml.WritePart(writer =>
    {
        writer.WriteStartElement(
            OdfNames.ManifestPrefix,
            "manifest",
            OdfNames.ManifestNs);
        writer.WriteAttributeString(
            OdfNames.ManifestPrefix,
            "version",
            OdfNames.ManifestNs,
            OdfNames.Version);

        WriteFileEntry(OdfNames.RootEntryPath, OdfNames.TextMediaType, withVersion: true);
        foreach (Part part in _parts)
        {
            WriteFileEntry(part.EntryName, part.MediaType, withVersion: false);
        }

        writer.WriteEndElement();

        void WriteFileEntry(string fullPath, string mediaType, bool withVersion)
        {
            writer.WriteStartElement(OdfNames.ManifestPrefix, "file-entry", OdfNames.ManifestNs);
            writer.WriteAttributeString(
                OdfNames.ManifestPrefix, "full-path", OdfNames.ManifestNs, fullPath);
            if (withVersion)
            {
                writer.WriteAttributeString(
                    OdfNames.ManifestPrefix, "version", OdfNames.ManifestNs, OdfNames.Version);
            }

            writer.WriteAttributeString(
                OdfNames.ManifestPrefix, "media-type", OdfNames.ManifestNs, mediaType);
            writer.WriteEndElement();
        }
    });

    private sealed record Part(string EntryName, string MediaType, byte[] Content);
}
