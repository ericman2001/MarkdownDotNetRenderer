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

using MarkdownDotNetRenderer.Mermaid.Class;
using MarkdownDotNetRenderer.Mermaid.Er;
using MarkdownDotNetRenderer.Mermaid.Flowchart;
using MarkdownDotNetRenderer.Mermaid.Gantt;
using MarkdownDotNetRenderer.Mermaid.Pie;
using MarkdownDotNetRenderer.Mermaid.Sequence;
using MarkdownDotNetRenderer.Mermaid.State;

namespace MarkdownDotNetRenderer.Mermaid;

/// <summary>
/// Detects a mermaid block's diagram type and dispatches it to the matching
/// <see cref="IDiagramRenderer"/>. The registry is an explicit dictionary built from the
/// renderers passed in — no assembly scanning, no attributes, no <c>Activator</c> — which is what
/// keeps Core reflection-free and AOT-clean (docs/06-aot-and-dependencies.md).
/// </summary>
public sealed class MermaidRenderer
{
    private readonly Dictionary<string, IDiagramRenderer> _registry;

    /// <summary>Creates a renderer with the built-in diagram renderers.</summary>
    public MermaidRenderer()
        : this(CreateBuiltInRenderers())
    {
    }

    /// <summary>Creates a renderer over an explicit renderer list.</summary>
    /// <param name="renderers">The renderers to register; later entries win on key conflicts.</param>
    public MermaidRenderer(IEnumerable<IDiagramRenderer> renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);

        _registry = new Dictionary<string, IDiagramRenderer>(StringComparer.OrdinalIgnoreCase);
        foreach (IDiagramRenderer renderer in renderers)
        {
            ArgumentNullException.ThrowIfNull(renderer);
            foreach (string type in renderer.DiagramTypes)
            {
                _registry[type] = renderer;
            }
        }
    }

    /// <summary>The diagram-type keywords this instance can render.</summary>
    public IReadOnlyCollection<string> SupportedDiagramTypes => _registry.Keys;

    /// <summary>
    /// The built-in renderer list: flowcharts, sequence diagrams, and the phase-4 additions — pie
    /// charts, state, class, ER, and Gantt diagrams.
    /// </summary>
    /// <returns>Freshly constructed renderers.</returns>
    public static IReadOnlyList<IDiagramRenderer> CreateBuiltInRenderers() =>
        [
            new FlowchartRenderer(),
            new SequenceRenderer(),
            new PieRenderer(),
            new StateRenderer(),
            new ClassRenderer(),
            new ErRenderer(),
            new GanttRenderer(),
        ];

    /// <summary>
    /// Renders one mermaid block. Never throws: an unknown type yields
    /// <see cref="RenderDiagnostic.UnsupportedDiagramType"/> and an escaping exception from a
    /// renderer yields <see cref="RenderDiagnostic.DiagramParseFailure"/>.
    /// </summary>
    /// <param name="mermaidSource">The verbatim mermaid source.</param>
    /// <param name="options">Options controlling fonts and sizing.</param>
    /// <returns>The rendered fragment, or a failure result describing the fallback reason.</returns>
    public DiagramRenderResult Render(string mermaidSource, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<RenderDiagnostic>();
        string? diagramType = DetectDiagramType(mermaidSource, diagnostics);

        if (diagramType is null)
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.UnsupportedDiagramType,
                "The mermaid block is empty, so no diagram type could be determined.",
                diagnostics);
        }

        if (!_registry.TryGetValue(diagramType, out IDiagramRenderer? renderer))
        {
            return DiagramRenderResult.Failed(
                RenderDiagnostic.UnsupportedDiagramType,
                $"Mermaid diagram type '{diagramType}' is not supported yet; " +
                "the source is emitted as a code block.",
                diagnostics);
        }

        DiagramRenderResult result;
        try
        {
            result = renderer.Render(mermaidSource, options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Backstop: renderers are required not to throw, so an exception here is a bug in a
            // renderer. Degrade the block rather than failing the whole document.
            return DiagramRenderResult.Failed(
                RenderDiagnostic.DiagramParseFailure,
                $"Rendering the '{diagramType}' diagram failed: {ex.Message}",
                diagnostics);
        }

        if (diagnostics.Count == 0)
        {
            return result;
        }

        // Detection and the diagram parser both walk the source, so they can spot the same ignored
        // construct (a %%{ … }%% directive) independently. Report each notice once.
        var combined = new List<RenderDiagnostic>(diagnostics.Count + result.Diagnostics.Count);
        var seen = new HashSet<(string Code, string Message)>();
        foreach (RenderDiagnostic diagnostic in diagnostics.Concat(result.Diagnostics))
        {
            if (seen.Add((diagnostic.Code, diagnostic.Message)))
            {
                combined.Add(diagnostic);
            }
        }

        return result with { Diagnostics = combined };
    }

    /// <summary>
    /// Reads the diagram type from a mermaid source: skip blank lines, <c>%%</c> comments, and
    /// <c>%%{ … }%%</c> directive blocks, then take the first token of the first remaining line,
    /// cut at whitespace or at one of <c>;</c>, <c>:</c>, <c>(</c>.
    /// </summary>
    /// <param name="mermaidSource">The verbatim mermaid source.</param>
    /// <param name="diagnostics">Receives one <c>MERMAID003</c> if a directive was ignored.</param>
    /// <returns>The diagram type, or <see langword="null"/> when the source has no content.</returns>
    public static string? DetectDiagramType(string mermaidSource, IList<RenderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(mermaidSource);
        ArgumentNullException.ThrowIfNull(diagnostics);

        string normalized = Normalize(mermaidSource);
        bool directiveIgnored = false;
        bool inDirective = false;
        void ReportDirectiveIgnored() =>
            diagnostics.Add(new RenderDiagnostic(
                DiagnosticSeverity.Info,
                RenderDiagnostic.IgnoredDiagramFeature,
                MermaidLines.DirectiveIgnored));

        foreach (string rawLine in normalized.Split('\n'))
        {
            string line = rawLine.Trim();

            if (inDirective)
            {
                if (line.Contains("}%%", StringComparison.Ordinal))
                {
                    inDirective = false;
                }

                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("%%{", StringComparison.Ordinal))
            {
                directiveIgnored = true;
                inDirective = !line.Contains("}%%", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (directiveIgnored)
            {
                ReportDirectiveIgnored();
            }

            int end = line.Length;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (char.IsWhiteSpace(c) || c is ';' or ':' or '(')
                {
                    end = i;
                    break;
                }
            }

            return end == 0 ? null : line[..end];
        }

        if (directiveIgnored)
        {
            ReportDirectiveIgnored();
        }

        return null;
    }

    /// <summary>Strips a UTF-8 BOM and normalizes CRLF/CR line endings to <c>\n</c>.</summary>
    /// <param name="source">The raw source.</param>
    /// <returns>The normalized source.</returns>
    public static string Normalize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.TrimStart('\uFEFF');
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}
