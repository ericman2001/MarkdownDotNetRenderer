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

using System.Reflection;
using MarkdownDotNetRenderer;

// Phase-0 CLI stub. Real argument handling (reading --input, writing --output in the selected
// --format) arrives in phase 1 (docs/phases/phase-1-html-flowchart.md). For now the executable
// only reports its identity and usage, then exits cleanly so the build gate can smoke-test the
// AOT-published native binary.

string version =
    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
    ?? "0.0.0";

Console.WriteLine($"mdrender ({MarkdownRenderer.ProductName}) {version}");
Console.WriteLine("Usage: mdrender --input <file.md> --output <file> --format <html|odt|docx>");
Console.WriteLine("Note: rendering is not implemented yet (phase 0 scaffolding).");

return 0;
