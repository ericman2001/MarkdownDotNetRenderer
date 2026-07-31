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

using MarkdownDotNetRenderer.Mermaid.Sequence;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Area 3 of docs/07-testing-strategy.md applied to the solver-free sequence layout: column and
/// row invariants, containment inside the canvas, and determinism.
/// </summary>
public sealed class SequenceLayoutTests
{
    private const double FontSize = 12;

    private static SequenceLayout Layout(string source)
    {
        SequenceParseResult parsed = SequenceParser.Parse(source);
        Assert.True(parsed.Success, parsed.FailureMessage);

        SequenceLayoutResult result = SequenceLayoutEngine.Compute(
            parsed.Model!, FontSize, SequenceMetrics.Default, SequenceTheme.Default.LabelWrapChars);

        Assert.True(result.Success, result.FailureMessage);
        Assert.NotNull(result.Layout);
        return result.Layout;
    }

    [Fact]
    public void Actor_Columns_Advance_Left_To_Right_In_First_Mention_Order()
    {
        SequenceLayout layout = Layout("""
            sequenceDiagram
                participant A
                participant B
                participant C
                C->>A: out of order
            """);

        Assert.Equal(["A", "B", "C"], layout.Actors.Select(column => column.Actor.Id));
        for (int i = 1; i < layout.Actors.Count; i++)
        {
            Assert.True(layout.Actors[i].CenterX > layout.Actors[i - 1].CenterX);
        }
    }

    [Fact]
    public void Neighbouring_Columns_Never_Overlap()
    {
        SequenceLayout layout = Layout("""
            sequenceDiagram
                participant Left as A very long participant label
                participant Right as B
                Left->>Right: a message with a fairly long label
            """);

        for (int i = 1; i < layout.Actors.Count; i++)
        {
            ActorColumn previous = layout.Actors[i - 1];
            ActorColumn current = layout.Actors[i];
            Assert.True(
                current.CenterX - (current.Width / 2) >= previous.CenterX + (previous.Width / 2));
        }
    }

    [Fact]
    public void Message_Rows_Are_Strictly_Increasing_In_Y_And_Follow_Source_Order()
    {
        SequenceLayout layout = Layout("""
            sequenceDiagram
                A->>B: one
                B->>B: two
                Note over A,B: three
                B-->>A: four
            """);

        Assert.Equal([0, 1, 2, 3], layout.Rows.Select(row => row.Event.Order));
        for (int i = 1; i < layout.Rows.Count; i++)
        {
            Assert.True(layout.Rows[i].Top > layout.Rows[i - 1].Top);
        }
    }

    [Fact]
    public void Message_Endpoints_Sit_On_Their_Actor_Lifelines()
    {
        SequenceLayout layout = Layout("""
            sequenceDiagram
                A->>B: one
                B-->>A: two
            """);

        double a = layout.Actors.Single(column => column.Actor.Id == "A").CenterX;
        double b = layout.Actors.Single(column => column.Actor.Id == "B").CenterX;

        List<MessageRow> rows = layout.Rows.OfType<MessageRow>().ToList();
        Assert.Equal(a, rows[0].StartX);
        Assert.Equal(b, rows[0].EndX);
        Assert.Equal(b, rows[1].StartX);
        Assert.Equal(a, rows[1].EndX);
    }

    [Fact]
    public void A_Self_Message_Loops_Out_From_Its_Own_Lifeline()
    {
        SequenceLayout layout = Layout("sequenceDiagram\n    A->>A: retry\n");

        var row = Assert.IsType<SelfMessageRow>(Assert.Single(layout.Rows));
        Assert.Equal(layout.Actors[0].CenterX, row.LifelineX);
        Assert.True(row.LoopWidth > 0);
        Assert.True(row.LoopHeight > 0);
    }

    [Fact]
    public void Everything_Drawn_Stays_Inside_The_Canvas()
    {
        SequenceLayout layout = Layout("""
            sequenceDiagram
                participant A as Alice
                participant B as Bob
                Note left of A: a note hanging off the left edge
                A->>A: a self message with a long trailing label
                A->>B: hello
                Note over A,B: spanning note
            """);

        Assert.All(layout.Actors, column =>
        {
            Assert.True(column.CenterX - (column.Width / 2) >= 0);
            Assert.True(column.CenterX + (column.Width / 2) <= layout.Width);
            Assert.True(column.Top >= 0);
        });

        foreach (SequenceRow row in layout.Rows)
        {
            Assert.True(row.Top >= layout.LifelineTop);
            Assert.True(row.Top + row.Height <= layout.Height);

            switch (row)
            {
                case NoteRow note:
                    Assert.True(note.BoxLeft >= 0);
                    Assert.True(note.BoxLeft + note.BoxWidth <= layout.Width);
                    break;
                case MessageRow message:
                    Assert.InRange(message.LineY, row.Top, row.Top + row.Height);
                    break;
                default:
                    break;
            }
        }

        Assert.True(layout.LifelineBottom <= layout.Height);
    }

    [Fact]
    public void Lifelines_Start_Below_The_Header_Boxes_And_Outlast_The_Last_Row()
    {
        SequenceLayout layout = Layout("sequenceDiagram\n    A->>B: hi\n");

        ActorColumn column = layout.Actors[0];
        Assert.Equal(column.Top + column.Height, layout.LifelineTop);
        Assert.True(layout.LifelineBottom > layout.Rows[^1].Top);
    }

    [Fact]
    public void Two_Layouts_Of_The_Same_Model_Produce_Identical_Coordinates()
    {
        const string source = """
            sequenceDiagram
                autonumber
                participant A as Alice
                participant B as Bob
                A->>B: hello
                B->>B: think
                Note over A,B: shared
                B-->>A: reply
            """;

        SequenceLayout first = Layout(source);
        SequenceLayout second = Layout(source);

        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.Equal(
            first.Actors.Select(column => column.CenterX),
            second.Actors.Select(column => column.CenterX));
        Assert.Equal(first.Rows.Select(row => row.Top), second.Rows.Select(row => row.Top));
    }

    [Fact]
    public void Too_Many_Participants_Trip_The_Guard_Instead_Of_Laying_Out()
    {
        var metrics = SequenceMetrics.Default with { MaxActors = 2 };
        SequenceParseResult parsed = SequenceParser.Parse(
            "sequenceDiagram\n    A->>B: one\n    B->>C: two\n");

        SequenceLayoutResult result = SequenceLayoutEngine.Compute(
            parsed.Model!, FontSize, metrics, SequenceTheme.Default.LabelWrapChars);

        Assert.False(result.Success);
        Assert.Null(result.Layout);
        Assert.Contains("participants", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_Many_Events_Trip_The_Guard_Instead_Of_Laying_Out()
    {
        var metrics = SequenceMetrics.Default with { MaxEvents = 1 };
        SequenceParseResult parsed = SequenceParser.Parse(
            "sequenceDiagram\n    A->>B: one\n    B->>A: two\n");

        SequenceLayoutResult result = SequenceLayoutEngine.Compute(
            parsed.Model!, FontSize, metrics, SequenceTheme.Default.LabelWrapChars);

        Assert.False(result.Success);
        Assert.Contains("events", result.FailureMessage, StringComparison.Ordinal);
    }
}
