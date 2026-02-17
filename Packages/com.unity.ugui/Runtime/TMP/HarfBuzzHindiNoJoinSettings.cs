using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TMPro
{
    [Serializable]
    public sealed class HarfBuzzHindiSelectiveNoJoinRule
    {
        [Tooltip("Exact word to match in text.")]
        public string Word = string.Empty;

        [Tooltip("Conjunct patterns to disjoin in this word. Use newline/comma/semicolon/tab separators.")]
        [TextArea(1, 4)]
        public string DisjoinPatterns = string.Empty;
    }

    [CreateAssetMenu(
        fileName = "HarfBuzzHindiNoJoinSettings",
        menuName = "TextMeshPro/HarfBuzz Hindi/No-Join Settings",
        order = 2500)]
    public sealed class HarfBuzzHindiNoJoinSettings : ScriptableObject
    {
        public const string ResourceName = "HarfBuzzHindiNoJoinSettings";
        public const string DefaultAssetPath = "Assets/Resources/HarfBuzzHindiNoJoinSettings.asset";

        [SerializeField]
        private List<string> m_NoJoinWords = new List<string>();
        [SerializeField]
        private List<HarfBuzzHindiSelectiveNoJoinRule> m_SelectiveNoJoinRules = new List<HarfBuzzHindiSelectiveNoJoinRule>();

        public List<string> NoJoinWords
        {
            get { return m_NoJoinWords; }
        }

        public List<HarfBuzzHindiSelectiveNoJoinRule> SelectiveNoJoinRules
        {
            get { return m_SelectiveNoJoinRules; }
        }

        public static HarfBuzzHindiNoJoinSettings Load()
        {
            return Resources.Load<HarfBuzzHindiNoJoinSettings>(ResourceName);
        }

        public static string SanitizeWord(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static List<string> ParseSeparatedTokens(string value)
        {
            List<string> output = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
                return output;

            string[] tokens = value.Split(new[] { '\r', '\n', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = SanitizeWord(tokens[i]);
                if (!string.IsNullOrEmpty(token))
                    output.Add(token);
            }

            return output;
        }

#if UNITY_EDITOR
        public void Normalize()
        {
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);

            for (int i = m_NoJoinWords.Count - 1; i >= 0; i--)
            {
                string sanitized = SanitizeWord(m_NoJoinWords[i]);
                if (string.IsNullOrEmpty(sanitized) || !unique.Add(sanitized))
                {
                    m_NoJoinWords.RemoveAt(i);
                    continue;
                }

                m_NoJoinWords[i] = sanitized;
            }

            m_NoJoinWords.Sort((left, right) => string.CompareOrdinal(left, right));

            Dictionary<string, HashSet<string>> mergedRules = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < m_SelectiveNoJoinRules.Count; i++)
            {
                HarfBuzzHindiSelectiveNoJoinRule rule = m_SelectiveNoJoinRules[i];
                if (rule == null)
                    continue;

                string word = SanitizeWord(rule.Word);
                if (string.IsNullOrEmpty(word))
                    continue;

                if (!mergedRules.TryGetValue(word, out HashSet<string> patterns))
                {
                    patterns = new HashSet<string>(StringComparer.Ordinal);
                    mergedRules[word] = patterns;
                }

                List<string> parsedPatterns = ParseSeparatedTokens(rule.DisjoinPatterns);
                for (int p = 0; p < parsedPatterns.Count; p++)
                    patterns.Add(parsedPatterns[p]);
            }

            m_SelectiveNoJoinRules = mergedRules
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => new HarfBuzzHindiSelectiveNoJoinRule
                {
                    Word = kvp.Key,
                    DisjoinPatterns = string.Join(", ", kvp.Value.OrderBy(value => value, StringComparer.Ordinal))
                })
                .ToList();
        }
#endif
    }
}
