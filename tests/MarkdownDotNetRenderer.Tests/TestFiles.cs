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

namespace MarkdownDotNetRenderer.Tests;

/// <summary>Locates repository files (samples, golden output) from the test binary's directory.</summary>
internal static class TestFiles
{
    private const string SolutionFile = "MarkdownDotNetRenderer.sln";

    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static string Sample(string name) =>
        Path.Combine(RepositoryRoot, "samples", name);

    internal static string Expected(string name) =>
        Path.Combine(RepositoryRoot, "samples", "expected", name);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}
