using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PantographSim.Core;
using PantographSim.Interaction;
using PantographSim.Parts.Mechanism;
using UnityEngine;

namespace PantographSim.Tests
{
    /// <summary>第 7 輪：R1 每一步每個目標，從「帶我過去」的鏡頭位置看得到（網格沒有擋）、點得到（PickRay 打到目標）。弓頭在降下的位置。</summary>
    public class FocusViewTests
    {
        [System.Serializable] class Sub { public string text, target, stand; }
        [System.Serializable] class Step { public string stepId; public Sub[] subActions; }
        [System.Serializable] class File { public Step[] steps; }

        static string Id(Collider c)
        {
            var it = c.GetComponentInParent<IInteractable>();
            return it != null ? it.Id : c.name;
        }

        [Test]
        public void EveryR1Target_VisibleAndClickable_FromFocusView()
        {
            var json = System.IO.File.ReadAllText("Assets/_Project/Modules/Procedures/Data/carbon_strip_slice.json");
            var steps = JsonUtility.FromJson<File>(json).steps;
            StationTests.WithScene(scene =>
            {
                var roots = scene.GetRootGameObjects();
                var ids = StationTests.SceneIds(scene);
                roots.SelectMany(g => g.GetComponentsInChildren<PantographMechanism>(true)).First().SetLift(0f);
                var focus = roots.SelectMany(g => g.GetComponentsInChildren<StepFocus>(true)).First();
                var orbit = roots.SelectMany(g => g.GetComponentsInChildren<OrbitCameraController>(true)).First();
                var s = new SimState(new EventBus());
                var bad = new List<string>(); int checkedCount = 0;
                var views = new List<(string step, string id, Transform t, Vector3 eye, Vector3 p)>();
                Physics.SyncTransforms();
                foreach (var st in steps.Where(x => x.subActions != null))
                    foreach (var sub in st.subActions)
                        foreach (var side in new[] { "L", "R" })
                        {
                            string id = (sub.target ?? "").Replace("{side}", side);
                            if (id.Length == 0 || id.EndsWith("*") || !ids.TryGetValue(id, out var t) || !t.gameObject.activeInHierarchy) continue;
                            if (id.StartsWith("env.work_order") || id.EndsWith(".bracket") || id.EndsWith(".install_check")) continue;   // 作業單是 UI；托架要滑板拆下才露出
                            if (id.StartsWith("env.tool_rack.slot_") && ids.TryGetValue("env.tool_bag", out var bag) && t.IsChildOf(bag)) continue;   // 工具袋用工具列拿
                            var anchor = GameObject.Find("StationAnchor_" + (sub.stand ?? ""));
                            if (anchor == null) continue;
                            t.TryGetComponent<Collider>(out var col); if (col == null) col = t.GetComponentInChildren<Collider>();
                            var p = col != null ? col.bounds.center : t.position;
                            orbit.Retarget(new Bounds(p, Vector3.one * 0.45f), focus.ViewFrom(anchor.transform.position, id, p));
                            views.Add((st.stepId, id, t, orbit.PlannedPosition, p));
                        }
                // 點得到：真實的碰撞體
                s.Set("hint.sub_target", "");
                foreach (var v in views)
                {
                    checkedCount++;
                    s.Set("hint.sub_target", v.id);
                    var dir = v.p - v.eye;
                    if (!PickRay.Cast(new Ray(v.eye, dir.normalized), dir.magnitude + 0.05f, SceneLayers.InteractMask, s, out var hit)) { bad.Add($"{v.step} {v.id}：打不到"); continue; }
                    string got = Id(hit.collider);
                    if (got != v.id && !got.StartsWith(v.id) && !v.id.StartsWith(got)) bad.Add($"{v.step} {v.id}：打到 {got}");
                }
                // 螺栓只有 2 cm：準星偏 1.5 cm 也要點得到
                foreach (var v in views.Where(x => SimIds.IsBoltId(x.id)))
                    foreach (var off in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        s.Set("hint.sub_target", v.id);
                        var at = v.p + off * 0.015f; var dir = at - v.eye;
                        if (!PickRay.Cast(new Ray(v.eye, dir.normalized), dir.magnitude + 0.05f, SceneLayers.InteractMask, s, out var hit) || Id(hit.collider) != v.id) bad.Add($"{v.step} {v.id}：偏 1.5 cm 點不到");
                    }
                // 看得到：給每個網格暫時加碰撞體，檢查視線上有沒有東西（場景不存檔）
                foreach (var mf in roots.SelectMany(g => g.GetComponentsInChildren<MeshFilter>()).Where(m => m.sharedMesh != null && m.TryGetComponent<Renderer>(out var r) && r.enabled && !m.TryGetComponent<Collider>(out _)))
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                Physics.SyncTransforms();
                foreach (var v in views)
                {
                    s.Set("hint.sub_target", v.id);
                    var dir = v.p - v.eye; float dist = dir.magnitude;
                    foreach (var h in Physics.RaycastAll(new Ray(v.eye, dir.normalized), dist - 0.03f, ~LayerMask.GetMask(SceneLayers.LocomotionBlocker, SceneLayers.Boundary, SceneLayers.WalkLimit), QueryTriggerInteraction.Ignore))
                    {
                        string got = Id(h.collider);
                        if (h.collider.transform.IsChildOf(v.t) || v.t.IsChildOf(h.collider.transform) || got.StartsWith(v.id) || SimIds.RayIgnores(s, got)) continue;
                        bad.Add($"{v.step} {v.id}：鏡頭 {v.eye:F2} 被 {h.collider.name} 擋住");
                    }
                }
                Assert.Greater(checkedCount, 30, "有檢查到目標");
                CollectionAssert.IsEmpty(bad.Distinct().ToList());
            });
        }
    }
}
