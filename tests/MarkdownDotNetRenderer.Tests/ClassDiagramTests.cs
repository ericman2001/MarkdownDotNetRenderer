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
using MarkdownDotNetRenderer.Mermaid.Class;
using MarkdownDotNetRenderer.Mermaid.Flowchart;
using MarkdownDotNetRenderer.Mermaid.Graph;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 2, 3, and 5 of docs/07-testing-strategy.md for the phase-4 class diagram: the relation
/// operators and their normalization, the three compartments, and deterministic output.
/// </summary>
public sealed class ClassDiagramTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private const string Simple = """
        classDiagram
            class Animal {
                <<interface>>
                +string name
                +speak()
            }
            class Dog {
                +speak()
            }
            Animal <|-- Dog
            Dog : +fetch()
        """;

    private static DiagramRenderResult Render(string source) =>
        new ClassRenderer().Render(source, RenderOptions.Html);

    private static XElement RenderSvg(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.SvgFragment);
        return XElement.Parse(result.SvgFragment);
    }

    [Fact]
    public void The_Parser_Reads_Blocks_Shorthand_Annotations_And_Relations()
    {
        ClassParseResult parsed = ClassParser.Parse(Simple);

        Assert.True(parsed.Success);
        ClassDiagramModel model = parsed.Model!;

        Assert.Equal(["Animal", "Dog"], model.Classes.Select(definition => definition.Name));
        ClassDefinition animal = model.Classes[0];
        Assert.Equal("<<interface>>", animal.Annotation);
        Assert.Equal(["+string name"], animal.Attributes.Select(member => member.Text));
        Assert.Equal(["+speak()"], animal.Operations.Select(member => member.Text));
        Assert.Equal(
            ["+speak()", "+fetch()"], model.Classes[1].Operations.Select(member => member.Text));

        ClassRelation relation = Assert.Single(model.Relations);
        Assert.Equal("Animal", relation.SourceId);
        Assert.Equal("Dog", relation.TargetId);
        Assert.Equal(ClassRelationKind.Inheritance, relation.Kind);
    }

    [Theory]
    [InlineData("Animal <|-- Dog", ClassRelationKind.Inheritance, "Animal", "Dog")]
    [InlineData("Dog --|> Animal", ClassRelationKind.Inheritance, "Animal", "Dog")]
    [InlineData("Car *-- Engine", ClassRelationKind.Composition, "Car", "Engine")]
    [InlineData("Engine --* Car", ClassRelationKind.Composition, "Car", "Engine")]
    [InlineData("Team o-- Player", ClassRelationKind.Aggregation, "Team", "Player")]
    [InlineData("A --> B", ClassRelationKind.Association, "A", "B")]
    [InlineData("A ..> B", ClassRelationKind.Dependency, "A", "B")]
    [InlineData("A ..|> B", ClassRelationKind.Realization, "B", "A")]
    [InlineData("A -- B", ClassRelationKind.Link, "A", "B")]
    public void Every_Relation_Operator_Normalizes_To_A_Marker_Carrying_Source(
        string statement,
        ClassRelationKind kind,
        string source,
        string target)
    {
        Assert.True(ClassParser.TryParseRelation(statement, out ClassRelation relation));

        Assert.Equal(kind, relation.Kind);
        Assert.Equal(source, relation.SourceId);
        Assert.Equal(target, relation.TargetId);
    }

    [Fact]
    public void Relation_Labels_And_Cardinalities_Are_Kept_With_Their_Own_End()
    {
        Assert.True(
            ClassParser.TryParseRelation("Order \"1\" *-- \"many\" Item : contains", out var relation));

        Assert.Equal("Order", relation.SourceId);
        Assert.Equal("Item", relation.TargetId);
        Assert.Equal("1", relation.SourceCardinality);
        Assert.Equal("many", relation.TargetCardinality);
        Assert.Equal("contains", relation.Label);
    }

    [Theory]
    [InlineData(ClassRelationKind.Inheritance, GraphMarker.HollowTriangle, false)]
    [InlineData(ClassRelationKind.Realization, GraphMarker.HollowTriangle, true)]
    [InlineData(ClassRelationKind.Composition, GraphMarker.FilledDiamond, false)]
    [InlineData(ClassRelationKind.Aggregation, GraphMarker.HollowDiamond, false)]
    public void Marker_Carrying_Kinds_Draw_Their_Glyph_At_The_Source_End(
        ClassRelationKind kind,
        GraphMarker marker,
        bool dashed)
    {
        Assert.Equal(marker, ClassLayoutEngine.StartMarker(kind));
        Assert.Null(ClassLayoutEngine.EndMarker(kind));
        Assert.Equal(dashed, ClassLayoutEngine.IsDashed(kind));
    }

    [Theory]
    [InlineData(ClassRelationKind.Association, false)]
    [InlineData(ClassRelationKind.Dependency, true)]
    public void Arrow_Kinds_Draw_Their_Glyph_At_The_Target_End(ClassRelationKind kind, bool dashed)
    {
        Assert.Equal(GraphMarker.Arrow, ClassLayoutEngine.EndMarker(kind));
        Assert.Null(ClassLayoutEngine.StartMarker(kind));
        Assert.Equal(dashed, ClassLayoutEngine.IsDashed(kind));
    }

    [Theory]
    [InlineData("classDiagram\n")]
    [InlineData("flowchart TD\n    A --> B\n")]
    public void Sources_Without_Classes_Degrade_To_Mermaid002(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
    }

    [Fact]
    public void Deferred_Statements_Report_One_Mermaid003_Each_And_Still_Render()
    {
        DiagramRenderResult result = Render("""
            classDiagram
                class A
                class B
                A --> B
                namespace Shipping {
                }
                click A "https://example.com"
                click B "https://example.com"
            """);

        Assert.True(result.Success);
        Assert.Equal(
            2,
            result.Diagnostics.Count(
                diagnostic => diagnostic.Code == RenderDiagnostic.IgnoredDiagramFeature));
    }

    [Fact]
    public void Each_Class_Is_Drawn_With_Compartments_Divider_And_Members()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> boxes = svg.Descendants(Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-class-node")
            .ToList();
        Assert.Equal(["Animal", "Dog"], boxes.Select(box => box.Attribute("data-id")!.Value));

        foreach (XElement box in boxes)
        {
            // An outer box, a name compartment, and one divider above the operations.
            Assert.Equal(2, box.Elements(Svg + "rect").Count());
            Assert.Single(box.Elements(Svg + "line"));
        }

        string text = string.Join('\n', svg.Descendants(Svg + "text").Select(t => t.Value));
        foreach (string expected in
            new[] { "<<interface>>", "Animal", "+string name", "+speak()", "+fetch()" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Relation_Marker_Is_Defined_Once_And_Referenced_By_The_Line()
    {
        XElement svg = RenderSvg(Simple);

        XElement marker = Assert.Single(svg.Descendants(Svg + "marker"));
        XElement edge = Assert.Single(
            svg.Descendants(Svg + "g"),
            group => group.Attribute("class")?.Value == "mdnr-edge");
        string geometry = edge.Elements().First().Attribute("marker-start")!.Value;

        Assert.Equal($"url(#{marker.Attribute("id")!.Value})", geometry);
    }

    [Fact]
    public void Boxes_Stay_Inside_The_Canvas_And_Do_Not_Overlap()
    {
        ClassLayoutResult layout = ClassLayoutEngine.Compute(
            ClassParser.Parse(Simple).Model!,
            12,
            ClassTheme.Default,
            new LayoutMetrics(),
            "abc");

        Assert.True(layout.Success);
        List<PlacedNode> boxes = layout.Layout!.Boxes.Select(box => box.Box).ToList();
        Assert.All(boxes, box =>
        {
            Assert.True(box.Left >= 0);
            Assert.True(box.Top >= 0);
            Assert.True(box.Left + box.Width <= layout.Layout.Width);
            Assert.True(box.Top + box.Height <= layout.Layout.Height);
        });

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                bool separated =
                    boxes[i].Left + boxes[i].Width <= boxes[j].Left ||
                    boxes[j].Left + boxes[j].Width <= boxes[i].Left ||
                    boxes[i].Top + boxes[i].Height <= boxes[j].Top ||
                    boxes[j].Top + boxes[j].Height <= boxes[i].Top;
                Assert.True(separated, $"{boxes[i].Id} overlaps {boxes[j].Id}");
            }
        }
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new ClassRenderer();

        Assert.Equal(
            renderer.Render(Simple, RenderOptions.Html).SvgFragment,
            renderer.Render(Simple, RenderOptions.Html).SvgFragment);
    }
}
