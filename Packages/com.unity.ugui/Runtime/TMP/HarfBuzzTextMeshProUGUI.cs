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
        private sealed class LineLayout
        {
            public readonly List<TMP_HBGlyph> glyphs = new List<TMP_HBGlyph>();
            public float width;
        }

        private static readonly uint k_DevanagariScriptTag = TMP_HarfBuzzNative.MakeTag("deva");
        private const char k_DevanagariVirama = '\u094D';
        private const char k_ZeroWidthNonJoiner = '\u200C';
        private const char k_ZeroWidthJoiner = '\u200D';

        [SerializeField] private bool m_EnableHarfBuzz = true;
        [SerializeField] private TextAsset m_HarfBuzzFontBytes;
        [SerializeField] private string m_HarfBuzzFontPath;
        [SerializeField] private string m_HarfBuzzLanguage = "hi";
        [SerializeField] private bool m_ForceDevanagariScript = true;
        [SerializeField] private bool m_FallbackToDefaultTMP = true;
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

        private readonly List<LineLayout> m_Lines = new List<LineLayout>();
        private readonly Dictionary<string, LineLayout> m_TokenShapeCache = new Dictionary<string, LineLayout>();
        private readonly Dictionary<string, string> m_EffectiveWordReplacementMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> m_EffectiveReplacementKeys = new List<string>();

        private readonly List<Vector3> m_Vertices = new List<Vector3>();
        private readonly List<Vector4> m_Uvs0 = new List<Vector4>();
        private readonly List<Vector2> m_Uvs2 = new List<Vector2>();
        private readonly List<Color32> m_Colors32 = new List<Color32>();
        private readonly List<int> m_Triangles = new List<int>();

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

            HorizontalAlign horizontalAlign = ResolveHorizontalAlign();
            Color32 vertexColor = color;
            int resolvedGlyphCount = 0;
            int zeroGlyphCount = 0;

            for (int lineIndex = 0; lineIndex < m_Lines.Count; lineIndex++)
            {
                LineLayout line = m_Lines[lineIndex];
                float lineWidth = line.width * hbScale;

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

                    if (TryResolveGlyph(fontAsset, shapedGlyph.GlyphId, out Glyph glyph))
                    {
                        AddGlyphQuad(fontAsset, glyph, shapedGlyph, penX, penY, hbScale, fontScale, xScale, vertexColor);
                        resolvedGlyphCount++;
                    }

                    penX += shapedGlyph.XAdvance * hbScale;
                    penY += shapedGlyph.YAdvance * hbScale;
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

            for (int i = 0; i < paragraphs.Length; i++)
            {
                string paragraph = paragraphs[i];
                if (string.IsNullOrEmpty(paragraph))
                {
                    m_Lines.Add(new LineLayout());
                    continue;
                }

                if (!m_EnableSimpleWordWrap || float.IsInfinity(maxLineWidthUnits) || maxLineWidthUnits <= 0)
                {
                    m_Lines.Add(ShapeToken(paragraph));
                    continue;
                }

                string[] words = paragraph.Split(' ');
                LineLayout currentLine = new LineLayout();

                for (int w = 0; w < words.Length; w++)
                {
                    bool hasLeadingSpace = w > 0;
                    string tokenText = hasLeadingSpace ? " " + words[w] : words[w];
                    if (tokenText.Length == 0)
                        continue;

                    LineLayout token = ShapeToken(tokenText);

                    if (currentLine.glyphs.Count > 0 && currentLine.width + token.width > maxLineWidthUnits)
                    {
                        m_Lines.Add(currentLine);
                        currentLine = new LineLayout();

                        string trimmedToken = tokenText.TrimStart(' ');
                        if (trimmedToken.Length > 0)
                            token = ShapeToken(trimmedToken);
                    }

                    currentLine.glyphs.AddRange(token.glyphs);
                    currentLine.width += token.width;
                }

                m_Lines.Add(currentLine);
            }

            if (m_Lines.Count == 0)
                m_Lines.Add(new LineLayout());
        }

        private string GetProcessedTextForShaping()
        {
            if (m_TextProcessingArray == null || m_TextProcessingArray.Length == 0)
                return ApplyNoJoinWordConfiguration(text ?? string.Empty);

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

            return ApplyNoJoinWordConfiguration(builder.ToString());
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

        private void AddGlyphQuad(
            TMP_FontAsset fontAsset,
            Glyph glyph,
            TMP_HBGlyph shapedGlyph,
            float penX,
            float penY,
            float hbScale,
            float fontScale,
            float xScale,
            Color32 vertexColor)
        {
            if (glyph == null || glyph.glyphRect.width == 0 || glyph.glyphRect.height == 0)
                return;

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

            int vertexStart = m_Vertices.Count;

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
                    Debug.LogWarning(
                        $"HarfBuzz font unavailable ({reason}). Assign 'Harf Buzz Font Bytes' for player builds (Android/iOS).",
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
            if (m_HarfBuzzFontBytes != null && m_HarfBuzzFontBytes.bytes != null && m_HarfBuzzFontBytes.bytes.Length > 0)
            {
                string fileName = $"tmp_hb_font_{m_HarfBuzzFontBytes.GetInstanceID()}_{m_HarfBuzzFontBytes.bytes.Length}.ttf";
                string targetPath = Path.Combine(Application.temporaryCachePath, fileName);

                if (!File.Exists(targetPath) || new FileInfo(targetPath).Length != m_HarfBuzzFontBytes.bytes.Length)
                    File.WriteAllBytes(targetPath, m_HarfBuzzFontBytes.bytes);

                m_CachedEmbeddedFontPath = targetPath;
                return targetPath;
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
