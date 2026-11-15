using System.Collections;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.XR;
using UnityEngine;
using UnityEngine.TestTools;

namespace PantographSim.Tests
{
    /// <summary>terry 186（規格第 5 節第 9 項）：頭從箱子外移進箱子中心再移出——暗幕先升到 maxAlpha 再回到 0，提示先出現再消失；不寫任何黑板鍵（不會有 ErrorLog）。
    /// 教學箱 FBX 未交付：用一個 HeadSolid 層的方塊代替，查詢走真的 Physics（CheckSphere、Linecast）。</summary>
    public class HeadSolidPlayTest
    {
        [UnityTest]
        public IEnumerator HeadIntoBoxAndOut_FadeAndHint()
        {
            int layer = LayerMask.NameToLayer(SceneLayers.HeadSolid);
            Assert.GreaterOrEqual(layer, 0);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.layer = layer; box.transform.position = new Vector3(100f, 1f, 100f); box.transform.localScale = Vector3.one;
            var def = ScriptableObject.CreateInstance<HeadSolidDef>();
            var logic = new HeadSolidLogic(def);
            int mask = SceneLayers.HeadSolidMask;
            bool Touch(Vector3 h) => Physics.CheckSphere(h, def.headSolidRadius, mask, QueryTriggerInteraction.Collide);
            int Cross(Vector3 a, Vector3 b) { var d = b - a; return d.sqrMagnitude < 1e-8f ? 0 : Physics.RaycastAll(a, d, d.magnitude, mask, QueryTriggerInteraction.Collide).Length; }
            var outside = new Vector3(102f, 1f, 100f); var center = box.transform.position;
            float t = 0f, peak = 0f; bool hintSeen = false;
            yield return null;
            Physics.SyncTransforms();   // 剛建的方塊：autoSyncTransforms 關著，查詢前先同步
            try
            {
                for (int i = 0; i <= 40; i++)   // 慢慢走進箱子中心
                {
                    var h = Vector3.Lerp(outside, center, i / 40f);
                    float a = logic.Tick(t += 0.02f, 0.02f, h, false, false, Touch, Cross);
                    peak = Mathf.Max(peak, a); hintSeen |= HeadSolidLogic.ShowHint(logic.Inside, FallZone.Normal);
                }
                for (int i = 0; i < 20; i++) peak = Mathf.Max(peak, logic.Tick(t += 0.02f, 0.02f, center, false, false, Touch, Cross));
                Assert.IsTrue(logic.Inside, "頭在箱子中心（球碰不到表面）仍算在裡面");
                Assert.AreEqual(def.maxAlpha, peak, 1e-3f, "暗幕升到 maxAlpha");
                Assert.IsTrue(hintSeen, "提示出現");
                float a2 = 1f;
                for (int i = 0; i <= 60; i++) a2 = logic.Tick(t += 0.02f, 0.02f, Vector3.Lerp(center, outside, Mathf.Min(1f, i / 20f)), false, false, Touch, Cross);
                Assert.AreEqual(0f, a2, 1e-3f, "出來後回到 0");
                Assert.IsFalse(HeadSolidLogic.ShowHint(logic.Inside, FallZone.Normal), "提示消失");
            }
            finally { Object.Destroy(box); }
        }
    }
}
