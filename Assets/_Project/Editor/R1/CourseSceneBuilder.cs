using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Mechanism;
using PantographSim.Parts.Common;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5b b4 第 4c 部分（D23、D41，建模 A model-spec v3.10 §8.3b 暫代物）：場景建置時加
    /// ・Anchor_O：專案原點 O＝弓頭中心正下方的車頂面（D41）。
    /// ・ENV_Catenary（env.catenary，存檔 inactive，日常課程顯示）：接觸線（O＋CourseDef.liftCapAboveO）、承力索（O＋messengerAboveO）、2 支支柱、接地器 #E1／#E2（ip.ocs.earthing_device_n，只能 Inspect）。
    ///   節點名稱、ID、Inspect 錨點都用正式名稱，正式 FBX（b5）到了只換 Visual。
    /// ・Crane_ParkPose：天車駐車位（X＝吊車台、Y＝CraneProfile.hookYMax、Z＝橋架），交給 CraneMechanism。
    /// ・LiftCaps：有升弓上限的課程，量滑板頂面到「接觸線 − contactGapM」時的升降值。
    /// 數值都在 RoofSceneDef／Courses.json，程式不寫死。
    /// </summary>
    public static class CourseSceneBuilder
    {
        public const string LiftCapsPath = "Assets/_Project/Modules/Parts/Mechanism/Resources/LiftCaps.asset";

        public static void Build(StringBuilder sb)
        {
            var d = R1Data.RoofScene;
            var env = GameObject.Find("Worksite").transform;
            var head = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().First(o => o.Id == SimIds.PanHead);
            var hb = PanGeometry.VertexBounds(((Component)head).GetComponentsInChildren<Renderer>()).Value;
            var o = Node(env, "Anchor_O", new Vector3(hb.center.x, d.roofFloorY, hb.center.z));
            sb.AppendLine($"Anchor_O（D41 專案原點，弓頭中心正下方的車頂面）：{o.position:F3}");

            var daily = StepsCsvImporter.LoadCourses().courses.First(c => c.courseId == "daily");
            BuildCatenary(env, d, o.position, daily.liftCapAboveO, sb);
            BuildParkPose(sb);
            MeasureLiftCaps(o.position, sb);
        }

        static Transform Node(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = pos; return t;
        }

        static Material Mat(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float diameter, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.position = (a + b) / 2f;
            g.transform.up = (b - a).normalized;
            g.transform.localScale = new Vector3(diameter, (b - a).magnitude / 2f, diameter);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            if (m != null) g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static void BuildCatenary(Transform env, RoofSceneDef d, Vector3 o, float contactAboveO, StringBuilder sb)
        {
            var root = new GameObject("ENV_Catenary").transform; root.SetParent(env, false); root.position = o;
            var visual = new GameObject("Visual").transform; visual.SetParent(root, false);
            var metal = Mat("Assets/_Project/Materials/Worksite/Metal.mat");
            var yg = Mat("Assets/_Project/Materials/Worksite/Paint.mat");
            float z = d.catenaryHalfLengthZ, yContact = o.y + contactAboveO, yMessenger = o.y + d.catenaryMessengerAboveO;
            Rod(visual, "ContactWire", new Vector3(o.x, yContact, o.z - z), new Vector3(o.x, yContact, o.z + z), d.catenaryWireDiameter, metal);
            Rod(visual, "MessengerWire", new Vector3(o.x, yMessenger, o.z - z), new Vector3(o.x, yMessenger, o.z + z), d.catenaryWireDiameter, metal);
            foreach (var (n, sz) in new[] { ("Mast_S", -1f), ("Mast_N", 1f) })
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m.name = n; m.transform.SetParent(visual, false);
                m.transform.position = new Vector3(o.x + d.catenaryMastX, o.y + (d.catenaryMastYMin + d.catenaryMastYMax) / 2f, o.z + sz * z);
                m.transform.localScale = new Vector3(d.catenaryMastSize, d.catenaryMastYMax - d.catenaryMastYMin, d.catenaryMastSize);
                Object.DestroyImmediate(m.GetComponent<Collider>());
                m.GetComponent<Renderer>().sharedMaterial = metal;
            }
            for (int i = 1; i <= 2; i++)
            {
                float ez = o.z + (i == 1 ? -1f : 1f) * d.earthingDeviceZ;
                var top = new Vector3(o.x, yContact, ez);
                var rail = new Vector3(o.x + d.earthingRailXY.x, o.y + d.earthingRailXY.y, ez);
                var dev = new GameObject("EarthingDevice_" + i).transform; dev.SetParent(root, false);
                Rod(dev, "Rod", top, rail, d.earthingDiameter, yg);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);   // Inspect 方塊（只能查看，抓不起來）
                box.name = "InspectBox"; box.transform.SetParent(dev, false);
                box.transform.position = (top + rail) / 2f; box.transform.localScale = Vector3.one * d.earthingInspectBox;
                Object.DestroyImmediate(box.GetComponent<Renderer>());
                box.GetComponent<Collider>().isTrigger = true;
                box.AddComponent<InteractionPointMarker>().Configure("ip.ocs.earthing_device_" + i, $"架空線接地器 #E{i}");
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "TagPlate"; plate.transform.SetParent(dev, false);
                plate.transform.position = rail + Vector3.up * 0.6f; plate.transform.localScale = new Vector3(0.12f, 0.08f, 0.01f);
                Object.DestroyImmediate(plate.GetComponent<Collider>());
                plate.GetComponent<Renderer>().sharedMaterial = yg;
                var label = new GameObject("Label_TagNo").AddComponent<TextMesh>();
                label.transform.SetParent(plate.transform, false); label.transform.localPosition = new Vector3(0f, 0f, -0.6f);
                label.text = "#E" + i; label.characterSize = 0.1f; label.anchor = TextAnchor.MiddleCenter; label.color = Color.black;
            }
            root.gameObject.AddComponent<InteractionPointMarker>().Configure("env.catenary", "架空線");
            root.gameObject.SetActive(false);   // 存檔 inactive；日常課程由 CourseDef 顯示
            sb.AppendLine($"ENV_Catenary（暫代物）：接觸線 Y {yContact:0.000}（O＋{contactAboveO}）、承力索 Y {yMessenger:0.000}、支柱 X {o.x + d.catenaryMastX:0.00}、接地器 Z ±{d.earthingDeviceZ}（存檔 inactive）");
        }

        static void BuildParkPose(StringBuilder sb)
        {
            var crane = Object.FindObjectOfType<CraneMechanism>(true);
            var d = R1Data.RoofScene;
            var profile = AssetDatabase.FindAssets("t:CraneProfile").Select(g => AssetDatabase.LoadAssetAtPath<CraneProfile>(AssetDatabase.GUIDToAssetPath(g))).First();
            var pose = Node(crane.transform, "Crane_ParkPose", new Vector3(d.craneParkXZ.x, profile.hookYMax, d.craneParkXZ.y));
            var so = new SerializedObject(crane);
            so.FindProperty("parkPose").objectReferenceValue = pose;
            so.ApplyModifiedPropertiesWithoutUndo();
            sb.AppendLine($"Crane_ParkPose（D23）：吊車台 X {pose.position.x:0.00}、吊鉤 Y {pose.position.y:0.000}（hookYMax）、橋架 Z {pose.position.z:0.00}");
        }

        /// <summary>滑板頂面（兩條滑板網格頂點的最高點）在升降值 v 時的世界 Y。</summary>
        public static float StripTopY(PantographMechanism mech, float v)
        {
            mech.SetLift(v);
            var strips = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(x => x.Id == SimIds.Strip("L") || x.Id == SimIds.Strip("R"))
                .SelectMany(x => ((Component)x).GetComponentsInChildren<Renderer>());
            return PanGeometry.VertexBounds(strips).Value.max.y;
        }

        static void MeasureLiftCaps(Vector3 o, StringBuilder sb)
        {
            var mech = Object.FindObjectOfType<PantographMechanism>(true);
            float keep = mech.Lift;
            var caps = AssetDatabase.LoadAssetAtPath<LiftCapsDef>(LiftCapsPath);
            if (caps == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LiftCapsPath));
                caps = ScriptableObject.CreateInstance<LiftCapsDef>();
                AssetDatabase.CreateAsset(caps, LiftCapsPath);
            }
            try
            {
                caps.entries = StepsCsvImporter.LoadCourses().courses.Where(c => c.liftCapAboveO > 0f).Select(c =>
                {
                    float wire = o.y + c.liftCapAboveO, stop = wire - c.contactGapM;
                    if (StripTopY(mech, 1f) <= stop) return new LiftCapsDef.Entry { courseId = c.courseId, contactWireY = wire, stopTopY = stop, liftMax = 1f };
                    float lo = 0f, hi = 1f;
                    for (int i = 0; i < 40; i++) { float mid = (lo + hi) / 2f; if (StripTopY(mech, mid) <= stop) lo = mid; else hi = mid; }
                    return new LiftCapsDef.Entry { courseId = c.courseId, contactWireY = wire, stopTopY = stop, liftMax = lo };
                }).ToArray();
            }
            finally { mech.SetLift(keep); }
            EditorUtility.SetDirty(caps);
            AssetDatabase.SaveAssets();
            foreach (var e in caps.entries)
                sb.AppendLine($"升弓上限（D23、D41）{e.courseId}：接觸線 Y {e.contactWireY:0.000}、滑板頂面停在 {e.stopTopY:0.000}、升降值上限 {e.liftMax:0.0000}");
        }
    }
}
