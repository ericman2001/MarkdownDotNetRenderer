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
/// Area 2 of docs/07-testing-strategy.md applied to the phase-3 sequence-diagram parser: the
/// supported syntax matrix, first-mention actor ordering, and the tolerant handling of deferred
/// and unknown constructs.
/// </summary>
public sealed class SequenceParserTests
{
    private static SequenceModel Parse(string source)
    {
        SequenceParseResult result = SequenceParser.Parse(source);

        Assert.True(result.Success, result.FailureMessage);
        Assert.NotNull(result.Model);
        return result.Model;
    }

    [Fact]
    public void Participants_Keep_Declaration_Order_And_Their_Aliases()
    {
        SequenceModel model = Parse("""
            sequenceDiagram
                participant C as Client
                actor S as "Server team"
                C->>S: hello
            """);

        Assert.Equal(["C", "S"], model.Actors.Select(actor => actor.Id));
        Assert.Equal(["Client", "Server team"], model.Actors.Select(actor => actor.Label));
        Assert.Equal([0, 1], model.Actors.Select(actor => actor.Order));
    }

    [Fact]
    public void Undeclared_Actors_Are_Added_In_First_Mention_Order_Labelled_By_Id()
    {
        SequenceModel model = Parse("""
            sequenceDiagram
                Bob->>Alice: hi
                Carol->>Bob: hey
            """);

        Assert.Equal(["Bob", "Alice", "Carol"], model.Actors.Select(actor => actor.Id));
        Assert.All(model.Actors, actor => Assert.Equal(actor.Id, actor.Label));
    }

    [Fact]
    public void A_Later_Participant_Declaration_Supplies_The_Display_Label()
    {
        SequenceModel model = Parse("""
            sequenceDiagram
                A->>B: hi
                participant A as Alice
            """);

        Assert.Equal(["A", "B"], model.Actors.Select(actor => actor.Id));
        Assert.Equal("Alice", model.Actors[0].Label);
    }

    [Theory]
    [InlineData("A->>B: t", SequenceLineStyle.Solid, SequenceArrowHead.Filled)]
    [InlineData("A-->>B: t", SequenceLineStyle.Dashed, SequenceArrowHead.Filled)]
    [InlineData("A->B: t", SequenceLineStyle.Solid, SequenceArrowHead.Open)]
    [InlineData("A-->B: t", SequenceLineStyle.Dashed, SequenceArrowHead.Open)]
    [InlineData("A-xB: t", SequenceLineStyle.Solid, SequenceArrowHead.Cross)]
    [InlineData("A--xB: t", SequenceLineStyle.Dashed, SequenceArrowHead.Cross)]
    [InlineData("A-)B: t", SequenceLineStyle.Solid, SequenceArrowHead.Async)]
    [InlineData("A--)B: t", SequenceLineStyle.Dashed, SequenceArrowHead.Async)]
    public void Every_Arrow_Spelling_Maps_To_Its_Line_And_Head(
        string statement,
        SequenceLineStyle line,
        SequenceArrowHead head)
    {
        SequenceModel model = Parse($"sequenceDiagram\n    {statement}\n");

        var message = Assert.IsType<SequenceMessage>(Assert.Single(model.Events));
        Assert.Equal("A", message.SourceId);
        Assert.Equal("B", message.TargetId);
        Assert.Equal("t", message.Label);
        Assert.Equal(line, message.Line);
        Assert.Equal(head, message.Head);
    }

    [Fact]
    public void Whitespace_Around_The_Arrow_And_Label_Is_Tolerated()
    {
        SequenceModel model = Parse("sequenceDiagram\n   Alice  ->>  Bob :   Hello there  \n");

        var message = Assert.IsType<SequenceMessage>(Assert.Single(model.Events));
        Assert.Equal("Alice", message.SourceId);
        Assert.Equal("Bob", message.TargetId);
        Assert.Equal("Hello there", message.Label);
    }

    [Fact]
    public void A_Self_Message_Keeps_One_Actor()
    {
        SequenceModel model = Parse("sequenceDiagram\n    A->>A: retry\n");

        var message = Assert.IsType<SequenceMessage>(Assert.Single(model.Events));
        Assert.True(message.IsSelfMessage);
        Assert.Single(model.Actors);
    }

    [Theory]
    [InlineData("Note left of A: text", SequenceNotePlacement.LeftOf, 1)]
    [InlineData("Note right of A: text", SequenceNotePlacement.RightOf, 1)]
    [InlineData("note over A,B: text", SequenceNotePlacement.Over, 2)]
    [InlineData("Note over A: text", SequenceNotePlacement.Over, 1)]
    public void Notes_Parse_With_Their_Placement_And_Actors(
        string statement,
        SequenceNotePlacement placement,
        int actorCount)
    {
        SequenceModel model = Parse($"sequenceDiagram\n    {statement}\n");

        var note = Assert.IsType<SequenceNote>(Assert.Single(model.Events));
        Assert.Equal(placement, note.Placement);
        Assert.Equal(actorCount, note.ActorIds.Count);
        Assert.Equal("text", note.Text);
    }

    [Fact]
    public void Autonumber_Numbers_Messages_But_Not_Notes()
    {
        SequenceModel model = Parse("""
            sequenceDiagram
                autonumber
                A->>B: first
                Note over A: aside
                B-->>A: second
            """);

        List<SequenceMessage> messages = model.Events.OfType<SequenceMessage>().ToList();
        Assert.Equal([1, 2], messages.Select(message => message.Number));
        Assert.Equal("1. first", messages[0].DisplayLabel);
        Assert.Equal("2. second", messages[1].DisplayLabel);
    }

    [Fact]
    public void Without_Autonumber_Labels_Are_Unprefixed()
    {
        SequenceModel model = Parse("sequenceDiagram\n    A->>B: first\n");

        var message = Assert.IsType<SequenceMessage>(Assert.Single(model.Events));
        Assert.Null(message.Number);
        Assert.Equal("first", message.DisplayLabel);
    }

    [Fact]
    public void Events_Keep_Source_Order_Across_Messages_And_Notes()
    {
        SequenceModel model = Parse("""
            sequenceDiagram
                A->>B: one
                Note right of B: aside
                B-->>A: two
            """);

        Assert.Collection(
            model.Events,
            item => Assert.Equal("one", Assert.IsType<SequenceMessage>(item).Label),
            item => Assert.Equal("aside", Assert.IsType<SequenceNote>(item).Text),
            item => Assert.Equal("two", Assert.IsType<SequenceMessage>(item).Label));
        Assert.Equal([0, 1, 2], model.Events.Select(item => item.Order));
    }

    [Fact]
    public void A_Loop_Keeps_Its_Messages_And_Reports_One_Mermaid003()
    {
        SequenceParseResult result = SequenceParser.Parse("""
            sequenceDiagram
                loop every minute
                    A->>B: poll
                    B-->>A: batch
                end
                loop again
                    A->>B: poll
                end
            """);

        Assert.True(result.Success);
        Assert.Equal(3, result.Model!.Events.Count);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
        Assert.Contains("loop", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("alt is ok")]
    [InlineData("opt maybe")]
    [InlineData("par both")]
    [InlineData("critical must")]
    [InlineData("break stop")]
    [InlineData("rect rgb(0,0,0)")]
    [InlineData("box Team")]
    [InlineData("activate B")]
    [InlineData("title A title")]
    public void Each_Deferred_Construct_Reports_One_Info_And_Keeps_The_Messages(string statement)
    {
        SequenceParseResult result = SequenceParser.Parse(
            $"sequenceDiagram\n    {statement}\n    A->>B: inside\n    end\n");

        Assert.True(result.Success);
        Assert.Single(result.Model!.Events);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }

    [Fact]
    public void Activation_Shorthand_Is_Stripped_From_The_Target()
    {
        SequenceParseResult result = SequenceParser.Parse("""
            sequenceDiagram
                A->>+B: request
                B-->>-A: response
            """);

        Assert.True(result.Success);
        Assert.Equal(["A", "B"], result.Model!.Actors.Select(actor => actor.Id));
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Comments_And_Blank_Lines_Are_Ignored()
    {
        SequenceParseResult result = SequenceParser.Parse("""
            sequenceDiagram
                %% a comment

                A->>B: hi %% trailing comment
            """);

        Assert.True(result.Success);
        var message = Assert.IsType<SequenceMessage>(Assert.Single(result.Model!.Events));
        Assert.Equal("hi", message.Label);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void A_Directive_Is_Reported_Once_And_Skipped()
    {
        SequenceParseResult result = SequenceParser.Parse(
            "%%{init: {'theme': 'dark'}}%%\nsequenceDiagram\n    A->>B: hi\n");

        Assert.True(result.Success);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void An_Unknown_Statement_Is_Reported_Once_And_Skipped()
    {
        SequenceParseResult result = SequenceParser.Parse("""
            sequenceDiagram
                A->>B: hi
                what even is this
                nor this
            """);

        Assert.True(result.Success);
        Assert.Single(result.Model!.Events);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }

    [Theory]
    [InlineData("flowchart TD\n    A --> B\n")]
    [InlineData("")]
    [InlineData("%% only a comment\n")]
    [InlineData("sequenceDiagram\n    A->>\n")]
    [InlineData("sequenceDiagram\n")]
    public void Sources_Without_A_Header_Or_Without_Actors_Fail_Without_Throwing(string source)
    {
        SequenceParseResult result = SequenceParser.Parse(source);

        Assert.False(result.Success);
        Assert.Null(result.Model);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public void Crlf_Endings_And_A_Byte_Order_Mark_Are_Normalized()
    {
        SequenceModel model = Parse("\uFEFFsequenceDiagram\r\n    A->>B: hi\r\n");

        Assert.Equal(["A", "B"], model.Actors.Select(actor => actor.Id));
    }

    [Fact]
    public void An_Over_Long_Label_Is_Truncated_Rather_Than_Laid_Out()
    {
        SequenceModel model = Parse(
            $"sequenceDiagram\n    A->>B: {new string('x', SequenceParser.MaxLabelLength + 50)}\n");

        var message = Assert.IsType<SequenceMessage>(Assert.Single(model.Events));
        Assert.Equal(SequenceParser.MaxLabelLength, message.Label.Length);
    }
}
