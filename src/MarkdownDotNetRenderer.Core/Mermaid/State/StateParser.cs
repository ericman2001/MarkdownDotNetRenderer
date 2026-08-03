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
using MarkdownDotNetRenderer.Mermaid.Flowchart;

namespace MarkdownDotNetRenderer.Mermaid.State;

/// <summary>
/// Line-oriented parser for the <c>stateDiagram</c>/<c>stateDiagram-v2</c> subset documented in
/// docs/phases/phase-4-additional-diagrams.md: transitions (including the <c>[*]</c> start and end
/// pseudo-states), <c>state "Long name" as S</c> aliases, <c>direction</c>, and simple notes.
/// Composite states, concurrency, choice/fork/join, and styling directives are recognized, reported
/// once each as <c>MERMAID003</c>, and flattened away rather than failing the diagram.
/// </summary>
public static class StateParser
{
    /// <summary>The v1 diagram-type keyword.</summary>
    public const string HeaderKeyword = "stateDiagram";

    /// <summary>The v2 diagram-type keyword, which renders identically here.</summary>
    public const string HeaderKeywordV2 = "stateDiagram-v2";

    /// <summary>Synthetic id of the shared <c>[*]</c> start pseudo-state.</summary>
    public const string StartStateId = "__start";

    /// <summary>Synthetic id of the shared <c>[*]</c> end pseudo-state.</summary>
    public const string EndStateId = "__end";

    /// <summary>Vertex-count guard; a larger diagram fails instead of laying out.</summary>
    public const int MaxStates = 200;

    /// <summary>Longest label kept.</summary>
    public const int MaxLabelLength = 200;

    /// <summary>The transition operator; the only one the subset supports.</summary>
    private const string TransitionOperator = "-->";

    /// <summary>Whether a diagram-type keyword names a state diagram.</summary>
    /// <param name="keyword">The first word of the first statement.</param>
    /// <returns>Whether this parser handles it.</returns>
    public static bool IsHeader(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);

        return keyword.Equals(HeaderKeyword, StringComparison.OrdinalIgnoreCase) ||
            keyword.Equals(HeaderKeywordV2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses a state-diagram source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static StateParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<StateNode>();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        var transitions = new List<StateTransition>();
        FlowDirection direction = FlowDirection.TopDown;
        bool headerSeen = false;
        bool inDirective = false;
        int noteCount = 0;
        string? openNoteState = null;
        var openNoteText = new List<string>();

        foreach (string rawLine in MermaidRenderer.Normalize(mermaidSource).Split('\n'))
        {
            string line = MermaidLines.StripComment(rawLine).Trim();

            if (inDirective)
            {
                inDirective = !rawLine.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (openNoteState is not null)
            {
                if (line.Equals("end note", StringComparison.OrdinalIgnoreCase))
                {
                    AddNote(openNoteState, string.Join(' ', openNoteText));
                    openNoteState = null;
                    openNoteText.Clear();
                }
                else if (line.Length > 0)
                {
                    openNoteText.Add(line);
                }

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

            string keyword = MermaidLines.FirstWord(line);

            if (!headerSeen)
            {
                if (!IsHeader(keyword))
                {
                    return new StateParseResult(
                        false,
                        null,
                        "The first statement is not a 'stateDiagram' header.",
                        diagnostics);
                }

                headerSeen = true;
                continue;
            }

            if (line == "}")
            {
                continue;
            }

            if (line == "--")
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "concurrency",
                    "Concurrent regions ('--') are not supported; the states are rendered in one " +
                    "region.");
                continue;
            }

            if (keyword.Equals("direction", StringComparison.OrdinalIgnoreCase))
            {
                direction = ParseDirection(line, diagnostics, reported, direction);
                continue;
            }

            if (keyword.Equals("note", StringComparison.OrdinalIgnoreCase))
            {
                HandleNote(line);
                continue;
            }

            if (IgnoredKeywords.Contains(keyword))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    keyword,
                    $"State-diagram statement '{keyword}' is not supported and was ignored.");
                continue;
            }

            if (line.Contains(TransitionOperator, StringComparison.Ordinal))
            {
                AddTransition(line);
                continue;
            }

            if (keyword.Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                HandleStateDeclaration(line[keyword.Length..].Trim());
                continue;
            }

            MermaidLines.ReportIgnored(
                diagnostics,
                reported,
                "unknown-statement",
                "One or more state-diagram statements were not recognized and were ignored; the " +
                "rest of the diagram is rendered.");
        }

        if (openNoteState is not null)
        {
            AddNote(openNoteState, string.Join(' ', openNoteText));
        }

        if (!headerSeen)
        {
            return new StateParseResult(
                false, null, "The mermaid block contains no 'stateDiagram' header.", diagnostics);
        }

        if (nodes.Count == 0)
        {
            return new StateParseResult(
                false, null, "The state diagram declares no states.", diagnostics);
        }

        if (nodes.Count > MaxStates)
        {
            return new StateParseResult(
                false,
                null,
                $"The state diagram has {nodes.Count} states, above the limit of {MaxStates}.",
                diagnostics);
        }

        return new StateParseResult(
            true,
            new StateDiagramModel(direction, nodes, transitions),
            null,
            diagnostics);

        // Registers a vertex, keeping first-mention order and letting a later alias set the label.
        string Ensure(string id, string? label, StateKind kind)
        {
            if (byId.TryGetValue(id, out int index))
            {
                if (label is { Length: > 0 } && nodes[index].Label.Length == 0)
                {
                    nodes[index] = nodes[index] with { Label = label };
                }

                return id;
            }

            byId[id] = nodes.Count;
            // An implicitly created state keeps an empty label so a 'state "..." as id' alias
            // appearing later still wins; the layout falls back to the id when none arrives.
            nodes.Add(new StateNode(id, label ?? string.Empty, kind, nodes.Count));
            return id;
        }

        // '[*]' is the start state on the left of a transition and the end state on the right.
        string Reference(string token, bool isTarget)
        {
            string text = token.Trim();
            if (text == "[*]")
            {
                return isTarget
                    ? Ensure(EndStateId, string.Empty, StateKind.End)
                    : Ensure(StartStateId, string.Empty, StateKind.Start);
            }

            if (text.EndsWith('{'))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "composite",
                    "Composite states are flattened; their nested states are rendered in one " +
                    "region.");
                text = text[..^1].Trim();
            }

            return Ensure(
                MermaidLines.Truncate(text, MaxLabelLength), null, StateKind.Normal);
        }

        void AddTransition(string line)
        {
            int operatorIndex = line.IndexOf(TransitionOperator, StringComparison.Ordinal);
            string left = line[..operatorIndex];
            string right = line[(operatorIndex + TransitionOperator.Length)..];

            string? label = null;
            int colon = right.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                label = MermaidLines.Truncate(right[(colon + 1)..].Trim(), MaxLabelLength);
                right = right[..colon];
                if (label.Length == 0)
                {
                    label = null;
                }
            }

            if (left.Trim().Length == 0 || right.Trim().Length == 0)
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "unknown-statement",
                    "One or more state-diagram statements were not recognized and were ignored; " +
                    "the rest of the diagram is rendered.");
                return;
            }

            transitions.Add(new StateTransition(
                Reference(left, isTarget: false), Reference(right, isTarget: true), label));
        }

        void HandleStateDeclaration(string rest)
        {
            if (rest.EndsWith('{'))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "composite",
                    "Composite states are flattened; their nested states are rendered in one " +
                    "region.");
                rest = rest[..^1].Trim();
            }

            int annotation = rest.IndexOf("<<", StringComparison.Ordinal);
            if (annotation >= 0)
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "pseudo-state",
                    "Choice, fork, and join pseudo-states are rendered as ordinary states.");
                rest = rest[..annotation].Trim();
            }

            int aliasIndex = rest.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (aliasIndex > 0)
            {
                string label = MermaidLines.Unquote(rest[..aliasIndex]);
                string id = rest[(aliasIndex + " as ".Length)..].Trim();
                if (id.Length > 0)
                {
                    Ensure(
                        MermaidLines.Truncate(id, MaxLabelLength),
                        MermaidLines.Truncate(label, MaxLabelLength),
                        StateKind.Normal);
                }

                return;
            }

            string plain = MermaidLines.Unquote(rest);
            if (plain.Length > 0)
            {
                Ensure(MermaidLines.Truncate(plain, MaxLabelLength), null, StateKind.Normal);
            }
        }

        void HandleNote(string line)
        {
            // 'note left of X : text', 'note right of X' + block, or 'note over X : text'.
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            string head = colon >= 0 ? line[..colon] : line;
            string[] words = head.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? state = words.Length >= 2 ? words[^1] : null;
            if (state is null || state.Equals("note", StringComparison.OrdinalIgnoreCase))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "note",
                    "A note without a target state was ignored.");
                return;
            }

            if (colon >= 0)
            {
                AddNote(state, line[(colon + 1)..].Trim());
                return;
            }

            openNoteState = state;
            openNoteText.Clear();
        }

        void AddNote(string state, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            noteCount++;
            string target = Ensure(state, null, StateKind.Normal);
            string id = string.Create(
                CultureInfo.InvariantCulture, $"__note{noteCount}");
            Ensure(id, MermaidLines.Truncate(text, MaxLabelLength), StateKind.Note);
            transitions.Add(new StateTransition(target, id, null, IsNoteLink: true));
        }
    }

    /// <summary>Statements recognized but deliberately not rendered.</summary>
    private static readonly HashSet<string> IgnoredKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "classDef",
            "class",
            "style",
            "click",
            "accTitle",
            "accDescr",
        };

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
                return FlowDirection.TopDown;

            case "LR":
                return FlowDirection.LeftRight;

            case "BT":
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "direction-bt",
                    "Direction 'BT' is rendered top-down.");
                return FlowDirection.TopDown;

            case "RL":
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "direction-rl",
                    "Direction 'RL' is rendered left-to-right.");
                return FlowDirection.LeftRight;

            default:
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "direction-unknown",
                    $"Direction '{value}' is not recognized; the diagram is rendered top-down.");
                return current;
        }
    }
}
