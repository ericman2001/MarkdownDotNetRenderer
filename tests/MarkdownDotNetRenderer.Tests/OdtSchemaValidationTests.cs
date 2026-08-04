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

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Commons.Xml.Relaxng;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Area 8 of docs/07-testing-strategy.md: proves the ODT package is not merely well-formed XML but
/// <em>conformant</em> to the OASIS OpenDocument v1.3 grammar. Each XML part is validated against
/// the official RelaxNG schema (<c>content.xml</c>, <c>styles.xml</c>, <c>meta.xml</c> against the
/// ODF schema; <c>META-INF/manifest.xml</c> against the manifest schema), closing the gap between
/// "well-formed" and "conformant" that let Word offer to recover — and then crash on — the output.
///
/// RelaxNG validation is not built into .NET, so the tests use Mono's managed
/// <c>Commons.Xml.Relaxng</c> validator (the <c>RelaxNG</c> package). The grammars live under
/// <c>Schemas/</c> and are copied next to the test binary by the project file.
/// </summary>
public sealed class OdtSchemaValidationTests
{
    private const string OdfSchema = "OpenDocument-v1.3-schema.rng";
    private const string ManifestSchema = "OpenDocument-v1.3-manifest-schema.rng";

    /// <summary>Compiled grammars are reused across parts; compiling the 600 KB ODF grammar is slow.</summary>
    private static readonly ConcurrentDictionary<string, RelaxngPattern> Grammars = new(StringComparer.Ordinal);

    [Fact]
    public Task Repository_Readme_Validates_Against_The_Odf_1_3_Grammar() =>
        AssertPackageIsConformantAsync(
            File.ReadAllTextAsync(Path.Combine(TestFiles.RepositoryRoot, "README.md")));

    [Fact]
    public Task Kitchen_Sink_Sample_Validates_Against_The_Odf_1_3_Grammar() =>
        AssertPackageIsConformantAsync(
            File.ReadAllTextAsync(TestFiles.Sample("kitchen-sink.md")));

    private static async Task AssertPackageIsConformantAsync(Task<string> markdown)
    {
        byte[] package = await RenderAsync(await markdown);

        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        foreach ((string part, string schema) in new[]
        {
            ("content.xml", OdfSchema),
            ("styles.xml", OdfSchema),
            ("meta.xml", OdfSchema),
            ("META-INF/manifest.xml", ManifestSchema),
        })
        {
            string xml = ReadEntry(archive, part);
            string? error = Validate(xml, schema);
            Assert.True(
                error is null,
                $"{part} is not valid OpenDocument 1.3 (validated against {schema}): {error}");
        }
    }

    /// <summary>Validates <paramref name="xml"/> against a grammar; returns null when conformant.</summary>
    private static string? Validate(string xml, string schema)
    {
        RelaxngPattern grammar = Grammars.GetOrAdd(schema, Compile);

        using var document = XmlReader.Create(new StringReader(xml));
        using var validating = new RelaxngValidatingReader(document, grammar);
        try
        {
            while (validating.Read())
            {
            }

            return null;
        }
        catch (RelaxngException ex)
        {
            return ex.Message;
        }
    }

    private static RelaxngPattern Compile(string schema)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Schemas", schema);
        using XmlReader reader = XmlReader.Create(path);
        RelaxngPattern grammar = RelaxngPattern.Read(reader);
        grammar.Compile();
        return grammar;
    }

    private static string ReadEntry(ZipArchive archive, string part)
    {
        ZipArchiveEntry entry = archive.GetEntry(part)
            ?? throw new InvalidOperationException($"The ODT package is missing '{part}'.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<byte[]> RenderAsync(string markdown)
    {
        var renderer = new MarkdownRenderer();
        RenderResult result = await renderer.RenderAsync(markdown, RenderOptions.Odt);
        return result.Content.ToArray();
    }
}
