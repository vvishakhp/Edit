#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TAG="${TREE_SITTER_NATIVES_TAG:-tree-sitter-natives}"
REPO="${TREE_SITTER_NATIVES_REPO:-}"
RID=""
OUT_DIR=""
ALLOW_MISSING=false

usage() {
  cat <<EOF
Usage: $(basename "$0") --rid RID [options]

Download Tree-sitter natives from the dedicated GitHub Release.

  --rid RID              linux-x64, win-x64, or osx-arm64
  --tag TAG              Release tag (default: tree-sitter-natives)
  --repo OWNER/NAME      GitHub repo (default: current gh repo / GITHUB_REPOSITORY)
  --out DIR              Extract directory (default: native/\$RID)
  --allow-missing        Exit 0 if the release/asset is missing
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --tag)
      TAG="$2"
      shift 2
      ;;
    --repo)
      REPO="$2"
      shift 2
      ;;
    --out)
      OUT_DIR="$2"
      shift 2
      ;;
    --allow-missing)
      ALLOW_MISSING=true
      shift
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

OUT_DIR="${OUT_DIR:-$ROOT/native/$RID}"
mkdir -p "$ROOT/native" "$OUT_DIR"

if [[ -z "$REPO" ]]; then
  REPO="${GITHUB_REPOSITORY:-}"
  if [[ -z "$REPO" ]] && command -v gh >/dev/null 2>&1; then
    REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || true)"
  fi
fi

if [[ -z "$REPO" ]]; then
  echo "Could not determine GitHub repository. Pass --repo OWNER/NAME." >&2
  exit 1
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI (gh) is required to download natives." >&2
  exit 1
fi

case "$RID" in
  win-x64)
    ASSET="natives-win-x64.zip"
    ;;
  osx-arm64)
    ASSET="natives-osx-arm64.tar.gz"
    ;;
  linux-x64)
    ASSET="natives-linux-x64.tar.gz"
    ;;
  *)
    echo "Unsupported RID: $RID" >&2
    exit 1
    ;;
esac

TMP="$(mktemp -d)"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

echo "Downloading $ASSET from $REPO@$TAG ..."
if ! gh release download "$TAG" \
  --repo "$REPO" \
  --pattern "$ASSET" \
  --pattern "index.json" \
  --dir "$TMP" \
  --clobber 2>"$TMP/download.err"; then
  if [[ "$ALLOW_MISSING" == true ]]; then
    echo "Release asset not found; continuing (--allow-missing)."
    cat "$TMP/download.err" >&2 || true
    exit 0
  fi
  cat "$TMP/download.err" >&2 || true
  echo "Failed to download Tree-sitter natives from release $TAG." >&2
  exit 1
fi

if [[ -f "$TMP/index.json" ]]; then
  cp "$TMP/index.json" "$ROOT/native/index.json"
fi

# Clear previous binaries for this RID, then extract.
find "$OUT_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
case "$ASSET" in
  *.zip)
    # Normalize backslash zip entries (PowerShell Compress-Archive) to forward slashes.
    export TS_UNZIP_SRC="$TMP/$ASSET"
    export TS_UNZIP_DST="$OUT_DIR"
    python3 - <<'PY'
import os
import zipfile
from pathlib import Path

src = Path(os.environ["TS_UNZIP_SRC"])
dst = Path(os.environ["TS_UNZIP_DST"])
with zipfile.ZipFile(src) as zf:
    for info in zf.infolist():
        name = info.filename.replace("\\", "/")
        if not name or name.endswith("/"):
            continue
        target = dst / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with zf.open(info) as rf, open(target, "wb") as wf:
            wf.write(rf.read())
PY
    ;;
  *)
    tar -xzf "$TMP/$ASSET" -C "$OUT_DIR"
    ;;
esac

echo "Tree-sitter natives extracted to $OUT_DIR"
