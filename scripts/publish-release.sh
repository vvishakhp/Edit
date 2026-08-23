#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID=""
VERSION=""
SELF_CONTAINED=""
SKIP_NATIVES=false
OUTPUT_DIR=""

usage() {
  cat <<EOF
Usage: $(basename "$0") --rid RID --version VERSION --self-contained true|false [options]

Build and package an Edit release for a runtime identifier.

Required:
  --rid RID                 linux-x64, win-x64, or osx-arm64
  --version VERSION         Release version (e.g. 0.1.0)
  --self-contained VAL      true or false

Options:
  --skip-natives            Skip Tree-sitter native build (reuse existing native/\$RID)
  --output-dir DIR          Publish output directory (default: artifacts/publish-\$RID)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --version)
      VERSION="$2"
      shift 2
      ;;
    --self-contained)
      SELF_CONTAINED="$2"
      shift 2
      ;;
    --skip-natives)
      SKIP_NATIVES=true
      shift
      ;;
    --output-dir)
      OUTPUT_DIR="$2"
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

if [[ -z "$RID" || -z "$VERSION" || -z "$SELF_CONTAINED" ]]; then
  usage >&2
  exit 1
fi

if [[ "$SELF_CONTAINED" != true && "$SELF_CONTAINED" != false ]]; then
  echo "--self-contained must be true or false" >&2
  exit 1
fi

OUTPUT_DIR="${OUTPUT_DIR:-$ROOT/artifacts/publish-$RID}"
ARTIFACTS_DIR="$ROOT/artifacts"
mkdir -p "$ARTIFACTS_DIR"

build_natives() {
  if [[ "$SKIP_NATIVES" == true ]]; then
    echo "Skipping Tree-sitter native build."
    return
  fi

  if [[ "$RID" == linux-x64 ]] && command -v docker >/dev/null 2>&1; then
    RID="$RID" "$ROOT/scripts/build-tree-sitter-linux.sh"
  else
    "$ROOT/scripts/build-tree-sitter.sh" --rid "$RID"
  fi
}

build_natives

publish_args=(
  dotnet publish "$ROOT/src/Edit.App/Edit.App.csproj"
  -c Release
  -r "$RID"
  "-p:Version=$VERSION"
  -p:PublishSingleFile=false
  -p:PublishTrimmed=false
  -o "$OUTPUT_DIR"
)

if [[ "$SELF_CONTAINED" == true ]]; then
  publish_args+=(-p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true)
else
  publish_args+=(-p:SelfContained=false)
fi

echo "Publishing Edit $VERSION for $RID (self-contained=$SELF_CONTAINED)..."
"${publish_args[@]}"

if [[ "$SELF_CONTAINED" == true ]]; then
  MODE="selfcontained"
else
  MODE="fxdependent"
fi

ARTIFACT_NAME="Edit-$VERSION-$RID-$MODE"
ARTIFACT_PATH="$ARTIFACTS_DIR/$ARTIFACT_NAME"

case "$RID" in
  win-x64)
    ARTIFACT_PATH="$ARTIFACT_PATH.zip"
    (
      cd "$OUTPUT_DIR"
      if command -v zip >/dev/null 2>&1; then
        zip -r "$ARTIFACT_PATH" .
      else
        powershell.exe -NoProfile -Command "Compress-Archive -Path * -DestinationPath '$ARTIFACT_PATH' -Force"
      fi
    )
    ;;
  *)
    ARTIFACT_PATH="$ARTIFACT_PATH.tar.gz"
    tar -czf "$ARTIFACT_PATH" -C "$OUTPUT_DIR" .
    ;;
esac

echo "Release artifact: $ARTIFACT_PATH"
echo "ARTIFACT_PATH=$ARTIFACT_PATH"
