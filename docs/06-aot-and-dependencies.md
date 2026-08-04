# 06 — Dependencies, AOT, and Cross-Platform Policy

## Dependency budget

The budget is **two NuGet packages, total**, and neither may bring a native or JavaScript
dependency.

| Package | Used by | License | LGPLv3 compatible as a dependency? |
| --- | --- | --- | --- |
| [Markdig](https://www.nuget.org/packages/Markdig) | Core (all phases) | BSD-2-Clause | Yes — permissive, no reciprocal obligations |
| [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml) `3.5.1` (exact) | Core, DOCX writer only (`Writers/Docx/*`) | MIT | Yes — permissive |
| xUnit (+ `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) | Tests only | Apache-2.0 / MIT | Yes; test-only, never shipped |

Core uses both runtime packages of the budget: `Markdig` for parsing and
`DocumentFormat.OpenXml` for the DOCX writer ([phase 5](phases/phase-5-docx.md)). The version is
pinned exactly, so a restore cannot silently pick up a release whose AOT behaviour has not been
measured here.

Also allowed, because they ship with the runtime: `System.IO.Compression`,
`System.Xml.XmlWriter`, `System.Text.Json` — this is what the ODT writer
([phase 2](phases/phase-2-odf-output.md)) is built from, so it needs **no** new package. ODT is
sequenced before DOCX partly for this reason: it delivers an office format at zero cost to the
dependency budget and the AOT story.

### Explicitly disallowed

- Any JS engine (Jint, ClearScript, Jurassic) or Node/browser process launcher — violates the
  core constraint of the project ([01-overview](01-overview.md)).
- SkiaSharp / Magick.NET / libgdiplus / Svg.Skia / ImageSharp — native or heavyweight; only
  reconsidered under [phase 6](phases/phase-6-docx-png-fallback.md), and only with an explicit
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
`TreatWarningsAsErrors`), so an accidental reflection call fails the local build rather than
surfacing as a runtime crash in a published binary.

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

### Measured AOT behaviour of DocumentFormat.OpenXml (phase 5)

`DocumentFormat.OpenXml` is **not** annotated as trim/AOT-safe and uses reflection internally for
schema and element metadata, so the risk was that referencing it from a project marked
`IsAotCompatible` raises `IL2xxx`/`IL3xxx` warnings that `TreatWarningsAsErrors` turns into build
failures.

**What was actually measured** with `3.5.1` on .NET 9 (Linux, `linux-x64`):

| Question | Result |
| --- | --- |
| `IL2xxx`/`IL3xxx` warnings building Core with `IsAotCompatible` | **None** for the APIs this writer uses (part creation, strongly typed elements, `CreateUnknownElement`). Other OpenXml APIs were not probed. |
| Warnings on `dotnet publish /p:PublishAot=true` of the CLI | **None.** |
| Does `--format docx` work in the native binary? | **Yes** — `build/verify` renders `samples/kitchen-sink.md` to `.docx` with the native binary and byte-compares it with the managed render. |

So **no suppressions were needed at all**, and neither the scoped `NoWarn` nor the separate
`MarkdownDotNetRenderer.OpenXml` assembly fallback was used. If a future OpenXml version does
raise `IL` warnings, the order of remedies is unchanged: narrow, commented suppression at the
DOCX writer file/member level first, then the separate-assembly split — never a solution-wide
`NoWarn`.

**Containment that still applies regardless:**

- All OpenXml usage is confined to `src/MarkdownDotNetRenderer.Core/Writers/Docx/*`. No OpenXml
  type appears in any public API signature — the public surface exchanges
  `DocumentContent`/`Stream`/`RenderOptions` only, and the writer is reached through
  `IDocumentWriter`.
- The SVG blip extension Word needs is built by parsing an XML string into an
  `OpenXmlUnknownElement` (`OpenXmlPartContainer.CreateUnknownElement`), not by reflection over
  the drawing schema.
- The HTML **and ODT** paths must remain fully AOT-clean, and this is verified, not assumed:
  `build/verify` publishes the CLI with `PublishAot` and runs the produced native binary
  end-to-end on an HTML render and an ODT render. That step is the definition of "AOT-clean", and
  because it lives in the script it runs identically on a dev box and in CI.
- The DOCX branch is exercised under `PublishAot` by the gate rather than assumed to work. Should
  a future runtime/package combination break it, `build/verify` fails on the DOCX step while the
  HTML and ODT smoke runs stay exactly as strict as they are today.

The ODT writer still has no OpenXml dependency at all (hand-written XML + `ZipArchive`), so it
remains the cheapest office path — but DOCX is no longer the AOT liability this section
anticipated.

## Cross-platform requirement

**The library, CLI, and tests must build and run on Linux, macOS, and Windows.** All three are
first-class; Linux is the primary development and verification target, not an afterthought.

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
- **No native dependencies.** Both the HTML and ODT paths are pure managed code over the BCL, so
  a plain `dotnet build`/`dotnet test` works in any SDK container with no extra system packages.
- **AOT prerequisites on Linux.** `PublishAot` requires `clang`, `zlib1g-dev`, and the
  standard build toolchain (`sudo apt-get install clang zlib1g-dev` on Debian/Ubuntu; the
  `dotnet/sdk` container images already include them). Cross-OS AOT publishing is not
  supported by the toolchain, so each RID is published on its own OS. Non-AOT `dotnet build`
  and `dotnet test` need no extra packages.
- **One gate, two places to run it.** The build/test/AOT gate is the committed
  `build/verify.{sh,ps1}` script ([phase 0](phases/phase-0-scaffolding.md),
  [07-testing-strategy](07-testing-strategy.md)). A GitHub Actions matrix (`ubuntu-latest`,
  `windows-latest`, `macos-latest`) runs that same script and nothing else — it is the practical
  way to cover Windows and macOS, which cannot be virtualized from a Linux dev box. Because the
  workflow holds no logic, the project stays fully verifiable by hand if Actions ever becomes
  unavailable (minutes are free and unlimited for public repos, metered for private ones).

Verification targets to record per phase, **on Linux**: the produced `.html` must open in
Firefox/Chromium; the produced `.odt` ([phase 2](phases/phase-2-odf-output.md)) must open in
LibreOffice Writer with correct text structure **and visible vector diagrams**; and the produced
`.docx` ([phase 5](phases/phase-5-docx.md)) must open in LibreOffice Writer with correct text
structure. Measured in phase 5: LibreOffice Writer 7.3 **does** import and display the SVG-only
diagrams (see [phase 5](phases/phase-5-docx.md) for the evidence), so the
[PNG fallback](phases/phase-6-docx-png-fallback.md) is about older Word versions and other
consumers, not about LibreOffice.

## Size and performance expectations

Not hard requirements, but sanity anchors for review: AOT-published HTML-capable CLI in the
single-digit MB range; a 100 KB Markdown document with a dozen modest diagrams rendering in
well under a second; memory proportional to document size with no per-diagram native
allocation.
