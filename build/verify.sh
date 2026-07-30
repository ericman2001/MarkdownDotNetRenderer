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
# Phase 0: the binary only prints usage and must exit 0. Phase 1 extends this to a real render.
"$BINARY"

echo "PASS"
