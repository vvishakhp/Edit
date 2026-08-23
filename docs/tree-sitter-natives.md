# Tree-sitter natives cache

Tree-sitter grammar binaries are built in a dedicated workflow and stored on a
rolling GitHub Release tag: **`tree-sitter-natives`** (prerelease).

CI and app Release workflows **download** those assets instead of rebuilding
every grammar on every run.

## When natives rebuild

CI detects changes to:

- [`config/tree-sitter-grammars.json`](../config/tree-sitter-grammars.json)
- build scripts / Docker files under `scripts/` and `docker/tree-sitter-linux/`
- the natives workflow itself

On push to `main`/`master`, [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs
[`.github/workflows/tree-sitter-natives.yml`](../.github/workflows/tree-sitter-natives.yml)
**before** the app build/test jobs, so Windows/macOS never download a half-updated release.

Plain application code pushes skip the natives rebuild and only download the existing release.

You can also run **Actions → Tree-sitter natives → Run workflow** (`force` rebuilds all).

## Incremental rebuilds

[`scripts/tree-sitter-natives.js`](../scripts/tree-sitter-natives.js) fingerprints each grammar from:

- grammar `id`, `repository`, `ref`, `subpath`, `function`
- platform library name for the RID
- pinned `tree-sitter-cli` version (`0.26.13`)
- hash of the native build scripts / Dockerfile

Only grammars whose fingerprint differs from `index.json` in the release are rebuilt.
Unchanged `.so` / `.dll` / `.dylib` files are kept from the previous release asset.

## Release assets

| Asset | Contents |
|---|---|
| `natives-linux-x64.tar.gz` | `native/linux-x64/**` |
| `natives-win-x64.zip` | `native/win-x64/**` |
| `natives-osx-arm64.tar.gz` | `native/osx-arm64/**` |
| `index.json` | Per-RID grammar fingerprints |

## Local usage

Download (requires `gh` auth and an existing natives release):

```bash
./scripts/download-tree-sitter-natives.sh --rid linux-x64
```

Update/pack one RID locally (download → plan → rebuild changed → pack):

```bash
./scripts/update-tree-sitter-natives.sh --rid linux-x64
```

Force full rebuild:

```bash
./scripts/update-tree-sitter-natives.sh --rid linux-x64 --force
```

Or build without the release cache (Docker on Linux):

```bash
./scripts/build-tree-sitter-linux.sh
```

## First-time bootstrap

Until the `tree-sitter-natives` release exists:

1. Run **Tree-sitter natives** via `workflow_dispatch` (optionally with `force`)
2. Confirm the prerelease and four assets appear under Releases
3. Subsequent CI / Release jobs will download them

Linux CI falls back to a local Docker build if the release is missing; Windows and macOS CI require the release.
