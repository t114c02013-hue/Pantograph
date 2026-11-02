using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>R1 場景件 prefab：每件都有 Visual／Walkable／Collision／Anchors，且沒有 Missing 腳本（之後換模型只換 Visual）。</summary>
    public class PrefabStructureTests
    {
        const string Folder = "Assets/_Project/Modules/Worksite/Prefabs";
        static readonly string[] Pieces =
        {
            "ENV_RoofSection", "Roof_Insulators", "Roof_Busbar", "Roof_CarBodyGround", "Car_Roof_Work", "ENV_CarBody", "ENV_PoleRack", "ENV_InspectionPlatform", "ENV_PlatformStair", "ENV_CabFloor", "ENV_Backdrop",
            "ENV_PlatformToolRack", "ENV_RoofToolbox", "ENV_BoltTray_L", "ENV_BoltTray_R", "TP_Marker",
        };

        [Test]
        public void EveryScenePiece_HasFourParts_AndNoMissingScripts([ValueSource(nameof(Pieces))] string name)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{name}.prefab");
            Assert.IsNotNull(go, "找不到 prefab " + name);
            foreach (var part in new[] { "Visual", "Walkable", "Collision", "Anchors" })
                Assert.IsNotNull(go.transform.Find(part), $"{name} 缺少 {part}");
            int missing = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            Assert.AreEqual(0, missing, name + " 有 Missing 腳本");
        }
    }
}
