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

using System.Text.RegularExpressions;

namespace MarkdownDotNetRenderer.Tests;

/// <summary>
/// The phase-1 self-containment contract: no scripts, no inline event handlers, and no fetched
/// http(s) resources. The SVG namespace URI is the one permitted http URL: it is an XML identifier,
/// never fetched.
/// </summary>
internal static partial class SelfContainment
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    internal static void Assert(string html)
    {
        Xunit.Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.Empty(EventHandlerAttribute().Matches(html));

        string withoutNamespace = html.Replace(SvgNamespace, string.Empty, StringComparison.Ordinal);
        Xunit.Assert.DoesNotContain("http://", withoutNamespace, StringComparison.OrdinalIgnoreCase);
        Xunit.Assert.DoesNotContain("https://", withoutNamespace, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Matches inline event-handler attributes such as <c>onclick=</c>.</summary>
    [GeneratedRegex(
        "<[^>]*\\son[a-z]+\\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerAttribute();
}
