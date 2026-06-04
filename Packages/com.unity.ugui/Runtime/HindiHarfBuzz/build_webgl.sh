#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_DIR="$PACKAGE_DIR/Runtime/Plugins/WebGL"
SRC_FILE="$SCRIPT_DIR/build_src~/hindi_harfbuzz_bridge.c"
BUILD_DIR="$SCRIPT_DIR/.build~/webgl"

UNITY_EDITOR_PATH="${UNITY_EDITOR_PATH:-/Applications/Unity/Hub/Editor/6000.4.0f1}"
UNITY_WEBGL_TOOLS_DIR="${UNITY_WEBGL_TOOLS_DIR:-$UNITY_EDITOR_PATH/PlaybackEngines/WebGLSupport/BuildTools/Emscripten}"
EMSCRIPTEN_DIR="${UNITY_EMSCRIPTEN_DIR:-$UNITY_WEBGL_TOOLS_DIR/emscripten}"
EM_CONFIG="${EM_CONFIG:-$UNITY_WEBGL_TOOLS_DIR/.emscripten}"
EMSCRIPTEN="${EMSCRIPTEN:-$EMSCRIPTEN_DIR}"
BINARYEN_ROOT="${BINARYEN_ROOT:-$UNITY_WEBGL_TOOLS_DIR/binaryen}"
EMCC="${EMCC:-$EMSCRIPTEN_DIR/emcc}"
EMCXX="${EMCXX:-$EMSCRIPTEN_DIR/em++}"
EMAR="${EMAR:-$EMSCRIPTEN_DIR/emar}"
EMRANLIB="${EMRANLIB:-$EMSCRIPTEN_DIR/emranlib}"

export EM_CONFIG
export EMSCRIPTEN
export BINARYEN_ROOT
export PATH="$UNITY_WEBGL_TOOLS_DIR/python/bin:$UNITY_WEBGL_TOOLS_DIR/node:$EMSCRIPTEN_DIR:$BINARYEN_ROOT/bin:$PATH"

HB_ROOT="$SCRIPT_DIR/third_party~/harfbuzz/webgl"
HB_INCLUDE_DIR="$HB_ROOT/include/harfbuzz"

if [ ! -x "$EMCC" ]; then
  echo "Missing emcc: $EMCC" >&2
  echo "Set UNITY_EDITOR_PATH, UNITY_EMSCRIPTEN_DIR, or EMCC to Unity's WebGL Emscripten toolchain." >&2
  exit 1
fi

if [ ! -x "$EMAR" ] || [ ! -x "$EMRANLIB" ]; then
  echo "Missing emar/emranlib beside Unity's Emscripten toolchain." >&2
  exit 1
fi

if [ ! -d "$HB_INCLUDE_DIR" ]; then
  echo "Missing HarfBuzz WebGL headers: $HB_INCLUDE_DIR" >&2
  exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR" "$OUT_DIR"
rm -f "$OUT_DIR/libHindiHarfBuzz.a"

"$EMCC" \
  -O2 \
  -I"$HB_INCLUDE_DIR" \
  -c "$SRC_FILE" \
  -o "$BUILD_DIR/hindi_harfbuzz_bridge.o"

"$EMAR" rcs "$OUT_DIR/libHindiHarfBuzz.a" \
  "$BUILD_DIR/hindi_harfbuzz_bridge.o"

"$EMRANLIB" "$OUT_DIR/libHindiHarfBuzz.a"

if [ ! -f "$OUT_DIR/libHindiHarfBuzz.a.meta" ]; then
  cat > "$OUT_DIR/libHindiHarfBuzz.a.meta" <<'EOF'
fileFormatVersion: 2
guid: 9d181fb4c12c4bcf89a2dfcd5eb25f87
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder:
    Any: 0
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      : Any
    second:
      enabled: 0
      settings: {}
  - first:
      WebGL: WebGL
    second:
      enabled: 1
      settings: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
EOF
fi

echo "Built: $OUT_DIR/libHindiHarfBuzz.a"
