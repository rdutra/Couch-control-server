#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
package_root="$repo_root/artifacts/win-x64/CouchControl"
setup_script="$repo_root/packaging/windows/CouchControl.nsi"
setup_exe="$repo_root/artifacts/win-x64/CouchControlSetup-win-x64.exe"

required_files=(
  "$package_root/agent/CouchControl.Agent.exe"
  "$package_root/cli/CouchControl.Cli.exe"
  "$package_root/README-INSTALL.md"
  "$package_root/PRIVACY.md"
  "$package_root/SUPPORT.md"
  "$package_root/VERSION"
  "$package_root/uninstall.ps1"
)

missing_files=()
for required_file in "${required_files[@]}"; do
  if [[ ! -f "$required_file" ]]; then
    missing_files+=("$required_file")
  fi
done

if (( ${#missing_files[@]} > 0 )); then
  echo "Build the win-x64 package first." >&2
  echo "Missing required package files:" >&2
  printf '  %s\n' "${missing_files[@]}" >&2
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
