# Edit

A modular, language-agnostic desktop IDE built with .NET and Avalonia. The code editor paints with SkiaSharp; the workbench uses Dock.Avalonia; language intelligence comes from external language servers; syntax highlighting uses self-built Tree-sitter grammars.

## Requirements

- .NET 9 SDK (or .NET 10 with roll-forward)
- Windows, Linux, or macOS
- **Docker** (Linux only, for building Tree-sitter grammar natives)

## Run


```bash
dotnet restore
./scripts/build-tree-sitter-linux.sh   # first time on Linux
dotnet run --project src/Edit.App
```

Editor playground:

```bash
dotnet run --project samples/Edit.Editor.Playground
```

## Test

```bash
dotnet test
```

Tree-sitter integration tests require `./scripts/build-tree-sitter-linux.sh` on Linux first.

## Release

Release binaries are built via GitHub Actions (see [`.github/workflows/release.yml`](.github/workflows/release.yml)). Each version ships six artifacts: self-contained and framework-dependent builds for `linux-x64`, `win-x64`, and `osx-arm64`.

Tree-sitter natives are built separately and cached on the `tree-sitter-natives` prerelease — see [docs/tree-sitter-natives.md](docs/tree-sitter-natives.md).

Local testing before push: [docs/release-testing.md](docs/release-testing.md).

```bash
./scripts/download-tree-sitter-natives.sh --rid linux-x64   # or build-tree-sitter-linux.sh
./scripts/publish-release.sh --rid linux-x64 --version 0.0.0-test --self-contained true --skip-natives
```

## Settings

User settings live at `%AppData%/Edit/settings.json` (or the OS equivalent). Configure `languageServers` with a command path to any LSP binary — none are bundled. Default keybindings and layout schema version are written on first run.

## Plugins

First-party in-process plugins: Files, Sample, Search, Git, Terminal. DAP client interface is reserved in `Edit.Dap` (noop until a debug phase).

## Tree-sitter

Syntax highlighting uses **TreeSitter.DotNet** for bindings and the core runtime. Language parser `.so` files are built from upstream `grammar.js` in Docker (see `docker/tree-sitter-linux/`). Query files ship alongside each built grammar under `native/linux-x64/{lang}/queries/`. Token colors are configured in `themes/syntax-colors.json` (VS Code Dark+ defaults).

## Layout

See `docs/requirements.md` for accepted product requirements. Solution projects live under `src/`, first-party plugins under `plugins/`, and tests under `tests/`.
