#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
game_dir="${GAME_DIR:-/home/tr0st/Games/cultist-simulator/game}"
managed_dir="$game_dir/cultistsimulator_Data/Managed"
out_dir="$repo_dir/build"
out_dll="$out_dir/ShiftPopulate.dll"
src="$repo_dir/src/ShiftPopulate/ShiftPopulate.cs"

if ! command -v csc >/dev/null 2>&1; then
  echo "csc is not available in PATH" >&2
  exit 1
fi

if [ ! -d "$managed_dir" ]; then
  echo "Managed assemblies directory not found: $managed_dir" >&2
  exit 1
fi

mkdir -p "$out_dir"

csc \
  -target:library \
  -langversion:latest \
  -out:"$out_dll" \
  -reference:"$managed_dir/netstandard.dll" \
  -reference:"$managed_dir/Assembly-CSharp.dll" \
  -reference:"$managed_dir/SecretHistories.Main.dll" \
  -reference:"$managed_dir/SecretHistories.Enums.dll" \
  -reference:"$managed_dir/SecretHistories.Interfaces.dll" \
  -reference:"$managed_dir/UnityEngine.dll" \
  -reference:"$managed_dir/UnityEngine.CoreModule.dll" \
  -reference:"$managed_dir/UnityEngine.UI.dll" \
  -reference:"$managed_dir/UnityEngine.UIModule.dll" \
  -reference:"$managed_dir/UnityEngine.InputLegacyModule.dll" \
  -reference:"$managed_dir/Unity.InputSystem.dll" \
  "$src"

echo "Built $out_dll"
