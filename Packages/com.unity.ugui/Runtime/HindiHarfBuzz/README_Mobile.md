# HarfBuzz Mobile Build Notes

This folder contains build scripts for Android and iOS native bridge libraries.

## Prerequisites

- `build_src~/hindi_harfbuzz_bridge.c` (already in this repo, in a Unity-ignored folder)
- HarfBuzz static libraries for target platform/arch

## Android (arm64-v8a)

1. Set `ANDROID_NDK_HOME`.
   - Use Android NDK `r28+` when possible.
2. Place HarfBuzz static files:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/android/arm64-v8a/include/harfbuzz/*.h`
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/android/arm64-v8a/lib/libharfbuzz.a`
3. Run:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/build_android.sh`
4. Output:
   - `Packages/com.unity.ugui/Runtime/Plugins/Android/arm64-v8a/libHindiHarfBuzz.so`
5. Verification:
   - `llvm-objdump -p Packages/com.unity.ugui/Runtime/Plugins/Android/arm64-v8a/libHindiHarfBuzz.so | grep LOAD`
   - Every `LOAD` segment should report `align 2**14` or higher for 16 KB support.

## iOS

1. Place HarfBuzz iOS static files:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/ios/include/harfbuzz/*.h`
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/ios/libharfbuzz_ios_device.a`
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/ios/libharfbuzz_ios_simulator.a`
2. Run:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/build_ios.sh`
3. Output:
   - `Packages/com.unity.ugui/Runtime/Plugins/iOS/HindiHarfBuzz.xcframework`

## macOS

To rebuild macOS dylib:

- `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/build_macos.sh`

## WebGL

Unity WebGL native plugins are linked into the final WebAssembly build, so
`TMP_HarfBuzzNative` imports HarfBuzz bridge symbols through `__Internal` for
WebGL players.

1. Use the same Emscripten toolchain that ships with the Unity editor used for
   the WebGL build.
   - This project was checked against Unity `6000.4.0f1`, whose bundled
     Emscripten reports `3.1.39-git`.
   - If your editor is in a different location, set `UNITY_EDITOR_PATH`.
   - Alternatively set `UNITY_EMSCRIPTEN_DIR`, `EMCC`, `EMAR`, and `EMRANLIB`.
2. Place HarfBuzz WebGL headers:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/third_party~/harfbuzz/webgl/include/harfbuzz/*.h`
3. Run:
   - `Packages/com.unity.ugui/Runtime/HindiHarfBuzz/build_webgl.sh`
4. Output:
   - `Packages/com.unity.ugui/Runtime/Plugins/WebGL/libHindiHarfBuzz.a`

Unity's WebGL TextRendering module already links HarfBuzz. The WebGL archive
contains only the IndicFlow bridge object and leaves `hb_*` HarfBuzz references
unresolved for Unity's built-in WebGL HarfBuzz to satisfy at final link time.
Do not bundle a second HarfBuzz static library into the WebGL plugin archive,
or `wasm-ld` will fail with duplicate `hb_*` symbols.

## Unity plugin import

- Android: place `.so` under `Packages/com.unity.ugui/Runtime/Plugins/Android/arm64-v8a/`.
- iOS: place `.a` or `.xcframework` under `Packages/com.unity.ugui/Runtime/Plugins/iOS/`.
- iOS `DllImport` must use `__Internal` (already handled in C# wrappers).
- WebGL: place `.a` under `Packages/com.unity.ugui/Runtime/Plugins/WebGL/`.
- WebGL `DllImport` must use `__Internal` (already handled in C# wrappers).
