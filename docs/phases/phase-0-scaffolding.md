# Phase 0 — Solution Scaffolding

**Goal:** an empty but correctly configured .NET 9 solution that builds and tests cleanly on
Linux, macOS, and Windows. No rendering logic in this phase.

**Prerequisites:** none. This is the first implementation phase.

**Reading:** [02-architecture](../02-architecture.md) (layout),
[06-aot-and-dependencies](../06-aot-and-dependencies.md) (build properties, cross-platform
rules), [08-licensing](../08-licensing.md) (headers and notices).

## Scope

- Solution + three projects with correct references.
- Shared build configuration via `Directory.Build.props`.
- Dependency installation (Markdig; OpenXml is added in [phase 2](phase-2-docx.md)).
- Repository hygiene: `.gitignore`, `.gitattributes`, `THIRD-PARTY-NOTICES.md`, CI workflow.
- **Not** in scope: any renderer, writer, CLI argument handling beyond a stub, or diagram code.

## Tasks

1. **Create the solution and projects** at the repository root:
   ```bash
   dotnet new sln -n MarkdownDotNetRenderer
   dotnet new classlib -o src/MarkdownDotNetRenderer.Core   -f net9.0
   dotnet new console  -o src/MarkdownDotNetRenderer.Cli    -f net9.0
   dotnet new xunit    -o tests/MarkdownDotNetRenderer.Tests -f net9.0
   dotnet sln add src/MarkdownDotNetRenderer.Core src/MarkdownDotNetRenderer.Cli tests/MarkdownDotNetRenderer.Tests
   dotnet add src/MarkdownDotNetRenderer.Cli reference src/MarkdownDotNetRenderer.Core
   dotnet add tests/MarkdownDotNetRenderer.Tests reference src/MarkdownDotNetRenderer.Core
   ```
   Delete the template `Class1.cs` / `UnitTest1.cs` placeholders (keep one trivial passing test,
   see task 7).

2. **`Directory.Build.props`** at the repository root, applying to all projects:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net9.0</TargetFramework>
       <LangVersion>latest</LangVersion>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
       <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
       <GenerateDocumentationFile>true</GenerateDocumentationFile>
       <InvariantGlobalization>true</InvariantGlobalization>
       <Authors>MarkdownDotNetRenderer contributors</Authors>
       <PackageLicenseExpression>LGPL-3.0-or-later</PackageLicenseExpression>
       <RepositoryUrl>https://github.com/ericman2001/MarkdownDotNetRenderer</RepositoryUrl>
     </PropertyGroup>
   </Project>
   ```
   Remove the now-duplicated properties from the three `.csproj` files.

3. **Per-project properties.**
   - `Core.csproj`: `<IsAotCompatible>true</IsAotCompatible>`, `<IsPackable>true</IsPackable>`,
     and a `PackageReference` to `Markdig` (latest stable; pin an exact version, no floating
     ranges).
   - `Cli.csproj`: `<PublishAot>true</PublishAot>`, `<AssemblyName>mdrender</AssemblyName>`,
     `<IsPackable>false</IsPackable>`.
   - `Tests.csproj`: `<IsPackable>false</IsPackable>`; leave AOT properties off (the test host
     is not AOT).

4. **Namespace and folder skeleton** matching
   [02-architecture](../02-architecture.md): create the `Markdown/`, `Mermaid/`, `Svg/`, and
   `Writers/` folders in Core with a `.gitkeep` or a single placeholder type each, so phase 1
   has obvious homes for files. Root namespace `MarkdownDotNetRenderer`.

5. **`.gitignore`** from the standard .NET template (`dotnet new gitignore`), plus `*.user`,
   `.vs/`, `.idea/`, `TestResults/`, and `artifacts/`.

6. **`.gitattributes`** to keep golden files stable across platforms
   ([07-testing-strategy](../07-testing-strategy.md)):
   ```
   * text=auto
   *.md   text eol=lf
   *.cs   text eol=lf
   *.html text eol=lf
   *.svg  text eol=lf
   *.docx binary
   *.odt  binary
   ```

7. **A trivial passing test** in `tests/`, e.g. a `SmokeTests.Solution_Builds_And_Tests_Run`
   asserting a public Core constant/type exists. Its purpose is to prove the test project is
   wired to Core and discovered by the runner.

8. **CLI stub.** `Program.cs` prints name, version, and a usage line, and exits 0. Real
   argument parsing arrives in [phase 1](phase-1-html-flowchart.md).

9. **License headers and notices.** Add the short LGPLv3 header comment to every `.cs` file
   (see [08-licensing](../08-licensing.md)) and create `THIRD-PARTY-NOTICES.md` listing Markdig
   (BSD-2-Clause) with its notice text. Confirm `LICENSE` (GPLv3) and `LICENSE.LESSER` (LGPLv3)
   both exist at the root.

10. **CI workflow** (`.github/workflows/ci.yml`):
    - Matrix `ubuntu-latest`, `windows-latest`, `macos-latest`; `actions/setup-dotnet` with
      `9.0.x`; steps `dotnet restore`, `dotnet build -c Release`,
      `dotnet test -c Release --logger trx`.
    - A separate `aot` job on `ubuntu-latest` and `windows-latest` that installs the AOT
      prerequisites on Linux (`sudo apt-get install -y clang zlib1g-dev`) and runs
      `dotnet publish src/MarkdownDotNetRenderer.Cli -c Release -r <rid> /p:PublishAot=true`,
      then executes the produced binary. In this phase the binary only prints usage; phase 1
      extends it to a real render.

11. **Verify on Linux explicitly.** Run the full build/test/publish sequence on Linux, since it
    is a primary target ([06-aot-and-dependencies](../06-aot-and-dependencies.md)).

## Acceptance criteria

- [ ] `dotnet build -c Release` succeeds from a clean clone with **zero warnings**
      (`TreatWarningsAsErrors` is on, so warnings are failures by construction).
- [ ] `dotnet test -c Release` runs and the single smoke test passes.
- [ ] `dotnet publish src/MarkdownDotNetRenderer.Cli -c Release -r linux-x64 /p:PublishAot=true`
      succeeds on Linux and the resulting native binary runs and exits 0.
- [ ] The same build + test sequence passes on Windows and macOS in CI.
- [ ] Solution layout matches [02-architecture](../02-architecture.md).
- [ ] `Core` references only `Markdig`; `Cli` references only `Core`; `Tests` references `Core`
      plus test tooling.
- [ ] `LICENSE`, `LICENSE.LESSER`, `THIRD-PARTY-NOTICES.md`, `.gitignore`, `.gitattributes`, and
      `Directory.Build.props` exist and are committed.
- [ ] No `.csproj` duplicates a property already set in `Directory.Build.props`.
