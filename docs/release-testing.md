# Release testing

How to validate release builds locally before pushing workflows to GitHub.

## Prerequisites

- .NET 10 SDK
- Docker (Linux Tree-sitter builds)
- [act](https://github.com/nektos/act) (optional, for workflow dry-runs)

## 1. Obtain Tree-sitter natives

Preferred (from the `tree-sitter-natives` GitHub Release):

```bash
./scripts/download-tree-sitter-natives.sh --rid linux-x64
```

Or build locally with Docker:

```bash
./scripts/build-tree-sitter-linux.sh
```

See [tree-sitter-natives.md](tree-sitter-natives.md) for the release-cache workflow.

## 2. Publish a local release artifact

Self-contained:

```bash
./scripts/publish-release.sh \
  --rid linux-x64 \
  --version 0.0.0-test \
  --self-contained true
```

Framework-dependent:

```bash
./scripts/publish-release.sh \
  --rid linux-x64 \
  --version 0.0.0-test \
  --self-contained false
```

Artifacts are written to `artifacts/`. Extract and run:

```bash
tar -xzf artifacts/Edit-0.0.0-test-linux-x64-selfcontained.tar.gz -C /tmp/edit-test
/tmp/edit-test/Edit.App
```

Verify the app launches and syntax highlighting works on a sample file.

To skip rebuilding grammars when iterating on publish settings:

```bash
./scripts/publish-release.sh \
  --rid linux-x64 \
  --version 0.0.0-test \
  --self-contained true \
  --skip-natives
```

## 3. Test CI workflow with act

Prefer `--bind` so gitignored `native/` is visible inside the job container:

```bash
act push -W .github/workflows/ci.yml -j build-test --matrix os:ubuntu-latest --bind
```

Without `--bind`, act skips gitignored `native/` and CI falls back to a full Docker grammar rebuild (slow).

Repository defaults for act live in [`.actrc`](../.actrc).

## 4. Test release workflow with act (Linux only)

`act` cannot emulate `windows-latest` or `macos-latest`. Run the Linux matrix slice:

```bash
act workflow_dispatch \
  -W .github/workflows/release.yml \
  -j build \
  --input version=0.0.0-act \
  --input dry_run=true \
  --matrix os:ubuntu-latest,rid:linux-x64,archive:tar.gz,shell:bash,publish_mode:selfcontained
```

Repeat for `publish_mode:fxdependent` if needed.

## 5. Dry-run on GitHub (Windows + macOS)

After pushing the release workflow branch:

1. Open **Actions → Release → Run workflow**
2. Set version to `0.0.0-ci-test`
3. Leave **dry_run** enabled (default) to build all six artifacts without creating a release
4. Confirm all matrix jobs pass on real runners

When ready for a real release:

1. Merge the workflow changes
2. Tag `v0.1.0` and push, or run workflow with **dry_run** disabled and version `0.1.0`

## Limitations

| Environment | What you can test locally |
|---|---|
| Arch Linux + Docker | Full Linux publish path, CI Linux job via act |
| act | Linux jobs only; no Windows/macOS runner emulation |
| GitHub Actions | All platforms; use `workflow_dispatch` before first tag |

## Artifact naming

Each release produces six downloads:

- `Edit-{version}-linux-x64-selfcontained.tar.gz`
- `Edit-{version}-linux-x64-fxdependent.tar.gz`
- `Edit-{version}-win-x64-selfcontained.zip`
- `Edit-{version}-win-x64-fxdependent.zip`
- `Edit-{version}-osx-arm64-selfcontained.tar.gz`
- `Edit-{version}-osx-arm64-fxdependent.tar.gz`

Self-contained bundles include the .NET runtime. Framework-dependent builds require .NET 10+ on the target machine.
