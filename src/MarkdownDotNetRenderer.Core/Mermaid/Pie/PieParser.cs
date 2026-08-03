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

namespace MarkdownDotNetRenderer.Mermaid.Pie;

/// <summary>
/// Line-oriented parser for the <c>pie</c> subset documented in
/// docs/phases/phase-4-additional-diagrams.md: a <c>pie</c> header optionally carrying
/// <c>showData</c> and/or <c>title …</c>, then one <c>"Label" : value</c> line per slice. A chart
/// with no usable slice, or with a non-positive total, fails so the block degrades to its verbatim
/// source instead of drawing an empty circle.
/// </summary>
public static class PieParser
{
    /// <summary>The diagram-type keyword this parser accepts.</summary>
    public const string HeaderKeyword = "pie";

    /// <summary>Slice-count guard; a longer chart fails rather than producing an unreadable legend.</summary>
    public const int MaxSlices = 64;

    /// <summary>Longest label kept; longer text is truncated so one line cannot blow up layout.</summary>
    public const int MaxLabelLength = 200;

    /// <summary>Parses a pie-chart source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static PieParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var slices = new List<PieSlice>();
        string? title = null;
        bool showData = false;
        bool headerSeen = false;
        bool inDirective = false;

        foreach (string rawLine in MermaidRenderer.Normalize(mermaidSource).Split('\n'))
        {
            string line = MermaidLines.StripComment(rawLine).Trim();

            if (inDirective)
            {
                inDirective = !rawLine.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("%%{", StringComparison.Ordinal))
            {
                MermaidLines.ReportIgnored(diagnostics, reported, "directive", MermaidLines.DirectiveIgnored);
                inDirective = !line.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (!headerSeen)
            {
                if (!MermaidLines.FirstWord(line).Equals(HeaderKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new PieParseResult(
                        false, null, "The first statement is not a 'pie' header.", diagnostics);
                }

                headerSeen = true;
                string rest = line[HeaderKeyword.Length..].Trim();
                (showData, title) = ParseHeaderTail(rest, showData, title);
                continue;
            }

            if (MermaidLines.FirstWord(line).Equals("showData", StringComparison.OrdinalIgnoreCase))
            {
                showData = true;
                continue;
            }

            if (MermaidLines.FirstWord(line).Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                string text = line["title".Length..].Trim();
                if (text.Length > 0)
                {
                    title = MermaidLines.Truncate(MermaidLines.Unquote(text), MaxLabelLength);
                }

                continue;
            }

            if (TryParseSlice(line, slices.Count, out PieSlice? slice))
            {
                slices.Add(slice);
                continue;
            }

            MermaidLines.ReportIgnored(
                diagnostics,
                reported,
                "unknown-statement",
                "One or more pie-chart statements were not recognized and were ignored; the rest " +
                "of the chart is rendered.");
        }

        if (!headerSeen)
        {
            return new PieParseResult(
                false, null, "The mermaid block contains no 'pie' header.", diagnostics);
        }

        if (slices.Count == 0)
        {
            return new PieParseResult(
                false, null, "The pie chart declares no slices.", diagnostics);
        }

        if (slices.Count > MaxSlices)
        {
            return new PieParseResult(
                false,
                null,
                $"The pie chart has {slices.Count} slices, above the limit of {MaxSlices}.",
                diagnostics);
        }

        var model = new PieModel(title, slices, showData);
        return model.Total <= 0
            ? new PieParseResult(
                false, null, "The pie chart's slice values sum to zero.", diagnostics)
            : new PieParseResult(true, model, null, diagnostics);
    }

    /// <summary>Reads the optional <c>showData</c> and <c>title …</c> tail of the header line.</summary>
    private static (bool ShowData, string? Title) ParseHeaderTail(
        string rest,
        bool showData,
        string? title)
    {
        if (rest.StartsWith("showData", StringComparison.OrdinalIgnoreCase))
        {
            showData = true;
            rest = rest["showData".Length..].Trim();
        }

        if (rest.StartsWith("title", StringComparison.OrdinalIgnoreCase))
        {
            string text = rest["title".Length..].Trim();
            if (text.Length > 0)
            {
                title = MermaidLines.Truncate(MermaidLines.Unquote(text), MaxLabelLength);
            }
        }

        return (showData, title);
    }

    /// <summary>Parses one <c>"Label" : value</c> slice line.</summary>
    /// <param name="line">The statement line.</param>
    /// <param name="order">The slice's 0-based source index.</param>
    /// <param name="slice">The parsed slice, when the line is one.</param>
    /// <returns>Whether the line was a slice with a positive value.</returns>
    public static bool TryParseSlice(string line, int order, out PieSlice slice)
    {
        ArgumentNullException.ThrowIfNull(line);

        slice = new PieSlice(string.Empty, 0, order);
        int separator = line.LastIndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        string label = MermaidLines.Unquote(line[..separator].Trim());
        string value = line[(separator + 1)..].Trim();
        if (label.Length == 0 ||
            !double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) ||
            !double.IsFinite(parsed) ||
            parsed <= 0)
        {
            return false;
        }

        slice = new PieSlice(MermaidLines.Truncate(label, MaxLabelLength), parsed, order);
        return true;
    }
}
