using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.TextCore;

namespace TMPro
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI (Canvas)/HarfBuzz TextMeshPro - Text (UI)", 12)]
    public class HarfBuzzTextMeshProUGUI : TextMeshProUGUI
    {
        [Serializable]
        private struct ParsedLinkRange
        {
            public string Id;
            public int StartIndex;
            public int Length;

            public ParsedLinkRange(string id, int startIndex, int length)
            {
                Id = id;
                StartIndex = startIndex;
                Length = length;
            }
        }

        private struct OpenLinkMarker
        {
            public string Id;
            public int StartIndex;

            public OpenLinkMarker(string id, int startIndex)
            {
                Id = id;
                StartIndex = startIndex;
            }
        }

        [Serializable]
        private sealed class LineLayout
        {
            public readonly List<TMP_HBGlyph> glyphs = new List<TMP_HBGlyph>();
            public float width;
        }

        private static readonly uint k_DevanagariScriptTag = TMP_HarfBuzzNative.MakeTag("deva");
        private static readonly string[] k_DefaultBundledFontResourcePaths =
        {
            "IndicFlow/NotoSansDevanagari-VariableFont_wdth,wght.ttf",
            "IndicFlow/NotoSansDevanagari-VariableFont_wdth,wght"
        };
        private static readonly string[] k_DefaultBundledFontAssetResourcePaths =
        {
            "IndicFlow/IndicFlow_Default_Devanagari_SDF",
            "IndicFlow/NotoSansDevanagari-VariableFont_wdth,wght SDF"
        };
        private const char k_DevanagariVirama = '\u094D';
        private const char k_ZeroWidthNonJoiner = '\u200C';
        private const char k_ZeroWidthJoiner = '\u200D';

        [SerializeField] private bool m_EnableHarfBuzz = true;
        [SerializeField] private TextAsset m_HarfBuzzFontBytes;
        [SerializeField] private string m_HarfBuzzFontPath;
        [SerializeField] private string m_HarfBuzzLanguage = "hi";
        [SerializeField] private bool m_ForceDevanagariScript = true;
        [SerializeField] private bool m_FallbackToDefaultTMP = true;
        [SerializeField] private bool m_UseBundledIndicFlowDefaults = true;
        [SerializeField] private bool m_EnableSimpleWordWrap = true;
        [SerializeField] private bool m_DisableConjunctJoiningForConfiguredWords = true;
        [SerializeField] private bool m_UseGlobalNoJoinWordSettings = true;
        [SerializeField] private List<string> m_NoJoinWords = new List<string>();

        private string m_ResolvedFontPath;
        private string m_CachedEmbeddedFontPath;
        private TMP_HBFontHandle m_FontHandle;
        private TMP_HBGlyph[] m_ShapeBuffer = Array.Empty<TMP_HBGlyph>();
        private bool m_LoggedMissingFontWarning;
        private bool m_LoggedInitFailure;
        private bool m_LoggedInitSuccess;
        private bool m_LoggedShapingFallbackWarning;
        private bool m_LoggedBundledFontFallback;
        private bool m_LoggedBundledFontAssetFallback;

        private readonly List<LineLayout> m_Lines = new List<LineLayout>();
        private readonly Dictionary<string, LineLayout> m_TokenShapeCache = new Dictionary<string, LineLayout>();
        private readonly Dictionary<string, string> m_EffectiveWordReplacementMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> m_EffectiveReplacementKeys = new List<string>();
        private readonly List<Color32> m_PreNoJoinCharColors = new List<Color32>();
        private readonly List<Color32> m_ShapedCharColors = new List<Color32>();
        private readonly List<float> m_PreNoJoinCharScales = new List<float>();
        private readonly List<float> m_ShapedCharScales = new List<float>();
        private readonly List<ParsedLinkRange> m_PreNoJoinLinks = new List<ParsedLinkRange>();
        private readonly List<ParsedLinkRange> m_ShapedLinks = new List<ParsedLinkRange>();
        private readonly List<int> m_ProcessedSourceCharIndices = new List<int>();
        private readonly List<int> m_Utf8ByteToCharIndex = new List<int>();
        private readonly List<uint> m_UniqueClusters = new List<uint>();
        private readonly Dictionary<uint, int> m_ClusterStartCharIndices = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> m_ClusterEndCharIndices = new Dictionary<uint, int>();
        private readonly List<int> m_CharLineNumbers = new List<int>();
        private readonly List<float> m_CharBaselines = new List<float>();
        private readonly List<float> m_CharMinX = new List<float>();
        private readonly List<float> m_CharMaxX = new List<float>();
        private readonly List<float> m_CharMinY = new List<float>();
        private readonly List<float> m_CharMaxY = new List<float>();
        private readonly List<int> m_CharFirstVertexIndex = new List<int>();
        private readonly List<bool> m_CharHasGeometry = new List<bool>();
        private readonly List<Color32> m_CharColors = new List<Color32>();
        private readonly List<TMP_CharacterInfo> m_CharacterInfoBuffer = new List<TMP_CharacterInfo>();

        private readonly List<Vector3> m_Vertices = new List<Vector3>();
        private readonly List<Vector4> m_Uvs0 = new List<Vector4>();
        private readonly List<Vector2> m_Uvs2 = new List<Vector2>();
        private readonly List<Color32> m_Colors32 = new List<Color32>();
        private readonly List<int> m_Triangles = new List<int>();

        private static TextAsset s_BundledFallbackFontBytes;
        private static bool s_BundledFallbackFontLoaded;
        private static TMP_FontAsset s_BundledFallbackFontAsset;
        private static bool s_BundledFallbackFontAssetLoaded;

        private void Reset()
        {
            if (!m_UseBundledIndicFlowDefaults)
                return;

            TextAsset bundledFontBytes = GetBundledFallbackFontBytes();
            if (bundledFontBytes != null)
                m_HarfBuzzFontBytes = bundledFontBytes;

            TMP_FontAsset bundledFontAsset = GetBundledFallbackFontAsset();
            if (bundledFontAsset == null)
                return;

            font = bundledFontAsset;
            if (bundledFontAsset.material != null)
                fontSharedMaterial = bundledFontAsset.material;
        }

        protected override void OnDisable()
        {
            ReleaseFontHandle();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            ReleaseFontHandle();
            base.OnDestroy();
        }

        protected override void GenerateTextMesh()
        {
            if (!m_EnableHarfBuzz)
            {
                base.GenerateTextMesh();
                return;
            }

            if (m_UseBundledIndicFlowDefaults)
            {
                TextAsset bundledFontBytes = GetBundledFallbackFontBytes();
                if (bundledFontBytes != null)
                    m_HarfBuzzFontBytes = bundledFontBytes;
            }

            EnsureBundledFallbackFontAssetAssigned(m_UseBundledIndicFlowDefaults);

            if (font == null || string.IsNullOrEmpty(text))
            {
                base.GenerateTextMesh();
                return;
            }

            if (!EnsureFontHandle())
            {
                if (m_FallbackToDefaultTMP)
                {
                    base.GenerateTextMesh();
                }
                else
                {
                    ClearMesh();
                    SyncClearedTextInfoMeshData();
                    m_IsAutoSizePointSizeSet = true;
                }

                return;
            }

            if (!GenerateHarfBuzzMesh())
            {
                if (m_FallbackToDefaultTMP)
                {
                    base.GenerateTextMesh();
                }
                else
                {
                    ClearMesh();
                    SyncClearedTextInfoMeshData();
                    m_IsAutoSizePointSizeSet = true;
                }

                return;
            }

            m_IsAutoSizePointSizeSet = true;
            TMPro_EventManager.ON_TEXT_CHANGED(this);
        }

        private bool GenerateHarfBuzzMesh()
        {
            TMP_FontAsset fontAsset = font;
            if (fontAsset == null)
                return false;

            fontAsset.ReadFontAssetDefinition();

            Material renderMaterial = fontSharedMaterial;
            if (fontSharedMaterial == null && fontAsset.material != null)
            {
                fontSharedMaterial = fontAsset.material;
                renderMaterial = fontSharedMaterial;
            }

            float unitsPerEm = m_FontHandle.UnitsPerEm > 0 ? m_FontHandle.UnitsPerEm : 1000f;
            float pointSize = Mathf.Max(1f, fontAsset.faceInfo.pointSize);
            float faceScale = fontAsset.faceInfo.scale;
            float fontScale = fontSize / pointSize * faceScale;
            float hbScale = fontSize / unitsPerEm * faceScale;
            float xScale = ComputeSdfXScale((fontStyle & FontStyles.Bold) == FontStyles.Bold);

            Rect rect = rectTransform.rect;
            Vector4 margins = margin;
            float contentWidth = Mathf.Max(0f, rect.width - margins.x - margins.z);
            float maxLineWidthUnits = m_EnableSimpleWordWrap && hbScale > 0 ? contentWidth / hbScale : float.PositiveInfinity;

            string shapeSource = GetProcessedTextForShaping();
            if (string.IsNullOrEmpty(shapeSource))
                return false;

            if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Static)
            {
                fontAsset.TryAddCharacters(shapeSource);
            }

            ShapeTextIntoLines(shapeSource, maxLineWidthUnits);

            float ascender = fontAsset.faceInfo.ascentLine * fontScale;
            float descender = fontAsset.faceInfo.descentLine * fontScale;
            float lineAdvance = fontAsset.faceInfo.lineHeight * fontScale;

            float contentHeight = Mathf.Max(0f, rect.height - margins.y - margins.w);
            float totalHeight = (ascender - descender) + Mathf.Max(0, m_Lines.Count - 1) * lineAdvance;

            float firstBaselineY = rect.yMax - margins.y - ascender;
            VerticalAlign verticalAlign = ResolveVerticalAlign();
            if (verticalAlign == VerticalAlign.Middle)
                firstBaselineY -= (contentHeight - totalHeight) * 0.5f;
            else if (verticalAlign == VerticalAlign.Bottom)
                firstBaselineY -= contentHeight - totalHeight;

            m_Vertices.Clear();
            m_Uvs0.Clear();
            m_Uvs2.Clear();
            m_Colors32.Clear();
            m_Triangles.Clear();
            m_CharacterInfoBuffer.Clear();
            InitializeCharacterMetadata(shapeSource);
            BuildClusterCharRangeMaps(shapeSource.Length);

            HorizontalAlign horizontalAlign = ResolveHorizontalAlign();
            Color32 fallbackVertexColor = color;
            int resolvedGlyphCount = 0;
            int zeroGlyphCount = 0;
            int totalGlyphCount = CountTotalGlyphs();
            bool clusterMappingUsable = IsClusterColorMappingUsable(totalGlyphCount);
            int globalGlyphIndex = 0;

            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                LineLayout line = m_Lines[lineIndex];
                int lineStartGlyphIndex = globalGlyphIndex;
                float lineWidth = ComputeRenderedLineWidth(line, hbScale, lineStartGlyphIndex, totalGlyphCount, clusterMappingUsable);

                float lineStartX = rect.xMin + margins.x;
                if (horizontalAlign == HorizontalAlign.Center)
                    lineStartX += (contentWidth - lineWidth) * 0.5f;
                else if (horizontalAlign == HorizontalAlign.Right)
                    lineStartX += contentWidth - lineWidth;

                float penX = lineStartX;
                float penY = firstBaselineY - lineIndex * lineAdvance;

                for (int i = 0; i < line.glyphs.Count; i++)
                {
                    TMP_HBGlyph shapedGlyph = line.glyphs[i];
                    if (shapedGlyph.GlyphId == 0)
                        zeroGlyphCount++;

                    float glyphScale = ResolveScaleForGlyph(
                        shapedGlyph.Cluster,
                        globalGlyphIndex,
                        totalGlyphCount,
                        clusterMappingUsable);
                    float advanceX = shapedGlyph.XAdvance * hbScale * glyphScale;
                    float advanceY = shapedGlyph.YAdvance * hbScale * glyphScale;
                    bool glyphVisible = false;
                    int vertexStart = 0;
                    float glyphMinX = penX;
                    float glyphMaxX = penX + advanceX;
                    float glyphMinY = penY + descender;
                    float glyphMaxY = penY + ascender;
                    Color32 glyphColorForMetadata = fallbackVertexColor;

                    if (TryResolveGlyph(fontAsset, shapedGlyph.GlyphId, out Glyph glyph))
                    {
                        Color32 glyphColor = ResolveColorForGlyph(
                            shapedGlyph.Cluster,
                            fallbackVertexColor,
                            globalGlyphIndex,
                            totalGlyphCount,
                            clusterMappingUsable);
                        glyphColorForMetadata = glyphColor;
                        glyphVisible = AddGlyphQuad(
                            fontAsset,
                            glyph,
                            shapedGlyph,
                            penX,
                            penY,
                            hbScale * glyphScale,
                            fontScale * glyphScale,
                            xScale,
                            glyphColor,
                            out vertexStart,
                            out glyphMinX,
                            out glyphMaxX,
                            out glyphMinY,
                            out glyphMaxY);
                        resolvedGlyphCount++;
                    }

                    ResolveCharacterSpanForGlyph(
                        shapedGlyph.Cluster,
                        globalGlyphIndex,
                        totalGlyphCount,
                        clusterMappingUsable,
                        shapeSource.Length,
                        out int charStart,
                        out int charEnd);

                    ApplyGlyphToCharacterMetadata(
                        charStart,
                        charEnd,
                        lineIndex,
                        penY,
                        glyphMinX,
                        glyphMaxX,
                        glyphMinY,
                        glyphMaxY,
                        glyphVisible,
                        vertexStart,
                        glyphColorForMetadata);

                    penX += advanceX;
                    penY += advanceY;
                    globalGlyphIndex++;
                }
            }

            if (resolvedGlyphCount > 0 && zeroGlyphCount >= resolvedGlyphCount)
            {
                if (!m_LoggedShapingFallbackWarning)
                {
                    Debug.LogWarning(
                        "HarfBuzz shaping produced mostly .notdef glyphs for this text. Falling back to TMP default rendering for this frame.",
                        this);
                    m_LoggedShapingFallbackWarning = true;
                }
                ClearMesh();
                return false;
            }

            m_LoggedShapingFallbackWarning = false;

            if (m_Vertices.Count == 0)
            {
                ClearMesh();
                return false;
            }

            if (m_mesh == null)
            {
                m_mesh = new Mesh();
                m_mesh.hideFlags = HideFlags.HideAndDontSave;
                m_mesh.name = "TextMeshPro UI Mesh";
            }

            m_mesh.Clear();
            m_mesh.MarkDynamic();
            m_mesh.SetVertices(m_Vertices);
            m_mesh.SetUVs(0, m_Uvs0);
            m_mesh.SetUVs(1, m_Uvs2);
            m_mesh.SetColors(m_Colors32);
            m_mesh.SetTriangles(m_Triangles, 0);
            m_mesh.RecalculateBounds();
            BuildCharacterInfoFromMetadata(shapeSource, firstBaselineY, lineAdvance, ascender, descender);
            SyncCharacterAndLinkInfo();
            SyncTextInfoMeshData();

            Canvas targetCanvas = canvas;
            if (targetCanvas != null && targetCanvas.additionalShaderChannels != (AdditionalCanvasShaderChannels)25)
                targetCanvas.additionalShaderChannels |= (AdditionalCanvasShaderChannels)25;

            UpdateGeometry(m_mesh, 0);

            if (renderMaterial == null)
                renderMaterial = materialForRendering;

            Texture atlasTexture = (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                ? fontAsset.atlasTextures[0]
                : mainTexture;

            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(renderMaterial, 0);
            canvasRenderer.SetTexture(atlasTexture);

            return true;
        }

        private void ShapeTextIntoLines(string sourceText, float maxLineWidthUnits)
        {
            m_Lines.Clear();
            m_TokenShapeCache.Clear();

            string normalized = (sourceText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] paragraphs = normalized.Split('\n');
            int paragraphStartByteOffset = 0;

            for (int i = 0; i < paragraphs.Length; i++)
            {
                string paragraph = paragraphs[i];
                int paragraphByteLength = Encoding.UTF8.GetByteCount(paragraph);
                if (string.IsNullOrEmpty(paragraph))
                {
                    m_Lines.Add(new LineLayout());
                    if (i < paragraphs.Length - 1)
                        paragraphStartByteOffset += 1;
                    continue;
                }

                if (!m_EnableSimpleWordWrap || float.IsInfinity(maxLineWidthUnits) || maxLineWidthUnits <= 0)
                {
                    m_Lines.Add(ShapeToken(paragraph, paragraphStartByteOffset));
                    paragraphStartByteOffset += paragraphByteLength;
                    if (i < paragraphs.Length - 1)
                        paragraphStartByteOffset += 1;
                    continue;
                }

                string[] words = paragraph.Split(' ');
                LineLayout currentLine = new LineLayout();
                int tokenByteCursor = paragraphStartByteOffset;

                for (int w = 0; w < words.Length; w++)
                {
                    bool hasLeadingSpace = w > 0;
                    string tokenText = hasLeadingSpace ? " " + words[w] : words[w];
                    if (tokenText.Length == 0)
                        continue;

                    int tokenByteLength = Encoding.UTF8.GetByteCount(tokenText);
                    int tokenByteOffset = tokenByteCursor;
                    LineLayout token = ShapeToken(tokenText, tokenByteOffset);

                    if (currentLine.glyphs.Count > 0 && currentLine.width + token.width > maxLineWidthUnits)
                    {
                        m_Lines.Add(currentLine);
                        currentLine = new LineLayout();

                        string trimmedToken = tokenText.TrimStart(' ');
                        if (trimmedToken.Length == 0)
                        {
                            tokenByteCursor += tokenByteLength;
                            continue;
                        }

                        int trimmedLeadingChars = tokenText.Length - trimmedToken.Length;
                        int trimmedLeadingBytes = trimmedLeadingChars > 0
                            ? Encoding.UTF8.GetByteCount(tokenText.Substring(0, trimmedLeadingChars))
                            : 0;

                        token = ShapeToken(trimmedToken, tokenByteOffset + trimmedLeadingBytes);
                    }

                    currentLine.glyphs.AddRange(token.glyphs);
                    currentLine.width += token.width;
                    tokenByteCursor += tokenByteLength;
                }

                m_Lines.Add(currentLine);
                paragraphStartByteOffset += paragraphByteLength;
                if (i < paragraphs.Length - 1)
                    paragraphStartByteOffset += 1;
            }

            if (m_Lines.Count == 0)
                m_Lines.Add(new LineLayout());
        }

        private string GetProcessedTextForShaping()
        {
            ParseInputText();

            if (m_TextProcessingArray == null || m_TextProcessingArray.Length == 0)
            {
                string fallbackSource = text ?? string.Empty;
                string fallbackStripped = StripRichTextTagsForShaping(fallbackSource, m_PreNoJoinCharColors, m_PreNoJoinCharScales, m_PreNoJoinLinks);
                string fallbackProcessed = ApplyNoJoinWordConfiguration(fallbackStripped);
                BuildProcessedCharColors(fallbackStripped, fallbackProcessed);
                BuildProcessedCharScales(fallbackStripped, fallbackProcessed);
                BuildProcessedLinks(fallbackStripped, fallbackProcessed);
                BuildUtf8ByteToCharIndexMap(fallbackProcessed);
                return fallbackProcessed;
            }

            StringBuilder builder = new StringBuilder(m_TextProcessingArray.Length);

            for (int i = 0; i < m_TextProcessingArray.Length; i++)
            {
                uint unicode = m_TextProcessingArray[i].unicode;
                if (unicode == 0)
                    break;

                if (unicode == 0x1A)
                    continue;

                if (unicode <= 0xFFFF)
                {
                    builder.Append((char)unicode);
                }
                else
                {
                    builder.Append(char.ConvertFromUtf32((int)unicode));
                }
            }

            string strippedSource = StripRichTextTagsForShaping(builder.ToString(), m_PreNoJoinCharColors, m_PreNoJoinCharScales, m_PreNoJoinLinks);
            string processedSource = ApplyNoJoinWordConfiguration(strippedSource);
            BuildProcessedCharColors(strippedSource, processedSource);
            BuildProcessedCharScales(strippedSource, processedSource);
            BuildProcessedLinks(strippedSource, processedSource);
            BuildUtf8ByteToCharIndexMap(processedSource);
            return processedSource;
        }

        private string StripRichTextTagsForShaping(string sourceText, List<Color32> outputCharColors, List<float> outputCharScales, List<ParsedLinkRange> outputLinks)
        {
            outputCharColors.Clear();
            outputCharScales.Clear();
            outputLinks.Clear();

            if (string.IsNullOrEmpty(sourceText))
                return sourceText;

            Color32 defaultColor = color;
            float defaultScale = 1f;
            if (!m_isRichText)
            {
                StringBuilder plainBuilder = new StringBuilder(sourceText.Length);
                for (int i = 0; i < sourceText.Length; i++)
                    AppendStyledChar(plainBuilder, outputCharColors, outputCharScales, sourceText[i], defaultColor, defaultScale);

                return plainBuilder.ToString();
            }

            StringBuilder builder = new StringBuilder(sourceText.Length);
            Stack<Color32> colorStack = new Stack<Color32>();
            colorStack.Push(defaultColor);
            Color32 currentColor = defaultColor;
            Stack<float> sizeScaleStack = new Stack<float>();
            sizeScaleStack.Push(defaultScale);
            float currentScale = defaultScale;
            Stack<OpenLinkMarker> linkStack = new Stack<OpenLinkMarker>();
            bool noParseMode = false;

            for (int i = 0; i < sourceText.Length; i++)
            {
                char c = sourceText[i];
                if (c == '<')
                {
                    int tagEnd = FindRichTextTagEnd(sourceText, i + 1);
                    if (tagEnd > i)
                    {
                        string tagToken = sourceText.Substring(i + 1, tagEnd - i - 1);
                        string trimmedTag = tagToken.Trim();

                        if (noParseMode)
                        {
                            if (trimmedTag.Length > 0 &&
                                trimmedTag[0] == '/' &&
                                string.Equals(ExtractTagName(trimmedTag, true), "noparse", StringComparison.OrdinalIgnoreCase))
                            {
                                noParseMode = false;
                            }
                            else
                            {
                                AppendStyledChar(builder, outputCharColors, outputCharScales, '<', currentColor, currentScale);
                                AppendStyledString(builder, outputCharColors, outputCharScales, tagToken, currentColor, currentScale);
                                AppendStyledChar(builder, outputCharColors, outputCharScales, '>', currentColor, currentScale);
                            }

                            i = tagEnd;
                            continue;
                        }

                        if (ProcessRichTextTagToken(
                                trimmedTag,
                                builder,
                                outputCharColors,
                                outputCharScales,
                                colorStack,
                                ref currentColor,
                                sizeScaleStack,
                                ref currentScale,
                                linkStack,
                                outputLinks,
                                ref noParseMode))
                        {
                            i = tagEnd;
                            continue;
                        }
                    }
                }

                AppendStyledChar(builder, outputCharColors, outputCharScales, c, currentColor, currentScale);
            }

            while (linkStack.Count > 0)
                CloseOpenLink(outputLinks, linkStack, builder.Length);

            return builder.ToString();
        }

        private static int FindRichTextTagEnd(string sourceText, int startIndex)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = startIndex; i < sourceText.Length; i++)
            {
                char c = sourceText[i];
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }

                if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }

                if (c == '>' && !inSingleQuote && !inDoubleQuote)
                    return i;
            }

            return -1;
        }

        private static void AppendStyledString(
            StringBuilder builder,
            List<Color32> outputCharColors,
            List<float> outputCharScales,
            string value,
            Color32 colorValue,
            float scaleValue)
        {
            if (string.IsNullOrEmpty(value))
                return;

            for (int i = 0; i < value.Length; i++)
                AppendStyledChar(builder, outputCharColors, outputCharScales, value[i], colorValue, scaleValue);
        }

        private static void AppendStyledChar(
            StringBuilder builder,
            List<Color32> outputCharColors,
            List<float> outputCharScales,
            char value,
            Color32 colorValue,
            float scaleValue)
        {
            builder.Append(value);
            outputCharColors.Add(colorValue);
            outputCharScales.Add(scaleValue);
        }

        private bool ProcessRichTextTagToken(
            string token,
            StringBuilder output,
            List<Color32> outputCharColors,
            List<float> outputCharScales,
            Stack<Color32> colorStack,
            ref Color32 currentColor,
            Stack<float> sizeScaleStack,
            ref float currentScale,
            Stack<OpenLinkMarker> linkStack,
            List<ParsedLinkRange> outputLinks,
            ref bool noParseMode)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            bool isClosing = token[0] == '/';
            string name = ExtractTagName(token, isClosing);
            if (string.IsNullOrEmpty(name))
                return false;

            if (string.Equals(name, "noparse", StringComparison.OrdinalIgnoreCase))
            {
                noParseMode = !isClosing;
                return true;
            }

            if (isClosing)
            {
                if (string.Equals(name, "color", StringComparison.OrdinalIgnoreCase) && colorStack.Count > 1)
                {
                    colorStack.Pop();
                    currentColor = colorStack.Peek();
                }
                else if (string.Equals(name, "size", StringComparison.OrdinalIgnoreCase) && sizeScaleStack.Count > 1)
                {
                    sizeScaleStack.Pop();
                    currentScale = sizeScaleStack.Peek();
                }
                else if (string.Equals(name, "link", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "a", StringComparison.OrdinalIgnoreCase))
                {
                    CloseOpenLink(outputLinks, linkStack, output.Length);
                }

                return true;
            }

            if (string.Equals(name, "color", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseColorTag(token, out Color32 parsedColor))
                {
                    colorStack.Push(parsedColor);
                    currentColor = parsedColor;
                }

                return true;
            }

            if (string.Equals(name, "size", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseSizeTag(token, currentScale, out float parsedScale))
                {
                    sizeScaleStack.Push(parsedScale);
                    currentScale = parsedScale;
                }

                return true;
            }

            if (string.Equals(name, "link", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseLinkTagId(token, out string linkId))
                    linkStack.Push(new OpenLinkMarker(linkId, output.Length));

                return true;
            }

            if (string.Equals(name, "a", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseNamedAttributeValue(token, "href", out string hrefValue))
                    linkStack.Push(new OpenLinkMarker(hrefValue, output.Length));

                return true;
            }

            if (string.Equals(name, "br", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "cr", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyledChar(output, outputCharColors, outputCharScales, '\n', currentColor, currentScale);
                return true;
            }

            if (string.Equals(name, "nbsp", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyledChar(output, outputCharColors, outputCharScales, '\u00A0', currentColor, currentScale);
                return true;
            }

            if (string.Equals(name, "zwsp", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyledChar(output, outputCharColors, outputCharScales, '\u200B', currentColor, currentScale);
                return true;
            }

            if (string.Equals(name, "zwj", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyledChar(output, outputCharColors, outputCharScales, '\u200D', currentColor, currentScale);
                return true;
            }

            if (string.Equals(name, "shy", StringComparison.OrdinalIgnoreCase))
            {
                AppendStyledChar(output, outputCharColors, outputCharScales, '\u00AD', currentColor, currentScale);
                return true;
            }

            // Unknown / unsupported rich-text tags are intentionally consumed
            // so they don't leak into shaped output.
            return true;
        }

        private static void CloseOpenLink(List<ParsedLinkRange> outputLinks, Stack<OpenLinkMarker> linkStack, int currentCharacterCount)
        {
            if (linkStack.Count == 0)
                return;

            OpenLinkMarker marker = linkStack.Pop();
            int linkLength = Mathf.Max(0, currentCharacterCount - marker.StartIndex);
            if (linkLength <= 0 || string.IsNullOrEmpty(marker.Id))
                return;

            outputLinks.Add(new ParsedLinkRange(marker.Id, marker.StartIndex, linkLength));
        }

        private static bool TryParseLinkTagId(string token, out string linkId)
        {
            linkId = string.Empty;
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex < 0 || equalsIndex >= token.Length - 1)
                return false;

            return TryParseAttributeValue(token, equalsIndex + 1, out linkId);
        }

        private static bool TryParseNamedAttributeValue(string token, string attributeName, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(attributeName))
                return false;

            int i = 0;
            while (i < token.Length)
            {
                while (i < token.Length && char.IsWhiteSpace(token[i]))
                    i++;

                if (i >= token.Length)
                    break;

                int nameStart = i;
                while (i < token.Length && !char.IsWhiteSpace(token[i]) && token[i] != '=')
                    i++;

                if (i <= nameStart)
                    break;

                string currentName = token.Substring(nameStart, i - nameStart);

                while (i < token.Length && char.IsWhiteSpace(token[i]))
                    i++;

                if (i >= token.Length || token[i] != '=')
                {
                    while (i < token.Length && !char.IsWhiteSpace(token[i]))
                        i++;
                    continue;
                }

                i++;
                while (i < token.Length && char.IsWhiteSpace(token[i]))
                    i++;

                if (!TryParseAttributeValue(token, i, out string parsedValue, out int nextIndex))
                    return false;

                i = nextIndex;
                if (string.Equals(currentName, attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    value = parsedValue;
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseAttributeValue(string token, int valueStart, out string value)
        {
            return TryParseAttributeValue(token, valueStart, out value, out _);
        }

        private static bool TryParseAttributeValue(string token, int valueStart, out string value, out int nextIndex)
        {
            value = string.Empty;
            nextIndex = valueStart;
            if (valueStart < 0 || valueStart >= token.Length)
                return false;

            int i = valueStart;
            if (token[i] == '"' || token[i] == '\'')
            {
                char quote = token[i++];
                int contentStart = i;
                while (i < token.Length && token[i] != quote)
                    i++;

                if (i >= token.Length)
                    return false;

                value = token.Substring(contentStart, i - contentStart);
                nextIndex = i < token.Length ? i + 1 : i;
                return true;
            }

            int start = i;
            while (i < token.Length && !char.IsWhiteSpace(token[i]))
                i++;

            if (i <= start)
                return false;

            value = token.Substring(start, i - start);
            nextIndex = i;
            return true;
        }

        private bool TryParseSizeTag(string token, float currentScale, out float scaleValue)
        {
            scaleValue = currentScale;

            int equalsIndex = token.IndexOf('=');
            if (equalsIndex < 0 || equalsIndex >= token.Length - 1)
                return false;

            string value = token.Substring(equalsIndex + 1).Trim();
            if (value.Length == 0)
                return false;

            if ((value[0] == '"' || value[0] == '\'') && value.Length > 1 && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);

            if (value.Length == 0)
                return false;

            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                string pctText = value.Substring(0, value.Length - 1).Trim();
                if (!float.TryParse(pctText, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
                    return false;

                scaleValue = Mathf.Max(0.01f, pct / 100f);
                return true;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float absoluteSize))
                return false;

            float baseSize = Mathf.Max(0.01f, fontSize);
            scaleValue = Mathf.Max(0.01f, absoluteSize / baseSize);
            return true;
        }

        private static bool TryParseColorTag(string token, out Color32 colorValue)
        {
            colorValue = default;

            int equalsIndex = token.IndexOf('=');
            if (equalsIndex < 0 || equalsIndex >= token.Length - 1)
                return false;

            string value = token.Substring(equalsIndex + 1).Trim();
            if (value.Length == 0)
                return false;

            if ((value[0] == '"' || value[0] == '\'') && value.Length > 1 && value[value.Length - 1] == value[0])
                value = value.Substring(1, value.Length - 2);

            if (value.Length == 0)
                return false;

            if (!value.StartsWith("#", StringComparison.Ordinal) && IsHexColorValue(value))
                value = "#" + value;

            if (!ColorUtility.TryParseHtmlString(value, out Color parsedColor))
                return false;

            colorValue = (Color32)parsedColor;
            return true;
        }

        private static bool IsHexColorValue(string value)
        {
            if (value.Length != 3 && value.Length != 4 && value.Length != 6 && value.Length != 8)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9')
                             || (c >= 'a' && c <= 'f')
                             || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }

            return true;
        }

        private static string ExtractTagName(string token, bool isClosing)
        {
            int index = isClosing ? 1 : 0;
            while (index < token.Length && char.IsWhiteSpace(token[index]))
                index++;

            int start = index;
            while (index < token.Length)
            {
                char c = token[index];
                if (char.IsWhiteSpace(c) || c == '=' || c == '/')
                    break;
                index++;
            }

            if (index <= start)
                return string.Empty;

            return token.Substring(start, index - start);
        }

        private void BuildProcessedCharColors(string sourceWithoutTags, string processedText)
        {
            m_ShapedCharColors.Clear();

            if (string.IsNullOrEmpty(processedText))
                return;

            Color32 defaultColor = color;
            if (m_PreNoJoinCharColors.Count == 0)
            {
                for (int i = 0; i < processedText.Length; i++)
                    m_ShapedCharColors.Add(defaultColor);
                return;
            }

            if (string.Equals(sourceWithoutTags, processedText, StringComparison.Ordinal))
            {
                for (int i = 0; i < processedText.Length; i++)
                {
                    if (i < m_PreNoJoinCharColors.Count)
                        m_ShapedCharColors.Add(m_PreNoJoinCharColors[i]);
                    else
                        m_ShapedCharColors.Add(defaultColor);
                }

                return;
            }

            int sourceIndex = 0;
            Color32 lastColor = m_PreNoJoinCharColors[0];

            for (int processedIndex = 0; processedIndex < processedText.Length; processedIndex++)
            {
                char outputChar = processedText[processedIndex];

                if (sourceIndex < sourceWithoutTags.Length && sourceWithoutTags[sourceIndex] == outputChar)
                {
                    Color32 sourceColor = sourceIndex < m_PreNoJoinCharColors.Count ? m_PreNoJoinCharColors[sourceIndex] : lastColor;
                    m_ShapedCharColors.Add(sourceColor);
                    lastColor = sourceColor;
                    sourceIndex++;
                    continue;
                }

                if (outputChar == k_ZeroWidthNonJoiner || outputChar == k_ZeroWidthJoiner)
                {
                    m_ShapedCharColors.Add(lastColor);
                    continue;
                }

                if (sourceIndex < m_PreNoJoinCharColors.Count)
                    lastColor = m_PreNoJoinCharColors[sourceIndex];

                m_ShapedCharColors.Add(lastColor);
            }
        }

        private void BuildProcessedCharScales(string sourceWithoutTags, string processedText)
        {
            m_ShapedCharScales.Clear();

            if (string.IsNullOrEmpty(processedText))
                return;

            const float defaultScale = 1f;
            if (m_PreNoJoinCharScales.Count == 0)
            {
                for (int i = 0; i < processedText.Length; i++)
                    m_ShapedCharScales.Add(defaultScale);
                return;
            }

            if (string.Equals(sourceWithoutTags, processedText, StringComparison.Ordinal))
            {
                for (int i = 0; i < processedText.Length; i++)
                {
                    if (i < m_PreNoJoinCharScales.Count)
                        m_ShapedCharScales.Add(m_PreNoJoinCharScales[i]);
                    else
                        m_ShapedCharScales.Add(defaultScale);
                }

                return;
            }

            int sourceIndex = 0;
            float lastScale = m_PreNoJoinCharScales[0];

            for (int processedIndex = 0; processedIndex < processedText.Length; processedIndex++)
            {
                char outputChar = processedText[processedIndex];

                if (sourceIndex < sourceWithoutTags.Length && sourceWithoutTags[sourceIndex] == outputChar)
                {
                    float sourceScale = sourceIndex < m_PreNoJoinCharScales.Count ? m_PreNoJoinCharScales[sourceIndex] : lastScale;
                    m_ShapedCharScales.Add(sourceScale);
                    lastScale = sourceScale;
                    sourceIndex++;
                    continue;
                }

                if (outputChar == k_ZeroWidthNonJoiner || outputChar == k_ZeroWidthJoiner)
                {
                    m_ShapedCharScales.Add(lastScale);
                    continue;
                }

                if (sourceIndex < m_PreNoJoinCharScales.Count)
                    lastScale = m_PreNoJoinCharScales[sourceIndex];

                m_ShapedCharScales.Add(lastScale);
            }
        }

        private void BuildProcessedLinks(string sourceWithoutTags, string processedText)
        {
            m_ShapedLinks.Clear();

            if (m_PreNoJoinLinks.Count == 0 || string.IsNullOrEmpty(processedText))
                return;

            if (string.Equals(sourceWithoutTags, processedText, StringComparison.Ordinal))
            {
                for (int i = 0; i < m_PreNoJoinLinks.Count; i++)
                {
                    ParsedLinkRange link = m_PreNoJoinLinks[i];
                    int start = Mathf.Clamp(link.StartIndex, 0, processedText.Length);
                    int end = Mathf.Clamp(link.StartIndex + link.Length, start, processedText.Length);
                    if (end > start && !string.IsNullOrEmpty(link.Id))
                        m_ShapedLinks.Add(new ParsedLinkRange(link.Id, start, end - start));
                }

                return;
            }

            BuildProcessedSourceCharIndices(sourceWithoutTags, processedText);
            if (m_ProcessedSourceCharIndices.Count == 0)
                return;

            for (int i = 0; i < m_PreNoJoinLinks.Count; i++)
            {
                ParsedLinkRange sourceLink = m_PreNoJoinLinks[i];
                int sourceStart = Mathf.Max(0, sourceLink.StartIndex);
                int sourceEnd = Mathf.Max(sourceStart, sourceLink.StartIndex + sourceLink.Length);
                if (sourceEnd <= sourceStart || string.IsNullOrEmpty(sourceLink.Id))
                    continue;

                int processedStart = -1;
                int processedEnd = -1;
                for (int p = 0; p < m_ProcessedSourceCharIndices.Count; p++)
                {
                    int sourceIndex = m_ProcessedSourceCharIndices[p];
                    if (sourceIndex < sourceStart || sourceIndex >= sourceEnd)
                        continue;

                    if (processedStart < 0)
                        processedStart = p;

                    processedEnd = p;
                }

                if (processedStart >= 0 && processedEnd >= processedStart)
                    m_ShapedLinks.Add(new ParsedLinkRange(sourceLink.Id, processedStart, processedEnd - processedStart + 1));
            }
        }

        private void BuildProcessedSourceCharIndices(string sourceWithoutTags, string processedText)
        {
            m_ProcessedSourceCharIndices.Clear();

            if (string.IsNullOrEmpty(processedText))
                return;

            if (string.IsNullOrEmpty(sourceWithoutTags))
            {
                for (int i = 0; i < processedText.Length; i++)
                    m_ProcessedSourceCharIndices.Add(0);
                return;
            }

            int sourceIndex = 0;
            int lastSourceIndex = 0;

            for (int processedIndex = 0; processedIndex < processedText.Length; processedIndex++)
            {
                char outputChar = processedText[processedIndex];

                if (sourceIndex < sourceWithoutTags.Length && sourceWithoutTags[sourceIndex] == outputChar)
                {
                    lastSourceIndex = sourceIndex;
                    m_ProcessedSourceCharIndices.Add(sourceIndex);
                    sourceIndex++;
                    continue;
                }

                if (outputChar == k_ZeroWidthNonJoiner || outputChar == k_ZeroWidthJoiner)
                {
                    m_ProcessedSourceCharIndices.Add(lastSourceIndex);
                    continue;
                }

                if (sourceIndex < sourceWithoutTags.Length)
                {
                    lastSourceIndex = sourceIndex;
                    m_ProcessedSourceCharIndices.Add(sourceIndex);
                    sourceIndex++;
                    continue;
                }

                m_ProcessedSourceCharIndices.Add(lastSourceIndex);
            }
        }

        private void BuildUtf8ByteToCharIndexMap(string textValue)
        {
            m_Utf8ByteToCharIndex.Clear();

            if (string.IsNullOrEmpty(textValue))
                return;

            for (int i = 0; i < textValue.Length; i++)
            {
                int byteCount;

                if (char.IsHighSurrogate(textValue[i]) && i + 1 < textValue.Length && char.IsLowSurrogate(textValue[i + 1]))
                {
                    byteCount = Encoding.UTF8.GetByteCount(textValue.Substring(i, 2));
                    for (int b = 0; b < byteCount; b++)
                        m_Utf8ByteToCharIndex.Add(i);
                    i++;
                    continue;
                }
                else
                {
                    byteCount = Encoding.UTF8.GetByteCount(textValue.Substring(i, 1));
                }

                if (byteCount <= 0)
                    byteCount = 1;

                for (int b = 0; b < byteCount; b++)
                    m_Utf8ByteToCharIndex.Add(i);
            }
        }

        private Color32 ResolveColorForCluster(uint cluster, Color32 fallbackColor)
        {
            if (m_ShapedCharColors.Count == 0 || m_Utf8ByteToCharIndex.Count == 0)
                return fallbackColor;

            int byteIndex = (int)cluster;
            if (byteIndex < 0)
                return fallbackColor;

            int charIndex = byteIndex < m_Utf8ByteToCharIndex.Count
                ? m_Utf8ByteToCharIndex[byteIndex]
                : m_Utf8ByteToCharIndex[m_Utf8ByteToCharIndex.Count - 1];

            if (charIndex >= 0 && charIndex < m_ShapedCharColors.Count)
                return m_ShapedCharColors[charIndex];

            return fallbackColor;
        }

        private int ResolveCharIndexForCluster(uint cluster)
        {
            if (m_Utf8ByteToCharIndex.Count == 0)
                return 0;

            int byteIndex = (int)cluster;
            if (byteIndex < 0)
                return 0;

            if (byteIndex < m_Utf8ByteToCharIndex.Count)
                return m_Utf8ByteToCharIndex[byteIndex];

            return m_Utf8ByteToCharIndex[m_Utf8ByteToCharIndex.Count - 1];
        }

        private int ResolveCharIndexForGlyph(uint cluster, int glyphIndex, int totalGlyphCount, bool clusterMappingUsable, int processedCharacterCount)
        {
            if (processedCharacterCount <= 0)
                return 0;

            if (clusterMappingUsable)
                return Mathf.Clamp(ResolveCharIndexForCluster(cluster), 0, processedCharacterCount - 1);

            if (totalGlyphCount <= 1)
                return 0;

            float t = Mathf.Clamp01((float)glyphIndex / (totalGlyphCount - 1));
            return Mathf.Clamp(Mathf.RoundToInt(t * (processedCharacterCount - 1)), 0, processedCharacterCount - 1);
        }

        private Color32 ResolveColorForGlyph(uint cluster, Color32 fallbackColor, int glyphIndex, int totalGlyphCount, bool clusterMappingUsable)
        {
            if (clusterMappingUsable)
                return ResolveColorForCluster(cluster, fallbackColor);

            if (m_ShapedCharColors.Count == 0)
                return fallbackColor;

            if (m_ShapedCharColors.Count == 1 || totalGlyphCount <= 1)
                return m_ShapedCharColors[0];

            float t = Mathf.Clamp01((float)glyphIndex / (totalGlyphCount - 1));
            int charIndex = Mathf.Clamp(Mathf.RoundToInt(t * (m_ShapedCharColors.Count - 1)), 0, m_ShapedCharColors.Count - 1);
            return m_ShapedCharColors[charIndex];
        }

        private float ResolveScaleForCluster(uint cluster, float fallbackScale)
        {
            if (m_ShapedCharScales.Count == 0 || m_Utf8ByteToCharIndex.Count == 0)
                return fallbackScale;

            int byteIndex = (int)cluster;
            if (byteIndex < 0)
                return fallbackScale;

            int charIndex = byteIndex < m_Utf8ByteToCharIndex.Count
                ? m_Utf8ByteToCharIndex[byteIndex]
                : m_Utf8ByteToCharIndex[m_Utf8ByteToCharIndex.Count - 1];

            if (charIndex >= 0 && charIndex < m_ShapedCharScales.Count)
                return Mathf.Max(0.01f, m_ShapedCharScales[charIndex]);

            return fallbackScale;
        }

        private float ResolveScaleForGlyph(uint cluster, int glyphIndex, int totalGlyphCount, bool clusterMappingUsable)
        {
            const float fallbackScale = 1f;

            if (clusterMappingUsable)
                return ResolveScaleForCluster(cluster, fallbackScale);

            if (m_ShapedCharScales.Count == 0)
                return fallbackScale;

            if (m_ShapedCharScales.Count == 1 || totalGlyphCount <= 1)
                return Mathf.Max(0.01f, m_ShapedCharScales[0]);

            float t = Mathf.Clamp01((float)glyphIndex / (totalGlyphCount - 1));
            int charIndex = Mathf.Clamp(Mathf.RoundToInt(t * (m_ShapedCharScales.Count - 1)), 0, m_ShapedCharScales.Count - 1);
            return Mathf.Max(0.01f, m_ShapedCharScales[charIndex]);
        }

        private float ComputeRenderedLineWidth(LineLayout line, float hbScale, int lineStartGlyphIndex, int totalGlyphCount, bool clusterMappingUsable)
        {
            if (line == null || line.glyphs.Count == 0)
                return 0f;

            float width = 0f;
            for (int i = 0; i < line.glyphs.Count; i++)
            {
                TMP_HBGlyph glyph = line.glyphs[i];
                int globalGlyphIndex = lineStartGlyphIndex + i;
                float glyphScale = ResolveScaleForGlyph(glyph.Cluster, globalGlyphIndex, totalGlyphCount, clusterMappingUsable);
                width += glyph.XAdvance * hbScale * glyphScale;
            }

            return width;
        }

        private void InitializeCharacterMetadata(string processedText)
        {
            m_CharLineNumbers.Clear();
            m_CharBaselines.Clear();
            m_CharMinX.Clear();
            m_CharMaxX.Clear();
            m_CharMinY.Clear();
            m_CharMaxY.Clear();
            m_CharFirstVertexIndex.Clear();
            m_CharHasGeometry.Clear();
            m_CharColors.Clear();

            int charCount = string.IsNullOrEmpty(processedText) ? 0 : processedText.Length;
            for (int i = 0; i < charCount; i++)
            {
                m_CharLineNumbers.Add(-1);
                m_CharBaselines.Add(0f);
                m_CharMinX.Add(float.PositiveInfinity);
                m_CharMaxX.Add(float.NegativeInfinity);
                m_CharMinY.Add(float.PositiveInfinity);
                m_CharMaxY.Add(float.NegativeInfinity);
                m_CharFirstVertexIndex.Add(-1);
                m_CharHasGeometry.Add(false);
                Color32 charColor = i < m_ShapedCharColors.Count ? m_ShapedCharColors[i] : color;
                m_CharColors.Add(charColor);
            }
        }

        private void BuildClusterCharRangeMaps(int processedCharacterCount)
        {
            m_UniqueClusters.Clear();
            m_ClusterStartCharIndices.Clear();
            m_ClusterEndCharIndices.Clear();

            if (processedCharacterCount <= 0)
                return;

            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                List<TMP_HBGlyph> glyphs = m_Lines[lineIndex].glyphs;
                for (int i = 0; i < glyphs.Count; i++)
                {
                    uint cluster = glyphs[i].Cluster;
                    if (m_ClusterStartCharIndices.ContainsKey(cluster))
                        continue;

                    int startCharIndex = Mathf.Clamp(ResolveCharIndexForCluster(cluster), 0, processedCharacterCount - 1);
                    m_ClusterStartCharIndices[cluster] = startCharIndex;
                    m_UniqueClusters.Add(cluster);
                }
            }

            if (m_UniqueClusters.Count == 0)
                return;

            m_UniqueClusters.Sort((left, right) => left.CompareTo(right));

            for (int i = 0; i < m_UniqueClusters.Count; i++)
            {
                uint cluster = m_UniqueClusters[i];
                int startCharIndex = m_ClusterStartCharIndices[cluster];
                int endCharIndex = processedCharacterCount;

                if (i + 1 < m_UniqueClusters.Count)
                {
                    int nextStartChar = m_ClusterStartCharIndices[m_UniqueClusters[i + 1]];
                    endCharIndex = Mathf.Clamp(nextStartChar, startCharIndex + 1, processedCharacterCount);
                }
                else
                {
                    endCharIndex = Mathf.Clamp(processedCharacterCount, startCharIndex + 1, processedCharacterCount);
                }

                m_ClusterEndCharIndices[cluster] = endCharIndex;
            }
        }

        private void ResolveCharacterSpanForGlyph(
            uint cluster,
            int glyphIndex,
            int totalGlyphCount,
            bool clusterMappingUsable,
            int processedCharacterCount,
            out int charStart,
            out int charEnd)
        {
            if (processedCharacterCount <= 0)
            {
                charStart = 0;
                charEnd = 0;
                return;
            }

            if (clusterMappingUsable
                && m_ClusterStartCharIndices.TryGetValue(cluster, out int clusterStart)
                && m_ClusterEndCharIndices.TryGetValue(cluster, out int clusterEnd))
            {
                charStart = Mathf.Clamp(clusterStart, 0, processedCharacterCount - 1);
                charEnd = Mathf.Clamp(clusterEnd, charStart + 1, processedCharacterCount);
                return;
            }

            charStart = ResolveCharIndexForGlyph(cluster, glyphIndex, totalGlyphCount, clusterMappingUsable, processedCharacterCount);
            charStart = Mathf.Clamp(charStart, 0, processedCharacterCount - 1);
            charEnd = Mathf.Clamp(charStart + 1, charStart + 1, processedCharacterCount);
        }

        private void ApplyGlyphToCharacterMetadata(
            int charStart,
            int charEnd,
            int lineIndex,
            float baseline,
            float minX,
            float maxX,
            float minY,
            float maxY,
            bool isVisible,
            int vertexStart,
            Color32 glyphColor)
        {
            if (charEnd <= charStart)
                return;

            int lastCharIndex = m_CharLineNumbers.Count - 1;
            if (lastCharIndex < 0)
                return;

            charStart = Mathf.Clamp(charStart, 0, lastCharIndex);
            charEnd = Mathf.Clamp(charEnd, charStart + 1, m_CharLineNumbers.Count);

            for (int charIndex = charStart; charIndex < charEnd; charIndex++)
            {
                if (m_CharLineNumbers[charIndex] < 0)
                    m_CharLineNumbers[charIndex] = lineIndex;
                m_CharBaselines[charIndex] = baseline;

                if (m_CharMinX[charIndex] > minX)
                    m_CharMinX[charIndex] = minX;
                if (m_CharMaxX[charIndex] < maxX)
                    m_CharMaxX[charIndex] = maxX;
                if (m_CharMinY[charIndex] > minY)
                    m_CharMinY[charIndex] = minY;
                if (m_CharMaxY[charIndex] < maxY)
                    m_CharMaxY[charIndex] = maxY;

                if (isVisible)
                {
                    m_CharHasGeometry[charIndex] = true;
                    if (m_CharFirstVertexIndex[charIndex] < 0)
                        m_CharFirstVertexIndex[charIndex] = vertexStart;
                }

                m_CharColors[charIndex] = glyphColor;
            }
        }

        private void BuildCharacterInfoFromMetadata(string processedText, float firstBaselineY, float lineAdvance, float ascender, float descender)
        {
            m_CharacterInfoBuffer.Clear();

            if (string.IsNullOrEmpty(processedText))
                return;

            float runningX = rectTransform.rect.xMin + margin.x;
            int runningLine = 0;

            for (int i = 0; i < processedText.Length; i++)
            {
                char characterValue = processedText[i];
                bool hasGeometry = i < m_CharHasGeometry.Count && m_CharHasGeometry[i];

                int lineIndex = i < m_CharLineNumbers.Count && m_CharLineNumbers[i] >= 0
                    ? m_CharLineNumbers[i]
                    : runningLine;

                float baseline = i < m_CharBaselines.Count && (hasGeometry || m_CharLineNumbers[i] >= 0)
                    ? m_CharBaselines[i]
                    : firstBaselineY - lineIndex * lineAdvance;

                float minX = hasGeometry ? m_CharMinX[i] : runningX;
                float maxX = hasGeometry ? m_CharMaxX[i] : runningX;
                float minY = hasGeometry ? m_CharMinY[i] : baseline + descender;
                float maxY = hasGeometry ? m_CharMaxY[i] : baseline + ascender;

                if (!hasGeometry && characterValue == '\n')
                {
                    runningLine = lineIndex + 1;
                    minX = maxX = runningX;
                }

                TMP_CharacterInfo characterInfo = default;
                characterInfo.elementType = TMP_TextElementType.Character;
                characterInfo.character = characterValue;
                characterInfo.index = i;
                characterInfo.stringLength = 1;
                characterInfo.pointSize = fontSize;
                characterInfo.lineNumber = lineIndex;
                characterInfo.pageNumber = 0;
                characterInfo.materialReferenceIndex = 0;
                characterInfo.vertexIndex = hasGeometry ? Mathf.Max(0, m_CharFirstVertexIndex[i]) : 0;
                characterInfo.origin = minX;
                characterInfo.xAdvance = maxX;
                characterInfo.ascender = maxY;
                characterInfo.baseLine = baseline;
                characterInfo.descender = minY;
                characterInfo.scale = i < m_ShapedCharScales.Count ? m_ShapedCharScales[i] : 1f;
                characterInfo.color = i < m_CharColors.Count ? m_CharColors[i] : color;
                characterInfo.isVisible = hasGeometry;
                characterInfo.style = fontStyle;
                characterInfo.bottomLeft = new Vector3(minX, minY, 0f);
                characterInfo.topLeft = new Vector3(minX, maxY, 0f);
                characterInfo.topRight = new Vector3(maxX, maxY, 0f);
                characterInfo.bottomRight = new Vector3(maxX, minY, 0f);

                m_CharacterInfoBuffer.Add(characterInfo);
                runningX = maxX;
            }
        }

        private void SyncCharacterAndLinkInfo()
        {
            if (m_textInfo == null)
                m_textInfo = new TMP_TextInfo(this);

            int characterCount = m_CharacterInfoBuffer.Count;
            if (m_textInfo.characterInfo == null || m_textInfo.characterInfo.Length < characterCount)
                TMP_TextInfo.Resize(ref m_textInfo.characterInfo, characterCount, false);

            for (int i = 0; i < characterCount; i++)
                m_textInfo.characterInfo[i] = m_CharacterInfoBuffer[i];

            m_textInfo.characterCount = characterCount;
            m_textInfo.spriteCount = 0;
            m_textInfo.spaceCount = 0;
            m_textInfo.wordCount = 0;
            m_textInfo.lineCount = m_Lines.Count;
            m_textInfo.pageCount = 1;

            BuildLinkInfoFromCharacterData();
        }

        private void BuildLinkInfoFromCharacterData()
        {
            m_textInfo.linkCount = 0;

            int characterCount = m_textInfo.characterCount;
            if (m_ShapedLinks.Count == 0 || characterCount <= 0)
                return;

            if (m_textInfo.linkInfo == null || m_textInfo.linkInfo.Length < m_ShapedLinks.Count)
                TMP_TextInfo.Resize(ref m_textInfo.linkInfo, m_ShapedLinks.Count);

            int linkCount = 0;
            for (int i = 0; i < m_ShapedLinks.Count; i++)
            {
                ParsedLinkRange link = m_ShapedLinks[i];
                if (string.IsNullOrEmpty(link.Id) || link.Length <= 0)
                    continue;

                int linkStart = Mathf.Clamp(link.StartIndex, 0, characterCount);
                int linkEnd = Mathf.Clamp(link.StartIndex + link.Length, linkStart, characterCount);
                int linkLength = linkEnd - linkStart;
                if (linkLength <= 0)
                    continue;

                TMP_LinkInfo linkInfo = m_textInfo.linkInfo[linkCount];
                linkInfo.textComponent = this;
                linkInfo.hashCode = TMP_TextUtilities.GetSimpleHashCode(link.Id);
                linkInfo.linkTextfirstCharacterIndex = linkStart;
                linkInfo.linkTextLength = linkLength;
                linkInfo.linkIdFirstCharacterIndex = 0;

                char[] idChars = link.Id.ToCharArray();
                linkInfo.SetLinkID(idChars, 0, idChars.Length);
                linkInfo.linkIdLength = idChars.Length;

                m_textInfo.linkInfo[linkCount] = linkInfo;
                linkCount++;
            }

            m_textInfo.linkCount = linkCount;
        }

        private int CountTotalGlyphs()
        {
            int count = 0;
            for (int i = 0; i < m_Lines.Count; i++)
                count += m_Lines[i].glyphs.Count;

            return count;
        }

        private bool IsClusterColorMappingUsable(int totalGlyphCount)
        {
            if (totalGlyphCount <= 1)
                return true;

            uint firstCluster = 0;
            bool firstSet = false;
            bool hasDifferentCluster = false;

            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                List<TMP_HBGlyph> glyphs = m_Lines[lineIndex].glyphs;
                for (int i = 0; i < glyphs.Count; i++)
                {
                    uint cluster = glyphs[i].Cluster;
                    if (!firstSet)
                    {
                        firstCluster = cluster;
                        firstSet = true;
                    }
                    else if (cluster != firstCluster)
                    {
                        hasDifferentCluster = true;
                        break;
                    }
                }

                if (hasDifferentCluster)
                    break;
            }

            return hasDifferentCluster;
        }

        private string ApplyNoJoinWordConfiguration(string sourceText)
        {
            if (!m_DisableConjunctJoiningForConfiguredWords || string.IsNullOrEmpty(sourceText))
                return sourceText;

            BuildEffectiveWordReplacements();
            if (m_EffectiveReplacementKeys.Count == 0)
                return sourceText;

            string processed = sourceText;
            for (int i = 0; i < m_EffectiveReplacementKeys.Count; i++)
            {
                string sourceWord = m_EffectiveReplacementKeys[i];
                if (m_EffectiveWordReplacementMap.TryGetValue(sourceWord, out string replacementWord))
                    processed = ReplaceWholeWordOccurrences(processed, sourceWord, replacementWord);
            }

            return processed;
        }

        private void BuildEffectiveWordReplacements()
        {
            m_EffectiveWordReplacementMap.Clear();
            m_EffectiveReplacementKeys.Clear();

            if (m_UseGlobalNoJoinWordSettings)
            {
                HarfBuzzHindiNoJoinSettings settings = HarfBuzzHindiNoJoinSettings.Load();
                if (settings != null)
                {
                    if (settings.NoJoinWords != null)
                    {
                        for (int i = 0; i < settings.NoJoinWords.Count; i++)
                        {
                            string word = HarfBuzzHindiNoJoinSettings.SanitizeWord(settings.NoJoinWords[i]);
                            AddWordReplacementIfValid(word, BuildNoJoinVariant(word), false);
                        }
                    }

                    if (settings.SelectiveNoJoinRules != null)
                    {
                        for (int i = 0; i < settings.SelectiveNoJoinRules.Count; i++)
                        {
                            HarfBuzzHindiSelectiveNoJoinRule rule = settings.SelectiveNoJoinRules[i];
                            if (rule == null)
                                continue;

                            string word = HarfBuzzHindiNoJoinSettings.SanitizeWord(rule.Word);
                            string replacement = BuildSelectiveNoJoinVariant(word, rule.DisjoinPatterns);
                            AddWordReplacementIfValid(word, replacement, true);
                        }
                    }
                }
            }

            if (m_NoJoinWords != null)
            {
                for (int i = 0; i < m_NoJoinWords.Count; i++)
                {
                    string word = HarfBuzzHindiNoJoinSettings.SanitizeWord(m_NoJoinWords[i]);
                    AddWordReplacementIfValid(word, BuildNoJoinVariant(word), true);
                }
            }

            m_EffectiveReplacementKeys.Sort((left, right) => right.Length.CompareTo(left.Length));
        }

        private void AddWordReplacementIfValid(string sourceWord, string replacementWord, bool overwriteExisting)
        {
            if (string.IsNullOrEmpty(sourceWord) || string.IsNullOrEmpty(replacementWord))
                return;

            if (string.Equals(sourceWord, replacementWord, StringComparison.Ordinal))
                return;

            if (m_EffectiveWordReplacementMap.ContainsKey(sourceWord))
            {
                if (overwriteExisting)
                    m_EffectiveWordReplacementMap[sourceWord] = replacementWord;

                return;
            }

            m_EffectiveWordReplacementMap[sourceWord] = replacementWord;
            m_EffectiveReplacementKeys.Add(sourceWord);
        }

        private static string ReplaceWholeWordOccurrences(string source, string word, string replacement)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(word))
                return source;

            if (string.IsNullOrEmpty(replacement) || string.Equals(word, replacement, StringComparison.Ordinal))
                return source;

            StringBuilder builder = null;
            int searchIndex = 0;
            int appendIndex = 0;

            while (searchIndex < source.Length)
            {
                int matchIndex = source.IndexOf(word, searchIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                    break;

                searchIndex = matchIndex + word.Length;

                if (!IsWordBoundary(source, matchIndex - 1) || !IsWordBoundary(source, matchIndex + word.Length))
                    continue;

                if (builder == null)
                    builder = new StringBuilder(source.Length + 16);

                builder.Append(source, appendIndex, matchIndex - appendIndex);
                builder.Append(replacement);
                appendIndex = matchIndex + word.Length;
            }

            if (builder == null)
                return source;

            if (appendIndex < source.Length)
                builder.Append(source, appendIndex, source.Length - appendIndex);

            return builder.ToString();
        }

        private static string BuildNoJoinVariant(string word)
        {
            if (string.IsNullOrEmpty(word) || word.IndexOf(k_DevanagariVirama) < 0)
                return word;

            StringBuilder builder = null;

            for (int i = 0; i < word.Length; i++)
            {
                char current = word[i];

                if (builder != null)
                    builder.Append(current);

                if (current != k_DevanagariVirama)
                    continue;

                bool alreadyControlled = i + 1 < word.Length &&
                                         (word[i + 1] == k_ZeroWidthNonJoiner || word[i + 1] == k_ZeroWidthJoiner);
                if (alreadyControlled)
                    continue;

                if (builder == null)
                {
                    builder = new StringBuilder(word.Length + 4);
                    builder.Append(word, 0, i + 1);
                }

                builder.Append(k_ZeroWidthNonJoiner);
            }

            return builder == null ? word : builder.ToString();
        }

        private static string BuildSelectiveNoJoinVariant(string word, string disjoinPatterns)
        {
            if (string.IsNullOrEmpty(word) || string.IsNullOrWhiteSpace(disjoinPatterns))
                return word;

            List<string> tokens = HarfBuzzHindiNoJoinSettings.ParseSeparatedTokens(disjoinPatterns);
            if (tokens.Count == 0)
                return word;

            tokens.Sort((left, right) => right.Length.CompareTo(left.Length));

            string result = word;
            for (int i = 0; i < tokens.Count; i++)
            {
                string pattern = tokens[i];
                if (pattern.IndexOf(k_DevanagariVirama) < 0)
                    continue;

                string replacement = BuildNoJoinVariant(pattern);
                if (string.Equals(pattern, replacement, StringComparison.Ordinal))
                    continue;

                result = ReplaceAllOrdinal(result, pattern, replacement);
            }

            return result;
        }

        private static string ReplaceAllOrdinal(string source, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue) || string.Equals(oldValue, newValue, StringComparison.Ordinal))
                return source;

            StringBuilder builder = null;
            int searchIndex = 0;
            int appendIndex = 0;

            while (searchIndex < source.Length)
            {
                int matchIndex = source.IndexOf(oldValue, searchIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                    break;

                searchIndex = matchIndex + oldValue.Length;

                if (builder == null)
                    builder = new StringBuilder(source.Length + 16);

                builder.Append(source, appendIndex, matchIndex - appendIndex);
                builder.Append(newValue);
                appendIndex = matchIndex + oldValue.Length;
            }

            if (builder == null)
                return source;

            if (appendIndex < source.Length)
                builder.Append(source, appendIndex, source.Length - appendIndex);

            return builder.ToString();
        }

        private static bool IsWordBoundary(string text, int index)
        {
            if (index < 0 || index >= text.Length)
                return true;

            return !IsWordCharacter(text[index]);
        }

        private static bool IsWordCharacter(char value)
        {
            if (value == k_ZeroWidthNonJoiner || value == k_ZeroWidthJoiner)
                return true;

            if (char.IsLetterOrDigit(value))
                return true;

            UnicodeCategory category = char.GetUnicodeCategory(value);
            return category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark;
        }

        private LineLayout ShapeToken(string token)
        {
            if (m_TokenShapeCache.TryGetValue(token, out LineLayout cached))
                return cached;

            LineLayout line = new LineLayout();
            if (string.IsNullOrEmpty(token))
            {
                m_TokenShapeCache[token] = line;
                return line;
            }

            EnsureShapeBufferCapacity(Mathf.Max(32, token.Length * 4 + 8));

            uint scriptTag = m_ForceDevanagariScript ? k_DevanagariScriptTag : 0;
            int glyphCount = m_FontHandle.Shape(token, m_HarfBuzzLanguage, scriptTag, TMP_HBDirection.LeftToRight, m_ShapeBuffer);
            if (glyphCount == m_ShapeBuffer.Length)
            {
                EnsureShapeBufferCapacity(m_ShapeBuffer.Length * 2);
                glyphCount = m_FontHandle.Shape(token, m_HarfBuzzLanguage, scriptTag, TMP_HBDirection.LeftToRight, m_ShapeBuffer);
            }

            for (int i = 0; i < glyphCount; i++)
            {
                line.glyphs.Add(m_ShapeBuffer[i]);
                line.width += m_ShapeBuffer[i].XAdvance;
            }

            m_TokenShapeCache[token] = line;
            return line;
        }

        private LineLayout ShapeToken(string token, int clusterByteOffset)
        {
            LineLayout line = ShapeToken(token);
            if (clusterByteOffset <= 0 || line.glyphs.Count == 0)
                return line;

            LineLayout offsetLine = new LineLayout();
            offsetLine.width = line.width;

            uint offset = (uint)clusterByteOffset;
            for (int i = 0; i < line.glyphs.Count; i++)
            {
                TMP_HBGlyph glyph = line.glyphs[i];
                glyph.Cluster += offset;
                offsetLine.glyphs.Add(glyph);
            }

            return offsetLine;
        }

        private void EnsureShapeBufferCapacity(int required)
        {
            if (m_ShapeBuffer.Length >= required)
                return;

            m_ShapeBuffer = new TMP_HBGlyph[required];
        }

        private bool TryResolveGlyph(TMP_FontAsset fontAsset, uint glyphId, out Glyph glyph)
        {
            glyph = null;

            if (fontAsset.glyphLookupTable != null && fontAsset.glyphLookupTable.TryGetValue(glyphId, out glyph))
                return true;

            if (fontAsset.TryAddGlyphInternal(glyphId, out glyph))
                return glyph != null;

            return false;
        }

        private bool AddGlyphQuad(
            TMP_FontAsset fontAsset,
            Glyph glyph,
            TMP_HBGlyph shapedGlyph,
            float penX,
            float penY,
            float hbScale,
            float fontScale,
            float xScale,
            Color32 vertexColor,
            out int vertexStart,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY)
        {
            vertexStart = m_Vertices.Count;
            minX = penX;
            maxX = penX;
            minY = penY;
            maxY = penY;

            if (glyph == null || glyph.glyphRect.width == 0 || glyph.glyphRect.height == 0)
                return false;

            GlyphMetrics metrics = glyph.metrics;
            float sdfPadding = fontAsset.atlasPadding;

            float glyphX = penX + shapedGlyph.XOffset * hbScale + (metrics.horizontalBearingX - sdfPadding) * fontScale;
            float glyphTop = penY + shapedGlyph.YOffset * hbScale + (metrics.horizontalBearingY + sdfPadding) * fontScale;
            float glyphWidth = (metrics.width + sdfPadding * 2f) * fontScale;
            float glyphHeight = (metrics.height + sdfPadding * 2f) * fontScale;

            float x0 = glyphX;
            float y0 = glyphTop - glyphHeight;
            float x1 = glyphX + glyphWidth;
            float y1 = glyphTop;

            GlyphRect glyphRect = glyph.glyphRect;
            float atlasWidth = fontAsset.atlasWidth;
            float atlasHeight = fontAsset.atlasHeight;

            float u0 = Mathf.Clamp01((glyphRect.x - sdfPadding) / atlasWidth);
            float v0 = Mathf.Clamp01((glyphRect.y - sdfPadding) / atlasHeight);
            float u1 = Mathf.Clamp01((glyphRect.x + glyphRect.width + sdfPadding) / atlasWidth);
            float v1 = Mathf.Clamp01((glyphRect.y + glyphRect.height + sdfPadding) / atlasHeight);

            vertexStart = m_Vertices.Count;

            m_Vertices.Add(new Vector3(x0, y0, 0));
            m_Vertices.Add(new Vector3(x0, y1, 0));
            m_Vertices.Add(new Vector3(x1, y1, 0));
            m_Vertices.Add(new Vector3(x1, y0, 0));

            m_Uvs0.Add(new Vector4(u0, v0, 0f, xScale));
            m_Uvs0.Add(new Vector4(u0, v1, 0f, xScale));
            m_Uvs0.Add(new Vector4(u1, v1, 0f, xScale));
            m_Uvs0.Add(new Vector4(u1, v0, 0f, xScale));

            m_Uvs2.Add(new Vector2(0, 0));
            m_Uvs2.Add(new Vector2(0, 1));
            m_Uvs2.Add(new Vector2(1, 1));
            m_Uvs2.Add(new Vector2(1, 0));

            Color32 finalVertexColor = m_ConvertToLinearSpace ? vertexColor.GammaToLinear() : vertexColor;
            m_Colors32.Add(finalVertexColor);
            m_Colors32.Add(finalVertexColor);
            m_Colors32.Add(finalVertexColor);
            m_Colors32.Add(finalVertexColor);

            m_Triangles.Add(vertexStart + 0);
            m_Triangles.Add(vertexStart + 1);
            m_Triangles.Add(vertexStart + 2);
            m_Triangles.Add(vertexStart + 0);
            m_Triangles.Add(vertexStart + 2);
            m_Triangles.Add(vertexStart + 3);

            minX = x0;
            maxX = x1;
            minY = y0;
            maxY = y1;
            return true;
        }

        private void SyncTextInfoMeshData()
        {
            if (m_textInfo == null)
            {
                m_textInfo = new TMP_TextInfo(this);
            }

            if (m_textInfo.meshInfo == null || m_textInfo.meshInfo.Length == 0)
            {
                m_textInfo.meshInfo = new TMP_MeshInfo[1];
            }

            m_textInfo.materialCount = 1;

            TMP_MeshInfo meshInfo = m_textInfo.meshInfo[0];
            meshInfo.mesh = m_mesh;
            meshInfo.vertexCount = m_Vertices.Count;
            meshInfo.vertices = m_Vertices.ToArray();
            meshInfo.uvs0 = m_Uvs0.ToArray();
            meshInfo.uvs2 = m_Uvs2.ToArray();
            meshInfo.colors32 = m_Colors32.ToArray();
            meshInfo.triangles = m_Triangles.ToArray();
            m_textInfo.meshInfo[0] = meshInfo;
        }

        private void SyncClearedTextInfoMeshData()
        {
            if (m_textInfo == null)
            {
                m_textInfo = new TMP_TextInfo(this);
            }

            if (m_textInfo.meshInfo == null || m_textInfo.meshInfo.Length == 0)
            {
                m_textInfo.meshInfo = new TMP_MeshInfo[1];
            }

            m_textInfo.materialCount = 1;
            m_textInfo.characterCount = 0;
            m_textInfo.linkCount = 0;
            m_textInfo.lineCount = 0;
            m_textInfo.pageCount = 0;

            TMP_MeshInfo meshInfo = m_textInfo.meshInfo[0];
            meshInfo.mesh = m_mesh;
            meshInfo.vertexCount = 0;
            meshInfo.vertices = Array.Empty<Vector3>();
            meshInfo.uvs0 = Array.Empty<Vector4>();
            meshInfo.uvs2 = Array.Empty<Vector2>();
            meshInfo.colors32 = Array.Empty<Color32>();
            meshInfo.triangles = Array.Empty<int>();
            m_textInfo.meshInfo[0] = meshInfo;
        }

        private float ComputeSdfXScale(bool bold)
        {
            float xScale = (1f - m_charWidthAdjDelta) * m_characterHorizontalScale;
            if (bold)
                xScale *= -1f;

            Canvas parentCanvas = canvas;
            float lossy = Mathf.Abs(transform.lossyScale.y);
            if (lossy < 0.0001f)
                lossy = 1f;

            if (parentCanvas == null)
                return xScale * lossy;

            switch (parentCanvas.renderMode)
            {
                case RenderMode.ScreenSpaceOverlay:
                    xScale *= lossy / Mathf.Max(0.0001f, parentCanvas.scaleFactor);
                    break;

                case RenderMode.ScreenSpaceCamera:
                    xScale *= parentCanvas.worldCamera == null ? 1f : lossy;
                    break;

                case RenderMode.WorldSpace:
                    xScale *= lossy;
                    break;

                default:
                    xScale *= lossy;
                    break;
            }

            return xScale;
        }

        private bool EnsureFontHandle()
        {
            string fontPath = ResolveFontPath();
            if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
            {
                if (!m_LoggedMissingFontWarning)
                {
                    string reason = string.IsNullOrWhiteSpace(fontPath)
                        ? "no font path could be resolved"
                        : $"font file not found at '{fontPath}'";
                    string hint = m_UseBundledIndicFlowDefaults
                        ? "Bundled IndicFlow font bytes could not be resolved from Runtime/Resources/IndicFlow."
                        : "Assign 'Harf Buzz Font Bytes' or verify bundled fallback bytes exist in Runtime/Resources/IndicFlow.";
                    Debug.LogWarning(
                        $"HarfBuzz font unavailable ({reason}). {hint}",
                        this);
                    m_LoggedMissingFontWarning = true;
                }
                return false;
            }

            m_LoggedMissingFontWarning = false;

            if (m_FontHandle != null && string.Equals(m_ResolvedFontPath, fontPath, StringComparison.Ordinal))
                return m_FontHandle.IsValid;

            ReleaseFontHandle();

            try
            {
                m_FontHandle = new TMP_HBFontHandle(fontPath);
                m_ResolvedFontPath = fontPath;
                m_LoggedInitFailure = false;
                if (!m_LoggedInitSuccess)
                {
                    Debug.Log($"HarfBuzz initialized for '{name}' using '{fontPath}'.", this);
                    m_LoggedInitSuccess = true;
                }
                return true;
            }
            catch (DllNotFoundException ex)
            {
                if (!m_LoggedInitFailure)
                {
                    Debug.LogError(
                        $"HarfBuzz native plugin missing ({ex.Message}). Ensure 'libHindiHarfBuzz' is present in package Runtime/Plugins for this platform.",
                        this);
                    m_LoggedInitFailure = true;
                }
                ReleaseFontHandle();
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                if (!m_LoggedInitFailure)
                {
                    Debug.LogError(
                        $"HarfBuzz native entry point missing ({ex.Message}). Verify plugin binary matches the managed API.",
                        this);
                    m_LoggedInitFailure = true;
                }
                ReleaseFontHandle();
                return false;
            }
            catch (Exception ex)
            {
                if (!m_LoggedInitFailure)
                {
                    Debug.LogError(
                        $"Failed to initialize HarfBuzz with '{fontPath}': {ex.Message}",
                        this);
                    m_LoggedInitFailure = true;
                }
                ReleaseFontHandle();
                return false;
            }
        }

        private string ResolveFontPath()
        {
            if (m_UseBundledIndicFlowDefaults)
            {
                TextAsset bundledDefaultFont = GetBundledFallbackFontBytes();
                if (TryCacheFontBytesToTemp(bundledDefaultFont, "tmp_hb_bundled_font", out string forcedBundledFontPath))
                {
                    if (!m_LoggedBundledFontFallback)
                    {
                        Debug.Log(
                            "Using bundled IndicFlow HarfBuzz font bytes from package Runtime/Resources.",
                            this);
                        m_LoggedBundledFontFallback = true;
                    }

                    m_CachedEmbeddedFontPath = forcedBundledFontPath;
                    return forcedBundledFontPath;
                }
            }

            if (TryCacheFontBytesToTemp(m_HarfBuzzFontBytes, "tmp_hb_font", out string configuredFontPath))
            {
                m_CachedEmbeddedFontPath = configuredFontPath;
                return configuredFontPath;
            }

            TextAsset bundledFallbackFont = GetBundledFallbackFontBytes();
            if (TryCacheFontBytesToTemp(bundledFallbackFont, "tmp_hb_bundled_font", out string bundledFontPath))
            {
                if (!m_LoggedBundledFontFallback && m_HarfBuzzFontBytes == null && string.IsNullOrWhiteSpace(m_HarfBuzzFontPath))
                {
                    Debug.Log(
                        "HarfBuzz font bytes were not assigned. Using bundled fallback font bytes from package Runtime/Resources.",
                        this);
                    m_LoggedBundledFontFallback = true;
                }

                m_CachedEmbeddedFontPath = bundledFontPath;
                return bundledFontPath;
            }

            if (!string.IsNullOrWhiteSpace(m_HarfBuzzFontPath))
            {
                if (Path.IsPathRooted(m_HarfBuzzFontPath))
                    return m_HarfBuzzFontPath;

                if (m_HarfBuzzFontPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                    if (!string.IsNullOrEmpty(projectRoot))
                        return Path.GetFullPath(Path.Combine(projectRoot, m_HarfBuzzFontPath));
                }

                return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), m_HarfBuzzFontPath));
            }

#if UNITY_EDITOR
            if (font != null && font.sourceFontFile != null)
            {
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(font.sourceFontFile);
                if (!string.IsNullOrEmpty(assetPath))
                    return Path.GetFullPath(assetPath);
            }
#endif

            return m_CachedEmbeddedFontPath;
        }

        private static TextAsset GetBundledFallbackFontBytes()
        {
            if (s_BundledFallbackFontLoaded)
                return s_BundledFallbackFontBytes;

            for (int i = 0; i < k_DefaultBundledFontResourcePaths.Length; i++)
            {
                s_BundledFallbackFontBytes = Resources.Load<TextAsset>(k_DefaultBundledFontResourcePaths[i]);
                if (s_BundledFallbackFontBytes != null)
                    break;
            }

            s_BundledFallbackFontLoaded = true;
            return s_BundledFallbackFontBytes;
        }

        private TMP_FontAsset GetBundledFallbackFontAsset()
        {
            if (s_BundledFallbackFontAssetLoaded)
                return s_BundledFallbackFontAsset;

            for (int i = 0; i < k_DefaultBundledFontAssetResourcePaths.Length; i++)
            {
                s_BundledFallbackFontAsset = Resources.Load<TMP_FontAsset>(k_DefaultBundledFontAssetResourcePaths[i]);
                if (s_BundledFallbackFontAsset != null)
                    break;
            }

            s_BundledFallbackFontAssetLoaded = true;
            return s_BundledFallbackFontAsset;
        }

        private void EnsureBundledFallbackFontAssetAssigned(bool forceAssign)
        {
            TMP_FontAsset bundledFontAsset = GetBundledFallbackFontAsset();
            if (bundledFontAsset == null)
                return;

            if (!forceAssign && font != null)
                return;

            bool replacedFontAsset = font != bundledFontAsset;
            font = bundledFontAsset;
            if (bundledFontAsset.material != null && (replacedFontAsset || fontSharedMaterial == null))
                fontSharedMaterial = bundledFontAsset.material;

            if (!m_LoggedBundledFontAssetFallback && replacedFontAsset)
            {
                string message = forceAssign
                    ? "Using bundled IndicFlow TMP font asset from package Runtime/Resources."
                    : "TMP font asset was not assigned. Using bundled fallback TMP font asset from package Runtime/Resources.";
                Debug.Log(message, this);
                m_LoggedBundledFontAssetFallback = true;
            }
        }

        private static bool TryCacheFontBytesToTemp(TextAsset fontBytes, string cachePrefix, out string cachedPath)
        {
            cachedPath = null;

            if (fontBytes == null || fontBytes.bytes == null || fontBytes.bytes.Length == 0)
                return false;

            string fileName = $"{cachePrefix}_{fontBytes.GetInstanceID()}_{fontBytes.bytes.Length}.ttf";
            string targetPath = Path.Combine(Application.temporaryCachePath, fileName);

            if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != fontBytes.bytes.Length)
                File.WriteAllBytes(targetPath, fontBytes.bytes);

            cachedPath = targetPath;
            return true;
        }

        private void ReleaseFontHandle()
        {
            if (m_FontHandle != null)
            {
                m_FontHandle.Dispose();
                m_FontHandle = null;
            }

            m_ResolvedFontPath = null;
        }

        private enum HorizontalAlign
        {
            Left,
            Center,
            Right,
        }

        private enum VerticalAlign
        {
            Top,
            Middle,
            Bottom,
        }

        private HorizontalAlign ResolveHorizontalAlign()
        {
            HorizontalAlignmentOptions horizontal = (HorizontalAlignmentOptions)((int)alignment & 0xFF);

            switch (horizontal)
            {
                case HorizontalAlignmentOptions.Center:
                    return HorizontalAlign.Center;

                case HorizontalAlignmentOptions.Right:
                    return HorizontalAlign.Right;

                default:
                    return HorizontalAlign.Left;
            }
        }

        private VerticalAlign ResolveVerticalAlign()
        {
            VerticalAlignmentOptions vertical = (VerticalAlignmentOptions)((int)alignment & 0xFF00);

            switch (vertical)
            {
                case VerticalAlignmentOptions.Middle:
                    return VerticalAlign.Middle;

                case VerticalAlignmentOptions.Bottom:
                case VerticalAlignmentOptions.Baseline:
                case VerticalAlignmentOptions.Geometry:
                case VerticalAlignmentOptions.Capline:
                    return VerticalAlign.Bottom;

                default:
                    return VerticalAlign.Top;
            }
        }
    }
}
