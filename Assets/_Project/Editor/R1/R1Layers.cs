using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1 圖層：Walkable（可走地板：角色控制器、傳送射線、站點落點射線）、Boundary（護欄、支架、不可見邊界牆：擋角色控制器與傳送射線，不擋點選）、
    /// LocomotionBlocker（不可見擋牆：只擋角色控制器）。XRI 互動層：ToolWidget（左手 poke 工具上的撥桿、± 鈕、ZERO）、PanelHandle（左手抓面板把手）。
    /// 不存在就加在空的使用者圖層／互動層上。
    /// </summary>
    public static class R1Layers
    {
        public static readonly string[] PhysicsLayers = { "Walkable", "Boundary", "LocomotionBlocker", "ContactTarget", "PanAssembly", "LoadProbe", "WalkLimit", "Crane", "HeldPole" };   // R1.5b：LoadProbe（吊物探測）、WalkLimit（車頂不可見邊牆）   // R1.5a a5：ContactTarget（新互動點的 trigger）、PanAssembly（集電弓整組的 Box）
        public static readonly string[] InteractionLayers = { "ToolWidget", "PanelHandle" };

        public static string Ensure()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var added = new System.Collections.Generic.List<string>();
            foreach (var name in PhysicsLayers)
            {
                bool exists = Enumerable.Range(0, layers.arraySize).Any(i => layers.GetArrayElementAtIndex(i).stringValue == name);
                if (exists) continue;
                for (int i = 8; i < layers.arraySize; i++)
                {
                    var p = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(p.stringValue)) continue;
                    p.stringValue = name; added.Add($"{name}={i}");
                    break;
                }
            }
            tm.ApplyModifiedPropertiesWithoutUndo();

            // XRI 互動層（InteractionLayerSettings 資產）
            var guid = AssetDatabase.FindAssets("InteractionLayerSettings t:ScriptableObject").FirstOrDefault();
            if (guid != null)
            {
                var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)));
                var names = so.FindProperty("m_LayerNames");
                if (names != null)
                {
                    foreach (var name in InteractionLayers)
                    {
                        bool exists = Enumerable.Range(0, names.arraySize).Any(i => names.GetArrayElementAtIndex(i).stringValue == name);
                        if (exists) continue;
                        for (int i = 1; i < names.arraySize; i++)
                        {
                            var p = names.GetArrayElementAtIndex(i);
                            if (!string.IsNullOrEmpty(p.stringValue)) continue;
                            p.stringValue = name; added.Add($"互動層 {name}={i}");
                            break;
                        }
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.SaveAssets();
            return added.Count == 0 ? "圖層：已存在" : "圖層：新增 " + string.Join("、", added);
        }

        public static int Layer(string name) => LayerMask.NameToLayer(name);
    }
}
