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
    /// <summary>
    /// R1.5a 第一條 PlayMode 測試（增補 5 第四節、增補 6 第 7 點 C）：學員站在 tp.roof.base_w 夾上（RM_00）／拆下（WC_03a b）車體接地座，正確動作不可以觸發警示。
    /// shortcut: RM_00／WC_03a 的工具與 RoofEdge 判定（頭部 |X| ＞ 1.19 持續 1 s）是 R1.5b，這裡用 StationLayoutCheck.HeadReach 的伸手估計（短絕緣桿桿頭）算頭部位置，
    ///           「沒有新錯誤」用 LogAssert（任何 Error 都算失敗）, R1.5b 有工具與判定後改成實際操作工具、讀 ErrorLog 與震動事件
    /// </summary>
    public class CarbodyGroundReachTest
    {
        const string Scene = "SC_CarbonStripSlice";
        StationSet set; RoofSceneDef def;

        IEnumerator Load()
        {
            SceneManager.LoadScene(Scene);
            for (int i = 0; i < 5; i++) yield return null;
#if UNITY_EDITOR
            set = UnityEditor.AssetDatabase.LoadAssetAtPath<StationSet>("Assets/_Project/Modules/Worksite/Data/StationSet.asset");
            def = UnityEditor.AssetDatabase.LoadAssetAtPath<RoofSceneDef>("Assets/_Project/Modules/Worksite/Data/RoofSceneDef.asset");
#endif
            Assert.IsNotNull(set, "讀不到 StationSet");
            Assert.IsNotNull(def, "讀不到 RoofSceneDef");
        }

        static LocomotionSettings Loco()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<LocomotionSettings>("Assets/_Project/Modules/Worksite/Data/LocomotionSettings.asset");
#else
            return null;
#endif
        }

        static Transform Point(string id) => Object.FindObjectsOfType<XrTeleportPoint>(true).First(p => p.PointId == id).transform;
        static Transform Sim(string id) => Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => o.Id == id).Select(o => ((Component)o).transform).First();

        [UnityTest]
        public IEnumerator ClampOn_RM00_And_Remove_WC03ab_HeadWithinLimit_NoNewErrors([Values("RM_00 夾上", "WC_03a b 拆下")] string action)
        {
            yield return Load();
            var station = Point("tp.roof.base_w").position;
            var clamp = Sim("ip.roof.carbody_ground").position;
            float maxHead = set.reachHeadMaxAbsX;
            float reach = set.shortPoleReachHorizontal;
            var (headX, dist) = StationLayoutCheck.HeadReach(station, clamp, reach);
            Debug.Log($"[CarbodyGroundReachTest] {action}：tp.roof.base_w {station} → Clamp_Point {clamp}，水平 {dist:0.000} m，頭部 |X| {headX:0.000}");
            Assert.LessOrEqual(headX, maxHead, $"{action}：頭部 |X| {headX:0.000} 超過 {maxHead}（紅線）");
            Assert.LessOrEqual(headX, def.roofBaseWarningX, "頭部不出紅線");
            for (int i = 0; i < 30; i++) yield return null;   // 讓場景跑一會：這段期間有任何 Error／Exception，Unity Test Framework 會讓測試失敗
        }

        /// <summary>增補 6 第 7 點 C：從站點往底架走，移動被擋下，身體不進入底架頂點範圍。</summary>
        [UnityTest]
        public IEnumerator WalkingFromStationTowardBase_IsBlocked_HeadNeverEntersBase()
        {
            yield return Load();
            var station = Point("tp.roof.base_w").position;
            var root = GameObject.Find("PAN_Root").transform;
            var baseB = StationLayoutCheck.VertexBounds(StationLayoutCheck.JointRenderers(root, "Base")).Value;
            var body = new GameObject("TestBody");
            var cc = body.AddComponent<CharacterController>();
            var loco = Loco();   // 和 XrRigBuilder 的學員 CharacterController 同一份資料
            cc.radius = loco.capsuleRadius; cc.stepOffset = loco.stepOffset; cc.height = set.eyeHeight; cc.center = new Vector3(0, set.eyeHeight / 2f, 0); cc.skinWidth = 0.02f;
            body.transform.position = station + Vector3.up * 0.01f;
            Physics.SyncTransforms();
            var dir = new Vector3(baseB.center.x - station.x, 0f, baseB.center.z - station.z).normalized;
            for (int i = 0; i < 60; i++)
            {
                cc.Move(dir * 0.05f + Vector3.down * 0.01f);
                var p = body.transform.position;
                bool inside = p.x > baseB.min.x && p.x < baseB.max.x && p.z > baseB.min.z && p.z < baseB.max.z;
                Assert.IsFalse(inside, $"第 {i} 步走進底架頂點範圍 {p}");
                yield return null;
            }
            float moved = Vector2.Distance(new Vector2(body.transform.position.x, body.transform.position.z), new Vector2(station.x, station.z));
            Debug.Log($"[CarbodyGroundReachTest] 往底架走 3 m，實際移動 {moved:0.000} m，停在 {body.transform.position}");
            Assert.Less(moved, Vector2.Distance(new Vector2(station.x, station.z), new Vector2(baseB.center.x, baseB.center.z)), "要被擋下");
            Object.Destroy(body);
        }
    }
}
