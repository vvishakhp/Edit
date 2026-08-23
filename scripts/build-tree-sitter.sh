#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${RID:-linux-x64}"
MANIFEST="${MANIFEST:-$ROOT/config/tree-sitter-grammars.json}"
OUT_SET=false
OUT="${OUT:-}"
ONLY="${ONLY:-}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [--rid RID] [--out DIR] [--manifest PATH] [--only id1,id2]

Build Tree-sitter grammar natives for a runtime identifier.

  --rid RID         linux-x64 (default), win-x64, or osx-arm64
  --out DIR         Output directory (default: native/\$RID)
  --manifest PATH   Grammar manifest JSON (default: config/tree-sitter-grammars.json)
  --only LIST       Comma-separated grammar ids to build (default: all)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --out)
      OUT="$2"
      OUT_SET=true
      shift 2
      ;;
    --manifest)
      MANIFEST="$2"
      shift 2
      ;;
    --only)
      ONLY="$2"
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

# Resolve OUT after --rid is known (do not bake the default RID into OUT early).
if [[ "$OUT_SET" != true || -z "$OUT" ]]; then
  OUT="$ROOT/native/$RID"
fi

if [[ ! -f "$MANIFEST" ]]; then
  echo "Manifest not found: $MANIFEST" >&2
  exit 1
fi

if ! command -v tree-sitter >/dev/null 2>&1; then
  echo "tree-sitter CLI not found. Install with: npm install -g tree-sitter-cli@0.26.13" >&2
  exit 1
fi

if ! command -v node >/dev/null 2>&1; then
  echo "node is required to read the grammar manifest." >&2
  exit 1
fi

mkdir -p "$OUT"

node - "$MANIFEST" "$OUT/manifest.json" "$RID" <<'NODE'
const fs = require('fs');

const [manifestPath, outManifestPath, rid] = process.argv.slice(2);
const source = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

function libraryName(library, targetRid) {
  const base = library.replace(/\.(so|dll|dylib)$/i, '');
  const libBase = base.startsWith('lib') ? base : `lib${base}`;

  switch (targetRid) {
    case 'win-x64':
    case 'win-x86':
      return `${libBase.replace(/^lib/, '')}.dll`;
    case 'osx-arm64':
    case 'osx-x64':
      return `${libBase}.dylib`;
    default:
      return `${libBase}.so`;
  }
}

const manifest = {
  platform: rid,
  treeSitterDotNetVersion: source.treeSitterDotNetVersion,
  grammars: source.grammars.map((grammar) => ({
    ...grammar,
    library: libraryName(grammar.library, rid),
  })),
};

fs.writeFileSync(outManifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
NODE

failures=()

build_grammar() {
  local id="$1"
  local repo="$2"
  local ref="$3"
  local library="$4"
  local subpath="${5:-}"

  local work
  work="$(mktemp -d "/tmp/grammar-$id.XXXXXX")"

  echo "=== Building $id ($ref) ==="
  if ! git clone --depth 1 --branch "$ref" "$repo" "$work"; then
    echo "ERROR: git clone failed for $id" >&2
    failures+=("$id (clone)")
    return 1
  fi

  if [[ -f "$work/package.json" ]]; then
    (cd "$work" && npm install --ignore-scripts 2>/dev/null || npm install 2>/dev/null || true)
  fi

  local root="$work"
  if [[ -n "$subpath" ]]; then
    root="$work/$subpath"
  fi

  if [[ ! -d "$root" ]]; then
    echo "ERROR: subpath not found for $id: $subpath" >&2
    failures+=("$id (missing subpath)")
    return 1
  fi

  (
    cd "$root"
    tree-sitter generate
    local dest="$OUT/$id"
    mkdir -p "$dest"
    tree-sitter build -o "$dest/$library"

    if [[ -d queries ]]; then
      rm -rf "$dest/queries"
      cp -a queries "$dest/queries"
    elif [[ -d "$work/queries" ]]; then
      rm -rf "$dest/queries"
      cp -a "$work/queries" "$dest/queries"
    fi

    [[ -f "$dest/$library" ]] || exit 1
  ) && echo "Built $id -> $OUT/$id/$library" || {
    echo "ERROR: build failed for $id" >&2
    failures+=("$id (build)")
    rm -rf "$work"
    return 1
  }

  rm -rf "$work"
}

should_build() {
  local id="$1"
  if [[ -z "$ONLY" ]]; then
    return 0
  fi
  IFS=',' read -r -a wanted <<< "$ONLY"
  for w in "${wanted[@]}"; do
    if [[ "$w" == "$id" ]]; then
      return 0
    fi
  done
  return 1
}

# Read rid-local manifest via argv so Windows paths are not broken by shell escaping.
manifest_field() {
  local index="$1"
  local field="$2"
  node - "$OUT/manifest.json" "$index" "$field" <<'NODE'
const fs = require('fs');
const [manifestPath, index, field] = process.argv.slice(2);
const m = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
if (field === 'length') {
  process.stdout.write(String(m.grammars.length));
} else {
  const g = m.grammars[Number(index)];
  process.stdout.write(String(field === 'subpath' ? (g.subpath || '') : g[field]));
}
NODE
}

count="$(manifest_field 0 length)"
built=0
for ((i=0; i<count; i++)); do
  id="$(manifest_field "$i" id)"
  if ! should_build "$id"; then
    continue
  fi
  repo="$(manifest_field "$i" repository)"
  ref="$(manifest_field "$i" ref)"
  library="$(manifest_field "$i" library)"
  subpath="$(manifest_field "$i" subpath)"
  build_grammar "$id" "$repo" "$ref" "$library" "$subpath" || true
  built=$((built + 1))
done

if [[ -n "$ONLY" && "$built" -eq 0 ]]; then
  echo "No matching grammars for --only=$ONLY" >&2
  exit 1
fi

if ((${#failures[@]} > 0)); then
  echo ""
  echo "Build finished with ${#failures[@]} failure(s):" >&2
  printf '  - %s\n' "${failures[@]}" >&2
  exit 1
fi

echo "Done. Artifacts in $OUT"
