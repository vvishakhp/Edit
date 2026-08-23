#!/usr/bin/env bash
set -euo pipefail

ROOT="/repo"
RID="${RID:-linux-x64}"
MANIFEST="${MANIFEST:-/config/tree-sitter-grammars.json}"
OUT="${OUT:-/out/$RID}"
ONLY="${ONLY:-}"

args=(--rid "$RID" --out "$OUT" --manifest "$MANIFEST")
if [[ -n "$ONLY" ]]; then
  args+=(--only "$ONLY")
fi

exec "$ROOT/scripts/build-tree-sitter.sh" "${args[@]}"
