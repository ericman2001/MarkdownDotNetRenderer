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

using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Mermaid.Sequence;

/// <summary>
/// Tunable geometry for <see cref="SequenceLayoutEngine"/>, the sequence-diagram counterpart of
/// <see cref="Flowchart.LayoutMetrics"/>. All lengths are CSS pixels.
/// </summary>
/// <param name="Margin">Margin around the whole diagram.</param>
/// <param name="ActorGap">Minimum gap between two neighbouring actor header boxes.</param>
/// <param name="ActorPaddingX">Horizontal padding on each side of an actor's label.</param>
/// <param name="ActorPaddingY">Vertical padding above and below an actor's label.</param>
/// <param name="MinActorWidth">Lower bound on an actor header box's width.</param>
/// <param name="MinActorHeight">Lower bound on an actor header box's height.</param>
/// <param name="LifelineHeadGap">Gap between the header boxes and the first row.</param>
/// <param name="LifelineFootGap">Gap between the last row and the lifeline's lower end.</param>
/// <param name="MessageLabelGap">Gap between a message label and its line.</param>
/// <param name="MessageRowGap">Gap below a message line before the next row.</param>
/// <param name="MinMessageRowHeight">Lower bound on the height a message row consumes.</param>
/// <param name="MessageLabelClearance">Extra column spacing demanded around a message label.</param>
/// <param name="SelfLoopWidth">How far a self-message's loop reaches right of its lifeline.</param>
/// <param name="SelfLoopHeight">Vertical drop of a self-message's loop.</param>
/// <param name="SelfLoopLabelGap">Gap between a self-message loop and its label.</param>
/// <param name="NotePaddingX">Horizontal padding inside a note box.</param>
/// <param name="NotePaddingY">Vertical padding inside a note box.</param>
/// <param name="NoteRowGap">Gap below a note box before the next row.</param>
/// <param name="NoteEdgeOffset">Gap between a lifeline and a note placed left or right of it.</param>
/// <param name="NoteOverOvershoot">How far an <c>over</c> note extends past the lifelines it spans.</param>
/// <param name="MinNoteWidth">Lower bound on a note box's width.</param>
/// <param name="MaxActors">Actor-count guard; exceeding it fails the layout with MERMAID004.</param>
/// <param name="MaxEvents">Event-count guard; exceeding it fails the layout with MERMAID004.</param>
public sealed record SequenceMetrics(
    double Margin = 16,
    double ActorGap = 40,
    double ActorPaddingX = 14,
    double ActorPaddingY = 8,
    double MinActorWidth = 80,
    double MinActorHeight = 32,
    double LifelineHeadGap = 18,
    double LifelineFootGap = 16,
    double MessageLabelGap = 6,
    double MessageRowGap = 18,
    double MinMessageRowHeight = 36,
    double MessageLabelClearance = 16,
    double SelfLoopWidth = 34,
    double SelfLoopHeight = 26,
    double SelfLoopLabelGap = 8,
    double NotePaddingX = 10,
    double NotePaddingY = 6,
    double NoteRowGap = 14,
    double NoteEdgeOffset = 12,
    double NoteOverOvershoot = 16,
    double MinNoteWidth = 60,
    int MaxActors = 60,
    int MaxEvents = 400)
{
    /// <summary>The defaults documented in docs/phases/phase-3-sequence-diagrams.md.</summary>
    public static SequenceMetrics Default { get; } = new();
}

/// <summary>One actor's header box and the lifeline dropped from it.</summary>
/// <param name="Actor">The parsed actor.</param>
/// <param name="CenterX">Centre of the header box, which is also the lifeline's x.</param>
/// <param name="Width">Header box width.</param>
/// <param name="Height">Header box height.</param>
/// <param name="Top">Header box top edge.</param>
/// <param name="LabelLines">The wrapped header label.</param>
public sealed record ActorColumn(
    SequenceActor Actor,
    double CenterX,
    double Width,
    double Height,
    double Top,
    IReadOnlyList<string> LabelLines);

/// <summary>A placed row of the diagram, in source order.</summary>
/// <param name="Event">The event this row draws.</param>
/// <param name="Top">Top edge of the band the row occupies.</param>
/// <param name="Height">Height of the band the row occupies.</param>
public abstract record SequenceRow(SequenceEvent Event, double Top, double Height);

/// <summary>A message drawn between two different lifelines.</summary>
/// <param name="Message">The parsed message.</param>
/// <param name="Top">Top edge of the row band.</param>
/// <param name="Height">Height of the row band.</param>
/// <param name="LineY">Y of the horizontal message line.</param>
/// <param name="StartX">X of the sending lifeline.</param>
/// <param name="EndX">X of the receiving lifeline.</param>
/// <param name="LabelLines">The wrapped label; empty entry when the message has no text.</param>
/// <param name="LabelWidth">Width of the widest label line.</param>
public sealed record MessageRow(
    SequenceMessage Message,
    double Top,
    double Height,
    double LineY,
    double StartX,
    double EndX,
    IReadOnlyList<string> LabelLines,
    double LabelWidth) : SequenceRow(Message, Top, Height);

/// <summary>A message that loops back to its own lifeline.</summary>
/// <param name="Message">The parsed message.</param>
/// <param name="Top">Top edge of the row band.</param>
/// <param name="Height">Height of the row band.</param>
/// <param name="LifelineX">X of the lifeline the loop leaves and re-enters.</param>
/// <param name="LoopTop">Y where the loop leaves the lifeline.</param>
/// <param name="LoopWidth">How far right the loop reaches.</param>
/// <param name="LoopHeight">Vertical drop of the loop.</param>
/// <param name="LabelLines">The wrapped label, drawn right of the loop.</param>
/// <param name="LabelWidth">Width of the widest label line.</param>
public sealed record SelfMessageRow(
    SequenceMessage Message,
    double Top,
    double Height,
    double LifelineX,
    double LoopTop,
    double LoopWidth,
    double LoopHeight,
    IReadOnlyList<string> LabelLines,
    double LabelWidth) : SequenceRow(Message, Top, Height);

/// <summary>A note box.</summary>
/// <param name="Note">The parsed note.</param>
/// <param name="Top">Top edge of the row band.</param>
/// <param name="Height">Height of the row band.</param>
/// <param name="BoxLeft">Left edge of the box.</param>
/// <param name="BoxWidth">Box width.</param>
/// <param name="BoxHeight">Box height.</param>
/// <param name="Lines">The wrapped note text.</param>
public sealed record NoteRow(
    SequenceNote Note,
    double Top,
    double Height,
    double BoxLeft,
    double BoxWidth,
    double BoxHeight,
    IReadOnlyList<string> Lines) : SequenceRow(Note, Top, Height);

/// <summary>The laid-out sequence diagram.</summary>
/// <param name="Actors">Actor columns in first-mention order.</param>
/// <param name="Rows">Placed rows in source order.</param>
/// <param name="Width">Total diagram width including margins.</param>
/// <param name="Height">Total diagram height including margins.</param>
/// <param name="LifelineTop">Y where every lifeline starts.</param>
/// <param name="LifelineBottom">Y where every lifeline ends.</param>
public sealed record SequenceLayout(
    IReadOnlyList<ActorColumn> Actors,
    IReadOnlyList<SequenceRow> Rows,
    double Width,
    double Height,
    double LifelineTop,
    double LifelineBottom);

/// <summary>The outcome of a sequence-diagram layout attempt.</summary>
/// <param name="Success">Whether coordinates were produced.</param>
/// <param name="Layout">The layout, or <see langword="null"/> when a guard tripped.</param>
/// <param name="FailureMessage">Why the layout was refused.</param>
public sealed record SequenceLayoutResult(
    bool Success,
    SequenceLayout? Layout,
    string? FailureMessage);

/// <summary>
/// The deterministic, solver-free sequence layout described in
/// docs/phases/phase-3-sequence-diagrams.md: actor columns are placed left to right in
/// first-mention order with a gap wide enough for the labels that span them, and rows are stacked
/// top to bottom in source order. Every coordinate is a pure function of the model, the metrics,
/// and the text metrics — there is no iteration and no heuristic, so repeated renders are
/// byte-identical.
/// </summary>
public static class SequenceLayoutEngine
{
    /// <summary>Lays out a parsed sequence diagram.</summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="fontSize">Label font size in CSS pixels.</param>
    /// <param name="metrics">Geometry and guards.</param>
    /// <param name="wrapChars">Soft wrap width, in characters, for labels and notes.</param>
    /// <returns>The layout, or a failure when a guard tripped.</returns>
    public static SequenceLayoutResult Compute(
        SequenceModel model,
        double fontSize,
        SequenceMetrics metrics,
        int wrapChars)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wrapChars);

        if (model.Actors.Count > metrics.MaxActors)
        {
            return new SequenceLayoutResult(
                false,
                null,
                $"The sequence diagram has {model.Actors.Count} participants, above the limit of " +
                $"{metrics.MaxActors}.");
        }

        if (model.Events.Count > metrics.MaxEvents)
        {
            return new SequenceLayoutResult(
                false,
                null,
                $"The sequence diagram has {model.Events.Count} events, above the limit of " +
                $"{metrics.MaxEvents}.");
        }

        double lineHeight = TextMetrics.LineHeight(fontSize);
        int actorCount = model.Actors.Count;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new List<IReadOnlyList<string>>(actorCount);
        var widths = new double[actorCount];
        double headerHeight = metrics.MinActorHeight;

        for (int i = 0; i < actorCount; i++)
        {
            SequenceActor actor = model.Actors[i];
            index[actor.Id] = i;
            IReadOnlyList<string> lines = TextMetrics.WrapLabel(actor.Label, wrapChars);
            labels.Add(lines);
            widths[i] = Math.Max(
                metrics.MinActorWidth,
                WidestLine(lines, fontSize) + (2 * metrics.ActorPaddingX));
            headerHeight = Math.Max(
                headerHeight, (lines.Count * lineHeight) + (2 * metrics.ActorPaddingY));
        }

        // Wrapped text per event, measured once and reused by both the column and the row passes.
        var wrapped = new List<IReadOnlyList<string>>(model.Events.Count);
        foreach (SequenceEvent item in model.Events)
        {
            wrapped.Add(item switch
            {
                SequenceMessage message => TextMetrics.WrapLabel(message.DisplayLabel, wrapChars),
                SequenceNote note => TextMetrics.WrapLabel(note.Text, wrapChars),
                _ => [string.Empty],
            });
        }

        double[] gaps = ComputeGaps(model, index, wrapped, fontSize, metrics);

        var centers = new double[actorCount];
        double cursor = metrics.Margin;
        for (int i = 0; i < actorCount; i++)
        {
            if (i > 0)
            {
                cursor += gaps[i - 1];
            }

            centers[i] = cursor + (widths[i] / 2);
            cursor = centers[i] + (widths[i] / 2);
        }

        double contentRight = cursor;
        double contentLeft = metrics.Margin;

        double headerTop = metrics.Margin;
        double lifelineTop = headerTop + headerHeight;
        double y = lifelineTop + metrics.LifelineHeadGap;

        var rows = new List<SequenceRow>(model.Events.Count);
        for (int i = 0; i < model.Events.Count; i++)
        {
            IReadOnlyList<string> lines = wrapped[i];
            double textWidth = WidestLine(lines, fontSize);
            double textHeight = lines.Count * lineHeight;

            switch (model.Events[i])
            {
                case SequenceMessage message when message.IsSelfMessage:
                {
                    if (!index.TryGetValue(message.SourceId, out int column))
                    {
                        continue;
                    }

                    double labelWidth = message.DisplayLabel.Length == 0 ? 0 : textWidth;
                    double height = Math.Max(
                        metrics.SelfLoopHeight + metrics.MessageRowGap,
                        textHeight + metrics.MessageRowGap);
                    rows.Add(new SelfMessageRow(
                        message,
                        y,
                        height,
                        centers[column],
                        y,
                        metrics.SelfLoopWidth,
                        metrics.SelfLoopHeight,
                        lines,
                        labelWidth));
                    contentRight = Math.Max(
                        contentRight,
                        centers[column] + metrics.SelfLoopWidth + metrics.SelfLoopLabelGap +
                            labelWidth);
                    y += height;
                    break;
                }

                case SequenceMessage message:
                {
                    if (!index.TryGetValue(message.SourceId, out int source) ||
                        !index.TryGetValue(message.TargetId, out int target))
                    {
                        continue;
                    }

                    double labelHeight = message.DisplayLabel.Length == 0 ? 0 : textHeight;
                    double lineY = y + labelHeight + metrics.MessageLabelGap;
                    double height = Math.Max(
                        metrics.MinMessageRowHeight,
                        labelHeight + metrics.MessageLabelGap + metrics.MessageRowGap);
                    rows.Add(new MessageRow(
                        message,
                        y,
                        height,
                        lineY,
                        centers[source],
                        centers[target],
                        lines,
                        message.DisplayLabel.Length == 0 ? 0 : textWidth));
                    y += height;
                    break;
                }

                case SequenceNote note:
                {
                    if (!TryPlaceNote(
                            note, textWidth, textHeight, index, centers, widths, metrics,
                            out double boxLeft, out double boxWidth, out double boxHeight))
                    {
                        continue;
                    }

                    double height = boxHeight + metrics.NoteRowGap;
                    rows.Add(new NoteRow(note, y, height, boxLeft, boxWidth, boxHeight, lines));
                    contentLeft = Math.Min(contentLeft, boxLeft);
                    contentRight = Math.Max(contentRight, boxLeft + boxWidth);
                    y += height;
                    break;
                }

                default:
                    break;
            }
        }

        // A note left of the first lifeline can reach past the left margin; shift the whole
        // diagram right rather than clipping it, so everything stays inside the viewBox.
        double shift = Math.Max(0, metrics.Margin - contentLeft);
        var columns = new List<ActorColumn>(actorCount);
        for (int i = 0; i < actorCount; i++)
        {
            columns.Add(new ActorColumn(
                model.Actors[i],
                centers[i] + shift,
                widths[i],
                headerHeight,
                headerTop,
                labels[i]));
        }

        if (shift > 0)
        {
            rows = rows.Select(row => Shift(row, shift)).ToList();
        }

        double lifelineBottom = rows.Count == 0
            ? lifelineTop + metrics.LifelineHeadGap + metrics.LifelineFootGap
            : y - LastRowTrailingGap(rows[^1], metrics) + metrics.LifelineFootGap;

        var layout = new SequenceLayout(
            columns,
            rows,
            contentRight + shift + metrics.Margin,
            lifelineBottom + metrics.Margin,
            lifelineTop,
            lifelineBottom);
        return new SequenceLayoutResult(true, layout, null);
    }

    /// <summary>
    /// Gap between each pair of neighbouring columns: the base gap, widened so a label that spans
    /// exactly that pair fits between the two lifelines.
    /// </summary>
    private static double[] ComputeGaps(
        SequenceModel model,
        Dictionary<string, int> index,
        List<IReadOnlyList<string>> wrapped,
        double fontSize,
        SequenceMetrics metrics)
    {
        var gaps = new double[Math.Max(0, model.Actors.Count - 1)];
        Array.Fill(gaps, metrics.ActorGap);

        for (int i = 0; i < model.Events.Count; i++)
        {
            double required = WidestLine(wrapped[i], fontSize) + metrics.MessageLabelClearance;
            switch (model.Events[i])
            {
                case SequenceMessage message when !message.IsSelfMessage:
                {
                    if (!index.TryGetValue(message.SourceId, out int source) ||
                        !index.TryGetValue(message.TargetId, out int target))
                    {
                        continue;
                    }

                    Widen(gaps, Math.Min(source, target), Math.Max(source, target), required);
                    break;
                }

                case SequenceNote note when note.Placement == SequenceNotePlacement.Over &&
                    note.ActorIds.Count > 1:
                {
                    if (!index.TryGetValue(note.ActorIds[0], out int first) ||
                        !index.TryGetValue(note.ActorIds[^1], out int last))
                    {
                        continue;
                    }

                    Widen(
                        gaps,
                        Math.Min(first, last),
                        Math.Max(first, last),
                        required + (2 * metrics.NotePaddingX));
                    break;
                }

                default:
                    break;
            }
        }

        return gaps;
    }

    /// <summary>Widens a span of gaps so the span totals at least <paramref name="required"/>.</summary>
    private static void Widen(double[] gaps, int from, int to, double required)
    {
        int spanned = to - from;
        if (spanned <= 0)
        {
            return;
        }

        // Spread the requirement evenly, so a label spanning three columns does not stretch one
        // gap into a chasm. The result depends only on the inputs, so it stays deterministic.
        double perGap = required / spanned;
        for (int i = from; i < to; i++)
        {
            gaps[i] = Math.Max(gaps[i], perGap);
        }
    }

    private static bool TryPlaceNote(
        SequenceNote note,
        double textWidth,
        double textHeight,
        Dictionary<string, int> index,
        double[] centers,
        double[] widths,
        SequenceMetrics metrics,
        out double boxLeft,
        out double boxWidth,
        out double boxHeight)
    {
        boxLeft = 0;
        boxWidth = 0;
        boxHeight = 0;

        if (!index.TryGetValue(note.ActorIds[0], out int first))
        {
            return false;
        }

        boxWidth = Math.Max(metrics.MinNoteWidth, textWidth + (2 * metrics.NotePaddingX));
        boxHeight = textHeight + (2 * metrics.NotePaddingY);

        switch (note.Placement)
        {
            case SequenceNotePlacement.LeftOf:
                boxLeft = centers[first] - metrics.NoteEdgeOffset - boxWidth;
                return true;

            case SequenceNotePlacement.RightOf:
                boxLeft = centers[first] + metrics.NoteEdgeOffset;
                return true;

            case SequenceNotePlacement.Over:
            default:
            {
                int last = first;
                if (note.ActorIds.Count > 1 &&
                    index.TryGetValue(note.ActorIds[^1], out int found))
                {
                    last = found;
                }

                double leftCenter = Math.Min(centers[first], centers[last]);
                double rightCenter = Math.Max(centers[first], centers[last]);
                double span = first == last
                    ? Math.Max(boxWidth, widths[first])
                    : rightCenter - leftCenter + (2 * metrics.NoteOverOvershoot);
                boxWidth = Math.Max(boxWidth, span);
                boxLeft = ((leftCenter + rightCenter) / 2) - (boxWidth / 2);
                return true;
            }
        }
    }

    /// <summary>The gap a row leaves below its drawn content, which the lifeline need not span.</summary>
    private static double LastRowTrailingGap(SequenceRow row, SequenceMetrics metrics) => row switch
    {
        NoteRow => metrics.NoteRowGap,
        _ => metrics.MessageRowGap,
    };

    private static SequenceRow Shift(SequenceRow row, double dx) => row switch
    {
        MessageRow message => message with
        {
            StartX = message.StartX + dx,
            EndX = message.EndX + dx,
        },
        SelfMessageRow self => self with { LifelineX = self.LifelineX + dx },
        NoteRow note => note with { BoxLeft = note.BoxLeft + dx },
        _ => row,
    };

    private static double WidestLine(IReadOnlyList<string> lines, double fontSize)
    {
        double widest = 0;
        foreach (string line in lines)
        {
            widest = Math.Max(widest, TextMetrics.MeasureWidth(line, fontSize));
        }

        return widest;
    }
}
