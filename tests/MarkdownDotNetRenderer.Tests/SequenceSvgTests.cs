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
using MarkdownDotNetRenderer.Mermaid.Sequence;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 3 and 5 of docs/07-testing-strategy.md: the sequence renderer's SVG emission, its
/// arrowhead markers, self-containment, and byte-identical repeated renders.
/// </summary>
public sealed class SequenceSvgTests
{
    private static string Fragment(string source, RenderOptions? options = null)
    {
        DiagramRenderResult result = new SequenceRenderer().Render(
            source, options ?? RenderOptions.Html);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.SvgFragment);
        return result.SvgFragment;
    }

    private static XElement RenderSvg(string source, RenderOptions? options = null) =>
        ParseSvg(Fragment(source, options));

    private static IEnumerable<XElement> Groups(XElement svg, string className) =>
        svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(g => g.Attribute("class")?.Value == className);

    [Theory]
    [InlineData("sequenceDiagram\n    A->>B: hi\n")]
    [InlineData("sequenceDiagram\n    autonumber\n    A->>A: think\n")]
    [InlineData("sequenceDiagram\n    Note over A,B: <it> & \"that\"\n    A-xB: gone\n")]
    [InlineData("sequenceDiagram\n    participant A as Alice\n    Note left of A: aside\n")]
    public void Every_Diagram_Is_Well_Formed_Xml(string source)
    {
        XElement svg = RenderSvg(source);

        Assert.Equal(DiagramTestHelpers.Svg + "svg", svg.Name);
    }

    [Fact]
    public void The_Root_Carries_Size_ViewBox_And_Accessible_Metadata()
    {
        XElement svg = RenderSvg("sequenceDiagram\n    A->>B: hi\n");

        Assert.NotNull(svg.Attribute("width"));
        Assert.NotNull(svg.Attribute("height"));
        Assert.Equal("img", svg.Attribute("role")?.Value);
        Assert.Contains(
            "sequence diagram", svg.Attribute("aria-label")?.Value, StringComparison.Ordinal);

        string[] viewBox = svg.Attribute("viewBox")!.Value.Split(' ');
        Assert.Equal(4, viewBox.Length);
        Assert.Equal("0", viewBox[0]);
        Assert.Equal("0", viewBox[1]);
    }

    [Fact]
    public void Each_Participant_Gets_One_Header_Box_And_One_Lifeline()
    {
        XElement svg = RenderSvg("""
            sequenceDiagram
                participant A as Alice
                participant B as Bob
                A->>B: hi
            """);

        List<XElement> actors = Groups(svg, "mdnr-actor").ToList();
        Assert.Equal(["A", "B"], actors.Select(actor => actor.Attribute("data-id")!.Value));
        Assert.All(actors, actor =>
        {
            Assert.NotNull(actor.Element(DiagramTestHelpers.Svg + "rect"));
            Assert.NotNull(actor.Element(DiagramTestHelpers.Svg + "text"));
        });
        Assert.Equal(
            ["Alice", "Bob"],
            actors.Select(actor => actor.Element(DiagramTestHelpers.Svg + "text")!.Value));

        List<XElement> lifelines = svg.Descendants(DiagramTestHelpers.Svg + "line")
            .Where(line => line.Attribute("class")?.Value == "mdnr-lifeline")
            .ToList();
        Assert.Equal(["A", "B"], lifelines.Select(line => line.Attribute("data-id")!.Value));
        Assert.All(lifelines, line => Assert.NotNull(line.Attribute("stroke-dasharray")));
    }

    [Fact]
    public void Each_Message_Becomes_One_Group_Carrying_Its_Endpoints()
    {
        XElement svg = RenderSvg("""
            sequenceDiagram
                A->>B: one
                B-->>A: two
                A->>A: three
            """);

        List<XElement> messages = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(g => g.Attribute("data-source") is not null)
            .ToList();

        Assert.Equal(3, messages.Count);
        Assert.Equal(["A", "B", "A"], messages.Select(m => m.Attribute("data-source")!.Value));
        Assert.Equal(["B", "A", "A"], messages.Select(m => m.Attribute("data-target")!.Value));
        Assert.Equal(
            2,
            messages.Count(m => m.Element(DiagramTestHelpers.Svg + "line") is not null));
        Assert.Single(messages, m => m.Element(DiagramTestHelpers.Svg + "path") is not null);
    }

    [Fact]
    public void Dashed_Messages_Are_Dashed_And_Solid_Ones_Are_Not()
    {
        XElement svg = RenderSvg("sequenceDiagram\n    A->>B: solid\n    B-->>A: dashed\n");

        List<XElement> lines = svg.Descendants(DiagramTestHelpers.Svg + "line")
            .Where(line => line.Attribute("class")?.Value != "mdnr-lifeline")
            .ToList();

        Assert.Null(lines[0].Attribute("stroke-dasharray"));
        Assert.NotNull(lines[1].Attribute("stroke-dasharray"));
    }

    [Fact]
    public void The_Four_Arrow_Styles_Use_Four_Distinct_Markers_Defined_Once()
    {
        XElement svg = RenderSvg("""
            sequenceDiagram
                A->>B: filled
                A->B: open
                A-xB: cross
                A-)B: async
            """);

        List<string> markerIds = svg.Descendants(DiagramTestHelpers.Svg + "marker")
            .Select(marker => marker.Attribute("id")!.Value)
            .ToList();
        Assert.Equal(4, markerIds.Count);
        Assert.Equal(markerIds.Count, markerIds.Distinct(StringComparer.Ordinal).Count());

        List<string> used = svg.Descendants(DiagramTestHelpers.Svg + "line")
            .Where(line => line.Attribute("marker-end") is not null)
            .Select(line => line.Attribute("marker-end")!.Value)
            .ToList();
        Assert.Equal(4, used.Distinct(StringComparer.Ordinal).Count());
        Assert.All(used, marker => Assert.Contains(
            markerIds,
            id => string.Equals(marker, $"url(#{id})", StringComparison.Ordinal)));
    }

    [Fact]
    public void Marker_Ids_Differ_Between_Two_Diagrams_In_One_Document()
    {
        string first = Fragment("sequenceDiagram\n    A->>B: one\n");
        string second = Fragment("sequenceDiagram\n    A->>B: two\n");

        string firstId = XElement.Parse(first)
            .Descendants(DiagramTestHelpers.Svg + "marker").First().Attribute("id")!.Value;
        string secondId = XElement.Parse(second)
            .Descendants(DiagramTestHelpers.Svg + "marker").First().Attribute("id")!.Value;

        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void Notes_Become_A_Box_With_Their_Text()
    {
        XElement svg = RenderSvg("""
            sequenceDiagram
                participant A
                participant B
                Note over A,B: shared context
            """);

        XElement note = Assert.Single(Groups(svg, "mdnr-note"));
        Assert.Equal("A,B", note.Attribute("data-actors")!.Value);
        Assert.NotNull(note.Element(DiagramTestHelpers.Svg + "rect"));
        Assert.Equal("shared context", note.Element(DiagramTestHelpers.Svg + "text")!.Value);
    }

    [Fact]
    public void Autonumbered_Labels_Are_Rendered_With_Their_Prefix()
    {
        XElement svg = RenderSvg("sequenceDiagram\n    autonumber\n    A->>B: hello\n");

        Assert.Contains(
            svg.Descendants(DiagramTestHelpers.Svg + "tspan"),
            span => span.Value == "1. hello");
    }

    [Fact]
    public void Special_Characters_In_Labels_Are_Escaped_Not_Injected()
    {
        string fragment = Fragment("sequenceDiagram\n    A->>B: a & b <c> \"d\"\n");

        Assert.DoesNotContain("<c>", fragment, StringComparison.Ordinal);
        Assert.Contains("&amp;", fragment, StringComparison.Ordinal);
        Assert.Contains(
            XElement.Parse(fragment).Descendants(DiagramTestHelpers.Svg + "tspan"),
            span => span.Value.Contains("a & b <c>", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Fragment_Is_Self_Contained_With_No_Scripts_Or_External_References()
    {
        string fragment = Fragment("""
            sequenceDiagram
                participant A as Alice
                A->>A: think
                Note left of A: aside
            """);

        Assert.DoesNotContain("<script", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<image", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("xmlns=\"http", fragment, StringComparison.Ordinal);

        // The only absolute URL is the SVG namespace itself.
        Assert.Single(
            fragment.Split("http", StringSplitOptions.None).Skip(1).ToList());
    }

    [Fact]
    public void A_Wide_Diagram_Is_Scaled_Down_Rather_Than_Clipped()
    {
        var options = new RenderOptions { MaxDiagramWidth = 120 };
        XElement svg = RenderSvg("""
            sequenceDiagram
                participant A as Alice
                participant B as Bob
                participant C as Carol
                A->>B: hello there
                B->>C: and onwards
            """, options);

        double width = double.Parse(
            svg.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        double viewBoxWidth = double.Parse(
            svg.Attribute("viewBox")!.Value.Split(' ')[2], CultureInfo.InvariantCulture);

        Assert.Equal(120, width);
        Assert.True(viewBoxWidth > width);
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        const string source = """
            sequenceDiagram
                autonumber
                participant A as Alice
                actor B as Bob
                A->>B: hello
                B->>B: think
                Note over A,B: shared
                B--xA: give up
            """;

        Assert.Equal(Fragment(source), Fragment(source));
    }

    [Fact]
    public void Fractional_Coordinates_Use_A_Dot_As_The_Decimal_Separator()
    {
        // The build runs in globalization-invariant mode, so this pins the formatting contract
        // rather than the ambient culture: every number must be invariant-formatted.
        XElement svg = RenderSvg("sequenceDiagram\n    A->>B: hi\n");

        List<string> numbers = svg.Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .Where(value => value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '-'))
            .ToList();

        Assert.NotEmpty(numbers);
        Assert.All(numbers, value => Assert.DoesNotContain(",", value, StringComparison.Ordinal));
        Assert.All(
            numbers.Where(value => value.Contains('.', StringComparison.Ordinal)),
            value => Assert.True(double.TryParse(
                value, CultureInfo.InvariantCulture, out double _)));
    }

    [Fact]
    public void Malformed_Source_Fails_With_Mermaid002_And_Does_Not_Throw()
    {
        DiagramRenderResult result = new SequenceRenderer().Render(
            "sequenceDiagram\n", RenderOptions.Html);

        Assert.False(result.Success);
        Assert.Null(result.SvgFragment);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.DiagramParseFailure, diagnostic.Code);
    }

    [Fact]
    public void Parser_Diagnostics_Survive_A_Successful_Render()
    {
        DiagramRenderResult result = new SequenceRenderer().Render("""
            sequenceDiagram
                loop every minute
                    A->>B: poll
                end
            """, RenderOptions.Html);

        Assert.True(result.Success);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }
}
