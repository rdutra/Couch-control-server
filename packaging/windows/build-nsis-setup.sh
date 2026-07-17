#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
package_root="$repo_root/artifacts/win-x64/CouchControl"
setup_script="$repo_root/packaging/windows/CouchControl.nsi"
setup_exe="$repo_root/artifacts/win-x64/CouchControlSetup-win-x64.exe"

if [[ ! -f "$package_root/agent/CouchControl.Agent.exe" || ! -f "$package_root/cli/CouchControl.Cli.exe" ]]; then
  echo "Build the win-x64 package first." >&2
  exit 1
fi

if ! command -v makensis >/dev/null 2>&1; then
  echo "makensis was not found. Install NSIS first, for example: brew install nsis" >&2
  exit 1
fi

cd "$repo_root"
makensis "$setup_script"

if [[ ! -f "$setup_exe" ]]; then
  echo "Expected setup executable was not created: $setup_exe" >&2
  exit 1
fi

echo "Windows x64 setup wizard created:"
echo "  $setup_exe"
