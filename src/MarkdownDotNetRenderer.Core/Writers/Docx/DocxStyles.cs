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
using DocumentFormat.OpenXml.Wordprocessing;

namespace MarkdownDotNetRenderer.Writers.Docx;

/// <summary>
/// The named styles and numbering definitions of a DOCX package: the <c>StyleDefinitionsPart</c>
/// content (<c>Normal</c>, <c>Heading1</c>–<c>Heading6</c>, the <c>CodeChar</c> run style and the
/// <c>Quote</c> paragraph style) and the <c>NumberingDefinitionsPart</c> content (one bullet and
/// one decimal abstract numbering, levels 0–4). Named styles are what keep the document
/// restyleable in Word; everything span-local is direct formatting on the run.
/// </summary>
internal static class DocxStyles
{
    /// <summary>Body paragraph style; the parent of every other style here.</summary>
    internal const string NormalStyleId = "Normal";

    /// <summary>Prefix of the heading paragraph style ids; the level is appended.</summary>
    internal const string HeadingStyleIdPrefix = "Heading";

    /// <summary>Run style of code spans and code-block lines.</summary>
    internal const string CodeCharStyleId = "CodeChar";

    /// <summary>Paragraph style of a block quote.</summary>
    internal const string QuoteStyleId = "Quote";

    /// <summary>Highest heading level WordprocessingML outlines and Markdown both support.</summary>
    internal const int MaxHeadingLevel = 6;

    /// <summary>Numbering id of the bullet list definition.</summary>
    internal const int BulletNumberingId = 1;

    /// <summary>First numbering id handed out to an ordered list; each list gets its own.</summary>
    internal const int FirstOrderedNumberingId = 2;

    /// <summary>Deepest list level the numbering definitions declare (<c>ilvl</c> 0–4).</summary>
    internal const int MaxListLevel = 4;

    private const int BulletAbstractNumberingId = 1;
    private const int DecimalAbstractNumberingId = 2;

    /// <summary>Ordered-list start value OOXML assumes when nothing overrides it.</summary>
    internal const int DefaultListStart = 1;

    /// <summary>Bullet glyph used when a theme supplies none.</summary>
    private const string FallbackBulletCharacter = "\u2022";

    /// <summary>Placeholder in a decimal level's text: <c>%1.</c> numbers level 0.</summary>
    private const string LevelTextFormat = "%{0}.";

    /// <summary>The paragraph style id for a heading level.</summary>
    /// <param name="level">Heading level, 1–6.</param>
    /// <returns>The style id, e.g. <c>Heading2</c>.</returns>
    internal static string HeadingStyleId(int level) =>
        HeadingStyleIdPrefix + DocxUnits.Integer(Math.Clamp(level, 1, MaxHeadingLevel));

    /// <summary>Builds the style definitions of a document.</summary>
    /// <param name="theme">Visual values to emit.</param>
    /// <param name="bodyFontFamily">Body font stack from <see cref="RenderOptions.FontFamily"/>.</param>
    /// <returns>The <c>StyleDefinitionsPart</c> content.</returns>
    internal static Styles BuildStyles(DocxTheme theme, string bodyFontFamily)
    {
        string body = DocxUnits.PrimaryFontFamily(bodyFontFamily, theme.FallbackBodyFontFamily);
        var styles = new Styles(BuildDocDefaults(theme, body), BuildNormalStyle(theme));

        for (int level = 1; level <= MaxHeadingLevel; level++)
        {
            styles.Append(BuildHeadingStyle(theme, level));
        }

        styles.Append(BuildCodeCharStyle(theme));
        styles.Append(BuildQuoteStyle(theme));
        return styles;
    }

    /// <summary>
    /// Builds the numbering definitions: the two abstract numberings plus one instance per list.
    /// Bullet lists share a single instance because they carry no counter, while every ordered
    /// list gets its own instance so a later list restarts instead of continuing the previous
    /// one's numbering.
    /// </summary>
    /// <param name="theme">Visual values to emit.</param>
    /// <param name="orderedListStarts">Start value of each ordered list, in document order.</param>
    /// <returns>The <c>NumberingDefinitionsPart</c> content.</returns>
    internal static Numbering BuildNumbering(
        DocxTheme theme,
        IReadOnlyList<int> orderedListStarts)
    {
        var numbering = new Numbering(
            BuildBulletAbstractNumbering(theme),
            BuildDecimalAbstractNumbering(theme));

        numbering.Append(new NumberingInstance(
            new AbstractNumId { Val = BulletAbstractNumberingId })
        {
            NumberID = BulletNumberingId,
        });

        for (int index = 0; index < orderedListStarts.Count; index++)
        {
            numbering.Append(BuildOrderedNumberingInstance(
                FirstOrderedNumberingId + index,
                orderedListStarts[index]));
        }

        return numbering;
    }

    /// <summary>Left indent of a list level, in inches.</summary>
    /// <param name="theme">Visual values supplying the per-level indent.</param>
    /// <param name="level">The list level, 0-based.</param>
    /// <returns>The indent in inches.</returns>
    internal static double ListLevelIndent(DocxTheme theme, int level) =>
        theme.ListHangingIndent + ((level + 1) * theme.ListLevelIndent);

    private static DocDefaults BuildDocDefaults(DocxTheme theme, string bodyFontFamily) =>
        new(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts
                {
                    Ascii = bodyFontFamily,
                    HighAnsi = bodyFontFamily,
                    ComplexScript = bodyFontFamily,
                },
                new FontSize { Val = DocxUnits.HalfPoints(theme.BodyFontSize) },
                new FontSizeComplexScript { Val = DocxUnits.HalfPoints(theme.BodyFontSize) })),
            new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines
                {
                    After = DocxUnits.TwipsOfPoints(theme.ParagraphSpaceBelow),
                })));

    private static Style BuildNormalStyle(DocxTheme theme) => new(
        new StyleName { Val = NormalStyleId },
        new PrimaryStyle(),
        new StyleParagraphProperties(new SpacingBetweenLines
        {
            After = DocxUnits.TwipsOfPoints(theme.ParagraphSpaceBelow),
        }))
    {
        Type = StyleValues.Paragraph,
        StyleId = NormalStyleId,
        Default = true,
    };

    private static Style BuildHeadingStyle(DocxTheme theme, int level) => new(
        new StyleName { Val = $"heading {DocxUnits.Integer(level)}" },
        new BasedOn { Val = NormalStyleId },
        new NextParagraphStyle { Val = NormalStyleId },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines
            {
                Before = DocxUnits.TwipsOfPoints(theme.HeadingSpaceAbove),
                After = DocxUnits.TwipsOfPoints(theme.HeadingSpaceBelow),
            },
            new OutlineLevel { Val = level - 1 }),
        new StyleRunProperties(
            new Bold(),
            new FontSize { Val = DocxUnits.HalfPoints(theme.HeadingFontSize(level)) },
            new FontSizeComplexScript
            {
                Val = DocxUnits.HalfPoints(theme.HeadingFontSize(level)),
            }))
    {
        Type = StyleValues.Paragraph,
        StyleId = HeadingStyleId(level),
    };

    private static Style BuildCodeCharStyle(DocxTheme theme) => new(
        new StyleName { Val = "Code Char" },
        new BasedOn { Val = NormalStyleId },
        new StyleRunProperties(
            new RunFonts
            {
                Ascii = theme.MonospaceFontFamily,
                HighAnsi = theme.MonospaceFontFamily,
                ComplexScript = theme.MonospaceFontFamily,
            },
            new FontSize { Val = DocxUnits.HalfPoints(theme.CodeFontSize) },
            new FontSizeComplexScript { Val = DocxUnits.HalfPoints(theme.CodeFontSize) },
            new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = theme.CodeBackgroundColor,
            }))
    {
        Type = StyleValues.Character,
        StyleId = CodeCharStyleId,
    };

    private static Style BuildQuoteStyle(DocxTheme theme) => new(
        new StyleName { Val = QuoteStyleId },
        new BasedOn { Val = NormalStyleId },
        new NextParagraphStyle { Val = NormalStyleId },
        new StyleParagraphProperties(
            new ParagraphBorders(new LeftBorder
            {
                Val = BorderValues.Single,
                Color = theme.QuoteBorderColor,
                Size = DocxUnits.EighthPoints(theme.QuoteBorderWidth),
                Space = 4,
            }),
            new Indentation { Left = DocxUnits.Twips(theme.QuoteIndent) }))
    {
        Type = StyleValues.Paragraph,
        StyleId = QuoteStyleId,
    };

    private static AbstractNum BuildBulletAbstractNumbering(DocxTheme theme)
    {
        var abstractNumbering = new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        {
            AbstractNumberId = BulletAbstractNumberingId,
        };

        for (int level = 0; level <= MaxListLevel; level++)
        {
            abstractNumbering.Append(new Level(
                new StartNumberingValue { Val = DefaultListStart },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = BulletCharacter(theme, level) },
                new LevelJustification { Val = LevelJustificationValues.Left },
                BuildLevelIndentation(theme, level))
            {
                LevelIndex = level,
            });
        }

        return abstractNumbering;
    }

    private static AbstractNum BuildDecimalAbstractNumbering(DocxTheme theme)
    {
        var abstractNumbering = new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        {
            AbstractNumberId = DecimalAbstractNumberingId,
        };

        for (int level = 0; level <= MaxListLevel; level++)
        {
            abstractNumbering.Append(new Level(
                new StartNumberingValue { Val = DefaultListStart },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = DecimalLevelText(level) },
                new LevelJustification { Val = LevelJustificationValues.Left },
                BuildLevelIndentation(theme, level))
            {
                LevelIndex = level,
            });
        }

        return abstractNumbering;
    }

    /// <summary>
    /// One numbering instance for one ordered list. Every level is overridden so nesting restarts
    /// at its own first value rather than inheriting the previous list's counter, and level 0
    /// starts at the value the Markdown asked for.
    /// </summary>
    private static NumberingInstance BuildOrderedNumberingInstance(int numberingId, int start)
    {
        var instance = new NumberingInstance(
            new AbstractNumId { Val = DecimalAbstractNumberingId })
        {
            NumberID = numberingId,
        };

        for (int level = 0; level <= MaxListLevel; level++)
        {
            instance.Append(new LevelOverride(new StartOverrideNumberingValue
            {
                Val = level == 0 ? start : DefaultListStart,
            })
            {
                LevelIndex = level,
            });
        }

        return instance;
    }

    private static PreviousParagraphProperties BuildLevelIndentation(DocxTheme theme, int level) =>
        new(new Indentation
        {
            Left = DocxUnits.Twips(ListLevelIndent(theme, level)),
            Hanging = DocxUnits.Twips(theme.ListHangingIndent),
        });

    private static string BulletCharacter(DocxTheme theme, int level) =>
        theme.BulletCharacters.Count == 0
            ? FallbackBulletCharacter
            : theme.BulletCharacters[level % theme.BulletCharacters.Count];

    private static string DecimalLevelText(int level) => string.Format(
        System.Globalization.CultureInfo.InvariantCulture,
        LevelTextFormat,
        level + 1);
}
