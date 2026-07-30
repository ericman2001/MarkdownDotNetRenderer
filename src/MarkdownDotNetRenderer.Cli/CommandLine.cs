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

using System.Globalization;
using System.Reflection;

namespace MarkdownDotNetRenderer.Cli;

/// <summary>
/// Hand-rolled argument parsing and the render entry point for <c>mdrender</c>. Hand-rolled keeps
/// the dependency budget at two packages (docs/06-aot-and-dependencies.md). Diagnostics go to
/// stderr and nothing goes to stdout on success, so the tool composes in scripts.
/// </summary>
internal static class CommandLine
{
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 1;
    private const int ExitStrictWarnings = 2;

    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            WriteUsage(error);
            return ExitUsageError;
        }

        var options = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    WriteUsage(output);
                    return ExitSuccess;

                case "--version":
                    output.WriteLine(Version());
                    return ExitSuccess;

                case "--strict":
                    options.Strict = true;
                    break;

                case "-i":
                case "--input":
                    if (!TryTakeValue(args, ref i, arg, error, out string? input))
                    {
                        return ExitUsageError;
                    }

                    options.InputPath = input;
                    break;

                case "-o":
                case "--output":
                    if (!TryTakeValue(args, ref i, arg, error, out string? outputPath))
                    {
                        return ExitUsageError;
                    }

                    options.OutputPath = outputPath;
                    break;

                case "-f":
                case "--format":
                    if (!TryTakeValue(args, ref i, arg, error, out string? format))
                    {
                        return ExitUsageError;
                    }

                    if (!TryParseFormat(format, out OutputFormat parsed))
                    {
                        error.WriteLine($"mdrender: unknown --format value '{format}'. " +
                            "Expected html, odt, or docx.");
                        return ExitUsageError;
                    }

                    options.Format = parsed;
                    break;

                case "--title":
                    if (!TryTakeValue(args, ref i, arg, error, out string? title))
                    {
                        return ExitUsageError;
                    }

                    options.Title = title;
                    break;

                default:
                    error.WriteLine($"mdrender: unknown argument '{arg}'.");
                    WriteUsage(error);
                    return ExitUsageError;
            }
        }

        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            error.WriteLine("mdrender: --input is required.");
            WriteUsage(error);
            return ExitUsageError;
        }

        if (!File.Exists(options.InputPath))
        {
            error.WriteLine($"mdrender: input file not found: {options.InputPath}");
            return ExitUsageError;
        }

        OutputFormat effectiveFormat = options.Format
            ?? InferFormat(options.OutputPath)
            ?? OutputFormat.Html;
        string outputTarget = options.OutputPath ?? Path.ChangeExtension(
            options.InputPath, MarkdownRenderer.GetFileExtension(effectiveFormat));

        var renderOptions = new RenderOptions
        {
            Format = effectiveFormat,
            DocumentTitle = options.Title,
        };

        var renderer = new MarkdownRenderer();
        RenderResult result;
        try
        {
            result = await renderer
                .RenderFileAsync(options.InputPath, outputTarget, renderOptions)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            error.WriteLine($"mdrender: {ex.Message}");
            return ExitUsageError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"mdrender: {ex.Message}");
            return ExitUsageError;
        }

        bool anyWarning = false;
        foreach (RenderDiagnostic diagnostic in result.Diagnostics)
        {
            anyWarning |= diagnostic.Severity == DiagnosticSeverity.Warning;
            error.WriteLine(Format(diagnostic));
        }

        return options.Strict && anyWarning ? ExitStrictWarnings : ExitSuccess;
    }

    private static string Format(RenderDiagnostic diagnostic)
    {
        string severity = diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "info";
        string location = diagnostic.SourceLine is int line
            ? string.Create(CultureInfo.InvariantCulture, $" [line {line}]")
            : string.Empty;
        return $"{severity} {diagnostic.Code}{location}: {diagnostic.Message}";
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        TextWriter error,
        out string? value)
    {
        if (index + 1 >= args.Length)
        {
            error.WriteLine($"mdrender: {option} requires a value.");
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryParseFormat(string? text, out OutputFormat format)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "html":
                format = OutputFormat.Html;
                return true;
            case "odt":
                format = OutputFormat.Odt;
                return true;
            case "docx":
                format = OutputFormat.Docx;
                return true;
            default:
                format = OutputFormat.Html;
                return false;
        }
    }

    private static OutputFormat? InferFormat(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        return Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".html" or ".htm" => OutputFormat.Html,
            ".odt" => OutputFormat.Odt,
            ".docx" => OutputFormat.Docx,
            _ => null,
        };
    }

    private static string Version()
    {
        Assembly assembly = typeof(CommandLine).Assembly;
        string version =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        return $"mdrender ({MarkdownRenderer.ProductName}) {version}";
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: mdrender --input <file.md> [--output <file>] [options]");
        writer.WriteLine();
        writer.WriteLine("Renders Markdown, including mermaid flowcharts, to a single");
        writer.WriteLine("self-contained document with no JavaScript.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -i, --input <path>    Markdown file to render (required).");
        writer.WriteLine("  -o, --output <path>   Output file. Defaults to the input path with the");
        writer.WriteLine("                        format's extension.");
        writer.WriteLine("  -f, --format <fmt>    html (default), odt (phase 2), or docx (phase 5).");
        writer.WriteLine("                        Inferred from --output's extension when omitted.");
        writer.WriteLine("      --title <text>    Document title; defaults to the first heading.");
        writer.WriteLine("      --strict          Exit 2 if any warning was reported.");
        writer.WriteLine("  -h, --help            Show this help.");
        writer.WriteLine("      --version         Show version information.");
        writer.WriteLine();
        writer.WriteLine("Diagnostics are written to stderr as");
        writer.WriteLine("  <severity> <code> [line N]: <message>");
        writer.WriteLine("Exit codes: 0 success, 1 usage or I/O error, 2 --strict with warnings.");
    }

    private sealed class CliOptions
    {
        public string? InputPath { get; set; }

        public string? OutputPath { get; set; }

        public OutputFormat? Format { get; set; }

        public string? Title { get; set; }

        public bool Strict { get; set; }
    }
}
