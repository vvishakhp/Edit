#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${EDIT_TREE_SITTER_IMAGE:-edit-tree-sitter-linux}"
RID="${RID:-linux-x64}"
ONLY="${ONLY:-}"

docker build -t "$IMAGE" -f "$ROOT/docker/tree-sitter-linux/Dockerfile" "$ROOT"
docker_args=(
  --rm
  -v "$ROOT/config/tree-sitter-grammars.json:/config/tree-sitter-grammars.json:ro"
  -v "$ROOT/scripts/build-tree-sitter.sh:/repo/scripts/build-tree-sitter.sh:ro"
  -v "$ROOT/native:/out"
  -e "RID=$RID"
)
if [[ -n "$ONLY" ]]; then
  docker_args+=(-e "ONLY=$ONLY")
fi

docker run "${docker_args[@]}" "$IMAGE"

echo "Tree-sitter natives written to $ROOT/native/$RID"
