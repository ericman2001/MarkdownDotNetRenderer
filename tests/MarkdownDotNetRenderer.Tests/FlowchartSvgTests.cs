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
using System.Xml.Linq;
using MarkdownDotNetRenderer.Mermaid;
using MarkdownDotNetRenderer.Mermaid.Flowchart;
using MarkdownDotNetRenderer.Svg;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Area 3 and area 5 of docs/07-testing-strategy.md: SVG emission.</summary>
public sealed class FlowchartSvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static XElement RenderSvg(string source, RenderOptions? options = null)
    {
        DiagramRenderResult result = new FlowchartRenderer().Render(
            source, options ?? RenderOptions.Html);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.SvgFragment);
        return XElement.Parse(result.SvgFragment);
    }

    [Theory]
    [InlineData("flowchart TD\n    A[One] --> B[Two]\n")]
    [InlineData("flowchart LR\n    A{\"Quotes & <angles>\"} --> B([Stadium])\n")]
    [InlineData("flowchart TD\n    A --> B --> C\n    A --> C\n")]
    [InlineData("flowchart TD\n    A --> A\n")]
    [InlineData("flowchart TD\n    A -->|label| B\n")]
    public void Every_Diagram_Is_Well_Formed_Xml(string source)
    {
        XElement svg = RenderSvg(source);

        Assert.Equal(Svg + "svg", svg.Name);
    }

    [Fact]
    public void The_Root_Carries_Size_ViewBox_And_Accessible_Metadata()
    {
        XElement svg = RenderSvg("flowchart TD\n    A[One] --> B[Two]\n");

        Assert.NotNull(svg.Attribute("width"));
        Assert.NotNull(svg.Attribute("height"));
        Assert.Equal("img", svg.Attribute("role")?.Value);
        Assert.Contains("flowchart", svg.Attribute("aria-label")?.Value, StringComparison.Ordinal);

        string[] viewBox = svg.Attribute("viewBox")!.Value.Split(' ');
        Assert.Equal(4, viewBox.Length);
        Assert.Equal("0", viewBox[0]);
        Assert.Equal("0", viewBox[1]);
    }

    [Fact]
    public void Special_Characters_In_Labels_Are_Escaped_Not_Injected()
    {
        DiagramRenderResult result = new FlowchartRenderer().Render(
            "flowchart TD\n    A[\"a & b <c> 'd' \\\"e\\\"\"] --> B\n", RenderOptions.Html);

        Assert.True(result.Success);
        Assert.DoesNotContain("<c>", result.SvgFragment, StringComparison.Ordinal);
        Assert.Contains("&amp;", result.SvgFragment, StringComparison.Ordinal);

        XElement svg = XElement.Parse(result.SvgFragment!);
        Assert.Contains(
            svg.Descendants(Svg + "tspan"),
            span => span.Value.Contains("a & b <c>", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_Node_Becomes_One_Group_With_A_Shape_And_A_Label()
    {
        XElement svg = RenderSvg("""
            flowchart TD
                A[Rect] --> B(Rounded)
                B --> C([Stadium])
                C --> D{Rhombus}
            """);

        List<XElement> nodes = svg.Descendants(Svg + "g")
            .Where(g => g.Attribute("class")?.Value == "mdnr-node")
            .ToList();

        Assert.Equal(4, nodes.Count);
        Assert.Equal(["A", "B", "C", "D"], nodes.Select(n => n.Attribute("data-id")!.Value));
        Assert.Equal(3, nodes.Count(n => n.Element(Svg + "rect") is not null));
        Assert.Single(nodes, n => n.Element(Svg + "polygon") is not null);
        Assert.All(nodes, node => Assert.NotNull(node.Element(Svg + "text")));
    }

    [Fact]
    public void Directed_Edges_Get_An_Arrow_Marker_And_Undirected_Ones_Do_Not()
    {
        XElement svg = RenderSvg("flowchart TD\n    A --> B\n    B --- C\n");

        List<XElement> edges = svg.Descendants(Svg + "g")
            .Where(g => g.Attribute("class")?.Value == "mdnr-edge")
            .ToList();

        Assert.Equal(2, edges.Count);
        XElement marker = Assert.Single(svg.Descendants(Svg + "marker"));
        string markerId = marker.Attribute("id")!.Value;

        XElement directed = edges[0].Elements().Single();
        Assert.Equal($"url(#{markerId})", directed.Attribute("marker-end")?.Value);
        Assert.Null(edges[1].Elements().Single().Attribute("marker-end"));
    }

    [Fact]
    public void Edge_Labels_Are_Emitted_With_A_Backing_Rectangle()
    {
        XElement svg = RenderSvg("flowchart TD\n    A -->|yes| B\n");

        XElement label = svg.Descendants(Svg + "g")
            .Single(g => g.Attribute("class")?.Value == "mdnr-edge-label");

        Assert.NotNull(label.Element(Svg + "rect"));
        Assert.Equal("yes", label.Element(Svg + "text")?.Value);
    }

    [Fact]
    public void Edge_Endpoints_Are_Clipped_To_The_Node_Boundary()
    {
        XElement svg = RenderSvg("flowchart TD\n    A[Source] --> B[Target]\n");
        XElement line = Assert.Single(svg.Descendants(Svg + "line"));

        double y1 = double.Parse(line.Attribute("y1")!.Value, CultureInfo.InvariantCulture);
        double y2 = double.Parse(line.Attribute("y2")!.Value, CultureInfo.InvariantCulture);
        XElement sourceRect = svg.Descendants(Svg + "g")
            .Single(g => g.Attribute("data-id")?.Value == "A")
            .Element(Svg + "rect")!;
        double sourceBottom =
            double.Parse(sourceRect.Attribute("y")!.Value, CultureInfo.InvariantCulture) +
            double.Parse(sourceRect.Attribute("height")!.Value, CultureInfo.InvariantCulture);

        Assert.Equal(sourceBottom, y1, 2);
        Assert.True(y2 > y1);
    }

    [Fact]
    public void Element_Ids_Are_Unique_Per_Diagram_And_Stable_Per_Source()
    {
        var renderer = new FlowchartRenderer();
        string first = renderer.Render("flowchart TD\n    A --> B\n", RenderOptions.Html).SvgFragment!;
        string again = renderer.Render("flowchart TD\n    A --> B\n", RenderOptions.Html).SvgFragment!;
        string other = renderer.Render("flowchart LR\n    A --> C\n", RenderOptions.Html).SvgFragment!;

        string firstId = XElement.Parse(first).Descendants(Svg + "marker")
            .Single().Attribute("id")!.Value;
        string againId = XElement.Parse(again).Descendants(Svg + "marker")
            .Single().Attribute("id")!.Value;
        string otherId = XElement.Parse(other).Descendants(Svg + "marker")
            .Single().Attribute("id")!.Value;

        Assert.Equal(firstId, againId);
        Assert.NotEqual(firstId, otherId);
    }

    [Fact]
    public void Rendering_Twice_Yields_Byte_Identical_Svg()
    {
        const string Source = """
            flowchart TD
                A[Start] --> B{Choice}
                B -->|left| C(One)
                B -->|right| D([Two])
                C --> E[End]
                D --> E
            """;

        var renderer = new FlowchartRenderer();

        Assert.Equal(
            renderer.Render(Source, RenderOptions.Html).SvgFragment,
            renderer.Render(Source, RenderOptions.Html).SvgFragment);
    }

    [Fact]
    public void Numbers_Use_The_Invariant_Culture()
    {
        Assert.Equal("1.5", SvgBuilder.Number(1.5));
        Assert.Equal("2", SvgBuilder.Number(2));
        Assert.Equal("-0.25", SvgBuilder.Number(-0.25));
    }

    [Fact]
    public void A_Wide_Diagram_Is_Scaled_Down_Rather_Than_Clipped()
    {
        const string Source = """
            flowchart LR
                A[Aaaaaaaaaaaaaaaaaaaa] --> B[Bbbbbbbbbbbbbbbbbbbb]
                B --> C[Cccccccccccccccccccc]
                C --> D[Dddddddddddddddddddd]
                D --> E[Eeeeeeeeeeeeeeeeeeee]
                E --> F[Ffffffffffffffffffff]
            """;

        XElement svg = RenderSvg(Source, new RenderOptions { MaxDiagramWidth = 200 });

        double width = double.Parse(
            svg.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        double viewBoxWidth = double.Parse(
            svg.Attribute("viewBox")!.Value.Split(' ')[2], CultureInfo.InvariantCulture);

        Assert.Equal(200, width, 2);
        Assert.True(viewBoxWidth > width);
    }

    [Fact]
    public void The_Font_Family_Option_Reaches_Label_Text()
    {
        XElement svg = RenderSvg(
            "flowchart TD\n    A[Label] --> B\n", new RenderOptions { FontFamily = "Iosevka" });

        Assert.All(
            svg.Descendants(Svg + "text"),
            text => Assert.Equal("Iosevka", text.Attribute("font-family")?.Value));
    }

    [Fact]
    public void Long_Labels_Wrap_Onto_Several_Lines()
    {
        XElement svg = RenderSvg(
            "flowchart TD\n    A[A very long label that certainly needs wrapping] --> B\n");

        XElement label = svg.Descendants(Svg + "g")
            .Single(g => g.Attribute("data-id")?.Value == "A")
            .Element(Svg + "text")!;

        Assert.True(label.Elements(Svg + "tspan").Count() > 1);
    }
}
