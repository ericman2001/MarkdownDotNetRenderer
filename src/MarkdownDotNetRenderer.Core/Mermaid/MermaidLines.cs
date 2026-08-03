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

/// <summary>
/// The line-level chores every mermaid parser repeats: stripping <c>%%</c> comments, taking a
/// statement's keyword, unquoting a label, capping a label's length, and reporting an ignored
/// construct at most once. Shared so each phase-4 diagram type adds a parser, not a copy of these
/// (docs/phases/phase-4-additional-diagrams.md, standing rule "reuse, don't fork").
/// </summary>
public static class MermaidLines
{
    /// <summary>The message reported when a <c>%%{ … }%%</c> directive block is skipped.</summary>
    public const string DirectiveIgnored =
        "A mermaid directive (%%{ … }%%) was ignored; the diagram is rendered with the built-in " +
        "theme.";

    /// <summary>Characters that end a statement's leading keyword.</summary>
    private static readonly char[] KeywordTerminators = [' ', '\t', ';', ':'];

    /// <summary>Removes a trailing <c>%%</c> comment, leaving a directive opener intact.</summary>
    /// <param name="line">The raw line.</param>
    /// <returns>The line without its comment.</returns>
    public static string StripComment(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        int index = line.IndexOf("%%", StringComparison.Ordinal);
        return index < 0 || line.TrimStart().StartsWith("%%{", StringComparison.Ordinal)
            ? line
            : line[..index];
    }

    /// <summary>The statement's leading keyword, cut at whitespace or <c>;</c>/<c>:</c>.</summary>
    /// <param name="line">The trimmed statement line.</param>
    /// <returns>The keyword, possibly the whole line.</returns>
    public static string FirstWord(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        int index = line.IndexOfAny(KeywordTerminators);
        return index < 0 ? line : line[..index];
    }

    /// <summary>Strips one pair of surrounding double quotes, if present.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The unquoted, trimmed value.</returns>
    public static string Unquote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string text = value.Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1].Trim() : text;
    }

    /// <summary>Caps a label's length so one pathological line cannot blow up layout.</summary>
    /// <param name="value">The label.</param>
    /// <param name="maxLength">The maximum length to keep.</param>
    /// <returns>The capped label.</returns>
    public static string Truncate(string value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>Records one <c>MERMAID003</c> per distinct <paramref name="key"/>.</summary>
    /// <param name="diagnostics">The diagnostic list to append to.</param>
    /// <param name="reported">Keys already reported.</param>
    /// <param name="key">The construct's key.</param>
    /// <param name="message">The message to report the first time.</param>
    public static void ReportIgnored(
        IList<RenderDiagnostic> diagnostics,
        ISet<string> reported,
        string key,
        string message)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(reported);

        if (reported.Add(key))
        {
            diagnostics.Add(new RenderDiagnostic(
                DiagnosticSeverity.Info, RenderDiagnostic.IgnoredDiagramFeature, message));
        }
    }
}
