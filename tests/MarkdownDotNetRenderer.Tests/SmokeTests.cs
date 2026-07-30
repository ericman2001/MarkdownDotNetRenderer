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

using MarkdownDotNetRenderer;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// Phase-0 smoke tests: prove the test project is wired to Core and discovered by the runner.
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void Solution_Builds_And_Tests_Run()
    {
        Assert.Equal("MarkdownDotNetRenderer", MarkdownRenderer.ProductName);
    }

    [Fact]
    public void RenderOptions_Presets_Carry_Their_Format()
    {
        Assert.Equal(OutputFormat.Html, RenderOptions.Html.Format);
        Assert.Equal(OutputFormat.Odt, RenderOptions.Odt.Format);
        Assert.Equal(OutputFormat.Docx, RenderOptions.Docx.Format);
    }
}
