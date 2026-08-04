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
using MarkdownDotNetRenderer.Mermaid.Pie;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Areas 2, 3, and 5 of docs/07-testing-strategy.md for the phase-4 pie chart: parsing, the
/// deterministic angle assignment, the emitted wedges and legend, and graceful degradation.
/// </summary>
public sealed class PieDiagramTests
{
    private const string Simple = """
        pie showData
            title Coverage
            "Covered" : 75
            "Missed" : 25
        """;

    private static XElement RenderSvg(string source)
    {
        DiagramRenderResult result = new PieRenderer().Render(source, RenderOptions.Html);

        return DiagramTestHelpers.RenderSvg(result);
    }

    [Fact]
    public void The_Parser_Reads_The_Title_Slices_And_ShowData_Flag()
    {
        PieParseResult parsed = PieParser.Parse(Simple);

        Assert.True(parsed.Success);
        PieModel model = Assert.IsType<PieModel>(parsed.Model);
        Assert.Equal("Coverage", model.Title);
        Assert.True(model.ShowData);
        Assert.Equal(["Covered", "Missed"], model.Slices.Select(slice => slice.Label));
        Assert.Equal([75d, 25d], model.Slices.Select(slice => slice.Value));
        Assert.Equal(100, model.Total);
    }

    [Fact]
    public void The_Title_May_Sit_On_The_Header_Line()
    {
        PieParseResult parsed = PieParser.Parse("pie title Split\n    \"A\" : 1\n");

        Assert.True(parsed.Success);
        Assert.Equal("Split", parsed.Model!.Title);
        Assert.False(parsed.Model.ShowData);
    }

    [Theory]
    [InlineData("pie\n")]
    [InlineData("pie\n    \"A\" : 0\n")]
    [InlineData("pie\n    \"A\" : -3\n")]
    [InlineData("pie\n    \"A\" : not-a-number\n")]
    [InlineData("flowchart TD\n    A --> B\n")]
    public void Charts_Without_Usable_Data_Fail_Rather_Than_Throw(string source)
    {
        PieParseResult parsed = PieParser.Parse(source);

        Assert.False(parsed.Success);
        Assert.Null(parsed.Model);
        Assert.NotNull(parsed.FailureMessage);
    }

    [Fact]
    public void A_Malformed_Chart_Degrades_To_Mermaid002()
    {
        DiagramRenderResult result = new PieRenderer().Render("pie\n", RenderOptions.Html);

        Assert.False(result.Success);
        Assert.Null(result.SvgFragment);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.DiagramParseFailure, diagnostic.Code);
    }

    [Fact]
    public void An_Unknown_Statement_Reports_One_Mermaid003_And_Still_Renders()
    {
        DiagramRenderResult result = new PieRenderer().Render(
            "pie\n    unexpected statement\n    also unexpected\n    \"A\" : 1\n",
            RenderOptions.Html);

        Assert.True(result.Success);
        RenderDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RenderDiagnostic.IgnoredDiagramFeature, diagnostic.Code);
    }

    [Fact]
    public void Slice_Angles_Cover_Exactly_One_Turn_In_Source_Order()
    {
        PieChartLayout layout = PieLayoutEngine.Compute(
            PieParser.Parse("pie\n    \"A\" : 1\n    \"B\" : 1\n    \"C\" : 1\n").Model!,
            12,
            new PieMetrics(),
            wrapChars: 40);

        Assert.Equal(3, layout.Slices.Count);
        Assert.Equal(PieLayoutEngine.StartAngleDegrees, layout.Slices[0].StartAngle);
        Assert.Equal(
            PieLayoutEngine.FullTurnDegrees,
            layout.Slices.Sum(slice => slice.SweepAngle),
            precision: 9);
        for (int i = 1; i < layout.Slices.Count; i++)
        {
            Assert.Equal(
                layout.Slices[i - 1].StartAngle + layout.Slices[i - 1].SweepAngle,
                layout.Slices[i].StartAngle,
                precision: 9);
        }
    }

    [Fact]
    public void Each_Slice_Gets_A_Wedge_And_A_Legend_Row_With_Its_Value_And_Share()
    {
        XElement svg = RenderSvg(Simple);

        List<XElement> slices = svg.Descendants(DiagramTestHelpers.Svg + "g")
            .Where(group => group.Attribute("class")?.Value == "mdnr-pie-slice")
            .ToList();
        Assert.Equal(2, slices.Count);
        Assert.All(slices, slice => Assert.NotNull(slice.Element(DiagramTestHelpers.Svg + "path")));

        string text = string.Join('\n', svg.Descendants(DiagramTestHelpers.Svg + "text").Select(t => t.Value));
        Assert.Contains("Coverage", text, StringComparison.Ordinal);
        Assert.Contains("Covered — 75 (75%)", text, StringComparison.Ordinal);
        Assert.Contains("Missed — 25 (25%)", text, StringComparison.Ordinal);
        Assert.Equal(2, svg.Descendants(DiagramTestHelpers.Svg + "rect").Count());
    }

    [Fact]
    public void A_Single_Slice_Is_Drawn_As_A_Full_Circle()
    {
        XElement svg = RenderSvg("pie\n    \"Only\" : 5\n");

        Assert.Single(svg.Descendants(DiagramTestHelpers.Svg + "circle"));
        Assert.Empty(svg.Descendants(DiagramTestHelpers.Svg + "path"));
    }

    [Fact]
    public void Distinct_Slices_Use_Distinct_Palette_Colours()
    {
        var colours = Enumerable.Range(0, PieTheme.Palette.Count)
            .Select(PieTheme.Colour)
            .ToList();

        Assert.Equal(colours.Count, colours.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PieTheme.Palette[0], PieTheme.Colour(PieTheme.Palette.Count));
    }

    [Fact]
    public void Repeated_Renders_Are_Byte_Identical()
    {
        var renderer = new PieRenderer();

        DiagramRenderResult first = renderer.Render(Simple, RenderOptions.Html);
        DiagramRenderResult second = renderer.Render(Simple, RenderOptions.Html);

        Assert.Equal(first.SvgFragment, second.SvgFragment);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
    }

    [Fact]
    public void The_Root_Carries_Size_ViewBox_And_Accessible_Metadata()
    {
        XElement svg = RenderSvg(Simple);

        Assert.Equal(DiagramTestHelpers.Svg + "svg", svg.Name);
        Assert.Equal("img", svg.Attribute("role")?.Value);
        Assert.Equal("mdnr-pie", svg.Attribute("class")?.Value);
        Assert.Contains("pie chart", svg.Attribute("aria-label")!.Value, StringComparison.Ordinal);
        Assert.StartsWith("0 0 ", svg.Attribute("viewBox")!.Value, StringComparison.Ordinal);
    }
}
