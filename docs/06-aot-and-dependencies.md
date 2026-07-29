# 06 — Dependencies, AOT, and Cross-Platform Policy

## Dependency budget

The budget is **two NuGet packages, total**, and neither may bring a native or JavaScript
dependency.

| Package | Used by | License | LGPLv3 compatible as a dependency? |
| --- | --- | --- | --- |
| [Markdig](https://www.nuget.org/packages/Markdig) | Core (all phases) | BSD-2-Clause | Yes — permissive, no reciprocal obligations |
| [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml) | Core, DOCX path only (phase 2+) | MIT | Yes — permissive |
| xUnit (+ `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) | Tests only | Apache-2.0 / MIT | Yes; test-only, never shipped |

Also allowed, because they ship with the runtime: `System.IO.Compression`,
`System.Xml.XmlWriter`, `System.Text.Json` — relevant to the planned ODT writer
([phase 6](phases/phase-6-odf-output.md)), which needs **no** new package.

### Explicitly disallowed

- Any JS engine (Jint, ClearScript, Jurassic) or Node/browser process launcher — violates the
  core constraint of the project ([01-overview](01-overview.md)).
- SkiaSharp / Magick.NET / libgdiplus / Svg.Skia / ImageSharp — native or heavyweight; only
  reconsidered under [phase 5](phases/phase-5-docx-png-fallback.md), and only with an explicit
  decision recorded there.
- `System.Drawing.Common` — Windows-only since .NET 7, which breaks the Linux requirement.
- Anything GPL/AGPL-licensed, which would impose obligations beyond LGPLv3 on consumers
  ([08-licensing](08-licensing.md)).

Adding a third runtime dependency requires updating this document with the reason, the
license, and its AOT impact.

## AOT and trimming policy

Target properties (set in `Directory.Build.props` / per-project, see
[phase 0](phases/phase-0-scaffolding.md)):

```xml
<!-- Directory.Build.props (all projects) -->
<TargetFramework>net9.0</TargetFramework>
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

<!-- Core -->
<IsAotCompatible>true</IsAotCompatible>   <!-- implies IsTrimmable + EnableAotAnalyzer + EnableTrimAnalyzer -->

<!-- Cli -->
<PublishAot>true</PublishAot>
<InvariantGlobalization>true</InvariantGlobalization>
<StackTraceSupport>false</StackTraceSupport>   <!-- optional size win; revisit if it hurts diagnostics -->
```

`IsAotCompatible` on Core turns the AOT/trim analyzers into build errors (given
`TreatWarningsAsErrors`), so an accidental reflection call fails CI rather than surfacing as a
runtime crash in a published binary.

### Rules for Core

1. **No reflection.** No `Activator.CreateInstance`, `Type.GetType`, `MakeGenericType`,
   attribute scanning, `dynamic`, or expression-tree compilation. The diagram-renderer
   registry and writer selection are explicit code
   ([04-mermaid-engine](04-mermaid-engine.md), [05-output-writers](05-output-writers.md)).
2. **No runtime code generation** and no `System.Reflection.Emit`.
3. **No culture-sensitive formatting.** All numeric formatting in SVG/XML uses
   `CultureInfo.InvariantCulture` — mandatory anyway, since a comma decimal separator would
   produce invalid SVG coordinates on a `de-DE` machine. This is also what makes
   `InvariantGlobalization` safe for the CLI.
4. **No `System.Text.Json` source-generator-less serialization** — currently no JSON at all.

### Known AOT risk: DocumentFormat.OpenXml

DocumentFormat.OpenXml is **not** annotated as trim/AOT-safe. It uses reflection internally
for schema and element metadata, and referencing it from a project marked `IsAotCompatible`
raises `IL2xxx`/`IL3xxx` warnings that `TreatWarningsAsErrors` would turn into build failures.

**Mitigation (the reason `IDocumentWriter` exists):**

- All OpenXml usage is confined to `Writers/DocxDocumentWriter.cs` and its helpers. No OpenXml
  type appears in any public API signature — the public surface exchanges
  `DocumentContent`/`Stream` only.
- The HTML path must remain fully AOT-clean, and this is verified, not assumed: a CI job
  publishes the CLI with `PublishAot` and runs the produced native binary end-to-end on an
  HTML render. That test is the definition of "AOT-clean".
- Trim/AOT warnings originating from OpenXml are suppressed **narrowly**, at the DOCX writer
  file/member level (targeted `#pragma warning disable` or a scoped `NoWarn`), never
  solution-wide, and each suppression carries a comment explaining it.
- If suppression proves too invasive, the fallback plan (decided at phase 2 time, recorded
  there) is to move the DOCX writer into a separate
  `MarkdownDotNetRenderer.OpenXml` package that Core does not reference, with the CLI
  registering it. That preserves an AOT-perfect HTML-only deployment at the cost of one more
  assembly.
- The DOCX branch is documented as **not guaranteed to work under `PublishAot`** until proven
  by test. A CLI invoked with `--format docx` on an AOT build that fails must produce a clear
  error, not a crash.

The planned ODT writer has no such problem (hand-written XML + `ZipArchive`), which is a point
in its favour as the primary "office document" path for AOT builds.

## Cross-platform requirement

**The library, CLI, and tests must build and run on Linux, macOS, and Windows.** All three are
first-class; Linux is a primary development and CI target, not an afterthought.

What this requires:

- **No Windows-only APIs.** No `System.Drawing.Common`, no registry access, no COM/Word
  automation, no `Microsoft.Office.Interop`. Producing DOCX/ODT via OOXML/ODF file formats
  (rather than automating an installed Office) is precisely what makes this possible.
- **Path handling** via `Path.Combine` / `Path.DirectorySeparatorChar`; never hard-coded `\`.
  Tests must not assume a case-insensitive file system — Linux is case-sensitive.
- **Line endings.** Rendered HTML uses `\n` explicitly rather than `Environment.NewLine`, so
  output is byte-identical across platforms and golden-file tests pass everywhere. A
  `.gitattributes` with `* text=auto` plus `*.md text eol=lf` keeps checked-in golden files
  stable.
- **Fonts.** Nothing may depend on an installed font, since a minimal Linux container has
  none. This is already satisfied: `TextMetrics` estimates widths from a static table
  ([04-mermaid-engine](04-mermaid-engine.md)). Output references font *families* by name and
  lets the consuming application substitute.
- **AOT prerequisites on Linux.** `PublishAot` requires `clang`, `zlib1g-dev`, and the
  standard build toolchain (`sudo apt-get install clang zlib1g-dev` on Debian/Ubuntu; the
  `dotnet/sdk` container images already include them). Cross-OS AOT publishing is not
  supported by the toolchain, so each RID is published on its own OS. Non-AOT `dotnet build`
  and `dotnet test` need no extra packages.
- **CI matrix.** `dotnet build` + `dotnet test` on `ubuntu-latest`, `windows-latest`, and
  `macos-latest`; `dotnet publish -r <rid> /p:PublishAot=true` plus a smoke run of the native
  binary on `linux-x64` and `win-x64` at minimum.

Verification targets to record per phase: **on Linux** the produced `.html` must open in
Firefox/Chromium, and (phase 2+) the produced `.docx` must open in LibreOffice Writer with
correct text structure — accepting that SVG-only diagrams may not display there until either
the [PNG fallback](phases/phase-5-docx-png-fallback.md) or the
[ODT writer](phases/phase-6-odf-output.md) lands.

## Size and performance expectations

Not hard requirements, but sanity anchors for review: AOT-published HTML-capable CLI in the
single-digit MB range; a 100 KB Markdown document with a dozen modest diagrams rendering in
well under a second; memory proportional to document size with no per-diagram native
allocation.
