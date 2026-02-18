# IndicFlow - TMP Text Shaping Engine

IndicFlow adds HarfBuzz-based shaping support for Hindi / Devanagari in TMP UGUI.

## Component

Use:

- `TMPro.HarfBuzzTextMeshProUGUI`

It is available in Add Component as:

- `UI (Canvas) > HarfBuzz TextMeshPro - Text (UI)`

It is also available in Create menu as:

- `GameObject > UI (Canvas) > Text - HarfBuzzTextMeshPro`

## Why this package

Default TMP shaping may not correctly handle all Devanagari features in some cases.  
This component shapes text using HarfBuzz and renders using TMP atlas/material.

## Supported shaping targets

- Matras (vowel signs)
- Conjuncts / ligatures
- Anusvar / chandrabindu
- Nuqta characters

## Setup

1. Create a UI text object using `HarfBuzzTextMeshProUGUI`.
2. TMP font asset:
   - If you assign one, that font is used.
   - If not assigned, package bundled fallback TMP font asset from `Runtime/Resources/IndicFlow` is used automatically.
3. HarfBuzz font source:
   - If you assign `m_HarfBuzzFontBytes` (`.ttf.bytes`), that file is used.
   - Else if you assign `m_HarfBuzzFontPath`, that path is used.
   - Else package bundled fallback bytes from `Runtime/Resources/IndicFlow` are used automatically (no sample import required).
4. Set language (default `hi`).

## No-Join Control (Full or Selective)

To control conjunct joining:

1. Open `Tools > TextMeshPro > HarfBuzz Hindi > No-Join Word Settings`.
2. Create/edit `Assets/Resources/HarfBuzzHindiNoJoinSettings.asset`.
3. Full no-join: add words in `No-Join Words`.
4. Selective no-join: add `Selective Disjoin Rules` entries:
   - `Word`: exact word match
   - `Disjoin Patterns`: only those conjunct patterns will be broken
5. For bulk full-word setup, paste newline/comma-separated words into `Bulk Add` and click `Add Pasted Words`.
6. Keep `Disable Conjunct Joining For Configured Words` enabled on `HarfBuzzTextMeshProUGUI`.

How it works:
- Full no-join words: inserts `ZWNJ` after every halant (`\u094D`) in the word.
- Selective rules: inserts `ZWNJ` only for the specified conjunct patterns in that word.
- This keeps the rest of your Hindi text fully shaped as normal.

## Native plugin

The component uses native library `HindiHarfBuzz`.

Prebuilt native plugins are included in this package:

- macOS: `Runtime/Plugins/macOS/libHindiHarfBuzz.dylib`
- Android: `Runtime/Plugins/Android/arm64-v8a/libHindiHarfBuzz.so`
- iOS: `Runtime/Plugins/iOS/HindiHarfBuzz.xcframework`

Build scripts in `Runtime/HindiHarfBuzz` are only for rebuilding native binaries, not required for normal package use.

## Git URL install

Install with:

```json
{
  "dependencies": {
    "com.unity.ugui": "https://github.com/NikhilCreaxt/indicflow.git?path=/Packages/com.unity.ugui#v2.0.0-hb.23"
  }
}
```

Use this if you want Package Manager `Update` to follow latest stable branch:

`https://github.com/NikhilCreaxt/indicflow.git?path=/Packages/com.unity.ugui`

Or in Package Manager, add package from git URL:

`https://github.com/NikhilCreaxt/indicflow.git?path=/Packages/com.unity.ugui#v2.0.0-hb.23`
