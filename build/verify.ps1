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
    # Phase 0: the binary only prints usage and must exit 0. Phase 1 extends this to a real render.
    Invoke-Checked { & $Binary }

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
