#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID=""
FORCE=false
SKIP_DOWNLOAD=false
ARTIFACTS_DIR="$ROOT/artifacts/tree-sitter-natives"

usage() {
  cat <<EOF
Usage: $(basename "$0") --rid RID [--force] [--skip-download]

Download existing natives (if any), rebuild only changed grammars, pack assets.

  --rid RID           linux-x64, win-x64, or osx-arm64
  --force             Rebuild all grammars
  --skip-download     Do not download the existing release first
  --artifacts-dir DIR Output dir for packed assets (default: artifacts/tree-sitter-natives)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --force)
      FORCE=true
      shift
      ;;
    --skip-download)
      SKIP_DOWNLOAD=true
      shift
      ;;
    --artifacts-dir)
      ARTIFACTS_DIR="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$RID" ]]; then
  usage >&2
  exit 1
fi

NATIVE_DIR="$ROOT/native/$RID"
INDEX_PATH="$ROOT/native/index.json"
mkdir -p "$NATIVE_DIR" "$ARTIFACTS_DIR"

if [[ "$SKIP_DOWNLOAD" != true ]]; then
  "$ROOT/scripts/download-tree-sitter-natives.sh" --rid "$RID" --allow-missing || true
fi

# Prefer the release/workspace index; fall back to a previously packed per-RID index.
PLAN_INDEX="$INDEX_PATH"
if [[ ! -f "$PLAN_INDEX" && -f "$ARTIFACTS_DIR/index-$RID.json" ]]; then
  PLAN_INDEX="$ARTIFACTS_DIR/index-$RID.json"
fi

plan_args=(--rid "$RID" --dir "$NATIVE_DIR" --index "$PLAN_INDEX" --changed-file "$ARTIFACTS_DIR/changed-$RID.txt")
if [[ "$FORCE" == true ]]; then
  plan_args+=(--force)
fi

echo "Planning Tree-sitter rebuild for $RID..."
node "$ROOT/scripts/tree-sitter-natives.js" plan "${plan_args[@]}" | tee "$ARTIFACTS_DIR/plan-$RID.json"

CHANGED_FILE="$ARTIFACTS_DIR/changed-$RID.txt"
if [[ ! -s "$CHANGED_FILE" ]]; then
  echo "No grammar rebuilds required for $RID."
else
  ONLY="$(tr '\n' ',' < "$CHANGED_FILE" | sed 's/,$//')"
  echo "Rebuilding grammars: $ONLY"

  while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    rm -rf "$NATIVE_DIR/$id" 2>/dev/null || {
      echo "Warning: could not remove $NATIVE_DIR/$id (permission?). Continuing." >&2
    }
  done < "$CHANGED_FILE"

  if [[ "$RID" == linux-x64 ]] && command -v docker >/dev/null 2>&1; then
    ONLY="$ONLY" RID="$RID" "$ROOT/scripts/build-tree-sitter-linux.sh"
  else
    "$ROOT/scripts/build-tree-sitter.sh" --rid "$RID" --only "$ONLY"
  fi
fi

# Remove grammar dirs that are no longer in the manifest.
node - "$ROOT/config/tree-sitter-grammars.json" "$NATIVE_DIR" <<'NODE'
const fs = require('fs');
const path = require('path');
const [manifestPath, nativeDir] = process.argv.slice(2);
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const keep = new Set(manifest.grammars.map((g) => g.id));
for (const entry of fs.readdirSync(nativeDir, { withFileTypes: true })) {
  if (!entry.isDirectory()) continue;
  if (!keep.has(entry.name)) {
    try {
      fs.rmSync(path.join(nativeDir, entry.name), { recursive: true, force: true });
    } catch (err) {
      console.error(`Warning: could not remove ${entry.name}: ${err.message}`);
    }
  }
}
NODE

node "$ROOT/scripts/tree-sitter-natives.js" write-index \
  --rid "$RID" \
  --dir "$NATIVE_DIR" \
  --index "$ARTIFACTS_DIR/index-$RID.json"

if cp "$ARTIFACTS_DIR/index-$RID.json" "$INDEX_PATH" 2>/dev/null; then
  :
else
  echo "Warning: could not write $INDEX_PATH (permission?). Index kept at $ARTIFACTS_DIR/index-$RID.json" >&2
fi

# Git Bash on Windows uses /d/... paths; Windows Python needs drive paths.
to_native_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s' "$1"
  fi
}

# Always emit zip entries with forward slashes so unzip works under Git Bash / Linux.
pack_win_zip() {
  local asset="$1"
  local root="$2"
  export TS_ZIP_OUT="$(to_native_path "$asset")"
  export TS_ZIP_ROOT="$(to_native_path "$root")"
  python3 - <<'PY'
import os
import zipfile
from pathlib import Path

root = Path(os.environ["TS_ZIP_ROOT"])
out = Path(os.environ["TS_ZIP_OUT"])
out.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for path in root.rglob("*"):
        if path.is_file():
            zf.write(path, arcname=path.relative_to(root).as_posix())
PY
}

case "$RID" in
  win-x64)
    ASSET="$ARTIFACTS_DIR/natives-win-x64.zip"
    rm -f "$ASSET"
    pack_win_zip "$ASSET" "$NATIVE_DIR"
    ;;
  osx-arm64)
    ASSET="$ARTIFACTS_DIR/natives-osx-arm64.tar.gz"
    tar -czf "$ASSET" -C "$NATIVE_DIR" .
    ;;
  linux-x64)
    ASSET="$ARTIFACTS_DIR/natives-linux-x64.tar.gz"
    tar -czf "$ASSET" -C "$NATIVE_DIR" .
    ;;
  *)
    echo "Unsupported RID: $RID" >&2
    exit 1
    ;;
esac

echo "Packed $ASSET"
echo "ASSET_PATH=$ASSET"
