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

using MarkdownDotNetRenderer.Mermaid.Flowchart;

namespace MarkdownDotNetRenderer.Mermaid.Er;

/// <summary>The four crow's-foot cardinalities the subset supports.</summary>
public enum ErCardinality
{
    /// <summary><c>||</c>: exactly one.</summary>
    ExactlyOne,

    /// <summary><c>|o</c> / <c>o|</c>: zero or one.</summary>
    ZeroOrOne,

    /// <summary><c>}|</c> / <c>|{</c>: one or many.</summary>
    OneOrMany,

    /// <summary><c>}o</c> / <c>o{</c>: zero or many.</summary>
    ZeroOrMany,
}

/// <summary>One attribute row of an entity.</summary>
/// <param name="Text">The row as rendered, e.g. <c>string name PK</c>.</param>
public sealed record ErAttribute(string Text);

/// <summary>An entity box.</summary>
/// <param name="Name">The entity name, which is also its id.</param>
/// <param name="Attributes">Attribute rows in source order; possibly empty.</param>
/// <param name="Order">0-based index of first mention, which fixes layout tie-breaking.</param>
public sealed record ErEntity(string Name, IReadOnlyList<ErAttribute> Attributes, int Order);

/// <summary>A relationship between two entities.</summary>
/// <param name="LeftId">The entity written on the left.</param>
/// <param name="RightId">The entity written on the right.</param>
/// <param name="LeftCardinality">Cardinality drawn at the left entity's end.</param>
/// <param name="RightCardinality">Cardinality drawn at the right entity's end.</param>
/// <param name="Label">The relationship label, or <see langword="null"/>.</param>
/// <param name="Identifying">Whether the relationship is identifying (<c>--</c>) rather than
/// non-identifying (<c>..</c>), which is drawn dashed.</param>
public sealed record ErRelationship(
    string LeftId,
    string RightId,
    ErCardinality LeftCardinality,
    ErCardinality RightCardinality,
    string? Label,
    bool Identifying);

/// <summary>A parsed entity-relationship diagram.</summary>
/// <param name="Direction">The layout direction; <c>TD</c> unless the source says otherwise.</param>
/// <param name="Entities">Entities in first-mention order.</param>
/// <param name="Relationships">Relationships in source order.</param>
public sealed record ErDiagramModel(
    FlowDirection Direction,
    IReadOnlyList<ErEntity> Entities,
    IReadOnlyList<ErRelationship> Relationships);

/// <summary>The outcome of parsing an ER-diagram source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record ErParseResult(
    bool Success,
    ErDiagramModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
