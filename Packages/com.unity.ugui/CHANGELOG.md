# Changelog

## [2.0.0-hb.21] - 2026-02-18
- Added bundled fallback HarfBuzz font bytes under `Runtime/Resources/IndicFlow`, so fresh git installs no longer depend on importing Samples for mobile shaping.
- Updated `TMPro.HarfBuzzTextMeshProUGUI` to automatically use bundled fallback bytes when no explicit `Harf Buzz Font Bytes` or path is configured.
- Clarified package docs: prebuilt native plugins are already shipped under `Runtime/Plugins` for Android/iOS/macOS, and build scripts are optional for rebuilding binaries.

## [2.0.0-hb.20] - 2026-02-18
- Fixed `TMP_LinkInfo` mapping in `TMPro.HarfBuzzTextMeshProUGUI` to use character indices (TMP-compatible) instead of glyph indices.
- Restored expected `textInfo.linkInfo` behavior for shaped Hindi text, including `GetLinkText()` and downstream link-hit / word-index workflows.

## [2.0.0-hb.19] - 2026-02-17
- Implemented **No-Join Word Settings** support in the package runtime.
- Added project-level no-join configuration through `HarfBuzzHindiNoJoinSettings`.
- Added selective disjoin support to break only targeted conjunct patterns inside specified words.
- Added editor configuration workflow to manage no-join entries and selective rules efficiently.

## [2.0.0-hb.1] - 2026-02-16
- Implemented HarfBuzz-based Hindi/Devanagari shaping successfully for TMP UGUI.
- Added `TMPro.HarfBuzzTextMeshProUGUI` for proper shaping behavior across ligatures, matras, nasal marks, and nuqta forms.
- Included native plugin integration for Editor, Android, and iOS targets.
- Established stable font input workflow using font bytes for reliable mobile rendering.
