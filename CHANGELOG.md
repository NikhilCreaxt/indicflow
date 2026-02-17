# Changelog

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
