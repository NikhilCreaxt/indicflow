using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TMPro.EditorUtilities
{
    public sealed class HarfBuzzHindiNoJoinSettingsWindow : EditorWindow
    {
        private HarfBuzzHindiNoJoinSettings m_Settings;
        private SerializedObject m_SerializedSettings;
        private SerializedProperty m_NoJoinWordsProperty;
        private SerializedProperty m_SelectiveRulesProperty;
        private string m_BulkInput = string.Empty;
        private string m_BulkStatus = string.Empty;
        private MessageType m_BulkStatusType = MessageType.None;

        [MenuItem("Tools/TextMeshPro/HarfBuzz Hindi/No-Join Word Settings", false, 2051)]
        private static void OpenWindow()
        {
            GetWindow<HarfBuzzHindiNoJoinSettingsWindow>("HB Hindi No-Join");
        }

        private void OnEnable()
        {
            RefreshSettingsReference();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("HarfBuzz Hindi No-Join Words", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Add exact Hindi words that should render without conjunct joining. The renderer inserts ZWNJ after halant (\u094D) only for listed words.",
                MessageType.Info);

            if (m_Settings == null)
            {
                EditorGUILayout.HelpBox(
                    "No global settings asset found. Create one at Assets/Resources/HarfBuzzHindiNoJoinSettings.asset.",
                    MessageType.Warning);

                if (GUILayout.Button("Create Settings Asset"))
                    CreateSettingsAsset();

                if (GUILayout.Button("Refresh"))
                    RefreshSettingsReference();

                return;
            }

            if (m_SerializedSettings == null || m_SerializedSettings.targetObject != m_Settings)
                BindSerializedSettings();

            m_SerializedSettings.Update();
            EditorGUILayout.PropertyField(m_NoJoinWordsProperty, new GUIContent("No-Join Words"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Selective Disjoin Rules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use when only specific conjuncts should be broken inside a word. Example: Word = द्वित्व, Disjoin Patterns = त्व",
                MessageType.None);
            EditorGUILayout.PropertyField(m_SelectiveRulesProperty, new GUIContent("Rules"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bulk Add", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Paste multiple words and click Add. Supported separators: newline, comma, semicolon, tab.",
                MessageType.None);
            m_BulkInput = EditorGUILayout.TextArea(m_BulkInput, GUILayout.MinHeight(90f));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Pasted Words"))
                AddBulkWords();

            if (GUILayout.Button("Clear Input"))
            {
                m_BulkInput = string.Empty;
                m_BulkStatus = string.Empty;
                m_BulkStatusType = MessageType.None;
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(m_BulkStatus))
                EditorGUILayout.HelpBox(m_BulkStatus, m_BulkStatusType);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Normalize List"))
            {
                m_SerializedSettings.ApplyModifiedProperties();
                Undo.RecordObject(m_Settings, "Normalize HarfBuzz No-Join Words");
                m_Settings.Normalize();
                EditorUtility.SetDirty(m_Settings);
                AssetDatabase.SaveAssets();
                BindSerializedSettings();
            }

            if (GUILayout.Button("Open Asset"))
            {
                Selection.activeObject = m_Settings;
                EditorGUIUtility.PingObject(m_Settings);
            }

            EditorGUILayout.EndHorizontal();

            if (m_SerializedSettings.ApplyModifiedProperties())
                EditorUtility.SetDirty(m_Settings);
        }

        private void AddBulkWords()
        {
            if (m_Settings == null)
                return;

            if (string.IsNullOrWhiteSpace(m_BulkInput))
            {
                m_BulkStatus = "Paste at least one word first.";
                m_BulkStatusType = MessageType.Warning;
                return;
            }

            m_SerializedSettings.ApplyModifiedProperties();
            Undo.RecordObject(m_Settings, "Bulk Add HarfBuzz No-Join Words");

            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            List<string> words = m_Settings.NoJoinWords;

            for (int i = 0; i < words.Count; i++)
            {
                string existingWord = HarfBuzzHindiNoJoinSettings.SanitizeWord(words[i]);
                if (!string.IsNullOrEmpty(existingWord))
                    existing.Add(existingWord);
            }

            int addedCount = 0;
            int skippedCount = 0;
            List<string> tokens = HarfBuzzHindiNoJoinSettings.ParseSeparatedTokens(m_BulkInput);
            for (int i = 0; i < tokens.Count; i++)
            {
                string word = HarfBuzzHindiNoJoinSettings.SanitizeWord(tokens[i]);
                if (string.IsNullOrEmpty(word))
                {
                    skippedCount++;
                    continue;
                }

                if (existing.Add(word))
                {
                    words.Add(word);
                    addedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }

            m_Settings.Normalize();
            EditorUtility.SetDirty(m_Settings);
            AssetDatabase.SaveAssets();
            BindSerializedSettings();

            if (addedCount > 0)
                m_BulkInput = string.Empty;

            m_BulkStatus = $"Added {addedCount} word(s). Skipped {skippedCount}.";
            m_BulkStatusType = addedCount > 0 ? MessageType.Info : MessageType.Warning;
        }

        private void RefreshSettingsReference()
        {
            m_Settings = AssetDatabase.LoadAssetAtPath<HarfBuzzHindiNoJoinSettings>(HarfBuzzHindiNoJoinSettings.DefaultAssetPath);
            if (m_Settings == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:HarfBuzzHindiNoJoinSettings");
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    HarfBuzzHindiNoJoinSettings found = AssetDatabase.LoadAssetAtPath<HarfBuzzHindiNoJoinSettings>(path);
                    if (found == null)
                        continue;

                    m_Settings = found;
                    break;
                }
            }

            BindSerializedSettings();
        }

        private void BindSerializedSettings()
        {
            if (m_Settings == null)
            {
                m_SerializedSettings = null;
                m_NoJoinWordsProperty = null;
                m_SelectiveRulesProperty = null;
                return;
            }

            m_SerializedSettings = new SerializedObject(m_Settings);
            m_NoJoinWordsProperty = m_SerializedSettings.FindProperty("m_NoJoinWords");
            m_SelectiveRulesProperty = m_SerializedSettings.FindProperty("m_SelectiveNoJoinRules");
        }

        private void CreateSettingsAsset()
        {
            EnsureFolderExists("Assets/Resources");

            HarfBuzzHindiNoJoinSettings asset = ScriptableObject.CreateInstance<HarfBuzzHindiNoJoinSettings>();
            AssetDatabase.CreateAsset(asset, HarfBuzzHindiNoJoinSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            m_Settings = asset;
            BindSerializedSettings();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
                return;

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }
    }
}
