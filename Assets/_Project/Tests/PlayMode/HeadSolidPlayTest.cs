using System.Collections;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PantographSim.Tests
{
    /// <summary>terry 186（規格第 5 節第 9 項）：頭從地面教學箱（電池箱，gen_roof_boxes v1.1）正面外移進箱體再移出——暗幕先升到 maxAlpha 再回到 0，提示先出現再消失；不寫任何黑板鍵（不會有 ErrorLog）。
    /// 載入正式場景，查詢走建置產生的 HeadSolid 碰撞框（CheckSphere、RaycastAll）。</summary>
    public class HeadSolidPlayTest
    {
        [UnityTest]
        public IEnumerator HeadIntoTrainingBoxAndOut_FadeAndHint()
        {
            SceneManager.LoadScene("SC_CarbonStripSlice");
            for (int i = 0; i < 5; i++) yield return null;
            Physics.SyncTransforms();
            var box = Object.FindObjectsOfType<Transform>(true).First(t => t.name == "TrainingBox_battery");
            var body = box.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Body").bounds;
            HeadSolidDef def = null;
#if UNITY_EDITOR
            def = UnityEditor.AssetDatabase.LoadAssetAtPath<HeadSolidDef>("Assets/_Project/Modules/XR/Resources/HeadSolidDef.asset");
#endif
            Assert.IsNotNull(def);
            var logic = new HeadSolidLogic(def);
            int mask = SceneLayers.HeadSolidMask;
            bool Touch(Vector3 h) => Physics.CheckSphere(h, def.headSolidRadius, mask, QueryTriggerInteraction.Collide);
            int Cross(Vector3 a, Vector3 b) { var d = b - a; return d.sqrMagnitude < 1e-8f ? 0 : Physics.RaycastAll(a, d, d.magnitude, mask, QueryTriggerInteraction.Collide).Length; }
            var center = body.center; var outside = new Vector3(center.x, center.y, body.max.z + 0.8f);   // 正面（北）外 0.8 m
            Assert.IsFalse(Touch(outside), "起點在箱子外");
            float t = 0f, peak = 0f; bool hintSeen = false;
            for (int i = 0; i <= 40; i++)   // 慢慢走進箱體中心
            {
                var h = Vector3.Lerp(outside, center, i / 40f);
                float a = logic.Tick(t += 0.02f, 0.02f, h, false, false, Touch, Cross);
                peak = Mathf.Max(peak, a); hintSeen |= HeadSolidLogic.ShowHint(logic.Inside, FallZone.Normal);
            }
            for (int i = 0; i < 20; i++) peak = Mathf.Max(peak, logic.Tick(t += 0.02f, 0.02f, center, false, false, Touch, Cross));
            Assert.IsTrue(logic.Inside, "頭在箱體中心仍算在裡面");
            Assert.AreEqual(def.maxAlpha, peak, 1e-3f, "暗幕升到 maxAlpha");
            Assert.IsTrue(hintSeen, "提示出現");
            float a2 = 1f;
            for (int i = 0; i <= 60; i++) a2 = logic.Tick(t += 0.02f, 0.02f, Vector3.Lerp(center, outside, Mathf.Min(1f, i / 20f)), false, false, Touch, Cross);
            Assert.AreEqual(0f, a2, 1e-3f, "出來後回到 0");
            Assert.IsFalse(HeadSolidLogic.ShowHint(logic.Inside, FallZone.Normal), "提示消失");
        }
    }
}
