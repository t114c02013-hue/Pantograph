using System;
using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;

namespace PantographSim.Worksite
{
    /// <summary>
    /// R1.5b b4 第 4 部分：載入課程時套用 CourseDef.placements——把物件（ISimObject 的 ID，或「name:物件名稱」）移到錨點、設定顯示與否。
    /// 同一個 ID 在場景裡只有一個物件，由課程決定擺在哪裡（不另建分身，判定讀原本的 ID）。回傳找不到的物件或錨點。
    /// </summary>
    public static class CourseApplier
    {
        /// <summary>黑板：目前載入的課程（periodic／daily）；R1 的 25 步沒有這個鍵。</summary>
        public const string CourseKey = "course.id";

        public static List<string> Apply(CourseDef course, Func<string, Transform> byId, Func<string, Transform> byName)
        {
            var problems = new List<string>();
            foreach (var p in course?.placements ?? Array.Empty<CourseDef.Placement>())
            {
                var t = p.id.StartsWith("name:") ? byName(p.id.Substring(5)) : byId(p.id);
                if (t == null) { problems.Add($"{course.courseId}：找不到 {p.id}"); continue; }
                if (!string.IsNullOrEmpty(p.anchor))
                {
                    var a = byName(p.anchor);
                    if (a == null) problems.Add($"{course.courseId}：{p.id} 的錨點 {p.anchor} 不在場景");
                    else t.position = a.position;
                }
                t.gameObject.SetActive(p.active);
            }
            return problems;
        }

        /// <summary>場景裡的 ISimObject（含 inactive）：ID → Transform。</summary>
        public static Dictionary<string, Transform> SceneIds() =>
            UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => !string.IsNullOrEmpty(o.Id))
                .GroupBy(o => o.Id).ToDictionary(g => g.Key, g => ((Component)g.First()).transform);

        /// <summary>依名稱找場景物件（含 inactive）。</summary>
        public static Transform FindByName(string name) =>
            Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == name && t.gameObject.scene.IsValid());

        public static List<string> Apply(CourseDef course, ISimState state = null)
        {
            var ids = SceneIds();
            state?.Set(CourseKey, course.courseId);
            return Apply(course, id => ids.TryGetValue(id, out var t) ? t : null, FindByName);
        }
    }
}
