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

using System.Xml.Linq;
using MarkdownDotNetRenderer.Mermaid;
using MarkdownDotNetRenderer.Mermaid.Flowchart;
using MarkdownDotNetRenderer.Mermaid.State;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 2, 3, and 5 of docs/07-testing-strategy.md for the phase-4 state diagram: parsing,
/// deterministic layout through the shared layered engine, SVG emission, and degradation.
/// </summary>
public sealed class StateDiagramTests
{
    private const string Simple = """
        stateDiagram-v2
            state "Waiting" as Idle
            [*] --> Idle
            Idle --> Busy : work arrives
            Busy --> [*]
        """;

    private static DiagramRenderResult Render(string source) =>
        new StateRenderer().Render(source, RenderOptions.Html);

    private static XElement RenderSvg(string source)
    {
        DiagramRenderResult result = Render(source);

        return DiagramTestHelpers.RenderSvg(result);
    }

    [Theory]
    [InlineData("stateDiagram")]
    [InlineData("stateDiagram-v2")]
    public void Both_Header_Spellings_Are_Accepted(string header)
    {
        StateParseResult parsed = StateParser.Parse($"{header}\n    [*] --> A\n");

        Assert.True(parsed.Success);
        Assert.Equal(FlowDirection.TopDown, parsed.Model!.Direction);
    }

    [Fact]
    public void The_Parser_Reads_Pseudo_States_Aliases_Labels_And_Notes()
    {
        StateParseResult parsed = StateParser.Parse(Simple + "\n    note right of Busy : slow\n");

        Assert.True(parsed.Success);
        StateDiagramModel model = parsed.Model!;

        Assert.Contains(
            model.States,
            state => state.Id == StateParser.StartStateId && state.Kind == StateKind.Start);
        Assert.Contains(
            model.States,
            state => state.Id == StateParser.EndStateId && state.Kind == StateKind.End);
        Assert.Equal("Waiting", model.States.Single(state => state.Id == "Idle").Label);
        Assert.Contains(model.States, state => state.Kind == StateKind.Note);
        Assert.Contains(
            model.Transitions,
            transition => transition.SourceId == "Idle" &&
                transition.TargetId == "Busy" &&
                transition.Label == "work arrives");
    }

    [Fact]
    public void A_Direction_Statement_Switches_The_Layout_Axis()
    {
        StateParseResult parsed = StateParser.Parse(
            "stateDiagram\n    direction LR\n    [*] --> A\n");

        Assert.Equal(FlowDirection.LeftRight, parsed.Model!.Direction);
    }

    [Theory]
    [InlineData("stateDiagram\n    state Composite {\n        A --> B\n    }\n")]
    [InlineData("stateDiagram\n    [*] --> A\n    A --> B\n    --\n    C --> D\n")]
    [InlineData("stateDiagram\n    [*] --> A\n    state Fork <<fork>>\n")]
    [InlineData("stateDiagram\n    [*] --> A\n    classDef hot fill:#f00\n")]
    public void Deferred_Constructs_Report_Mermaid003_And_Still_Render(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.True(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.IgnoredDiagramFeature);
        Assert.All(
            result.Diagnostics.GroupBy(diagnostic => diagnostic.Message),
            group => Assert.Single(group));
    }

    [Theory]
    [InlineData("stateDiagram\n")]
    [InlineData("flowchart TD\n    A --> B\n")]
    public void Sources_Without_States_Degrade_To_Mermaid002(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.False(result.Success);
        Assert.Null(result.SvgFragment);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
    }

    [Fact]
    public void Every_State_Gets_A_Shape_And_Every_Transition_An_Edge()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> states = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value.StartsWith(
                "mdnr-state-node", StringComparison.Ordinal) == true)
            .ToList();
        Assert.Equal(
            ["Busy", "Idle", StateParser.EndStateId, StateParser.StartStateId],
            states.Select(state => state.Attribute("data-id")!.Value)
                .OrderBy(id => id, StringComparer.Ordinal));

        // Pseudo-states are circles; ordinary states are stadium-shaped rectangles.
        Assert.Equal(2, svg.Descendants(DiagramTestHelpers.Svg + "rect").Count(IsStadium));
        Assert.Equal(3, svg.Descendants(DiagramTestHelpers.Svg + "circle").Count());
        Assert.Equal(
            3,
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Count(group => group.Attribute("class")?.Value == "mdnr-edge"));
        Assert.Contains(
            "work arrives",
            svg.Descendants(DiagramTestHelpers.Svg + "text").Select(text => text.Value),
            StringComparer.Ordinal);

        static bool IsStadium(XElement rect) =>
            rect.Attribute("rx") is { Value: not "0" } &&
            rect.Parent?.Attribute("class")?.Value.StartsWith(
                "mdnr-state-node", StringComparison.Ordinal) == true;
    }

    [Fact]
    public void A_Note_Is_Drawn_Dashed_And_Linked_To_Its_State()
    {
        XElement svg = RenderSvg(Simple + "\n    note right of Busy : slow\n");

        Assert.Contains(
            svg.Descendants(DiagramTestHelpers.Svg + "rect"),
            rect => rect.Attribute("stroke-dasharray") is not null);
        Assert.Contains(
            "slow", svg.Descendants(DiagramTestHelpers.Svg + "text").Select(text => text.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void Layout_Coordinates_Are_Stable_Across_Repeated_Computations()
    {
        StateDiagramModel model = StateParser.Parse(Simple).Model!;

        StateLayoutResult first = StateLayoutEngine.Compute(
            model, 12, StateTheme.Default, new LayoutMetrics());
        StateLayoutResult second = StateLayoutEngine.Compute(
            model, 12, StateTheme.Default, new LayoutMetrics());

        Assert.True(first.Success);
        Assert.Equal(
            first.Layout!.Boxes.Select(box => (box.Node.Id, box.Box.CenterX, box.Box.CenterY)),
            second.Layout!.Boxes.Select(box => (box.Node.Id, box.Box.CenterX, box.Box.CenterY)));
        Assert.All(
            first.Layout.Boxes,
            box =>
            {
                Assert.True(box.Box.Left >= 0);
                Assert.True(box.Box.Top >= 0);
                Assert.True(box.Box.Left + box.Box.Width <= first.Layout.Width);
                Assert.True(box.Box.Top + box.Box.Height <= first.Layout.Height);
            });
    }

    [Fact]
    public void An_Alias_Declared_After_The_State_Is_Used_Still_Names_It()
    {
        StateParseResult parsed = StateParser.Parse("""
            stateDiagram-v2
                [*] --> Idle
                state "Waiting for input" as Idle
            """);

        Assert.True(parsed.Success);
        Assert.Equal(
            "Waiting for input", parsed.Model!.States.Single(state => state.Id == "Idle").Label);
    }

    [Fact]
    public void A_Transition_Label_Sits_Clear_Of_The_States_It_Joins()
    {
        XElement svg = RenderSvg("""
            stateDiagram-v2
                direction LR
                state "Waiting for input" as Idle
                Idle --> Parsing : markdown arrives
            """);

        XElement label = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Element(DiagramTestHelpers.Svg + "rect")!;
        double labelLeft = Number(label, "x");
        double labelRight = labelLeft + Number(label, "width");

        foreach (XElement box in svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value.StartsWith(
                "mdnr-state-node", StringComparison.Ordinal) == true)
            .Elements(DiagramTestHelpers.Svg + "rect"))
        {
            double boxLeft = Number(box, "x");
            double boxRight = boxLeft + Number(box, "width");
            Assert.True(
                labelRight <= boxLeft || labelLeft >= boxRight,
                $"label {labelLeft}..{labelRight} overlaps state box {boxLeft}..{boxRight}");
        }
    }

    [Fact]
    public void A_Self_Transition_And_Its_Label_Stay_Inside_The_Canvas()
    {
        DiagramRenderResult result = Render("""
            stateDiagram-v2
                [*] --> Idle
                Idle --> Idle : loop back to itself
            """);

        Assert.True(result.Success);
        XElement svg = XElement.Parse(result.SvgFragment!);
        XElement label = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Element(DiagramTestHelpers.Svg + "rect")!;

        Assert.True(Number(label, "x") >= 0);
        Assert.True(Number(label, "x") + Number(label, "width") <= result.Width);
    }

    [Fact]
    public void A_Self_Transition_Label_Overlaps_Neither_Its_Node_Nor_Another_Label()
    {
        XElement svg = RenderSvg("""
            stateDiagram-v2
                direction LR
                state "Waiting for input" as Idle
                Idle --> Idle : poll
                Idle --> Parsing : markdown arrives
            """);

        List<(double Left, double Right)> labels = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Elements(DiagramTestHelpers.Svg + "rect")
            .Select(rect => (Number(rect, "x"), Number(rect, "x") + Number(rect, "width")))
            .ToList();

        Assert.Equal(2, labels.Count);
        Assert.True(
            labels[0].Right <= labels[1].Left || labels[1].Right <= labels[0].Left,
            $"labels {labels[0]} and {labels[1]} overlap");

        XElement node = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("data-id")?.Value == "Idle")
            .Element(DiagramTestHelpers.Svg + "rect")!;
        double nodeRight = Number(node, "x") + Number(node, "width");
        Assert.All(labels, label => Assert.True(label.Left >= nodeRight));
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new StateRenderer();

        Assert.Equal(
            renderer.Render(Simple, RenderOptions.Html).SvgFragment,
            renderer.Render(Simple, RenderOptions.Html).SvgFragment);
    }
}
