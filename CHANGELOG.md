# Changelog

## [2.0.0-hb.23] - 2026-02-18
- Added bundled fallback TMP font asset to package runtime resources.
- `TMPro.HarfBuzzTextMeshProUGUI` now auto-assigns bundled fallback TMP font asset when no font asset is set on the component.
- Combined with bundled fallback `.ttf.bytes`, fresh git installs now work without importing Samples for required font assets.

## [2.0.0-hb.22] - 2026-02-18
- Fixed bundled fallback font resource loading by resolving `.ttf.bytes` via the correct Resources path (`...ttf`) in player builds.
- Added resilient fallback lookup for both legacy and corrected resource keys.

## [2.0.0-hb.21] - 2026-02-18
- Added bundled fallback HarfBuzz font bytes under `Runtime/Resources/IndicFlow`, so fresh git installs no longer depend on importing Samples for mobile shaping.
- Updated `TMPro.HarfBuzzTextMeshProUGUI` to automatically use bundled fallback bytes when no explicit `Harf Buzz Font Bytes` or path is configured.
- Clarified package docs: prebuilt native plugins are already shipped under `Runtime/Plugins` for Android/iOS/macOS, and build scripts are optional for rebuilding binaries.

## [2.0.0-hb.20] - 2026-02-18
- Fixed `TMP_LinkInfo` mapping in `TMPro.HarfBuzzTextMeshProUGUI` to use character indices (TMP-compatible) instead of glyph indices.
- Restored expected `textInfo.linkInfo` behavior for shaped Hindi text, including `GetLinkText()` and downstream link-hit / word-index workflows.

## [2.0.0-hb.19] - 2026-02-17
- Implemented **No-Join Word Settings** for precise control over Hindi conjunct rendering.
- Added a configurable settings asset (`HarfBuzzHindiNoJoinSettings`) to define words that should not auto-join.
- Added selective disjoin rules so only specific conjunct patterns inside a word can be broken when required.
- Added editor tooling for easier setup and bulk entry, reducing manual per-word configuration effort.

## [2.0.0-hb.1] - 2026-02-16
- Implemented HarfBuzz-based Hindi/Devanagari shaping successfully in TMP UGUI.
- Added `TMPro.HarfBuzzTextMeshProUGUI` rendering flow for correct matras, conjuncts, bindu/chandrabindu, and nuqta handling.
- Established cross-platform runtime support for Editor, Android, and iOS with native plugin integration.
- Standardized font-source usage (`.ttf.bytes`) for stable mobile shaping behavior.
