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
- A local `build/verify.{sh,ps1}` script as the authoritative build/test/AOT gate, with an
  optional CI workflow that merely calls it.
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

10. **Local verification script — the authoritative build gate.** The project does not depend on
    a hosted CI service to know it is green; the gate is a script any contributor can run on
    their own machine before pushing. Create both, kept in lockstep:
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
    - Document it in `README.md` and `CONTRIBUTING` guidance: **"run `build/verify.sh` (or
      `build/verify.ps1`) and paste the summary line before merging."** Every later phase's
      acceptance criteria are satisfied by this script passing.

11. **Optional CI workflow** (`.github/workflows/ci.yml`). Nice to have, not the gate — it must
    only *invoke the same script* (`bash build/verify.sh` / `pwsh build/verify.ps1`) so the two
    can never drift, and so deleting the workflow costs no coverage. Matrix `ubuntu-latest`,
    `windows-latest`, `macos-latest`; `actions/setup-dotnet` with `9.0.x`; the Linux job installs
    the AOT prerequisites first (`sudo apt-get install -y clang zlib1g-dev`).

    Note on cost: GitHub Actions minutes are **free and unlimited for public repositories** and
    are only metered on private ones, so for this repo as it stands the workflow is free. If the
    repo ever goes private, or minutes are otherwise unavailable, delete or disable the workflow
    — the local script remains the definition of "green".

12. **Verify on Linux explicitly.** Run `build/verify.sh` on Linux, since it is a primary target
    ([06-aot-and-dependencies](../06-aot-and-dependencies.md)). Cross-OS coverage is covered
    under "manual cross-platform checks" in
    [07-testing-strategy](../07-testing-strategy.md).

## Acceptance criteria

- [ ] `dotnet build -c Release` succeeds from a clean clone with **zero warnings**
      (`TreatWarningsAsErrors` is on, so warnings are failures by construction).
- [ ] `dotnet test -c Release` runs and the single smoke test passes.
- [ ] `dotnet publish src/MarkdownDotNetRenderer.Cli -c Release -r linux-x64 /p:PublishAot=true`
      succeeds on Linux and the resulting native binary runs and exits 0.
- [ ] `build/verify.sh` runs all of the above end-to-end on Linux and prints `PASS`; the
      `.ps1` equivalent does the same on Windows. Neither requires a hosted CI service.
- [ ] The build + test sequence has been run manually on at least one non-Linux OS, or the
      omission is recorded — whichever is true is written down rather than assumed.
- [ ] Solution layout matches [02-architecture](../02-architecture.md).
- [ ] `Core` references only `Markdig`; `Cli` references only `Core`; `Tests` references `Core`
      plus test tooling.
- [ ] `LICENSE`, `LICENSE.LESSER`, `THIRD-PARTY-NOTICES.md`, `.gitignore`, `.gitattributes`, and
      `Directory.Build.props` exist and are committed.
- [ ] No `.csproj` duplicates a property already set in `Directory.Build.props`.
