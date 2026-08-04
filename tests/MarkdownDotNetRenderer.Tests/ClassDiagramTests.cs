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
    /// <summary>How finely a relation line is sampled when checking a label does not cover it.</summary>
    private const int LineSamples = 200;

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

        return DiagramTestHelpers.RenderSvg(result);
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

        List<XElement> boxes = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-class-node")
            .ToList();
        Assert.Equal(["Animal", "Dog"], boxes.Select(box => box.Attribute("data-id")!.Value));

        foreach (XElement box in boxes)
        {
            // An outer box, a name compartment, and one divider above the operations.
            Assert.Equal(2, box.Elements(DiagramTestHelpers.Svg + "rect").Count());
            Assert.Single(box.Elements(DiagramTestHelpers.Svg + "line"));
        }

        string text = string.Join(
            '\n',
            svg.Descendants(DiagramTestHelpers.Svg + "text").Select(t => t.Value));
        foreach (string expected in
            new[] { "<<interface>>", "Animal", "+string name", "+speak()", "+fetch()" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_Generic_Class_Is_One_Box_Captioned_With_Its_Type_Parameters()
    {
        XElement svg = RenderSvg("""
            classDiagram
                class Repo~T~ {
                    +Find(id)
                }
                Base <|-- Repo
            """);

        // The type parameters are not part of the class's identity, so the plain 'Repo' in the
        // relation is the same box rather than a second, empty one.
        Assert.Equal(
            ["Base", "Repo"],
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Select(group => group.Attribute("data-id")?.Value)
                .Where(id => id is not null)
                .Order(StringComparer.Ordinal));
        Assert.Contains(
            "Repo<T>",
            svg.Descendants(DiagramTestHelpers.Svg + "text").Select(text => text.Value));
    }

    [Fact]
    public void A_Cardinality_Label_Stays_Off_A_Box_It_Merely_Passes()
    {
        XElement svg = RenderSvg("""
            classDiagram
                class Repo~T~ {
                    +List~int~ ids
                }
                class Base
                class Node
                Base <|-- Repo
                Base "1" *-- "0..*" Leaf
                Base "1" o-- "*" Twig
                Node "1" --> "*" Node : children
            """);

        // A label clears every box, not just the two its own edge joins, and every other label:
        // an opaque backing rect over either erases what it covers.
        List<XElement> boxes = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("data-id") is not null)
            .Elements(DiagramTestHelpers.Svg + "rect")
            .ToList();
        List<XElement> labels = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Elements(DiagramTestHelpers.Svg + "rect")
            .ToList();

        Assert.All(labels, label => Assert.All(boxes, box => Assert.False(
            Overlaps(label, box),
            $"label {label} covers box {box}")));
        for (int i = 0; i < labels.Count; i++)
        {
            for (int j = i + 1; j < labels.Count; j++)
            {
                Assert.False(
                    Overlaps(labels[i], labels[j]),
                    $"label {labels[i]} covers label {labels[j]}");
            }
        }

        // Nor over a relation line, whose stroke an opaque rect would break into dashes, which in a
        // class diagram would read as a dependency.
        Assert.All(
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Where(group => group.Attribute("class")?.Value == "mdnr-edge")
                .Elements(DiagramTestHelpers.Svg + "line"),
            line => Assert.All(labels, label => Assert.False(
                Crosses(label, line),
                $"label {label} covers line {line}")));
    }

    private static bool Crosses(XElement label, XElement line)
    {
        double x = Number(label, "x");
        double y = Number(label, "y");
        double fromX = Number(line, "x1");
        double fromY = Number(line, "y1");
        double runX = Number(line, "x2") - fromX;
        double runY = Number(line, "y2") - fromY;
        for (int step = 0; step <= LineSamples; step++)
        {
            double along = (double)step / LineSamples;
            double atX = fromX + (runX * along);
            double atY = fromY + (runY * along);
            if (atX > x && atX < x + Number(label, "width") &&
                atY > y && atY < y + Number(label, "height"))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_Relation_Glyph_Is_Drawn_As_Geometry_Beside_Its_Line()
    {
        XElement svg = RenderSvg(Simple);

        // Glyphs are real geometry rather than <marker> references, so each fragment stays
        // self-contained with no per-document <defs> to manage.
        Assert.Empty(svg.Descendants(DiagramTestHelpers.Svg + "marker"));
        XElement edge = Assert.Single(
            svg.Descendants(DiagramTestHelpers.Svg + "g"),
            group => group.Attribute("class")?.Value == "mdnr-edge");

        XElement glyph = Assert.Single(
            edge.Elements(DiagramTestHelpers.Svg + "g"),
            group => group.Attribute("class")?.Value == "mdnr-edge-marker");
        Assert.Equal("triangle", glyph.Attribute("data-marker")!.Value);
        Assert.NotEmpty(glyph.Elements(DiagramTestHelpers.Svg + "path"));
    }

    [Fact]
    public void Boxes_Stay_Inside_The_Canvas_And_Do_Not_Overlap()
    {
        ClassLayoutResult layout = ClassLayoutEngine.Compute(
            ClassParser.Parse(Simple).Model!,
            12,
            ClassTheme.Default,
            new LayoutMetrics());

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
    public void A_Composition_Diamond_Is_Drawn_Beside_Its_Class_Box()
    {
        XElement svg = RenderSvg("""
            classDiagram
                A "1" *-- "1" B
            """);

        XElement line = Assert.Single(
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Single(group => group.Attribute("class")?.Value == "mdnr-edge")
                .Elements(DiagramTestHelpers.Svg + "line"));
        double lineTop = Number(line, "y1");
        double boxBottom = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("data-id")?.Value == "A")
            .Elements(DiagramTestHelpers.Svg + "rect")
            .Max(rect => Number(rect, "y") + Number(rect, "height"));

        // The glyph is drawn behind its endpoint, so the endpoint must be a whole glyph clear of
        // the box or the box's fill would hide it.
        double glyph = GraphMarkers.EndpointInset(
            GraphMarker.FilledDiamond,
            ClassTheme.Default.Edge.MarkerSize,
            ClassTheme.Default.Edge.StrokeWidth);
        Assert.True(lineTop - boxBottom >= glyph, $"endpoint {lineTop} is inside box {boxBottom}");
    }

    [Fact]
    public void Cardinality_Labels_Sit_Beside_The_Relation_Line()
    {
        XElement svg = RenderSvg("""
            classDiagram
                A "1" *-- "1" B
            """);

        XElement line = Assert.Single(
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Single(group => group.Attribute("class")?.Value == "mdnr-edge")
                .Elements(DiagramTestHelpers.Svg + "line"));
        double lineX = Number(line, "x1");
        double halfMarker =
            ClassTheme.Default.Edge.MarkerSize * ClassTheme.Default.Edge.StrokeWidth / 2;

        // A label centred on the line would paint its opaque rect over the connector and over the
        // glyph the endpoint carries, both of which are drawn before it.
        Assert.All(
            svg.Descendants(DiagramTestHelpers.Svg + "g")
                .Where(group => group.Attribute("class")?.Value == "mdnr-edge-label")
                .Elements(DiagramTestHelpers.Svg + "rect"),
            rect =>
            {
                double left = Number(rect, "x");
                double right = left + Number(rect, "width");
                Assert.True(
                    right <= lineX - halfMarker || left >= lineX + halfMarker,
                    $"label {left}..{right} covers the line at {lineX}");
            });
    }

    [Fact]
    public void A_Cardinality_Label_Stays_Outside_Both_Class_Boxes()
    {
        XElement svg = RenderSvg("""
            classDiagram
                direction TD
                class IDiagramRenderer {
                    <<interface>>
                    +DiagramTypes
                    +Render(source, options)
                }
                class FlowchartRenderer {
                    -LayeredLayout layout
                    +Render(source, options)
                }
                class DiagramRenderResult {
                    +bool Success
                }
                IDiagramRenderer <|.. FlowchartRenderer
                FlowchartRenderer ..> DiagramRenderResult : returns
                FlowchartRenderer "1" *-- "1" LayeredLayout
                Base "1" o-- "*" Twig
            """);

        List<(double Left, double Right, double Top, double Bottom)> boxes = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("data-id") is not null)
            .Elements(DiagramTestHelpers.Svg + "rect")
            .Select(rect => (
                Number(rect, "x"),
                Number(rect, "x") + Number(rect, "width"),
                Number(rect, "y"),
                Number(rect, "y") + Number(rect, "height")))
            .ToList();

        // A label whose rect reaches into a box erases the caption underneath it.
        Assert.All(LabelRects(svg), label => Assert.All(boxes, box => Assert.True(
            label.Right <= box.Left || box.Right <= label.Left ||
                label.Bottom <= box.Top || box.Bottom <= label.Top,
            $"label {label} reaches into box {box}")));
    }

    [Fact]
    public void A_Self_Relations_Labels_Do_Not_Overlap_One_Another()
    {
        XElement svg = RenderSvg("""
            classDiagram
                Node "1" --> "*" Node : children
            """);

        List<(double Left, double Right, double Top, double Bottom)> labels = LabelRects(svg);

        Assert.Equal(3, labels.Count);
        for (int i = 0; i < labels.Count; i++)
        {
            for (int j = i + 1; j < labels.Count; j++)
            {
                Assert.True(
                    labels[i].Right <= labels[j].Left || labels[j].Right <= labels[i].Left ||
                        labels[i].Bottom <= labels[j].Top || labels[j].Bottom <= labels[i].Top,
                    $"labels {labels[i]} and {labels[j]} overlap");
            }
        }
    }

    private static List<(double Left, double Right, double Top, double Bottom)> LabelRects(
        XElement svg) =>
        svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Elements(DiagramTestHelpers.Svg + "rect")
            .Select(rect => (
                Number(rect, "x"),
                Number(rect, "x") + Number(rect, "width"),
                Number(rect, "y"),
                Number(rect, "y") + Number(rect, "height")))
            .ToList();

    private static bool Overlaps(XElement first, XElement second) =>
        Number(first, "x") < Number(second, "x") + Number(second, "width") &&
        Number(second, "x") < Number(first, "x") + Number(first, "width") &&
        Number(first, "y") < Number(second, "y") + Number(second, "height") &&
        Number(second, "y") < Number(first, "y") + Number(first, "height");

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new ClassRenderer();

        Assert.Equal(
            renderer.Render(Simple, RenderOptions.Html).SvgFragment,
            renderer.Render(Simple, RenderOptions.Html).SvgFragment);
    }
}
