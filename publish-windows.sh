#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$ROOT_DIR/src/CouchControl.Cli/CouchControl.Cli.csproj"
OUTPUT_ROOT="$ROOT_DIR/artifacts/publish"

export HOME="${HOME:-/tmp}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1

echo "Publishing framework-dependent Windows build..."
dotnet publish "$PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -o "$OUTPUT_ROOT/win-x64-fdd"

echo "Publishing self-contained single-file Windows build..."
dotnet publish "$PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUTPUT_ROOT/win-x64-sc"

echo
echo "Done."
echo "Framework-dependent output: $OUTPUT_ROOT/win-x64-fdd"
echo "Self-contained output:     $OUTPUT_ROOT/win-x64-sc"
