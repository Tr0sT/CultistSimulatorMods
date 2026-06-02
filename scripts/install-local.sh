#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
profile_dir="${CS_PROFILE_DIR:-/home/tr0st/Games/cultist-simulator/prefix/drive_c/users/tr0st/AppData/LocalLow/Weather Factory/Cultist Simulator}"
mods_dir="$profile_dir/mods"
mods_file="$profile_dir/mods.txt"
shift_dll="$repo_dir/build/ShiftPopulate.dll"

if [ ! -f "$shift_dll" ]; then
  echo "Missing $shift_dll. Run ./scripts/build.sh first." >&2
  exit 1
fi

mkdir -p "$mods_dir/GHIRBI"
mkdir -p "$mods_dir/Shift Populate/dll"

cp "$repo_dir/mods/GHIRBI/synopsis.json" "$mods_dir/GHIRBI/synopsis.json"
cp "$repo_dir/mods/Shift Populate/synopsis.json" "$mods_dir/Shift Populate/synopsis.json"
cp "$shift_dll" "$mods_dir/Shift Populate/dll/ShiftPopulate.dll"

touch "$mods_file"
tmp="$(mktemp)"
{
  printf 'GHIRBI\n'
  printf 'Shift Populate\n'
  grep -Fxv 'GHIRBI' "$mods_file" | grep -Fxv 'Shift Populate' | sed '/^[[:space:]]*$/d' || true
} > "$tmp"
mv "$tmp" "$mods_file"

echo "Installed Shift Populate to $mods_dir"
echo "Enabled GHIRBI and Shift Populate in $mods_file"
