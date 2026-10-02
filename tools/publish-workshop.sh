#!/usr/bin/env bash
# Upload a mod as a new version of its existing Steam Workshop item.
#
# Usage: tools/publish-workshop.sh <mod-src-dir> <workshop-item-id> [change note]
#   e.g. tools/publish-workshop.sh RuinarchPlus 3811868047 "Ruinarch+ 0.10.1"
#
# Builds the mod into a clean folder (DLL, mod.json, README.md, art/audio/bundles; never a
# player's config.json or logs), then launches Ruinarch through Steam once with the
# WorkshopPublish release tool installed. The tool uploads the folder through the game's
# own Steam session and quits. Only the files and the change note change: the item's
# title, description, images and visibility stay as set on Steam.
#
# Needs Steam running and logged in as the item's owner, the game closed, and a
# RuinarchModLoader checkout next to this repo (or RUIN_LOADER_DIR) with the loader built
# and installed in the game.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
loader="${RUIN_LOADER_DIR:-$here/../../RuinarchModLoader}"
source "$loader/tools/env.sh"

src="$(cd "${1:?usage: publish-workshop.sh <mod-src-dir> <workshop-item-id> [change note]}" && pwd)"
item="${2:?workshop item id required}"
[[ "$item" =~ ^[0-9]+$ ]] || { echo "item id must be a number" >&2; exit 2; }
name="$(basename "$src")"
version="$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$src/mod.json")"
note="${3:-Version $version}"
mods="$RUIN_GAME_DIR/Mods"
tool="$mods/WorkshopPublish"

pgrep -f 'Ruinarch.exe' >/dev/null && { echo "Ruinarch is running; close it first" >&2; exit 2; }
pgrep -x steam >/dev/null || pgrep -f 'steamwebhelper' >/dev/null || { echo "Steam is not running" >&2; exit 2; }

stage="$(mktemp -d /tmp/workshop-publish.XXXXXX)"
cleanup() {
  pgrep -f 'Ruinarch.exe' >/dev/null && { pkill -f 'Ruinarch.exe'; sleep 4; pkill -9 -f 'Ruinarch.exe' || true; }
  rm -rf "$tool" "$stage"
}
trap cleanup EXIT

( cd "$loader" && tools/build-mod.sh "$src" "$stage" >/dev/null )
[ -f "$src/README.md" ] && cp "$src/README.md" "$stage/$name/"
( cd "$loader" && tools/build-mod.sh "$here/WorkshopPublish" "$mods" >/dev/null )

# The game runs under Proton: Steam reads the folder through the Windows view of / (Z:).
winpath="Z:${stage//\//\\}\\$name"
python3 - "$tool/publish.json" "$item" "$winpath" "$note" <<'EOF'
import json, sys
path, item, folder, note = sys.argv[1:]
json.dump({"item": int(item), "folder": folder, "note": note}, open(path, "w"))
EOF

echo "Uploading $name $version ($(ls "$stage/$name" | tr '\n' ' ')) to Workshop item $item..."
steam "steam://rungameid/909320" >/dev/null 2>&1 &
result="$tool/publish-result.txt"
for ((t = 0; t < 600; t += 3)); do
  sleep 3
  [ -f "$result" ] && break
done
if [ ! -f "$result" ]; then
  echo "FAIL no result within 10 minutes; see $mods/mods.log" >&2
  exit 1
fi
cat "$result"
grep -q '^OK' "$result"
