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

namespace MarkdownDotNetRenderer.Mermaid.Class;

/// <summary>
/// Line-oriented parser for the <c>classDiagram</c> subset documented in
/// docs/phases/phase-4-additional-diagrams.md: <c>class Foo { … }</c> blocks, the
/// <c>Foo : +member()</c> shorthand, <c>&lt;&lt;interface&gt;&gt;</c> annotations, the six relation
/// operators with optional labels and quoted cardinalities, and <c>direction</c>. Namespaces,
/// <c>click</c>, and styling are reported once each as <c>MERMAID003</c> and skipped.
/// </summary>
public static class ClassParser
{
    /// <summary>The diagram-type keyword.</summary>
    public const string HeaderKeyword = "classDiagram";

    /// <summary>The v2 spelling, which renders identically here.</summary>
    public const string HeaderKeywordV2 = "classDiagram-v2";

    /// <summary>Class-count guard; a larger diagram fails instead of laying out.</summary>
    public const int MaxClasses = 120;

    /// <summary>Longest name, member, or label kept.</summary>
    public const int MaxTextLength = 200;

    /// <summary>Most members kept per class.</summary>
    public const int MaxMembersPerClass = 40;

    /// <summary>
    /// The relation operators, longest first so that <c>&lt;|--</c> is never mistaken for
    /// <c>--</c>. <c>SwapEnds</c> marks the spellings written right-to-left, which are normalized
    /// so the marker-carrying end is always the relation's source.
    /// </summary>
    private static readonly (string Token, ClassRelationKind Kind, bool SwapEnds)[] Operators =
    [
        ("<|--", ClassRelationKind.Inheritance, false),
        ("--|>", ClassRelationKind.Inheritance, true),
        ("<|..", ClassRelationKind.Realization, false),
        ("..|>", ClassRelationKind.Realization, true),
        ("*--", ClassRelationKind.Composition, false),
        ("--*", ClassRelationKind.Composition, true),
        ("o--", ClassRelationKind.Aggregation, false),
        ("--o", ClassRelationKind.Aggregation, true),
        ("-->", ClassRelationKind.Association, false),
        ("<--", ClassRelationKind.Association, true),
        ("..>", ClassRelationKind.Dependency, false),
        ("<..", ClassRelationKind.Dependency, true),
        ("--", ClassRelationKind.Link, false),
        ("..", ClassRelationKind.Link, false),
    ];

    /// <summary>Statements recognized but deliberately not rendered.</summary>
    private static readonly HashSet<string> IgnoredKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "namespace",
            "click",
            "style",
            "classDef",
            "cssClass",
            "note",
            "callback",
            "link",
            "accTitle",
            "accDescr",
        };

    /// <summary>Whether a diagram-type keyword names a class diagram.</summary>
    /// <param name="keyword">The first word of the first statement.</param>
    /// <returns>Whether this parser handles it.</returns>
    public static bool IsHeader(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        return keyword.Equals(HeaderKeyword, StringComparison.OrdinalIgnoreCase) ||
            keyword.Equals(HeaderKeywordV2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses a class-diagram source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static ClassParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        var byName = new Dictionary<string, ClassBuilder>(StringComparer.Ordinal);
        var relations = new List<ClassRelation>();
        FlowDirection direction = FlowDirection.TopDown;
        bool headerSeen = false;
        bool inDirective = false;
        string? openClass = null;

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
                if (!IsHeader(MermaidLines.FirstWord(line)))
                {
                    return new ClassParseResult(
                        false,
                        null,
                        "The first statement is not a 'classDiagram' header.",
                        diagnostics);
                }

                headerSeen = true;
                continue;
            }

            if (openClass is not null)
            {
                if (line.StartsWith('}'))
                {
                    openClass = null;
                    continue;
                }

                AddMember(openClass, line);
                continue;
            }

            string keyword = MermaidLines.FirstWord(line);

            if (keyword.Equals("direction", StringComparison.OrdinalIgnoreCase))
            {
                direction = ParseDirection(line, diagnostics, reported, direction);
                continue;
            }

            if (line == "}")
            {
                continue;
            }

            if (TryParseRelation(line, out ClassRelation? relation))
            {
                Ensure(relation.SourceId);
                Ensure(relation.TargetId);
                relations.Add(relation);
                continue;
            }

            if (IgnoredKeywords.Contains(keyword))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    keyword,
                    $"Class-diagram statement '{keyword}' is not supported and was ignored.");
                continue;
            }

            if (keyword.Equals("class", StringComparison.OrdinalIgnoreCase))
            {
                string rest = line[keyword.Length..].Trim();
                bool opensBlock = rest.EndsWith('{');
                if (opensBlock)
                {
                    rest = rest[..^1].Trim();
                }

                string name = ClassName(rest);
                if (name.Length == 0)
                {
                    continue;
                }

                Ensure(name);
                if (opensBlock)
                {
                    openClass = name;
                }

                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                string name = ClassName(line[..colon].Trim());
                if (name.Length > 0)
                {
                    Ensure(name);
                    AddMember(name, line[(colon + 1)..].Trim());
                    continue;
                }
            }

            MermaidLines.ReportIgnored(
                diagnostics,
                reported,
                "unknown-statement",
                "One or more class-diagram statements were not recognized and were ignored; the " +
                "rest of the diagram is rendered.");
        }

        if (!headerSeen)
        {
            return new ClassParseResult(
                false, null, "The mermaid block contains no 'classDiagram' header.", diagnostics);
        }

        if (order.Count == 0)
        {
            return new ClassParseResult(
                false, null, "The class diagram declares no classes.", diagnostics);
        }

        if (order.Count > MaxClasses)
        {
            return new ClassParseResult(
                false,
                null,
                $"The class diagram has {order.Count} classes, above the limit of {MaxClasses}.",
                diagnostics);
        }

        var classes = new List<ClassDefinition>(order.Count);
        for (int i = 0; i < order.Count; i++)
        {
            ClassBuilder builder = byName[order[i]];
            classes.Add(new ClassDefinition(
                builder.Name, builder.Annotation, builder.Members, i));
        }

        return new ClassParseResult(
            true,
            new ClassDiagramModel(direction, classes, relations),
            null,
            diagnostics);

        void Ensure(string name)
        {
            if (byName.ContainsKey(name))
            {
                return;
            }

            byName[name] = new ClassBuilder(name);
            order.Add(name);
        }

        void AddMember(string className, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            Ensure(className);
            ClassBuilder builder = byName[className];

            if (text.StartsWith("<<", StringComparison.Ordinal))
            {
                builder.Annotation = MermaidLines.Truncate(text, MaxTextLength);
                return;
            }

            if (builder.Members.Count >= MaxMembersPerClass)
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "member-limit",
                    $"Classes are limited to {MaxMembersPerClass} members; the extra members were " +
                    "not rendered.");
                return;
            }

            builder.Members.Add(new ClassMember(
                MermaidLines.Truncate(text, MaxTextLength),
                text.Contains('(', StringComparison.Ordinal)));
        }

        // 'class Foo~T~' and 'class Foo["label"]' keep only the plain name; generics stay as text.
        string ClassName(string raw)
        {
            string name = MermaidLines.Unquote(raw);
            int bracket = name.IndexOf('[', StringComparison.Ordinal);
            if (bracket > 0)
            {
                name = name[..bracket];
            }

            return MermaidLines.Truncate(name.Trim(), MaxTextLength);
        }
    }

    /// <summary>Parses one relation statement.</summary>
    /// <param name="line">The statement line.</param>
    /// <param name="relation">The normalized relation, when the line is one.</param>
    /// <returns>Whether the line was a relation.</returns>
    public static bool TryParseRelation(string line, out ClassRelation relation)
    {
        ArgumentNullException.ThrowIfNull(line);

        relation = new ClassRelation(
            string.Empty, string.Empty, ClassRelationKind.Link, null, null, null);

        string statement = line;
        string? label = null;
        int colon = statement.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            label = MermaidLines.Truncate(statement[(colon + 1)..].Trim(), MaxTextLength);
            statement = statement[..colon];
            if (label.Length == 0)
            {
                label = null;
            }
        }

        foreach ((string token, ClassRelationKind kind, bool swap) in Operators)
        {
            int index = statement.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            string leftText = statement[..index].Trim();
            string rightText = statement[(index + token.Length)..].Trim();

            (string leftName, string? leftCardinality) = SplitCardinality(leftText, trailing: true);
            (string rightName, string? rightCardinality) =
                SplitCardinality(rightText, trailing: false);

            if (leftName.Length == 0 || rightName.Length == 0)
            {
                return false;
            }

            relation = swap
                ? new ClassRelation(
                    rightName, leftName, kind, label, rightCardinality, leftCardinality)
                : new ClassRelation(
                    leftName, rightName, kind, label, leftCardinality, rightCardinality);
            return true;
        }

        return false;
    }

    /// <summary>Splits a quoted cardinality away from the class name beside an operator.</summary>
    /// <param name="text">The text on one side of the operator.</param>
    /// <param name="trailing">Whether the cardinality would follow the name.</param>
    /// <returns>The name and the cardinality, if any.</returns>
    private static (string Name, string? Cardinality) SplitCardinality(string text, bool trailing)
    {
        string value = text.Trim();
        if (value.Length == 0)
        {
            return (value, null);
        }

        if (trailing && value.EndsWith('"'))
        {
            int open = value.LastIndexOf('"', value.Length - 2);
            if (open >= 0)
            {
                return (
                    MermaidLines.Truncate(value[..open].Trim(), MaxTextLength),
                    MermaidLines.Truncate(value[(open + 1)..^1].Trim(), MaxTextLength));
            }
        }

        if (!trailing && value.StartsWith('"'))
        {
            int close = value.IndexOf('"', 1);
            if (close > 0)
            {
                return (
                    MermaidLines.Truncate(value[(close + 1)..].Trim(), MaxTextLength),
                    MermaidLines.Truncate(value[1..close].Trim(), MaxTextLength));
            }
        }

        return (MermaidLines.Truncate(value, MaxTextLength), null);
    }

    private static FlowDirection ParseDirection(
        string line,
        IList<RenderDiagnostic> diagnostics,
        ISet<string> reported,
        FlowDirection current)
    {
        string value = line["direction".Length..].Trim();
        switch (value.ToUpperInvariant())
        {
            case "TD":
            case "TB":
            case "BT":
                return FlowDirection.TopDown;

            case "LR":
            case "RL":
                return FlowDirection.LeftRight;

            default:
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "direction-unknown",
                    $"Direction '{value}' is not recognized; the diagram keeps its current " +
                    "direction.");
                return current;
        }
    }

    /// <summary>Accumulates a class's members while its statements are being read.</summary>
    private sealed class ClassBuilder(string name)
    {
        public string Name { get; } = name;

        public string? Annotation { get; set; }

        public List<ClassMember> Members { get; } = [];
    }
}
