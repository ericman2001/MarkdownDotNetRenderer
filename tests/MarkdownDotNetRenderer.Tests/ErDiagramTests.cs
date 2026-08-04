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
using MarkdownDotNetRenderer.Mermaid.Er;
using MarkdownDotNetRenderer.Mermaid.Graph;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 2, 3, and 5 of docs/07-testing-strategy.md for the phase-4 ER diagram: relationship
/// parsing with all four cardinality pairs, attribute blocks, and both-ended crow's-foot markers.
/// </summary>
public sealed class ErDiagramTests
{
    private const string Simple = """
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER {
                string name
                string id PK "the key"
            }
        """;

    private static DiagramRenderResult Render(string source) =>
        new ErRenderer().Render(source, RenderOptions.Html);

    private static XElement RenderSvg(string source)
    {
        DiagramRenderResult result = Render(source);

        return DiagramTestHelpers.RenderSvg(result);
    }

    [Fact]
    public void The_Parser_Reads_Relationships_And_Attribute_Blocks()
    {
        ErParseResult parsed = ErParser.Parse(Simple);

        Assert.True(parsed.Success);
        ErDiagramModel model = parsed.Model!;

        Assert.Equal(["CUSTOMER", "ORDER"], model.Entities.Select(entity => entity.Name));
        Assert.Equal(
            ["string name", "string id PK"],
            model.Entities[0].Attributes.Select(attribute => attribute.Text));
        Assert.Empty(model.Entities[1].Attributes);

        ErRelationship relationship = Assert.Single(model.Relationships);
        Assert.Equal("CUSTOMER", relationship.LeftId);
        Assert.Equal("ORDER", relationship.RightId);
        Assert.Equal(ErCardinality.ExactlyOne, relationship.LeftCardinality);
        Assert.Equal(ErCardinality.ZeroOrMany, relationship.RightCardinality);
        Assert.Equal("places", relationship.Label);
        Assert.True(relationship.Identifying);
    }

    [Theory]
    [InlineData("A ||--|| B", ErCardinality.ExactlyOne, ErCardinality.ExactlyOne)]
    [InlineData("A |o--o| B", ErCardinality.ZeroOrOne, ErCardinality.ZeroOrOne)]
    [InlineData("A }|--|{ B", ErCardinality.OneOrMany, ErCardinality.OneOrMany)]
    [InlineData("A }o--o{ B", ErCardinality.ZeroOrMany, ErCardinality.ZeroOrMany)]
    public void All_Four_Cardinality_Pairs_Are_Understood(
        string statement,
        ErCardinality left,
        ErCardinality right)
    {
        Assert.True(ErParser.TryParseRelationship(statement, out ErRelationship relationship));

        Assert.Equal(left, relationship.LeftCardinality);
        Assert.Equal(right, relationship.RightCardinality);
    }

    [Fact]
    public void A_Non_Identifying_Relationship_Is_Dashed()
    {
        Assert.True(ErParser.TryParseRelationship("A ||..o{ B", out ErRelationship relationship));

        Assert.False(relationship.Identifying);
    }

    [Theory]
    [InlineData("erDiagram\n")]
    [InlineData("flowchart TD\n    A --> B\n")]
    public void Sources_Without_Entities_Degrade_To_Mermaid002(string source)
    {
        DiagramRenderResult result = Render(source);

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == RenderDiagnostic.DiagramParseFailure);
    }

    [Fact]
    public void Unrecognized_Statements_Report_One_Mermaid003_And_Still_Render()
    {
        DiagramRenderResult result = Render("""
            erDiagram
                A ||--|| B
                this is not a statement
                nor is this one
            """);

        Assert.True(result.Success);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }

    [Fact]
    public void Each_Entity_Is_Drawn_With_A_Name_Band_And_Its_Attributes()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> entities = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-entity")
            .ToList();
        Assert.Equal(["CUSTOMER", "ORDER"], entities.Select(e => e.Attribute("data-id")!.Value));
        Assert.All(
            entities,
            entity => Assert.Equal(2, entity.Elements(DiagramTestHelpers.Svg + "rect").Count()));

        string text = string.Join(
            '\n',
            svg.Descendants(DiagramTestHelpers.Svg + "text").Select(t => t.Value));
        foreach (string expected in new[] { "CUSTOMER", "ORDER", "string id PK", "places" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_Ends_Of_A_Relationship_Carry_Their_Own_Crows_Foot_Glyph()
    {
        XElement svg = RenderSvg(Simple);

        // Both glyphs are drawn as ordinary geometry at their line ends, so the fragment stays
        // self-contained with no per-document <defs> to manage; this also fixed lost ER bars.
        Assert.Empty(svg.Descendants(DiagramTestHelpers.Svg + "marker"));
        List<XElement> glyphs = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("class")?.Value == "mdnr-edge")
            .Elements(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-edge-marker")
            .ToList();

        Assert.Equal(2, glyphs.Count);
        Assert.Equal(
            ["er-one", "er-zero-many"],
            glyphs.Select(glyph => glyph.Attribute("data-marker")!.Value));
        Assert.All(glyphs, glyph => Assert.NotEmpty(glyph.Elements()));
    }

    [Theory]
    [InlineData(ErCardinality.ExactlyOne, GraphMarker.ErExactlyOne)]
    [InlineData(ErCardinality.ZeroOrOne, GraphMarker.ErZeroOrOne)]
    [InlineData(ErCardinality.OneOrMany, GraphMarker.ErOneOrMany)]
    [InlineData(ErCardinality.ZeroOrMany, GraphMarker.ErZeroOrMany)]
    public void Each_Cardinality_Maps_To_Its_Own_Glyph(
        ErCardinality cardinality,
        GraphMarker marker) =>
        Assert.Equal(marker, ErLayoutEngine.Marker(cardinality));

    [Fact]
    public void A_Relationship_Label_Leaves_Both_Crows_Foot_Glyphs_Visible()
    {
        XElement svg = RenderSvg("""
            erDiagram
                DOCUMENT ||--o{ BLOCK : contains
            """);

        XElement line = svg.Descendants(DiagramTestHelpers.Svg + "line").Single();
        double glyph = GraphMarkers.EndpointInset(
            GraphMarker.ErExactlyOne,
            ErTheme.Default.Edge.MarkerSize,
            ErTheme.Default.Edge.StrokeWidth);

        // Both glyphs are drawn behind their endpoint, i.e. inwards along the line, and the label's
        // opaque rect is painted after them.
        double glyphTop = Number(line, "y1") - glyph;
        double glyphBottom = Number(line, "y2") + glyph;
        XElement label = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Element(DiagramTestHelpers.Svg + "rect")!;
        double top = Number(label, "y");
        double bottom = top + Number(label, "height");

        Assert.True(top >= Number(line, "y1"), $"label top {top} covers the start glyph");
        Assert.True(bottom <= Number(line, "y2"), $"label bottom {bottom} covers the end glyph");
        Assert.True(glyphTop < top && glyphBottom > bottom);
    }

    [Fact]
    public void A_Relationship_Label_Leaves_The_Connector_Itself_Visible()
    {
        XElement svg = RenderSvg("""
            erDiagram
                DOCUMENT ||--o{ BLOCK : contains
            """);

        XElement line = svg.Descendants(DiagramTestHelpers.Svg + "line").Single();
        double lineX = Number(line, "x1");
        XElement label = svg
            .Descendants(DiagramTestHelpers.Svg + "g")
            .Single(group => group.Attribute("class")?.Value == "mdnr-edge-label")
            .Element(DiagramTestHelpers.Svg + "rect")!;
        double left = Number(label, "x");
        double right = left + Number(label, "width");

        // A label centred on a short connector paints over all of it but the two stubs its own rect
        // leaves behind, so it has to sit beside the line instead.
        Assert.True(
            right <= lineX || left >= lineX, $"label {left}..{right} covers the line at {lineX}");
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new ErRenderer();

        Assert.Equal(
            renderer.Render(Simple, RenderOptions.Html).SvgFragment,
            renderer.Render(Simple, RenderOptions.Html).SvgFragment);
    }
}
