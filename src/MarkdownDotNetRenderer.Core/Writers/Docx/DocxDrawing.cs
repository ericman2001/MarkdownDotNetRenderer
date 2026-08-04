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

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WP = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// Builds the inline <c>Drawing</c> that displays an embedded image, including the SVG extension
/// Word 2016+ reads. One method covers both cases so the phase 6 raster fallback only has to pass
/// a second relationship id: <c>a:blip/@r:embed</c> takes the raster part when there is one and
/// the SVG part otherwise, while <c>asvg:svgBlip/@r:embed</c> always names the SVG part.
/// </summary>
internal static class DocxDrawing
{
    /// <summary>Name given to the picture shape; the alt text carries the meaning.</summary>
    private const string PictureNameFormat = "Image {0}";

    /// <summary>Non-visual picture id; Word only requires it to be present.</summary>
    private const uint PictureShapeId = 0;

    /// <summary>
    /// Builds an inline drawing of one image.
    /// </summary>
    /// <param name="container">The part owning the image relationships, for unknown-element creation.</param>
    /// <param name="imageRelationshipId">Relationship id of the image the blip embeds.</param>
    /// <param name="svgRelationshipId">
    /// Relationship id of the SVG part, or <see langword="null"/> when the image is not an SVG and
    /// therefore needs no extension element.
    /// </param>
    /// <param name="widthEmu">Display width in EMU.</param>
    /// <param name="heightEmu">Display height in EMU.</param>
    /// <param name="drawingId">Document-unique drawing id, from the writer's counter.</param>
    /// <param name="description">Alt text, or <see langword="null"/> for none.</param>
    /// <returns>The drawing to place inside a run.</returns>
    internal static Drawing BuildInlineImage(
        OpenXmlPartContainer container,
        string imageRelationshipId,
        string? svgRelationshipId,
        long widthEmu,
        long heightEmu,
        uint drawingId,
        string? description)
    {
        string name = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PictureNameFormat,
            DocxUnits.Integer(drawingId));

        var blip = new A.Blip { Embed = imageRelationshipId };
        if (svgRelationshipId is not null)
        {
            blip.Append(new A.BlipExtensionList(new A.BlipExtension(
                BuildSvgBlip(container, svgRelationshipId))
            {
                Uri = OoxmlNames.SvgBlipExtensionUri,
            }));
        }

        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties
                {
                    Id = PictureShapeId,
                    Name = name,
                    Description = description,
                },
                new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(blip, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0, Y = 0 },
                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                new A.PresetGeometry(new A.AdjustValueList())
                {
                    Preset = A.ShapeTypeValues.Rectangle,
                }));

        return new Drawing(new WP.Inline(
            new WP.Extent { Cx = widthEmu, Cy = heightEmu },
            new WP.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            new WP.DocProperties
            {
                Id = drawingId,
                Name = name,
                Description = description,
            },
            new WP.NonVisualGraphicFrameDrawingProperties(
                new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(picture)
            {
                Uri = OoxmlNames.PictureGraphicDataUri,
            }))
        {
            DistanceFromTop = 0,
            DistanceFromBottom = 0,
            DistanceFromLeft = 0,
            DistanceFromRight = 0,
        });
    }

    /// <summary>
    /// Builds the <c>asvg:svgBlip</c> element. DocumentFormat.OpenXml has no strongly-typed class
    /// for it, so it is created from an XML string — string construction, deliberately not
    /// reflection, so nothing here affects AOT compatibility
    /// (docs/05-output-writers.md, docs/06-aot-and-dependencies.md).
    /// </summary>
    private static OpenXmlUnknownElement BuildSvgBlip(
        OpenXmlPartContainer container,
        string svgRelationshipId) =>
        container.CreateUnknownElement(
            $"<{OoxmlNames.SvgBlipPrefix}:{OoxmlNames.SvgBlipElement} "
            + $"xmlns:{OoxmlNames.SvgBlipPrefix}=\"{OoxmlNames.SvgBlipNamespace}\" "
            + $"xmlns:{OoxmlNames.RelationshipsPrefix}=\"{OoxmlNames.RelationshipsNamespace}\" "
            + $"{OoxmlNames.RelationshipsPrefix}:{OoxmlNames.EmbedAttribute}=\"{svgRelationshipId}\" />");
}
