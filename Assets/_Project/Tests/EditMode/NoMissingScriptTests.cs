using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PantographSim.Tests
{
    /// <summary>a6 8.1b：所有 prefab 與專案場景都沒有 Missing 腳本（Play 時不再出現 "missing script" 警告）。</summary>
    public class NoMissingScriptTests
    {
        /// <summary>掃 root 底下所有子物件（含隱藏），每個有 Missing 腳本的物件記一行「來源 : 階層路徑 (數量)」。</summary>
        static void Scan(string source, GameObject root, List<string> bad)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (n > 0) bad.Add($"{source} : {AnimationUtility.CalculateTransformPath(t, null)} ({n})");
            }
        }

        [Test]
        public void AllPrefabs_HaveNoMissingScripts()
        {
            var bad = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) Scan(path, go, bad);
            }
            Assert.IsEmpty(bad, "prefab 有 Missing 腳本：\n" + string.Join("\n", bad));
        }

        [Test]
        public void ProjectScenes_HaveNoMissingScripts()
        {
            // shortcut: 只掃 Assets/_Project 的場景（Samples/ThirdParty 的示範場景不會進建置，也不是我們能改的）, 若之後把 Samples/ThirdParty 場景加進 Build Settings 就把資料夾加進來
            var bad = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = SceneManager.GetSceneByPath(path);
                bool inHierarchy = scene.IsValid(), opened = !scene.isLoaded;
                // 沒開的場景以 Additive 開來掃、掃完關掉；已開著的直接掃，不動它（不存檔、不弄髒）
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try { foreach (var root in scene.GetRootGameObjects()) Scan(path, root, bad); }
                finally { if (opened) EditorSceneManager.CloseScene(scene, !inHierarchy); }
            }
            Assert.IsEmpty(bad, "場景有 Missing 腳本：\n" + string.Join("\n", bad));
        }
    }
}
