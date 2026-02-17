#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PACKAGE_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
PLUGIN_DIR="$SCRIPT_DIR"
OUT_DIR="$PACKAGE_DIR/Runtime/Plugins/iOS"
SRC_FILE="$PLUGIN_DIR/build_src~/hindi_harfbuzz_bridge.c"
BUILD_DIR="$PLUGIN_DIR/.build/ios"
mkdir -p "$BUILD_DIR"
mkdir -p "$OUT_DIR"

HB_ROOT="$PLUGIN_DIR/third_party~/harfbuzz/ios"
HB_INCLUDE_DIR="$HB_ROOT/include/harfbuzz"
HB_LIB_DEVICE="$HB_ROOT/libharfbuzz_ios_device.a"
HB_LIB_SIM="$HB_ROOT/libharfbuzz_ios_simulator.a"

if [ ! -f "$HB_LIB_DEVICE" ] || [ ! -f "$HB_LIB_SIM" ]; then
  echo "Missing HarfBuzz iOS static libs."
  echo "Expected:"
  echo "  $HB_LIB_DEVICE"
  echo "  $HB_LIB_SIM"
  exit 1
fi

IOS_SDK=$(xcrun --sdk iphoneos --show-sdk-path)
SIM_SDK=$(xcrun --sdk iphonesimulator --show-sdk-path)

clang -O2 -fembed-bitcode -arch arm64 -isysroot "$IOS_SDK" \
  -I"$HB_INCLUDE_DIR" -c "$SRC_FILE" -o "$BUILD_DIR/hindi_harfbuzz_bridge_ios_device.o"

clang -O2 -fembed-bitcode -arch arm64 -isysroot "$SIM_SDK" \
  -I"$HB_INCLUDE_DIR" -c "$SRC_FILE" -o "$BUILD_DIR/hindi_harfbuzz_bridge_ios_sim.o"

libtool -static -o "$BUILD_DIR/libHindiHarfBuzz_ios_device.a" \
  "$BUILD_DIR/hindi_harfbuzz_bridge_ios_device.o" "$HB_LIB_DEVICE"

libtool -static -o "$BUILD_DIR/libHindiHarfBuzz_ios_simulator.a" \
  "$BUILD_DIR/hindi_harfbuzz_bridge_ios_sim.o" "$HB_LIB_SIM"

rm -rf "$OUT_DIR/HindiHarfBuzz.xcframework"

xcodebuild -create-xcframework \
  -library "$BUILD_DIR/libHindiHarfBuzz_ios_device.a" \
  -library "$BUILD_DIR/libHindiHarfBuzz_ios_simulator.a" \
  -output "$OUT_DIR/HindiHarfBuzz.xcframework"

echo "Built: $OUT_DIR/HindiHarfBuzz.xcframework"
