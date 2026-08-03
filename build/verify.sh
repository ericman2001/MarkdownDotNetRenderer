#!/usr/bin/env bash
# MarkdownDotNetRenderer
# Copyright (C) 2026 MarkdownDotNetRenderer contributors
#
# This library is free software; you can redistribute it and/or modify it under the terms of the
# GNU Lesser General Public License as published by the Free Software Foundation; either version 3
# of the License, or (at your option) any later version. See LICENSE.LESSER.
#
# The single authoritative build/test/AOT gate. Runs restore -> build (warnings are errors) ->
# test -> AOT publish of the CLI -> a smoke run of the native binary, and prints one PASS/FAIL
# line. Pass --no-aot to skip the native publish and smoke run.

set -euo pipefail

NO_AOT=0
for arg in "$@"; do
  case "$arg" in
    --no-aot) NO_AOT=1 ;;
    *) echo "Unknown argument: $arg" >&2; echo "Usage: build/verify.sh [--no-aot]" >&2; exit 2 ;;
  esac
done

# Repository root is the parent of this script's directory, so the gate is runnable from anywhere.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." >/dev/null 2>&1 && pwd)"
cd "$REPO_ROOT"

SLN="MarkdownDotNetRenderer.sln"
CLI_PROJECT="src/MarkdownDotNetRenderer.Cli/MarkdownDotNetRenderer.Cli.csproj"
CONFIG="Release"

fail() {
  echo "FAIL"
  exit 1
}
trap fail ERR

echo "==> Restore"
dotnet restore "$SLN"

echo "==> Build ($CONFIG, warnings as errors)"
dotnet build "$SLN" -c "$CONFIG" --no-restore

echo "==> Test ($CONFIG)"
dotnet test "$SLN" -c "$CONFIG" --no-build --logger trx

if [ "$NO_AOT" -eq 1 ]; then
  echo "==> Skipping AOT publish and smoke run (--no-aot)"
  echo "PASS"
  exit 0
fi

# The AOT toolchain cannot cross-compile, so publish for the host RID only.
RID="$(dotnet --info | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n1)"
if [ -z "$RID" ]; then
  echo "Could not determine host RID from 'dotnet --info'." >&2
  fail
fi

PUBLISH_DIR="artifacts/publish/$RID"
echo "==> AOT publish CLI ($CONFIG, rid=$RID)"
dotnet publish "$CLI_PROJECT" -c "$CONFIG" -r "$RID" /p:PublishAot=true -o "$PUBLISH_DIR"

BINARY="$PUBLISH_DIR/mdrender"
if [ ! -x "$BINARY" ]; then
  echo "Published native binary not found or not executable at $BINARY" >&2
  fail
fi

echo "==> Smoke run of native binary"
"$BINARY" --version >/dev/null

SMOKE_DIR="$(mktemp -d)"
trap 'rm -rf "$SMOKE_DIR"' EXIT
SMOKE_HTML="$SMOKE_DIR/flowchart-demo.html"

# A real render through the native binary: the AOT build must produce self-contained HTML with
# inline SVG, not just start up.
"$BINARY" --input samples/flowchart-demo.md --output "$SMOKE_HTML" --format html

if [ ! -s "$SMOKE_HTML" ]; then
  echo "Native smoke render produced no output at $SMOKE_HTML" >&2
  fail
fi

for needle in '<!DOCTYPE html>' '<svg ' 'mermaid-figure'; do
  if ! grep -qF -- "$needle" "$SMOKE_HTML"; then
    echo "Smoke render is missing expected content: $needle" >&2
    fail
  fi
done

if grep -qiF -- '<script' "$SMOKE_HTML"; then
  echo "Smoke render contains a <script> element; output must be script-free." >&2
  fail
fi

# Every http(s) URL except the SVG namespace identifier would make the output non-self-contained.
if grep -oiE 'https?://[^"'"'"' )]*' "$SMOKE_HTML" | grep -v '^http://www\.w3\.org/2000/svg$' | grep -q .; then
  echo "Smoke render references external resources; output must be self-contained." >&2
  fail
fi

echo "==> Smoke run of native binary (odt)"
SMOKE_ODT="$SMOKE_DIR/flowchart-demo.odt"
"$BINARY" --input samples/flowchart-demo.md --output "$SMOKE_ODT" --format odt

if [ ! -s "$SMOKE_ODT" ]; then
  echo "Native ODT smoke render produced no output at $SMOKE_ODT" >&2
  fail
fi

# A zip local file header, so the package is at least structurally a zip.
if [ "$(dd if="$SMOKE_ODT" bs=1 count=2 2>/dev/null)" != "PK" ]; then
  echo "ODT smoke render is not a zip package." >&2
  fail
fi

# The mimetype entry must come first and be stored, so the first local file header is read out of
# the package: its own name and extra-field lengths give the offset of the data, rather than
# assuming a header layout the runtime is free to change.
odt_uint() {
  od -An -tu1 -j "$1" -N "$2" "$SMOKE_ODT" | tr -s ' ' '\n' | grep . |
    awk -v m=1 '{ v += $1 * m; m *= 256 } END { print v + 0 }'
}

ODT_METHOD="$(odt_uint 8 2)"
ODT_NAME_LENGTH="$(odt_uint 26 2)"
ODT_EXTRA_LENGTH="$(odt_uint 28 2)"
ODT_FIRST_ENTRY="$(dd if="$SMOKE_ODT" bs=1 skip=30 count="$ODT_NAME_LENGTH" 2>/dev/null)"
ODT_MIMETYPE="$(dd if="$SMOKE_ODT" bs=1 \
  skip=$((30 + ODT_NAME_LENGTH + ODT_EXTRA_LENGTH)) count=39 2>/dev/null)"

if [ "$ODT_FIRST_ENTRY" != "mimetype" ]; then
  echo "ODT smoke render's first zip entry is '$ODT_FIRST_ENTRY', not 'mimetype'." >&2
  fail
fi
if [ "$ODT_METHOD" != "0" ]; then
  echo "ODT smoke render stores mimetype with compression method $ODT_METHOD, not 0 (stored)." >&2
  fail
fi
if [ "$ODT_MIMETYPE" != "application/vnd.oasis.opendocument.text" ]; then
  echo "ODT smoke render's mimetype entry holds the wrong bytes." >&2
  echo "Found: $ODT_MIMETYPE" >&2
  fail
fi

# Entry names live uncompressed in the zip directory, so the required parts are greppable.
for needle in 'content.xml' 'styles.xml' 'meta.xml' 'META-INF/manifest.xml' 'Pictures/diagram-1.svg'; do
  if ! grep -qaF -- "$needle" "$SMOKE_ODT"; then
    echo "ODT smoke render is missing the package entry: $needle" >&2
    fail
  fi
done

echo "==> Smoke run of native binary (sequence diagrams)"
SEQ_HTML="$SMOKE_DIR/sequence-demo.html"
SEQ_ODT="$SMOKE_DIR/sequence-demo.odt"
"$BINARY" --input samples/sequence-demo.md --output "$SEQ_HTML" --format html
"$BINARY" --input samples/sequence-demo.md --output "$SEQ_ODT" --format odt

for needle in 'mdnr-sequence' 'mdnr-lifeline' 'sequence diagram with'; do
  if ! grep -qF -- "$needle" "$SEQ_HTML"; then
    echo "Sequence smoke render is missing expected content: $needle" >&2
    fail
  fi
done

# The verbatim source only survives when a diagram fell back to a code block.
if grep -qF -- 'sequenceDiagram' "$SEQ_HTML"; then
  echo "Sequence smoke render fell back to a code block instead of drawing SVG." >&2
  fail
fi

if ! grep -qaF -- 'Pictures/diagram-1.svg' "$SEQ_ODT"; then
  echo "Sequence ODT smoke render has no diagram picture part." >&2
  fail
fi

# Formats whose writers have not shipped must fail loudly rather than write a broken file.
# A non-zero exit is the expectation here, so the ERR trap has to stand down for one command.
trap - ERR
set +e
"$BINARY" --input samples/flowchart-demo.md --output "$SMOKE_DIR/out.docx" --format docx \
  >/dev/null 2>"$SMOKE_DIR/docx.err"
DOCX_STATUS=$?
set -e
trap fail ERR
if [ "$DOCX_STATUS" -eq 0 ]; then
  echo "--format docx must fail until its writer ships." >&2
  fail
fi
if ! grep -qF 'not implemented' "$SMOKE_DIR/docx.err"; then
  echo "--format docx must explain that the writer is not implemented yet." >&2
  cat "$SMOKE_DIR/docx.err" >&2
  fail
fi

echo "PASS"
