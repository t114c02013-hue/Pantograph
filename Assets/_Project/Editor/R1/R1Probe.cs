using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>R1 量測工具：開切片場景，量集電弓各部位（降弓／升弓）的範圍，寫到 Logs/r1_probe.txt。</summary>
    public static class R1Probe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath);
            var sb = new StringBuilder();
            var mech = Object.FindObjectOfType<PantographMechanism>();
            sb.AppendLine("rig pos " + mech.transform.position.ToString("F3") + " rot " + mech.transform.eulerAngles.ToString("F1"));
            foreach (float lift in new[] { 0f, 1f })
            {
                mech.SetLift(lift);
                sb.AppendLine($"--- lift {lift}  union {SliceSceneBuilder.RendererBounds(mech.gameObject).min:F3} .. {SliceSceneBuilder.RendererBounds(mech.gameObject).max:F3}");
                foreach (var r in mech.GetComponentsInChildren<Renderer>().Where(r => r.transform.parent == mech.transform || r.transform.parent?.parent == mech.transform || r.name.StartsWith("CarbonStrip") || r.name.StartsWith("PanHead")))
                    sb.AppendLine($"{Path(r.transform, mech.transform),-50} min {r.bounds.min:F3} max {r.bounds.max:F3}");
            }
            mech.SetLift(0f);
            foreach (var t in Object.FindObjectsOfType<Transform>(true).Where(t => t.name.StartsWith("CarbonStrip_") || t.name.StartsWith("Bolt_") || t.name == "GroundClampPoint" || t.name == "LowerCheckPoint"))
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }) : new Bounds(t.position, Vector3.zero);
                sb.AppendLine($"{t.name,-20} pos {t.position:F3} bmin {b.min:F3} bmax {b.max:F3}");
            }
            foreach (var n in new[] { "CarRoof", "Floor", "Worksite", "PlatformRail", "XR", "TeleportPoints" })
            {
                var g = GameObject.Find(n);
                sb.AppendLine($"{n}: {(g != null ? g.transform.position.ToString("F3") : "無")}");
            }
            foreach (var t in Object.FindObjectsOfType<Transform>(true).Where(t => t.parent == null)) sb.AppendLine("root: " + t.name);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/r1_probe.txt", sb.ToString());
        }

        static string Path(Transform t, Transform root)
        {
            var s = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
