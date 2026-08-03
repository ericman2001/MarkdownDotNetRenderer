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

namespace MarkdownDotNetRenderer.Mermaid.Pie;

/// <summary>One slice of a pie chart.</summary>
/// <param name="Label">The slice label.</param>
/// <param name="Value">The slice value; strictly positive.</param>
/// <param name="Order">0-based index in source order, which fixes the colour assignment.</param>
public sealed record PieSlice(string Label, double Value, int Order);

/// <summary>A parsed pie chart.</summary>
/// <param name="Title">The chart title, or <see langword="null"/>.</param>
/// <param name="Slices">Slices in source order; at least one.</param>
/// <param name="ShowData">Whether the source asked for values in the legend. Values are always
/// shown, so this only records that the keyword was present.</param>
public sealed record PieModel(string? Title, IReadOnlyList<PieSlice> Slices, bool ShowData)
{
    /// <summary>The sum of all slice values; strictly positive for a parsed model.</summary>
    public double Total
    {
        get
        {
            double total = 0;
            foreach (PieSlice slice in Slices)
            {
                total += slice.Value;
            }

            return total;
        }
    }
}

/// <summary>The outcome of parsing a pie-chart source.</summary>
/// <param name="Success">Whether a model was produced.</param>
/// <param name="Model">The parsed model, or <see langword="null"/> on failure.</param>
/// <param name="FailureMessage">Why parsing failed, when it did.</param>
/// <param name="Diagnostics">Diagnostics for ignored constructs.</param>
public sealed record PieParseResult(
    bool Success,
    PieModel? Model,
    string? FailureMessage,
    IReadOnlyList<RenderDiagnostic> Diagnostics);
