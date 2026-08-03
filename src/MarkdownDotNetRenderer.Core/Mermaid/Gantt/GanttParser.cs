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

namespace MarkdownDotNetRenderer.Mermaid.Gantt;

/// <summary>
/// Line-oriented parser for the <c>gantt</c> subset documented in
/// docs/phases/phase-4-additional-diagrams.md: <c>title</c>, <c>dateFormat</c>, <c>section</c>, and
/// task lines of the form <c>Name :[tags,] [id,] start, duration|end</c>, where a start is an ISO
/// date or <c>after &lt;id&gt;</c> and a duration is <c>Nd</c> or <c>Nw</c>. Only ISO
/// <c>YYYY-MM-DD</c> dates are understood; anything else about the source's formatting is reported
/// once as <c>MERMAID003</c> and the ISO reading is used.
/// </summary>
public static class GanttParser
{
    /// <summary>The diagram-type keyword.</summary>
    public const string HeaderKeyword = "gantt";

    /// <summary>The only date format understood, and the format axis labels are written in.</summary>
    public const string IsoDateFormat = "yyyy-MM-dd";

    /// <summary>Task-count guard; a larger chart fails instead of laying out.</summary>
    public const int MaxTasks = 200;

    /// <summary>Longest name or title kept.</summary>
    public const int MaxTextLength = 200;

    /// <summary>Duration used when a task's length cannot be read.</summary>
    public const int FallbackDurationDays = 1;

    /// <summary>Days in a <c>w</c> duration unit.</summary>
    public const int DaysPerWeek = 7;

    /// <summary>The task tags the subset understands.</summary>
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "done",
        "active",
        "crit",
        "milestone",
    };

    /// <summary>Chart-level statements recognized but deliberately not honoured.</summary>
    private static readonly HashSet<string> IgnoredKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "axisFormat",
            "excludes",
            "includes",
            "todayMarker",
            "tickInterval",
            "weekday",
            "inclusiveEndDates",
            "topAxis",
            "displayMode",
            "click",
            "accTitle",
            "accDescr",
        };

    /// <summary>Parses a Gantt source into a model.</summary>
    /// <param name="mermaidSource">The complete mermaid source, header line included.</param>
    /// <returns>The parse outcome.</returns>
    public static GanttParseResult Parse(string mermaidSource)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);

        var diagnostics = new List<RenderDiagnostic>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var sections = new List<(string Name, List<GanttTask> Tasks)>();
        var endById = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        string? title = null;
        bool headerSeen = false;
        bool inDirective = false;
        int taskCount = 0;
        DateOnly? previousEnd = null;

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

            string keyword = MermaidLines.FirstWord(line);

            if (!headerSeen)
            {
                if (!keyword.Equals(HeaderKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return new GanttParseResult(
                        false, null, "The first statement is not a 'gantt' header.", diagnostics);
                }

                headerSeen = true;
                continue;
            }

            if (keyword.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                string text = line[keyword.Length..].Trim();
                if (text.Length > 0)
                {
                    title = MermaidLines.Truncate(text, MaxTextLength);
                }

                continue;
            }

            if (keyword.Equals("dateFormat", StringComparison.OrdinalIgnoreCase))
            {
                string format = line[keyword.Length..].Trim();
                if (!format.Equals("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                {
                    MermaidLines.ReportIgnored(
                        diagnostics,
                        reported,
                        "dateFormat",
                        $"Date format '{format}' is not supported; dates are read as " +
                        "YYYY-MM-DD.");
                }

                continue;
            }

            if (keyword.Equals("section", StringComparison.OrdinalIgnoreCase))
            {
                string name = MermaidLines.Truncate(
                    line[keyword.Length..].Trim(), MaxTextLength);
                sections.Add((name, []));
                continue;
            }

            if (IgnoredKeywords.Contains(keyword))
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    keyword,
                    $"Gantt statement '{keyword}' is not supported and was ignored.");
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                MermaidLines.ReportIgnored(
                    diagnostics,
                    reported,
                    "unknown-statement",
                    "One or more Gantt statements were not recognized and were ignored; the rest " +
                    "of the chart is rendered.");
                continue;
            }

            if (TryParseTask(
                MermaidLines.Truncate(line[..colon].Trim(), MaxTextLength),
                line[(colon + 1)..],
                taskCount,
                previousEnd,
                endById,
                diagnostics,
                reported,
                out GanttTask? task))
            {
                if (sections.Count == 0)
                {
                    sections.Add((string.Empty, []));
                }

                sections[^1].Tasks.Add(task);
                taskCount++;
                previousEnd = task.End;
                if (task.Id.Length > 0)
                {
                    endById[task.Id] = task.End;
                }

                continue;
            }

            MermaidLines.ReportIgnored(
                diagnostics,
                reported,
                "task",
                "One or more task lines had no readable start date and were ignored; the rest of " +
                "the chart is rendered.");
        }

        if (!headerSeen)
        {
            return new GanttParseResult(
                false, null, "The mermaid block contains no 'gantt' header.", diagnostics);
        }

        if (taskCount == 0)
        {
            return new GanttParseResult(
                false, null, "The Gantt chart declares no dated tasks.", diagnostics);
        }

        if (taskCount > MaxTasks)
        {
            return new GanttParseResult(
                false,
                null,
                $"The Gantt chart has {taskCount} tasks, above the limit of {MaxTasks}.",
                diagnostics);
        }

        var model = new GanttModel(
            title,
            sections
                .Where(section => section.Tasks.Count > 0)
                .Select(section => new GanttSection(section.Name, section.Tasks))
                .ToList());

        return new GanttParseResult(true, model, null, diagnostics);
    }

    /// <summary>Parses one task line's tail — everything after the name's colon.</summary>
    /// <param name="name">The task's display name.</param>
    /// <param name="tail">The comma-separated fields after the colon.</param>
    /// <param name="order">The task's 0-based source index.</param>
    /// <param name="previousEnd">End of the previous task, used when no start is given.</param>
    /// <param name="endById">Ends of the tasks parsed so far, for <c>after &lt;id&gt;</c>.</param>
    /// <param name="diagnostics">Diagnostics to append ignored-construct notices to.</param>
    /// <param name="reported">Ignored-construct keys already reported.</param>
    /// <param name="task">The parsed task, when the line is one.</param>
    /// <returns>Whether a dated task was produced.</returns>
    public static bool TryParseTask(
        string name,
        string tail,
        int order,
        DateOnly? previousEnd,
        IReadOnlyDictionary<string, DateOnly> endById,
        IList<RenderDiagnostic> diagnostics,
        ISet<string> reported,
        out GanttTask task)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(endById);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(reported);

        task = new GanttTask(
            name,
            string.Empty,
            default,
            default,
            GanttTaskState.Planned,
            false,
            false,
            order);

        var fields = tail
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        GanttTaskState state = GanttTaskState.Planned;
        bool critical = false;
        bool milestone = false;
        while (fields.Count > 0 && Tags.Contains(fields[0]))
        {
            switch (fields[0].ToLowerInvariant())
            {
                case "done":
                    state = GanttTaskState.Done;
                    break;
                case "active":
                    state = GanttTaskState.Active;
                    break;
                case "crit":
                    critical = true;
                    break;
                default:
                    milestone = true;
                    break;
            }

            fields.RemoveAt(0);
        }

        string id = string.Empty;
        if (fields.Count >= 3)
        {
            id = fields[0];
            fields.RemoveAt(0);
        }

        string? startField = fields.Count > 0 ? fields[0] : null;
        string? lengthField = fields.Count > 1 ? fields[1] : null;
        if (fields.Count == 1 && !LooksLikeStart(fields[0]))
        {
            // A single field is a duration; the task starts where the previous one ended.
            startField = null;
            lengthField = fields[0];
        }

        DateOnly? start = ResolveStart(startField, previousEnd, endById);
        if (start is null)
        {
            return false;
        }

        DateOnly end;
        if (lengthField is null)
        {
            end = milestone ? start.Value : start.Value.AddDays(FallbackDurationDays);
        }
        else if (DateOnly.TryParseExact(
            lengthField,
            IsoDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly explicitEnd))
        {
            end = explicitEnd < start.Value ? start.Value : explicitEnd;
        }
        else if (TryParseDuration(lengthField, out int days))
        {
            end = start.Value.AddDays(days);
        }
        else
        {
            MermaidLines.ReportIgnored(
                diagnostics,
                reported,
                "duration",
                $"Duration '{lengthField}' is not supported; {FallbackDurationDays} day was used.");
            end = start.Value.AddDays(FallbackDurationDays);
        }

        task = new GanttTask(name, id, start.Value, end, state, critical, milestone, order);
        return true;
    }

    /// <summary>Whether a field is a start rather than a duration.</summary>
    /// <param name="field">The field text.</param>
    /// <returns>Whether it reads as a date or an <c>after</c> reference.</returns>
    public static bool LooksLikeStart(string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.StartsWith("after ", StringComparison.OrdinalIgnoreCase) ||
            DateOnly.TryParseExact(
                field,
                IsoDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
    }

    /// <summary>Parses a <c>Nd</c>/<c>Nw</c> duration into whole days.</summary>
    /// <param name="field">The duration field.</param>
    /// <param name="days">The duration in days.</param>
    /// <returns>Whether the field was a supported duration.</returns>
    public static bool TryParseDuration(string field, out int days)
    {
        ArgumentNullException.ThrowIfNull(field);

        days = 0;
        if (field.Length < 2)
        {
            return false;
        }

        char unit = char.ToLowerInvariant(field[^1]);
        if (!int.TryParse(
            field[..^1],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int count) || count < 0)
        {
            return false;
        }

        switch (unit)
        {
            case 'd':
                days = count;
                return true;
            case 'w':
                days = count * DaysPerWeek;
                return true;
            default:
                return false;
        }
    }

    private static DateOnly? ResolveStart(
        string? startField,
        DateOnly? previousEnd,
        IReadOnlyDictionary<string, DateOnly> endById)
    {
        if (startField is null)
        {
            return previousEnd;
        }

        if (DateOnly.TryParseExact(
            startField,
            IsoDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed))
        {
            return parsed;
        }

        if (startField.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
        {
            DateOnly? latest = null;
            foreach (string reference in startField["after ".Length..].Split(
                ' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (endById.TryGetValue(reference, out DateOnly end) &&
                    (latest is null || end > latest))
                {
                    latest = end;
                }
            }

            return latest ?? previousEnd;
        }

        return previousEnd;
    }
}
