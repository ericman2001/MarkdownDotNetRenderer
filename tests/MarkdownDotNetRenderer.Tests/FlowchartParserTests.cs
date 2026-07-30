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

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 3 of docs/07-testing-strategy.md: the flowchart syntax matrix.</summary>
public sealed class FlowchartParserTests
{
    private static FlowchartModel Parse(string source)
    {
        FlowchartParseResult result = FlowchartParser.Parse(source);
        Assert.True(result.Success, result.FailureMessage);
        Assert.NotNull(result.Model);
        return result.Model;
    }

    [Theory]
    [InlineData("flowchart TD", FlowDirection.TopDown)]
    [InlineData("flowchart TB", FlowDirection.TopDown)]
    [InlineData("flowchart LR", FlowDirection.LeftRight)]
    [InlineData("graph TD", FlowDirection.TopDown)]
    [InlineData("graph LR", FlowDirection.LeftRight)]
    [InlineData("FLOWCHART td", FlowDirection.TopDown)]
    public void Headers_Set_The_Direction(string header, FlowDirection expected)
    {
        Assert.Equal(expected, Parse($"{header}\n    A --> B\n").Direction);
    }

    [Theory]
    [InlineData("BT", FlowDirection.TopDown)]
    [InlineData("RL", FlowDirection.LeftRight)]
    public void Reversed_Directions_Are_Normalized_With_Mermaid003(
        string direction,
        FlowDirection expected)
    {
        FlowchartParseResult result = FlowchartParser.Parse($"flowchart {direction}\n    A --> B\n");

        Assert.True(result.Success);
        Assert.Equal(expected, result.Model!.Direction);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.IgnoredDiagramFeature);
    }

    [Theory]
    [InlineData("A[Label]", FlowNodeShape.Rectangle, "Label")]
    [InlineData("A(Label)", FlowNodeShape.Rounded, "Label")]
    [InlineData("A([Label])", FlowNodeShape.Stadium, "Label")]
    [InlineData("A{Label}", FlowNodeShape.Rhombus, "Label")]
    [InlineData("A", FlowNodeShape.Rectangle, "A")]
    [InlineData("A[\"Quoted, label\"]", FlowNodeShape.Rectangle, "Quoted, label")]
    public void Node_Shapes_And_Labels_Are_Parsed(
        string spec,
        FlowNodeShape shape,
        string label)
    {
        FlowchartModel model = Parse($"flowchart TD\n    {spec} --> Z\n");
        FlowNode node = model.Nodes[0];

        Assert.Equal("A", node.Id);
        Assert.Equal(shape, node.Shape);
        Assert.Equal(label, node.Label);
    }

    [Fact]
    public void A_Bare_Node_Statement_Registers_The_Node()
    {
        FlowchartModel model = Parse("flowchart TD\n    Alone[Only node]\n");

        FlowNode node = Assert.Single(model.Nodes);
        Assert.Equal("Alone", node.Id);
        Assert.Empty(model.Edges);
    }

    [Fact]
    public void Directed_And_Undirected_Links_Are_Distinguished()
    {
        FlowchartModel model = Parse("flowchart TD\n    A --> B\n    B --- C\n");

        Assert.True(model.Edges[0].Directed);
        Assert.False(model.Edges[1].Directed);
    }

    [Theory]
    [InlineData("A -->|yes| B", "yes")]
    [InlineData("A -- maybe --> B", "maybe")]
    [InlineData("A ---|both| B", "both")]
    [InlineData("A --> B", null)]
    public void Edge_Labels_Are_Parsed_In_Both_Forms(string statement, string? expected)
    {
        FlowchartModel model = Parse($"flowchart TD\n    {statement}\n");

        Assert.Equal(expected, Assert.Single(model.Edges).Label);
    }

    [Fact]
    public void Chains_Become_Consecutive_Edges()
    {
        FlowchartModel model = Parse("flowchart TD\n    A --> B --> C\n");

        Assert.Collection(
            model.Edges,
            edge => Assert.Equal(("A", "B"), (edge.SourceId, edge.TargetId)),
            edge => Assert.Equal(("B", "C"), (edge.SourceId, edge.TargetId)));
    }

    [Fact]
    public void Semicolons_Separate_Statements()
    {
        FlowchartModel model = Parse("flowchart TD; A --> B; B --> C;\n");

        Assert.Equal(3, model.Nodes.Count);
        Assert.Equal(2, model.Edges.Count);
    }

    [Fact]
    public void Ampersand_Shorthand_Fans_Out_And_Reports_Mermaid003()
    {
        FlowchartParseResult result = FlowchartParser.Parse("flowchart TD\n    A & B --> C\n");

        Assert.True(result.Success);
        Assert.Equal(2, result.Model!.Edges.Count);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.IgnoredDiagramFeature);
    }

    [Fact]
    public void Comments_Are_Ignored()
    {
        FlowchartModel model = Parse("""
            %% leading comment
            flowchart TD
                %% about the edge
                A --> B %% trailing comment
            """);

        Assert.Equal(2, model.Nodes.Count);
        Assert.Single(model.Edges);
    }

    [Fact]
    public void Nodes_Are_Registered_In_First_Mention_Order_And_Labels_Are_Kept()
    {
        FlowchartModel model = Parse("""
            flowchart TD
                B --> A
                A[Named later] --> C
            """);

        Assert.Equal(["B", "A", "C"], model.Nodes.Select(node => node.Id));
        Assert.Equal("Named later", model.Nodes.Single(node => node.Id == "A").Label);
    }

    [Theory]
    [InlineData("subgraph One")]
    [InlineData("classDef hot fill:#f00")]
    [InlineData("class A hot")]
    [InlineData("style A fill:#eee")]
    [InlineData("click A \"page.html\"")]
    [InlineData("linkStyle 0 stroke:#f00")]
    public void Unsupported_Constructs_Are_Ignored_Once_And_Parsing_Continues(string statement)
    {
        FlowchartParseResult result = FlowchartParser.Parse(
            $"flowchart TD\n    {statement}\n    {statement}\n    A --> B\n");

        Assert.True(result.Success, result.FailureMessage);
        Assert.Equal(2, result.Model!.Nodes.Count);
        Assert.Single(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.IgnoredDiagramFeature);
    }

    [Fact]
    public void Subgraph_Bodies_Still_Contribute_Their_Nodes()
    {
        FlowchartModel model = Parse("""
            flowchart TD
                subgraph Ingest
                    Q[Queue] --> W[Worker]
                end
                W --> S[Store]
            """);

        Assert.Equal(["Q", "W", "S"], model.Nodes.Select(node => node.Id));
        Assert.Equal(2, model.Edges.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A --> B\n")]
    [InlineData("sequenceDiagram\n    A ->> B: hi\n")]
    public void A_Missing_Or_Wrong_Header_Fails(string source)
    {
        FlowchartParseResult result = FlowchartParser.Parse(source);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public void A_Link_With_A_Missing_Endpoint_Fails()
    {
        FlowchartParseResult result = FlowchartParser.Parse("flowchart TD\n    A -->\n");

        Assert.False(result.Success);
        Assert.Contains("endpoint", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Header_Without_Nodes_Fails()
    {
        FlowchartParseResult result = FlowchartParser.Parse("flowchart TD\n");

        Assert.False(result.Success);
    }

    [Fact]
    public void Self_Loops_Are_Kept()
    {
        FlowchartModel model = Parse("flowchart TD\n    A --> A\n");

        FlowEdge edge = Assert.Single(model.Edges);
        Assert.Equal("A", edge.SourceId);
        Assert.Equal("A", edge.TargetId);
    }
}
