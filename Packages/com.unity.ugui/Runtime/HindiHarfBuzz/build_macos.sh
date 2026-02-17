#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
SRC_FILE="$SCRIPT_DIR/build_src~/hindi_harfbuzz_bridge.c"
OUT_FILE="$PACKAGE_DIR/Runtime/Plugins/macOS/libHindiHarfBuzz.dylib"

if [ ! -f "$SRC_FILE" ]; then
  echo "Missing source file: $SRC_FILE" >&2
  exit 1
fi

cc \
  -O2 \
  -fPIC \
  -shared \
  "$SRC_FILE" \
  -I/opt/homebrew/include/harfbuzz \
  -L/opt/homebrew/lib \
  -lharfbuzz \
  -o "$OUT_FILE"

install_name_tool -id "@rpath/libHindiHarfBuzz.dylib" "$OUT_FILE"

echo "Built $OUT_FILE"
