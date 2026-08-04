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

using System.Text.RegularExpressions;

namespace MarkdownDotNetRenderer.Mermaid.Sequence;

/// <summary>
/// Line-oriented parser for the phase-3 <c>sequenceDiagram</c> syntax subset documented in
/// docs/phases/phase-3-sequence-diagrams.md. Deferred constructs (fragments, activations, boxes)
/// are reported once each with <c>MERMAID003</c> and their contained messages still parse, so
/// nothing is dropped silently. Only a source that is not a sequence diagram at all, or one that
/// yields no lifelines, fails.
/// </summary>
public static class SequenceParser
{
    /// <summary>Longest label kept; longer text is truncated so one line cannot blow up layout.</summary>
    public const int MaxLabelLength = 400;

    /// <summary>The diagram-type keyword this parser accepts.</summary>
    public const string HeaderKeyword = "sequenceDiagram";

    // "Alice->>Bob: text", including the dashed, open, cross, and async arrow spellings.
    private static readonly Regex MessagePattern = new(
        @"^(?<source>[^:]+?)\s*(?<arrow>--?>>|--?\)|--?x|--?>)\s*(?<target>[^:]+?)\s*:(?<label>.*)$",
        RegexOptions.CultureInvariant);

    // "Note over A,B: text", "Note left of A: text".
    private static readonly Regex NotePattern = new(
        @"^note\s+(?<placement>left of|right of|over)\s+(?<actors>[^:]+?)\s*:(?<text>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // "participant A as Alice", "actor A".
    private static readonly Regex ParticipantPattern = new(
        @"^(?:participant|actor)\s+(?<body>\S.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex AliasPattern = new(
        @"^(?<id>.+?)\s+as\s+(?<label>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Constructs recognized but not laid out in this phase. Their contained messages still
    /// render; only the labelled frame or activation bar is missing.
    /// </summary>
    private static readonly string[] DeferredKeywords =
    [
        "loop", "alt", "else", "opt", "par", "and", "critical", "option", "break", "rect", "box",
        "activate", "deactivate", "title", "link", "links",
    ];

    /// <summary>Parses a sequence-diagram source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static SequenceParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var actors = new Dictionary<string, SequenceActor>(StringComparer.Ordinal);
        var order = new List<string>();
        var events = new List<SequenceEvent>();

        bool headerSeen = false;
        bool inDirective = false;
        bool autonumber = false;
        int messageNumber = 0;

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
                Report(diagnostics, reported, "directive", MermaidLines.DirectiveIgnored);
                inDirective = !line.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (!headerSeen)
            {
                if (!MermaidLines.FirstWord(line).Equals(
                    HeaderKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new SequenceParseResult(
                        false,
                        null,
                        "The first statement is not a 'sequenceDiagram' header.",
                        diagnostics);
                }

                headerSeen = true;
                continue;
            }

            // 'end' closes a construct whose opener was already reported, so it is silent.
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MermaidLines.FirstWord(line).Equals(
                "autonumber", StringComparison.OrdinalIgnoreCase))
            {
                autonumber = true;
                continue;
            }

            Match participant = ParticipantPattern.Match(line);
            if (participant.Success)
            {
                ParseParticipant(participant.Groups["body"].Value, actors, order);
                continue;
            }

            Match note = NotePattern.Match(line);
            if (note.Success)
            {
                ParseNote(note, actors, order, events);
                continue;
            }

            Match message = MessagePattern.Match(line);
            if (message.Success &&
                TryParseMessage(message, actors, order, events, autonumber, ref messageNumber,
                    diagnostics, reported))
            {
                continue;
            }

            string keyword = MermaidLines.FirstWord(line);
            bool deferred = false;
            foreach (string candidate in DeferredKeywords)
            {
                if (!keyword.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                deferred = true;
                Report(diagnostics, reported, candidate,
                    $"'{candidate}' is not laid out in this phase and was ignored; any messages " +
                    "it contains still render in source order.");
                break;
            }

            if (!deferred)
            {
                Report(diagnostics, reported, "unknown-statement",
                    "One or more sequence-diagram statements were not recognized and were " +
                    "ignored; the rest of the diagram is rendered.");
            }
        }

        if (!headerSeen)
        {
            return new SequenceParseResult(
                false, null, "The mermaid block contains no 'sequenceDiagram' header.", diagnostics);
        }

        if (order.Count == 0)
        {
            return new SequenceParseResult(
                false, null, "The sequence diagram declares no participants.", diagnostics);
        }

        var model = new SequenceModel(
            order.Select(id => actors[id]).ToArray(),
            events);
        return new SequenceParseResult(true, model, null, diagnostics);
    }

    private static void ParseParticipant(
        string body,
        Dictionary<string, SequenceActor> actors,
        List<string> order)
    {
        string id = body.Trim();
        string? label = null;

        Match alias = AliasPattern.Match(id);
        if (alias.Success)
        {
            id = alias.Groups["id"].Value.Trim();
            label = Unquote(alias.Groups["label"].Value.Trim());
        }

        id = Unquote(id);
        if (id.Length == 0)
        {
            return;
        }

        EnsureActor(id, label, actors, order);
    }

    private static void ParseNote(
        Match note,
        Dictionary<string, SequenceActor> actors,
        List<string> order,
        List<SequenceEvent> events)
    {
        SequenceNotePlacement placement =
            note.Groups["placement"].Value.ToUpperInvariant() switch
            {
                "LEFT OF" => SequenceNotePlacement.LeftOf,
                "RIGHT OF" => SequenceNotePlacement.RightOf,
                _ => SequenceNotePlacement.Over,
            };

        var ids = new List<string>();
        foreach (string part in note.Groups["actors"].Value.Split(','))
        {
            string id = Unquote(part.Trim());
            if (id.Length == 0)
            {
                continue;
            }

            EnsureActor(id, null, actors, order);
            ids.Add(id);
        }

        if (ids.Count == 0)
        {
            return;
        }

        events.Add(new SequenceNote(
            events.Count, placement, ids, Truncate(note.Groups["text"].Value.Trim())));
    }

    private static bool TryParseMessage(
        Match message,
        Dictionary<string, SequenceActor> actors,
        List<string> order,
        List<SequenceEvent> events,
        bool autonumber,
        ref int messageNumber,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        string source = Unquote(message.Groups["source"].Value.Trim());
        string target = StripActivationShorthand(
            Unquote(message.Groups["target"].Value.Trim()), diagnostics, reported);

        if (source.Length == 0 || target.Length == 0)
        {
            return false;
        }

        (SequenceLineStyle line, SequenceArrowHead head) = ArrowStyle(message.Groups["arrow"].Value);

        EnsureActor(source, null, actors, order);
        EnsureActor(target, null, actors, order);

        int? number = autonumber ? ++messageNumber : null;
        events.Add(new SequenceMessage(
            events.Count,
            source,
            target,
            Truncate(message.Groups["label"].Value.Trim()),
            line,
            head,
            number));
        return true;
    }

    /// <summary>Maps a mermaid arrow token to its line style and arrowhead.</summary>
    /// <param name="arrow">The arrow token, e.g. <c>--&gt;&gt;</c>.</param>
    /// <returns>The line style and head to draw.</returns>
    public static (SequenceLineStyle Line, SequenceArrowHead Head) ArrowStyle(string arrow)
    {
        ArgumentNullException.ThrowIfNull(arrow);

        SequenceLineStyle line = arrow.StartsWith("--", StringComparison.Ordinal)
            ? SequenceLineStyle.Dashed
            : SequenceLineStyle.Solid;

        SequenceArrowHead head =
            arrow.EndsWith(">>", StringComparison.Ordinal) ? SequenceArrowHead.Filled :
            arrow.EndsWith('x') ? SequenceArrowHead.Cross :
            arrow.EndsWith(')') ? SequenceArrowHead.Async :
            SequenceArrowHead.Open;

        return (line, head);
    }

    /// <summary>Drops the <c>+</c>/<c>-</c> activation shorthand from a target id.</summary>
    private static string StripActivationShorthand(
        string target,
        List<RenderDiagnostic> diagnostics,
        HashSet<string> reported)
    {
        if (target.Length < 2 || (target[0] != '+' && target[0] != '-'))
        {
            return target;
        }

        Report(diagnostics, reported, "activate",
            "Activation bars ('+'/'-' shorthand, 'activate'/'deactivate') are not drawn in this " +
            "phase; the messages themselves still render.");
        return target[1..].Trim();
    }

    private static void EnsureActor(
        string id,
        string? label,
        Dictionary<string, SequenceActor> actors,
        List<string> order)
    {
        if (actors.TryGetValue(id, out SequenceActor? existing))
        {
            // A later 'participant A as Alice' still supplies the display label.
            if (label is { Length: > 0 })
            {
                actors[id] = existing with { Label = Truncate(label) };
            }

            return;
        }

        actors[id] = new SequenceActor(
            id,
            Truncate(label is { Length: > 0 } ? label : id),
            order.Count);
        order.Add(id);
    }

    private static string Unquote(string value)
    {
        string text = value.Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1].Trim()
            : text;
    }

    private static string Truncate(string value) =>
        value.Length <= MaxLabelLength ? value : value[..MaxLabelLength];

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
