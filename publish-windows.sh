#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_PROJECT_PATH="$ROOT_DIR/src/CouchControl.Cli/CouchControl.Cli.csproj"
AGENT_PROJECT_PATH="$ROOT_DIR/src/CouchControl.Agent/CouchControl.Agent.csproj"
OUTPUT_ROOT="$ROOT_DIR/artifacts/publish"

export HOME="${HOME:-/tmp}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1

echo "Publishing framework-dependent Windows CLI build..."
dotnet publish "$CLI_PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -o "$OUTPUT_ROOT/cli-win-x64-fdd"

echo "Publishing self-contained single-file Windows CLI build..."
dotnet publish "$CLI_PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUTPUT_ROOT/cli-win-x64-sc"

echo "Publishing framework-dependent Windows agent build..."
dotnet publish "$AGENT_PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -o "$OUTPUT_ROOT/agent-win-x64-fdd"

echo "Publishing self-contained single-file Windows agent build..."
dotnet publish "$AGENT_PROJECT_PATH" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUTPUT_ROOT/agent-win-x64-sc"

echo
echo "Done."
echo "CLI framework-dependent output:   $OUTPUT_ROOT/cli-win-x64-fdd"
echo "CLI self-contained output:        $OUTPUT_ROOT/cli-win-x64-sc"
echo "Agent framework-dependent output: $OUTPUT_ROOT/agent-win-x64-fdd"
echo "Agent self-contained output:      $OUTPUT_ROOT/agent-win-x64-sc"
