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

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MarkdownDotNetRenderer.Mermaid.Flowchart;

/// <summary>
/// Line-oriented parser for the phase-1 <c>flowchart</c>/<c>graph</c> syntax subset documented in
/// docs/04-mermaid-engine.md. Unknown constructs are ignored with an informational diagnostic and
/// never fail the parse; only a source that is not a flowchart at all, or a statement with a
/// missing endpoint, fails.
/// </summary>
public static class FlowchartParser
{
    private const int MaxLabelLength = 400;

    // "A -- text --> B": an opening link run, a label, then the closing run and optional arrow.
    private static readonly Regex LabelledLinkPattern = new(
        @"\G(?<run1>-{2,}|={2,}|-\.{1,3}-)\s+(?<label>\S.*?)\s+(?<run2>-{2,}|={2,}|-\.{1,3}-)(?<arrow>>|[ox](?=[\s\[({]|$))?",
        RegexOptions.CultureInvariant);

    // "A --> B", "A --- B", "A -->|text| B", plus the dotted/thick variants.
    private static readonly Regex PlainLinkPattern = new(
        @"\G(?<run>-{2,}|={2,}|-\.{1,3}-)(?<arrow>>|[ox](?=[\s\[({]|$))?(?:\|(?<label>[^|]*)\|)?",
        RegexOptions.CultureInvariant);

    private static readonly string[] IgnoredKeywords =
    [
        "subgraph", "end", "classdef", "class", "style", "click", "linkstyle", "direction",
    ];

    /// <summary>Parses a flowchart source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static FlowchartParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, FlowNode>(StringComparer.Ordinal);
        var order = new List<string>();
        var edges = new List<FlowEdge>();

        string[] lines = MermaidRenderer.Normalize(mermaidSource).Split('\n');
        FlowDirection? direction = null;
        bool inDirective = false;

        foreach (string rawLine in lines)
        {
            string line = StripComment(rawLine).Trim();
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
                Report(diagnostics, reported, "directive",
                    "A mermaid directive (%%{ … }%%) was ignored; the diagram is rendered with " +
                    "the built-in theme.");
                inDirective = !line.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            // The header may share its line with statements, as in 'flowchart TD; A --> B'.
            foreach (string statement in SplitStatements(line))
            {
                if (direction is null)
                {
                    if (!TryParseHeader(statement, diagnostics, reported, out FlowDirection parsed))
                    {
                        return new FlowchartParseResult(
                            false,
                            null,
                            "The first statement is not a 'flowchart' or 'graph' header.",
                            diagnostics);
                    }

                    direction = parsed;
                    continue;
                }

                if (!ParseStatement(statement, nodes, order, edges, diagnostics, reported,
                        out string? failure))
                {
                    return new FlowchartParseResult(false, null, failure, diagnostics);
                }
            }
        }

        if (direction is null)
        {
            return new FlowchartParseResult(
                false, null, "The mermaid block contains no flowchart header.", diagnostics);
        }

        if (order.Count == 0)
        {
            return new FlowchartParseResult(
                false, null, "The flowchart declares no nodes.", diagnostics);
        }

        var model = new FlowchartModel(
            direction.Value,
            order.Select(id => nodes[id]).ToArray(),
            edges);
        return new FlowchartParseResult(true, model, null, diagnostics);
    }

    private static bool TryParseHeader(
        string line,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported,
        out FlowDirection direction)
    {
        direction = FlowDirection.TopDown;

        string header = line.TrimEnd(';').Trim();
        string[] parts = header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        string keyword = parts[0].TrimEnd(';');
        if (!keyword.Equals("flowchart", StringComparison.OrdinalIgnoreCase) &&
            !keyword.Equals("graph", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        string token = parts[1].TrimEnd(';');
        switch (token.ToUpperInvariant())
        {
            case "TD":
            case "TB":
                direction = FlowDirection.TopDown;
                return true;
            case "LR":
                direction = FlowDirection.LeftRight;
                return true;
            case "BT":
                direction = FlowDirection.TopDown;
                Report(diagnostics, reported, "direction-bt",
                    "Direction 'BT' is rendered top-down in this phase.");
                return true;
            case "RL":
                direction = FlowDirection.LeftRight;
                Report(diagnostics, reported, "direction-rl",
                    "Direction 'RL' is rendered left-to-right in this phase.");
                return true;
            default:
                Report(diagnostics, reported, "direction-unknown",
                    $"Unrecognized flowchart direction '{token}'; rendering top-down.");
                return true;
        }
    }

    private static bool ParseStatement(
        string statement,
        Dictionary<string, FlowNode> nodes,
        List<string> order,
        List<FlowEdge> edges,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported,
        [NotNullWhen(false)] out string? failure)
    {
        failure = null;
        string text = statement.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        string firstWord = FirstWord(text);
        foreach (string keyword in IgnoredKeywords)
        {
            if (!firstWord.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!keyword.Equals("end", StringComparison.OrdinalIgnoreCase) &&
                !keyword.Equals("direction", StringComparison.OrdinalIgnoreCase))
            {
                Report(diagnostics, reported, keyword,
                    $"'{firstWord}' is not supported in this phase and was ignored; the rest of " +
                    "the diagram was rendered.");
            }

            return true;
        }

        // Split the statement into node groups separated by link operators.
        var groups = new List<string>();
        var links = new List<(string? Label, bool Directed)>();
        int position = 0;
        int segmentStart = 0;
        int depth = 0;

        while (position < text.Length)
        {
            char c = text[position];
            if (c is '[' or '(' or '{')
            {
                depth++;
                position++;
                continue;
            }

            if (c is ']' or ')' or '}')
            {
                depth = Math.Max(0, depth - 1);
                position++;
                continue;
            }

            if (depth == 0 && (c is '-' or '='))
            {
                Match match = LabelledLinkPattern.Match(text, position);
                bool labelled = match.Success && match.Index == position;
                if (!labelled)
                {
                    match = PlainLinkPattern.Match(text, position);
                }

                if (match.Success && match.Index == position && match.Length > 0)
                {
                    groups.Add(text[segmentStart..position]);
                    string? label = match.Groups["label"].Success
                        ? match.Groups["label"].Value.Trim()
                        : null;
                    bool directed = match.Groups["arrow"].Success &&
                        match.Groups["arrow"].Value.Length > 0;
                    if (labelled)
                    {
                        directed = match.Groups["arrow"].Value.Length > 0;
                    }

                    string run = labelled ? match.Groups["run2"].Value : match.Groups["run"].Value;
                    if (run.Contains('=', StringComparison.Ordinal) ||
                        run.Contains('.', StringComparison.Ordinal))
                    {
                        Report(diagnostics, reported, "link-style",
                            "Thick and dotted link styles are drawn as plain links in this phase.");
                    }

                    links.Add((string.IsNullOrEmpty(label) ? null : label, directed));
                    position += match.Length;
                    segmentStart = position;
                    continue;
                }
            }

            position++;
        }

        groups.Add(text[segmentStart..]);

        var parsedGroups = new List<List<FlowNode>>(groups.Count);
        foreach (string group in groups)
        {
            List<FlowNode> groupNodes = ParseNodeGroup(group, nodes, order, diagnostics, reported);
            if (groupNodes.Count == 0)
            {
                failure = links.Count == 0
                    ? $"Statement '{text}' declares no node."
                    : $"Statement '{text}' has a link with a missing endpoint.";
                return false;
            }

            parsedGroups.Add(groupNodes);
        }

        for (int i = 0; i < links.Count; i++)
        {
            (string? label, bool directed) = links[i];
            foreach (FlowNode source in parsedGroups[i])
            {
                foreach (FlowNode target in parsedGroups[i + 1])
                {
                    edges.Add(new FlowEdge(source.Id, target.Id, label, directed));
                }
            }
        }

        return true;
    }

    private static List<FlowNode> ParseNodeGroup(
        string group,
        Dictionary<string, FlowNode> nodes,
        List<string> order,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        var result = new List<FlowNode>();
        foreach (string spec in SplitOnAmpersand(group, diagnostics, reported))
        {
            FlowNode? node = ParseNodeSpec(spec, nodes, order);
            if (node is not null)
            {
                result.Add(node);
            }
        }

        return result;
    }

    private static FlowNode? ParseNodeSpec(
        string spec,
        Dictionary<string, FlowNode> nodes,
        List<string> order)
    {
        string text = spec.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        int idEnd = 0;
        while (idEnd < text.Length &&
               !char.IsWhiteSpace(text[idEnd]) &&
               text[idEnd] is not ('[' or '(' or '{'))
        {
            idEnd++;
        }

        string id = text[..idEnd];
        if (id.Length == 0)
        {
            return null;
        }

        string? label = null;
        FlowNodeShape? shape = null;
        string rest = text[idEnd..].TrimStart();
        if (rest.Length > 0)
        {
            (string Open, string Close, FlowNodeShape Shape)[] shapes =
            [
                ("([", "])", FlowNodeShape.Stadium),
                ("[[", "]]", FlowNodeShape.Rectangle),
                ("((", "))", FlowNodeShape.Stadium),
                ("{{", "}}", FlowNodeShape.Rhombus),
                ("[", "]", FlowNodeShape.Rectangle),
                ("(", ")", FlowNodeShape.Rounded),
                ("{", "}", FlowNodeShape.Rhombus),
            ];

            foreach ((string open, string close, FlowNodeShape candidate) in shapes)
            {
                if (!rest.StartsWith(open, StringComparison.Ordinal))
                {
                    continue;
                }

                int closeIndex = rest.LastIndexOf(close, StringComparison.Ordinal);
                if (closeIndex <= open.Length - 1)
                {
                    continue;
                }

                label = CleanLabel(rest[open.Length..closeIndex]);
                shape = candidate;
                break;
            }
        }

        if (nodes.TryGetValue(id, out FlowNode? existing))
        {
            if (label is null && shape is null)
            {
                return existing;
            }

            FlowNode updated = existing with
            {
                Label = label ?? existing.Label,
                Shape = shape ?? existing.Shape,
            };
            nodes[id] = updated;
            return updated;
        }

        var node = new FlowNode(id, label ?? id, shape ?? FlowNodeShape.Rectangle, order.Count);
        nodes[id] = node;
        order.Add(id);
        return node;
    }

    private static IEnumerable<string> SplitOnAmpersand(
        string group,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < group.Length; i++)
        {
            char c = group[i];
            if (c is '[' or '(' or '{')
            {
                depth++;
            }
            else if (c is ']' or ')' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == '&' && depth == 0)
            {
                parts.Add(group[start..i]);
                start = i + 1;
            }
        }

        parts.Add(group[start..]);
        if (parts.Count > 1)
        {
            Report(diagnostics, reported, "ampersand",
                "The '&' shorthand is expanded into individual nodes and links in this phase.");
        }

        return parts;
    }

    private static string CleanLabel(string raw)
    {
        string label = raw.Trim();
        if (label.Length >= 2 &&
            ((label[0] == '"' && label[^1] == '"') || (label[0] == '\'' && label[^1] == '\'')))
        {
            label = label[1..^1];
        }

        label = label
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
    }

    private static IEnumerable<string> SplitStatements(string line)
    {
        var statements = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c is '[' or '(' or '{')
            {
                depth++;
            }
            else if (c is ']' or ')' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ';' && depth == 0)
            {
                statements.Add(line[start..i]);
                start = i + 1;
            }
        }

        statements.Add(line[start..]);
        return statements;
    }

    private static string StripComment(string line)
    {
        if (line.TrimStart().StartsWith("%%{", StringComparison.Ordinal))
        {
            return line;
        }

        int depth = 0;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            char c = line[i];
            if (c is '[' or '(' or '{')
            {
                depth++;
            }
            else if (c is ']' or ')' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && c == '%' && line[i + 1] == '%')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string FirstWord(string text)
    {
        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return text[..end];
    }

    private static void Report(
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported,
        string key,
        string message)
    {
        if (reported.Add(key))
        {
            diagnostics.Add(new RenderDiagnostic(
                DiagnosticSeverity.Info, RenderDiagnostic.IgnoredDiagramFeature, message));
        }
    }
}
