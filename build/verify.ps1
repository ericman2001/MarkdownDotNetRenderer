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

Add-Type -AssemblyName System.IO.Compression.FileSystem

# A DOCX must be a zip holding the WordprocessingML parts, with the diagram as an SVG image part.
function Assert-Docx {
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path $Package) -or (Get-Item $Package).Length -eq 0) {
        throw "$Label DOCX render produced no output at $Package"
    }

    $Archive = [System.IO.Compression.ZipFile]::OpenRead($Package)
    try {
        $Names = $Archive.Entries | ForEach-Object { $_.FullName }
        foreach ($needle in @('[Content_Types].xml', 'word/document.xml', 'word/styles.xml',
                'word/numbering.xml', 'docProps/core.xml', 'media/image.svg')) {
            if ($Names -notcontains $needle) {
                throw "$Label DOCX render is missing the package entry: $needle"
            }
        }
    }
    finally {
        $Archive.Dispose()
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

    $SmokeDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mdnr-smoke-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $SmokeDir | Out-Null

    # The managed render of a DOCX: the OOXML package the AOT smoke run below also has to produce.
    Write-Host '==> Render a sample to .docx (managed)'
    $ManagedDocx = Join-Path $SmokeDir 'kitchen-sink.docx'
    Invoke-Checked {
        dotnet run --project $CliProject -c $Config --no-build -- `
            --input 'samples/kitchen-sink.md' --output $ManagedDocx --format docx
    }
    Assert-Docx -Package $ManagedDocx -Label 'Managed'

    if ($NoAot) {
        Write-Host '==> Skipping AOT publish and smoke run (-NoAot)'
        Remove-Item -Recurse -Force $SmokeDir -ErrorAction SilentlyContinue
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

        Write-Host '==> Smoke run of native binary (odt)'
        $SmokeOdt = Join-Path $SmokeDir 'flowchart-demo.odt'
        Invoke-Checked {
            & $Binary --input 'samples/flowchart-demo.md' --output $SmokeOdt --format odt
        }

        if (-not (Test-Path $SmokeOdt) -or (Get-Item $SmokeOdt).Length -eq 0) {
            throw "Native ODT smoke render produced no output at $SmokeOdt"
        }

        $Package = [System.IO.Compression.ZipFile]::OpenRead($SmokeOdt)
        try {
            $First = $Package.Entries[0]
            if ($First.FullName -ne 'mimetype') {
                throw "The first ODT entry must be mimetype but was $($First.FullName)."
            }
            # A stored entry compresses to exactly its own length.
            if ($First.CompressedLength -ne $First.Length) {
                throw 'The ODT mimetype entry must be stored uncompressed.'
            }

            $Reader = New-Object System.IO.StreamReader($First.Open())
            try {
                $Mimetype = $Reader.ReadToEnd()
            }
            finally {
                $Reader.Dispose()
            }
            if ($Mimetype -ne 'application/vnd.oasis.opendocument.text') {
                throw "Unexpected ODT mimetype content: $Mimetype"
            }

            $Names = $Package.Entries | ForEach-Object { $_.FullName }
            foreach ($needle in @('content.xml', 'styles.xml', 'meta.xml', 'META-INF/manifest.xml', 'Pictures/diagram-1.svg')) {
                if ($Names -notcontains $needle) {
                    throw "ODT smoke render is missing the package entry: $needle"
                }
            }
        }
        finally {
            $Package.Dispose()
        }

        Write-Host '==> Smoke run of native binary (sequence diagrams)'
        $SeqHtml = Join-Path $SmokeDir 'sequence-demo.html'
        $SeqOdt = Join-Path $SmokeDir 'sequence-demo.odt'
        Invoke-Checked {
            & $Binary --input 'samples/sequence-demo.md' --output $SeqHtml --format html
        }
        Invoke-Checked {
            & $Binary --input 'samples/sequence-demo.md' --output $SeqOdt --format odt
        }

        $SeqContent = Get-Content -Raw -Path $SeqHtml
        foreach ($needle in @('mdnr-sequence', 'mdnr-lifeline', 'sequence diagram with')) {
            if (-not $SeqContent.Contains($needle)) {
                throw "Sequence smoke render is missing expected content: $needle"
            }
        }

        # The verbatim source only survives when a diagram fell back to a code block.
        if ($SeqContent.Contains('sequenceDiagram')) {
            throw 'Sequence smoke render fell back to a code block instead of drawing SVG.'
        }

        $SeqPackage = [System.IO.Compression.ZipFile]::OpenRead($SeqOdt)
        try {
            $SeqNames = $SeqPackage.Entries | ForEach-Object { $_.FullName }
            if ($SeqNames -notcontains 'Pictures/diagram-1.svg') {
                throw 'Sequence ODT smoke render has no diagram picture part.'
            }
        }
        finally {
            $SeqPackage.Dispose()
        }

        Write-Host '==> Smoke run of native binary (phase 4 diagram gallery)'
        $GalleryHtml = Join-Path $SmokeDir 'diagram-gallery.html'
        $GalleryOdt = Join-Path $SmokeDir 'diagram-gallery.odt'
        Invoke-Checked {
            & $Binary --input 'samples/diagram-gallery.md' --output $GalleryHtml --format html
        }
        Invoke-Checked {
            & $Binary --input 'samples/diagram-gallery.md' --output $GalleryOdt --format odt
        }

        $GalleryContent = Get-Content -Raw -Path $GalleryHtml
        foreach ($needle in @('mdnr-pie', 'mdnr-state', 'mdnr-class', 'mdnr-er', 'mdnr-gantt')) {
            if (-not $GalleryContent.Contains($needle)) {
                throw "Gallery smoke render is missing expected content: $needle"
            }
        }

        # The verbatim source only survives when a diagram fell back to a code block.
        foreach ($needle in @('stateDiagram-v2', 'classDiagram', 'erDiagram', 'dateFormat')) {
            if ($GalleryContent.Contains($needle)) {
                throw "Gallery smoke render fell back to a code block for: $needle"
            }
        }

        $GalleryPackage = [System.IO.Compression.ZipFile]::OpenRead($GalleryOdt)
        try {
            $GalleryNames = $GalleryPackage.Entries | ForEach-Object { $_.FullName }
            # One picture part per diagram, so the highest-numbered one must exist too.
            if ($GalleryNames -notcontains 'Pictures/diagram-5.svg') {
                throw 'Gallery ODT smoke render is missing a diagram picture part.'
            }
        }
        finally {
            $GalleryPackage.Dispose()
        }

        # DOCX under AOT: DocumentFormat.OpenXml is reflection-based, so the native binary is where
        # a trimmed-away member would surface. It has to produce the same package the managed run
        # did (docs/06-aot-and-dependencies.md).
        Write-Host '==> Smoke run of native binary (docx)'
        $NativeDocx = Join-Path $SmokeDir 'kitchen-sink-aot.docx'
        Invoke-Checked {
            & $Binary --input 'samples/kitchen-sink.md' --output $NativeDocx --format docx
        }
        Assert-Docx -Package $NativeDocx -Label 'Native'

        $ManagedBytes = [System.IO.File]::ReadAllBytes($ManagedDocx)
        $NativeBytes = [System.IO.File]::ReadAllBytes($NativeDocx)
        if (-not [System.Linq.Enumerable]::SequenceEqual($ManagedBytes, $NativeBytes)) {
            throw 'Native DOCX render differs from the managed one; the writer is not deterministic.'
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
