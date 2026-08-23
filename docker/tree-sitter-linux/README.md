# Linux Tree-sitter grammar build

Builds language parser `.so` files and copies upstream `queries/` from pinned grammar repos.

## Prerequisites

- Docker

## Usage

From the repository root:

```bash
./scripts/build-tree-sitter-linux.sh
```

Output: `native/linux-x64/{grammarId}/libtree-sitter-*.so` and `queries/*.scm`.

For other platforms, use the shared script directly (requires `tree-sitter-cli@0.26.13`):

```bash
./scripts/build-tree-sitter.sh --rid win-x64
./scripts/build-tree-sitter.sh --rid osx-arm64
```

The manifest is at [`config/tree-sitter-grammars.json`](../../config/tree-sitter-grammars.json).

CI caches Windows/macOS (and Linux) natives on the `tree-sitter-natives` GitHub Release —
see [docs/tree-sitter-natives.md](../../docs/tree-sitter-natives.md).
