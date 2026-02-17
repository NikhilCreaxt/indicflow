#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PLUGIN_DIR="$SCRIPT_DIR"
OUT_DIR="$PACKAGE_DIR/Runtime/Plugins/Android/arm64-v8a"
BUILD_DIR="$PLUGIN_DIR/.build/android-arm64"

: "${ANDROID_NDK_HOME:?Set ANDROID_NDK_HOME to your Android NDK path}"

HB_ROOT="$PLUGIN_DIR/third_party~/harfbuzz/android/arm64-v8a"
HB_INCLUDE_DIR="$HB_ROOT/include/harfbuzz"
HB_LIBRARY="$HB_ROOT/lib/libharfbuzz.a"

mkdir -p "$OUT_DIR"

if [ ! -f "$HB_LIBRARY" ]; then
  echo "Missing $HB_LIBRARY"
  echo "Place prebuilt HarfBuzz static library at: $HB_LIBRARY"
  exit 1
fi

cmake -S "$PLUGIN_DIR" -B "$BUILD_DIR" \
  -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
  -DANDROID_ABI=arm64-v8a \
  -DANDROID_PLATFORM=android-23 \
  -DCMAKE_BUILD_TYPE=Release \
  -DHARFBUZZ_INCLUDE_DIR="$HB_INCLUDE_DIR" \
  -DHARFBUZZ_LIBRARY="$HB_LIBRARY"

cmake --build "$BUILD_DIR" --config Release

cp "$BUILD_DIR/libHindiHarfBuzz.so" "$OUT_DIR/libHindiHarfBuzz.so"

echo "Built: $OUT_DIR/libHindiHarfBuzz.so"
