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
- Dependency installation (Markdig; OpenXml is not added until [phase 5](phase-5-docx.md), and
  [phase 2](phase-2-odf-output.md) adds no package at all).
- Repository hygiene: `.gitignore`, `.gitattributes`, `THIRD-PARTY-NOTICES.md`.
- A `build/verify.{sh,ps1}` script as the single authoritative build/test/AOT gate, plus a
  three-OS CI workflow that does nothing but call it.
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

10. **Verification script — the single authoritative build gate.** One definition of "green",
    runnable both on a contributor's machine and by CI. Create both, kept in lockstep:
    - `build/verify.sh` (bash, `set -euo pipefail`) and `build/verify.ps1` (PowerShell,
      `$ErrorActionPreference = 'Stop'`), both committed with the shell script marked executable
      via `.gitattributes`.
    - Each runs, in order: `dotnet restore`, `dotnet build -c Release`,
      `dotnet test -c Release --logger trx`, then — unless invoked with `--no-aot` —
      `dotnet publish src/MarkdownDotNetRenderer.Cli -c Release -r <host rid> /p:PublishAot=true`
      and an execution of the produced native binary. RID is detected from the host, not
      hard-coded. In this phase the binary only prints usage; phase 1 extends this step to a real
      render.
    - Non-zero exit on the first failure, and a single clear PASS/FAIL summary line at the end so
      the result is unambiguous when read by a human rather than a status badge.
    - Document it in `README.md`: **"run `build/verify.sh` (or `build/verify.ps1`) before
      pushing."** Every later phase's acceptance criteria are satisfied by this script passing.

11. **CI workflow** (`.github/workflows/ci.yml`) — enabled, because Actions minutes are free and
    unlimited for public repositories (metered only on private ones), and a matrix is the only
    practical way to cover Windows and macOS.
    - Matrix `ubuntu-latest`, `windows-latest`, `macos-latest`; `actions/setup-dotnet` with
      `9.0.x`; the Linux job installs the AOT prerequisites first
      (`sudo apt-get install -y clang zlib1g-dev`).
    - **No build logic in the YAML.** Every step is environment setup or
      `bash build/verify.sh` / `pwsh build/verify.ps1`. This is what prevents drift between
      "passes on my machine" and "passes in CI", and it keeps the project verifiable if the repo
      ever goes private and minutes stop being free.
    - Add the status badge to `README.md`.

12. **Verify on Linux explicitly.** Run `build/verify.sh` on Linux, since it is the primary
    development target ([06-aot-and-dependencies](../06-aot-and-dependencies.md)); let the matrix
    confirm Windows and macOS ([07-testing-strategy](../07-testing-strategy.md)).

## Acceptance criteria

- [ ] `dotnet build -c Release` succeeds from a clean clone with **zero warnings**
      (`TreatWarningsAsErrors` is on, so warnings are failures by construction).
- [ ] `dotnet test -c Release` runs and the single smoke test passes.
- [ ] `dotnet publish src/MarkdownDotNetRenderer.Cli -c Release -r linux-x64 /p:PublishAot=true`
      succeeds on Linux and the resulting native binary runs and exits 0.
- [ ] `build/verify.sh` runs all of the above end-to-end on Linux and prints `PASS`, with no step
      that exists only inside the CI workflow.
- [ ] The CI matrix is green on all three OSes, each job having invoked that same script.
- [ ] Solution layout matches [02-architecture](../02-architecture.md).
- [ ] `Core` references only `Markdig`; `Cli` references only `Core`; `Tests` references `Core`
      plus test tooling.
- [ ] `LICENSE`, `LICENSE.LESSER`, `THIRD-PARTY-NOTICES.md`, `.gitignore`, `.gitattributes`, and
      `Directory.Build.props` exist and are committed.
- [ ] No `.csproj` duplicates a property already set in `Directory.Build.props`.
