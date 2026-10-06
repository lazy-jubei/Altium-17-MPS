#!/bin/bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
wine_root="${ALTIUM_WINE_ROOT:-$HOME/AltiumWine17}"
altium_dir="${ALTIUM_INSTALL_DIR:-$wine_root/prefix/drive_c/Program Files (x86)/Altium/AD17}"
dotnet_cli="${DOTNET_CLI:-$(command -v dotnet || true)}"
[ -n "$dotnet_cli" ] || dotnet_cli="$HOME/AltiumWine/tools/dotnet-sdk/dotnet"
[ -x "$dotnet_cli" ] || { echo 'Install .NET SDK 8 or later, or set DOTNET_CLI.' >&2; exit 1; }
"$dotnet_cli" build "$repo_dir/Altium17-PartSearch/Altium17-PartSearch.csproj" -c Release --nologo "-p:AltiumInstallDir=$altium_dir"
