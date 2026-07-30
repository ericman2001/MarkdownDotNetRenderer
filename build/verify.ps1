# MarkdownDotNetRenderer
# Copyright (C) 2026 MarkdownDotNetRenderer contributors
#
# This library is free software; you can redistribute it and/or modify it under the terms of the
# GNU Lesser General Public License as published by the Free Software Foundation; either version 3
# of the License, or (at your option) any later version. See LICENSE.LESSER.
#
# The single authoritative build/test/AOT gate (Windows / PowerShell). Kept in lockstep with
# build/verify.sh. Runs restore -> build (warnings are errors) -> test -> AOT publish of the CLI
# -> a smoke run of the native binary, and prints one PASS/FAIL line. Use -NoAot to skip the
# native publish and smoke run.

[CmdletBinding()]
param(
    [switch]$NoAot
)

$ErrorActionPreference = 'Stop'

# Fail fast on any non-zero exit from a native/dotnet invocation.
function Invoke-Checked {
    param([Parameter(Mandatory)][scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE"
    }
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot
try {
    $Sln = 'MarkdownDotNetRenderer.sln'
    $CliProject = 'src/MarkdownDotNetRenderer.Cli/MarkdownDotNetRenderer.Cli.csproj'
    $Config = 'Release'

    Write-Host '==> Restore'
    Invoke-Checked { dotnet restore $Sln }

    Write-Host "==> Build ($Config, warnings as errors)"
    Invoke-Checked { dotnet build $Sln -c $Config --no-restore }

    Write-Host "==> Test ($Config)"
    Invoke-Checked { dotnet test $Sln -c $Config --no-build --logger trx }

    if ($NoAot) {
        Write-Host '==> Skipping AOT publish and smoke run (-NoAot)'
        Write-Host 'PASS'
        exit 0
    }

    # The AOT toolchain cannot cross-compile, so publish for the host RID only.
    $Rid = (& dotnet --info | Select-String -Pattern '^\s*RID:\s*(.+)$').Matches[0].Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($Rid)) {
        throw "Could not determine host RID from 'dotnet --info'."
    }

    $PublishDir = "artifacts/publish/$Rid"
    Write-Host "==> AOT publish CLI ($Config, rid=$Rid)"
    Invoke-Checked { dotnet publish $CliProject -c $Config -r $Rid /p:PublishAot=true -o $PublishDir }

    $Binary = Join-Path $PublishDir 'mdrender.exe'
    if (-not (Test-Path $Binary)) {
        throw "Published native binary not found at $Binary"
    }

    Write-Host '==> Smoke run of native binary'
    Invoke-Checked { & $Binary --version | Out-Null }

    $SmokeDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mdnr-smoke-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $SmokeDir | Out-Null
    try {
        # A real render through the native binary: the AOT build must produce self-contained HTML
        # with inline SVG, not just start up.
        $SmokeHtml = Join-Path $SmokeDir 'flowchart-demo.html'
        Invoke-Checked {
            & $Binary --input 'samples/flowchart-demo.md' --output $SmokeHtml --format html
        }

        if (-not (Test-Path $SmokeHtml) -or (Get-Item $SmokeHtml).Length -eq 0) {
            throw "Native smoke render produced no output at $SmokeHtml"
        }

        $Html = Get-Content -Raw -Path $SmokeHtml
        foreach ($needle in @('<!DOCTYPE html>', '<svg ', 'mermaid-figure')) {
            if (-not $Html.Contains($needle)) {
                throw "Smoke render is missing expected content: $needle"
            }
        }

        if ($Html -match '(?i)<script') {
            throw 'Smoke render contains a <script> element; output must be script-free.'
        }

        # Every http(s) URL except the SVG namespace identifier would make the output
        # non-self-contained.
        $External = [regex]::Matches($Html, '(?i)https?://[^"'' )]*') |
            ForEach-Object { $_.Value } |
            Where-Object { $_ -ne 'http://www.w3.org/2000/svg' }
        if ($External.Count -gt 0) {
            throw "Smoke render references external resources: $($External -join ', ')"
        }

        # Formats whose writers have not shipped must fail loudly rather than write a broken file.
        $OdtOut = Join-Path $SmokeDir 'out.odt'
        $OdtErrors = & $Binary --input 'samples/flowchart-demo.md' --output $OdtOut --format odt 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw '--format odt must fail until its writer ships.'
        }
        if (($OdtErrors -join "`n") -notmatch 'not implemented') {
            throw '--format odt must explain that the writer is not implemented yet.'
        }
    }
    finally {
        Remove-Item -Recurse -Force $SmokeDir -ErrorAction SilentlyContinue
    }

    Write-Host 'PASS'
}
catch {
    Write-Host $_.Exception.Message
    Write-Host 'FAIL'
    exit 1
}
finally {
    Pop-Location
}
