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

namespace MarkdownDotNetRenderer.Mermaid.Class;

/// <summary>The UML relation kinds the subset supports.</summary>
public enum ClassRelationKind
{
    /// <summary><c>&lt;|--</c>: a hollow triangle at the parent end.</summary>
    Inheritance,

    /// <summary><c>*--</c>: a filled diamond at the whole end.</summary>
    Composition,

    /// <summary><c>o--</c>: a hollow diamond at the whole end.</summary>
    Aggregation,

    /// <summary><c>--&gt;</c>: an open arrow at the target end.</summary>
    Association,

    /// <summary><c>..&gt;</c>: a dashed open arrow at the target end.</summary>
    Dependency,

    /// <summary><c>..|&gt;</c>: a dashed hollow triangle at the interface end.</summary>
    Realization,

    /// <summary><c>--</c> or <c>..</c>: a plain line with no marker.</summary>
    Link,
}

/// <summary>One line inside a class box's attribute or operation compartment.</summary>
/// <param name="Text">The member text, verbatim apart from trimming.</param>
/// <param name="IsOperation">Whether the member goes in the operations compartment.</param>
public sealed record ClassMember(string Text, bool IsOperation);

/// <summary>A class box.</summary>
/// <param name="Name">The class name, which is also its id; a generic's type parameters are not
/// part of it, so <c>Repo~T~</c> and a later <c>Repo</c> are the same box.</param>
/// <param name="Annotation">A stereotype such as <c>&lt;&lt;interface&gt;&gt;</c>, or <see langword="null"/>.</param>
/// <param name="Members">Members in source order.</param>
/// <param name="Order">0-based index of first mention, which fixes layout tie-breaking.</param>
/// <param name="TypeParameters">The type parameters between the tildes of a generic such as
/// <c>Repo~T~</c>, or <see langword="null"/> for a plain class.</param>
public sealed record ClassDefinition(
    string Name,
    string? Annotation,
    IReadOnlyList<ClassMember> Members,
    int Order,
    string? TypeParameters = null)
{
    /// <summary>The caption drawn in the box's name compartment.</summary>
    public string Title => TypeParameters is { Length: > 0 } parameters
        ? $"{Name}<{parameters}>"
        : Name;

    /// <summary>The members shown in the attributes compartment.</summary>
    public IEnumerable<ClassMember> Attributes => Members.Where(member => !member.IsOperation);

    /// <summary>The members shown in the operations compartment.</summary>
    public IEnumerable<ClassMember> Operations => Members.Where(member => member.IsOperation);
}

/// <summary>
/// A relation, normalized so that <paramref name="SourceId"/> is always the end that carries the
/// distinguishing marker — the parent of an inheritance, the whole of a composition — and the
/// layered layout therefore ranks it above the other end.
/// </summary>
/// <param name="SourceId">The marker-carrying end, ranked first.</param>
/// <param name="TargetId">The other end.</param>
/// <param name="Kind">The relation kind.</param>
/// <param name="Label">Mid-line label, or <see langword="null"/>.</param>
/// <param name="SourceCardinality">Cardinality shown at the source end, or <see langword="null"/>.</param>
/// <param name="TargetCardinality">Cardinality shown at the target end, or <see langword="null"/>.</param>
public sealed record ClassRelation(
    string SourceId,
    string TargetId,
    ClassRelationKind Kind,
    string? Label,
    string? SourceCardinality,
    string? TargetCardinality);

/// <summary>A parsed class diagram.</summary>
/// <param name="Direction">The layout direction; <c>TD</c> unless the source says otherwise.</param>
/// <param name="Classes">Class boxes in first-mention order.</param>
/// <param name="Relations">Relations in source order.</param>
public sealed record ClassDiagramModel(
    FlowDirection Direction,
    IReadOnlyList<ClassDefinition> Classes,
    IReadOnlyList<ClassRelation> Relations);

/// <summary>The outcome of parsing a class-diagram source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record ClassParseResult(
    bool Success,
    ClassDiagramModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
