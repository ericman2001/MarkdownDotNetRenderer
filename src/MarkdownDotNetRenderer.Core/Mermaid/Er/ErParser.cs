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

using MarkdownDotNetRenderer.Mermaid.Flowchart;

namespace MarkdownDotNetRenderer.Mermaid.Er;

/// <summary>
/// Line-oriented parser for the <c>erDiagram</c> subset documented in
/// docs/phases/phase-4-additional-diagrams.md: relationship statements such as
/// <c>CUSTOMER ||--o{ ORDER : places</c> with all four crow's-foot cardinality pairs, plus optional
/// <c>ENTITY { type name PK }</c> attribute blocks.
/// </summary>
public static class ErParser
{
    /// <summary>The diagram-type keyword.</summary>
    public const string HeaderKeyword = "erDiagram";

    /// <summary>Entity-count guard; a larger diagram fails instead of laying out.</summary>
    public const int MaxEntities = 120;

    /// <summary>Longest name, attribute row, or label kept.</summary>
    public const int MaxTextLength = 200;

    /// <summary>Most attribute rows kept per entity.</summary>
    public const int MaxAttributesPerEntity = 40;

    /// <summary>The identifying-relationship line token.</summary>
    private const string IdentifyingToken = "--";

    /// <summary>The non-identifying-relationship line token, drawn dashed.</summary>
    private const string NonIdentifyingToken = "..";

    /// <summary>Parses an ER-diagram source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static ErParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        var attributes = new Dictionary<string, List<ErAttribute>>(StringComparer.Ordinal);
        var relationships = new List<ErRelationship>();
        bool headerSeen = false;
        bool inDirective = false;
        string? openEntity = null;

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
                MermaidLines.ReportIgnored(
                    diagnostics, reported, "directive", MermaidLines.DirectiveIgnored);
                inDirective = !line.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (!headerSeen)
            {
                if (!MermaidLines.FirstWord(line)
                    .Equals(HeaderKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new ErParseResult(
                        false,
                        null,
                        "The first statement is not an 'erDiagram' header.",
                        diagnostics);
                }

                headerSeen = true;
                continue;
            }

            if (openEntity is not null)
            {
                if (line.StartsWith('}'))
                {
                    openEntity = null;
                    continue;
                }

                AddAttribute(openEntity, line);
                continue;
            }

            if (TryParseRelationship(line, out ErRelationship? relationship))
            {
                Ensure(relationship.LeftId);
                Ensure(relationship.RightId);
                relationships.Add(relationship);
                continue;
            }

            if (line.EndsWith('{'))
            {
                string name = EntityName(line[..^1]);
                if (name.Length > 0)
                {
                    Ensure(name);
                    openEntity = name;
                    continue;
                }
            }

            if (!line.Contains(' ', StringComparison.Ordinal) && line != "}")
            {
                Ensure(EntityName(line));
                continue;
            }

            if (line != "}")
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "unknown-statement",
                    "One or more ER-diagram statements were not recognized and were ignored; the " +
                    "rest of the diagram is rendered.");
            }
        }

        if (!headerSeen)
        {
            return new ErParseResult(
                false, null, "The mermaid block contains no 'erDiagram' header.", diagnostics);
        }

        if (order.Count == 0)
        {
            return new ErParseResult(
                false, null, "The ER diagram declares no entities.", diagnostics);
        }

        if (order.Count > MaxEntities)
        {
            return new ErParseResult(
                false,
                null,
                $"The ER diagram has {order.Count} entities, above the limit of {MaxEntities}.",
                diagnostics);
        }

        var entities = new List<ErEntity>(order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            entities.Add(new ErEntity(order[i], attributes[order[i]], i));
        }

        return new ErParseResult(
            true,
            new ErDiagramModel(FlowDirection.TopDown, entities, relationships),
            null,
            diagnostics);

        void Ensure(string name)
        {
            if (name.Length == 0 || attributes.ContainsKey(name))
            {
                return;
            }

            attributes[name] = [];
            order.Add(name);
        }

        void AddAttribute(string entity, string text)
        {
            List<ErAttribute> rows = attributes[entity];
            if (rows.Count >= MaxAttributesPerEntity)
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "attribute-limit",
                    $"Entities are limited to {MaxAttributesPerEntity} attributes; the extra rows " +
                    "were not rendered.");
                return;
            }

            // 'string name PK "the comment"' keeps its declaration and drops the trailing comment.
            int quote = text.IndexOf('"', StringComparison.Ordinal);
            string row = (quote >= 0 ? text[..quote] : text).Trim();
            if (row.Length > 0)
            {
                rows.Add(new ErAttribute(MermaidLines.Truncate(row, MaxTextLength)));
            }
        }

        static string EntityName(string raw) =>
            MermaidLines.Truncate(MermaidLines.Unquote(raw).Trim(), MaxTextLength);
    }

    /// <summary>Parses one relationship statement.</summary>
    /// <param name="line">The statement line.</param>
    /// <param name="relationship">The parsed relationship, when the line is one.</param>
    /// <returns>Whether the line was a relationship.</returns>
    public static bool TryParseRelationship(string line, out ErRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(line);

        relationship = new ErRelationship(
            string.Empty,
            string.Empty,
            ErCardinality.ExactlyOne,
            ErCardinality.ExactlyOne,
            null,
            true);

        string statement = line;
        string? label = null;
        int colon = statement.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            label = MermaidLines.Truncate(
                MermaidLines.Unquote(statement[(colon + 1)..].Trim()), MaxTextLength);
            statement = statement[..colon];
            if (label.Length == 0)
            {
                label = null;
            }
        }

        int index = statement.IndexOf(IdentifyingToken, StringComparison.Ordinal);
        bool identifying = index >= 0;
        if (!identifying)
        {
            index = statement.IndexOf(NonIdentifyingToken, StringComparison.Ordinal);
        }

        if (index < 2 || index + 2 + 2 > statement.Length)
        {
            return false;
        }

        string left = statement[..index];
        string right = statement[(index + 2)..];
        if (left.Length < 2 || right.Length < 2)
        {
            return false;
        }

        if (!TryParseLeftCardinality(left[^2..], out ErCardinality leftCardinality) ||
            !TryParseRightCardinality(right[..2], out ErCardinality rightCardinality))
        {
            return false;
        }

        string leftName = MermaidLines.Truncate(
            MermaidLines.Unquote(left[..^2].Trim()), MaxTextLength);
        string rightName = MermaidLines.Truncate(
            MermaidLines.Unquote(right[2..].Trim()), MaxTextLength);
        if (leftName.Length == 0 || rightName.Length == 0 ||
            leftName.Contains(' ', StringComparison.Ordinal) ||
            rightName.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        relationship = new ErRelationship(
            leftName, rightName, leftCardinality, rightCardinality, label, identifying);
        return true;
    }

    /// <summary>Maps the two glyph characters written left of the line.</summary>
    /// <param name="token">The two characters.</param>
    /// <param name="cardinality">The mapped cardinality.</param>
    /// <returns>Whether the token is a valid left-hand cardinality.</returns>
    public static bool TryParseLeftCardinality(string token, out ErCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(token);

        switch (token)
        {
            case "||":
                cardinality = ErCardinality.ExactlyOne;
                return true;
            case "|o":
                cardinality = ErCardinality.ZeroOrOne;
                return true;
            case "}|":
                cardinality = ErCardinality.OneOrMany;
                return true;
            case "}o":
                cardinality = ErCardinality.ZeroOrMany;
                return true;
            default:
                cardinality = ErCardinality.ExactlyOne;
                return false;
        }
    }

    /// <summary>Maps the two glyph characters written right of the line.</summary>
    /// <param name="token">The two characters.</param>
    /// <param name="cardinality">The mapped cardinality.</param>
    /// <returns>Whether the token is a valid right-hand cardinality.</returns>
    public static bool TryParseRightCardinality(string token, out ErCardinality cardinality)
    {
        ArgumentNullException.ThrowIfNull(token);

        switch (token)
        {
            case "||":
                cardinality = ErCardinality.ExactlyOne;
                return true;
            case "o|":
                cardinality = ErCardinality.ZeroOrOne;
                return true;
            case "|{":
                cardinality = ErCardinality.OneOrMany;
                return true;
            case "o{":
                cardinality = ErCardinality.ZeroOrMany;
                return true;
            default:
                cardinality = ErCardinality.ExactlyOne;
                return false;
        }
    }
}
