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

using System.Buffers.Binary;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// Resolves a Markdown image target to a local file the DOCX package can embed, and reads that
/// file's intrinsic size from its header. Word needs a display size in EMU before it will show an
/// image, and this project may not take a decoding dependency
/// (docs/06-aot-and-dependencies.md), so the few well-known container headers are read directly.
/// A target that is not a readable local file of a known type is reported as unsupported by the
/// caller rather than guessed at.
/// </summary>
internal static class DocxImages
{
    /// <summary>An image the writer can embed: its bytes, content type, and intrinsic size.</summary>
    /// <param name="Path">Absolute path of the resolved file.</param>
    /// <param name="ContentType">Content type to give the image part.</param>
    /// <param name="Width">Intrinsic width in CSS pixels.</param>
    /// <param name="Height">Intrinsic height in CSS pixels.</param>
    internal sealed record ResolvedImage(
        string Path,
        string ContentType,
        double Width,
        double Height);

    private const string PngContentType = "image/png";
    private const string JpegContentType = "image/jpeg";
    private const string GifContentType = "image/gif";
    private const string BmpContentType = "image/bmp";

    /// <summary>Bytes read from the front of a file; enough for every header inspected here.</summary>
    private const int HeaderProbeLength = 64 * 1024;

    /// <summary>Length of the PNG signature plus the IHDR length/type fields.</summary>
    private const int PngHeaderLength = 24;

    private const int GifHeaderLength = 10;
    private const int BmpHeaderLength = 26;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Resolves an image target. Only absolute paths and paths relative to the process working
    /// directory are considered: <c>RenderOptions</c> carries no base directory, so anything with
    /// a URI scheme (<c>http</c>, <c>data</c>, …) is deliberately not fetched.
    /// </summary>
    /// <param name="target">The image URL from the Markdown source.</param>
    /// <returns>The resolved image, or <see langword="null"/> when it cannot be embedded.</returns>
    internal static ResolvedImage? Resolve(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            return null;
        }

        string path = uri?.IsFile == true ? uri.LocalPath : target;
        string? contentType = ContentTypeOf(path);
        if (contentType is null)
        {
            return null;
        }

        byte[] header;
        try
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                return null;
            }

            header = ReadHeader(full);
            path = full;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return null;
        }

        (double width, double height)? size = contentType switch
        {
            PngContentType => PngSize(header),
            GifContentType => GifSize(header),
            BmpContentType => BmpSize(header),
            JpegContentType => JpegSize(header),
            OoxmlNames.SvgContentType => SvgSize(header),
            _ => null,
        };

        return size is { } resolved && resolved.width > 0 && resolved.height > 0
            ? new ResolvedImage(path, contentType, resolved.width, resolved.height)
            : null;
    }

    private static string? ContentTypeOf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => PngContentType,
            ".jpg" or ".jpeg" => JpegContentType,
            ".gif" => GifContentType,
            ".bmp" => BmpContentType,
            ".svg" => OoxmlNames.SvgContentType,
            _ => null,
        };

    private static byte[] ReadHeader(string path)
    {
        using FileStream file = File.OpenRead(path);
        byte[] buffer = new byte[(int)Math.Min(HeaderProbeLength, file.Length)];
        file.ReadExactly(buffer);
        return buffer;
    }

    private static (double Width, double Height)? PngSize(ReadOnlySpan<byte> header)
    {
        if (header.Length < PngHeaderLength || !header[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return null;
        }

        return (
            BinaryPrimitives.ReadUInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadUInt32BigEndian(header[20..24]));
    }

    private static (double Width, double Height)? GifSize(ReadOnlySpan<byte> header) =>
        header.Length < GifHeaderLength
            ? null
            : (BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]));

    private static (double Width, double Height)? BmpSize(ReadOnlySpan<byte> header)
    {
        if (header.Length < BmpHeaderLength)
        {
            return null;
        }

        // A BMP height is signed: a negative value means the rows are stored top-down.
        return (
            BinaryPrimitives.ReadInt32LittleEndian(header[18..22]),
            Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(header[22..26])));
    }

    /// <summary>
    /// Walks the JPEG marker segments to the first start-of-frame, which is the only place the
    /// pixel dimensions are recorded.
    /// </summary>
    private static (double Width, double Height)? JpegSize(ReadOnlySpan<byte> header)
    {
        const byte markerPrefix = 0xFF;
        const int minimumFrameLength = 7;

        int index = 2;
        while (index + 4 <= header.Length)
        {
            if (header[index] != markerPrefix)
            {
                index++;
                continue;
            }

            byte marker = header[index + 1];
            if (IsStartOfFrame(marker))
            {
                return index + 9 <= header.Length
                        && BinaryPrimitives.ReadUInt16BigEndian(header[(index + 2)..]) >= minimumFrameLength
                    ? (BinaryPrimitives.ReadUInt16BigEndian(header[(index + 7)..]),
                        BinaryPrimitives.ReadUInt16BigEndian(header[(index + 5)..]))
                    : null;
            }

            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                index += 2;
                continue;
            }

            index += 2 + BinaryPrimitives.ReadUInt16BigEndian(header[(index + 2)..]);
        }

        return null;

        // SOF0–SOF15, excluding the DHT/JPG/DAC markers that share the range.
        static bool IsStartOfFrame(byte marker) =>
            marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
    }

    /// <summary>Reads an SVG's intrinsic size from <c>width</c>/<c>height</c>, else its viewBox.</summary>
    private static (double Width, double Height)? SvgSize(byte[] header)
    {
        XElement? root;
        try
        {
            using var reader = XmlReader.Create(
                new MemoryStream(header),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            root = XElement.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (TryLength(root.Attribute("width")?.Value, out double width)
            && TryLength(root.Attribute("height")?.Value, out double height))
        {
            return (width, height);
        }

        string[] viewBox = (root.Attribute("viewBox")?.Value ?? string.Empty)
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return viewBox.Length == 4
                && TryLength(viewBox[2], out double boxWidth)
                && TryLength(viewBox[3], out double boxHeight)
            ? (boxWidth, boxHeight)
            : null;
    }

    /// <summary>Parses a CSS length, accepting a bare number or one suffixed with <c>px</c>.</summary>
    private static bool TryLength(string? value, out double length)
    {
        const string pixelSuffix = "px";

        length = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.EndsWith(pixelSuffix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^pixelSuffix.Length];
        }

        return double.TryParse(
            trimmed,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out length);
    }
}
