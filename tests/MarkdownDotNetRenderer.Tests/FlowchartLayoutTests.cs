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

using System.Text;
using MarkdownDotNetRenderer.Mermaid.Flowchart;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 3 of docs/07-testing-strategy.md: layout invariants.</summary>
public sealed class FlowchartLayoutTests
{
    private static FlowchartLayout Layout(string source)
    {
        FlowchartParseResult parsed = FlowchartParser.Parse(source);
        Assert.True(parsed.Success, parsed.FailureMessage);

        LayoutResult result = LayeredLayout.Compute(
            parsed.Model!,
            FlowchartRenderer.MeasureNodes(parsed.Model!, 12),
            LayoutMetrics.Default);

        Assert.True(result.Success, result.FailureMessage);
        Assert.NotNull(result.Layout);
        return result.Layout;
    }

    private static LayoutNode Node(FlowchartLayout layout, string id) =>
        layout.Nodes.Single(node => !node.IsVirtual && node.Id == id);

    [Fact]
    public void A_Chain_Gets_One_Layer_Per_Step()
    {
        FlowchartLayout layout = Layout("flowchart TD\n    A --> B --> C\n");

        Assert.Equal(0, Node(layout, "A").Layer);
        Assert.Equal(1, Node(layout, "B").Layer);
        Assert.Equal(2, Node(layout, "C").Layer);
    }

    [Fact]
    public void Top_Down_Advances_On_Y_And_Left_Right_Advances_On_X()
    {
        FlowchartLayout topDown = Layout("flowchart TD\n    A --> B\n");
        Assert.True(Node(topDown, "B").CenterY > Node(topDown, "A").CenterY);
        Assert.Equal(Node(topDown, "A").CenterX, Node(topDown, "B").CenterX, 3);

        FlowchartLayout leftRight = Layout("flowchart LR\n    A --> B\n");
        Assert.True(Node(leftRight, "B").CenterX > Node(leftRight, "A").CenterX);
        Assert.Equal(Node(leftRight, "A").CenterY, Node(leftRight, "B").CenterY, 3);
    }

    [Fact]
    public void Siblings_Share_A_Layer_And_Do_Not_Overlap()
    {
        FlowchartLayout layout = Layout("""
            flowchart TD
                A --> B
                A --> C
                A --> D
            """);

        Assert.Equal(1, Node(layout, "B").Layer);
        Assert.Equal(1, Node(layout, "C").Layer);
        Assert.Equal(1, Node(layout, "D").Layer);
        AssertNoOverlaps(layout);
    }

    [Theory]
    [InlineData("flowchart TD\n    A --> B\n    B --> C\n    C --> A\n")]
    [InlineData("flowchart TD\n    A --> B\n    B --> A\n")]
    [InlineData("flowchart TD\n    A --> A\n    A --> B\n")]
    public void Cycles_Are_Laid_Out_Without_Hanging(string source)
    {
        FlowchartLayout layout = Layout(source);

        Assert.NotEmpty(layout.Nodes);
        AssertNoOverlaps(layout);
    }

    [Fact]
    public void Long_Edges_Get_Virtual_Bend_Points()
    {
        FlowchartLayout layout = Layout("flowchart TD\n    A --> B --> C\n    A --> C\n");

        Assert.Contains(layout.Nodes, node => node.IsVirtual);
        LayoutEdge longEdge = layout.Edges.Single(
            edge => edge.Edge.SourceId == "A" && edge.Edge.TargetId == "C");
        Assert.Equal(3, longEdge.Points.Count);
    }

    [Fact]
    public void A_Reversed_Edge_Still_Runs_From_Its_Own_Source_To_Its_Own_Target()
    {
        FlowchartLayout layout = Layout("flowchart TD\n    A --> B\n    B --> A\n");
        LayoutEdge back = layout.Edges.Single(
            edge => edge.Edge.SourceId == "B" && edge.Edge.TargetId == "A");

        Assert.True(back.Reversed);
        Assert.Equal(Node(layout, "B").CenterY, back.Points[0].Y, 3);
        Assert.Equal(Node(layout, "A").CenterY, back.Points[^1].Y, 3);
    }

    [Fact]
    public void Every_Node_Sits_Inside_The_Reported_Canvas()
    {
        FlowchartLayout layout = Layout("""
            flowchart LR
                A[A rather long node label here] --> B{Branch}
                B --> C([Stadium])
                B --> D(Rounded)
                C --> E[End]
                D --> E
            """);

        foreach (LayoutNode node in layout.Nodes)
        {
            Assert.InRange(node.Left, 0, layout.Width);
            Assert.InRange(node.Top, 0, layout.Height);
            Assert.InRange(node.Left + node.Width, 0, layout.Width);
            Assert.InRange(node.Top + node.Height, 0, layout.Height);
        }

        AssertNoOverlaps(layout);
    }

    [Fact]
    public void Layout_Is_Deterministic_Across_Runs()
    {
        const string Source = """
            flowchart TD
                A --> B
                A --> C
                B --> D
                C --> D
                D --> E
                A --> E
            """;

        Assert.Equal(Describe(Layout(Source)), Describe(Layout(Source)));
    }

    [Fact]
    public void The_Node_Guard_Refuses_Oversized_Diagrams()
    {
        var source = new StringBuilder("flowchart TD\n");
        for (int i = 0; i < 600; i++)
        {
            source.Append("    N").Append(i).Append(" --> N").Append(i + 1).Append('\n');
        }

        FlowchartParseResult parsed = FlowchartParser.Parse(source.ToString());
        Assert.True(parsed.Success, parsed.FailureMessage);

        LayoutResult result = LayeredLayout.Compute(
            parsed.Model!,
            FlowchartRenderer.MeasureNodes(parsed.Model!, 12),
            LayoutMetrics.Default);

        Assert.False(result.Success);
        Assert.Contains("limit", result.FailureMessage, StringComparison.Ordinal);
    }

    private static string Describe(FlowchartLayout layout)
    {
        var text = new StringBuilder();
        foreach (LayoutNode node in layout.Nodes)
        {
            text.Append(node.Id).Append(':').Append(node.Layer).Append(':')
                .Append(node.CenterX.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',')
                .Append(node.CenterY.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return text.ToString();
    }

    private static void AssertNoOverlaps(FlowchartLayout layout)
    {
        List<LayoutNode> real = layout.Nodes.Where(node => !node.IsVirtual).ToList();
        for (int i = 0; i < real.Count; i++)
        {
            for (int j = i + 1; j < real.Count; j++)
            {
                bool overlaps =
                    real[i].Left < real[j].Left + real[j].Width &&
                    real[j].Left < real[i].Left + real[i].Width &&
                    real[i].Top < real[j].Top + real[j].Height &&
                    real[j].Top < real[i].Top + real[i].Height;

                Assert.False(
                    overlaps,
                    $"Nodes '{real[i].Id}' and '{real[j].Id}' overlap.");
            }
        }
    }
}
