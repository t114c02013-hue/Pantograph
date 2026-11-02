using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PantographSim.App;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Interaction;
using PantographSim.Parts.Common;
using PantographSim.Parts.Mechanism;
using PantographSim.Recording;
using PantographSim.Tools;
using PantographSim.UI;
using PantographSim.Worksite;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// W13：產生 Assets/_Project/Content/Scenes/SC_CarbonStripSlice.unity（Build Settings 第 0 位）。
    /// 依序：匯入步驟資料 → 產生模型 → 組場景（可動集電弓、碳滑板與螺栓、托架遮罩、接地設備、控制盤、工具台、零件盒、備品架、
    /// 工具列、介面、組裝點）→ 存檔 → 資料檢查 → 輸出預覽圖 rig_slice_scene/strip_L/bolts.png。可重跑。
    /// </summary>
    public static class SliceSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Content/Scenes/SC_CarbonStripSlice.unity";
        /// <summary>存檔前的擴充點（例：XR 編輯器模組加 XR Origin、傳送點、扶手）。桌面建置不需要它。</summary>
        public static event Action<StringBuilder> BeforeSave;
        static readonly Dictionary<string, Type> ToolTypes = new Dictionary<string, Type>
        {
            { "tool.voltage_detector", typeof(VoltageDetectorTool) }, { "tool.grounding_rod", typeof(GroundingRodTool) }, { "tool.caliper", typeof(CaliperTool) },
            { "tool.socket_wrench", typeof(SocketWrenchTool) }, { "tool.torque_wrench", typeof(TorqueWrenchTool) }, { "tool.cleaning_cloth", typeof(CleaningClothTool) },
            { "tool.marker", typeof(MarkerTool) }, { "tool.lockout_tag", typeof(LockoutTagTool) }, { "tool.safety_harness", typeof(SafetyHarnessTool) }
        };

        [MenuItem("PantographSim/Slice/Build Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log("[SliceSceneBuilder]\n" + Build());
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine(StepJsonImporter.Import());
            sb.AppendLine(SliceModelBuilder.Build());
            var mats = SliceModelBuilder.Materials();

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.1f; light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);
            sb.AppendLine(R1Layers.Ensure());
            var roofDef = R1Data.RoofScene;
            var stationSet = R1Data.ApplyR15a();   // R1.5a a3：站點相對平台邊、駕駛室移位、tp.platform.crane

            // ---- 可動集電弓 ----
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PantographRigBuilder.PrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            PrefabUtility.UnpackPrefabInstance(rig, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rig.name = "Pantograph";
            var mech = rig.GetComponent<PantographMechanism>();
            mech.SetLift(0f);
            rig.AddComponent<PantographPoseReporter>();

            var panHead = rig.GetComponentsInChildren<Transform>(true).First(t => t.name == "PanHead");
            // R1 座標系：原點＝弓頭中心（降弓）正下方的車頂地板點；底架放在絕緣子上（pantographBaseHeight）
            {
                var hb = panHead.GetComponent<Renderer>().bounds;
                var baseB = rig.transform.Find("Base").GetComponent<Renderer>().bounds;
                rig.transform.position += new Vector3(-hb.center.x, roofDef.roofFloorY + roofDef.pantographBaseHeight - baseB.min.y, -hb.center.z);
            }
            var headMarker = panHead.gameObject.AddComponent<PartBehaviour>();
            headMarker.Configure(SimIds.PanHead, "弓頭（點一下＝親眼確認降弓）", "inspect");
            panHead.gameObject.AddComponent<MeshCollider>().sharedMesh = panHead.GetComponent<MeshFilter>().sharedMesh;
            panHead.gameObject.AddComponent<Highlightable>();
            Track(panHead.gameObject, SimIds.PanHead);

            // ---- 碳滑板、螺栓、托架遮罩 ----
            var boltPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SliceModelBuilder.BoltPrefabPath);
            foreach (var (side, source) in new[] { ("L", "CarbonStrip_2"), ("R", "CarbonStrip_1") })
            {
                var orig = panHead.Find(source);
                var origMesh = orig.GetComponent<MeshFilter>().sharedMesh;
                var b = origMesh.bounds;
                var localPos = orig.localPosition;
                Object.DestroyImmediate(orig.gameObject);

                var strip = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SliceModelBuilder.StripPrefabPath(side)), scene);
                strip.transform.SetParent(panHead, false);
                strip.transform.localPosition = localPos;
                strip.transform.localRotation = side == "L" ? Quaternion.identity : Quaternion.Euler(0, 180, 0);   // +Z（斜面側）朝外
                strip.name = $"CarbonStrip_{side}";

                string sideName = side == "L" ? "肘側" : "外側";
                var slots = new Transform[4];
                for (int n = 1; n <= 4; n++)
                {
                    var bolt = (GameObject)PrefabUtility.InstantiatePrefab(boltPrefab, scene);
                    bolt.name = $"Bolt_{side}_{n}";
                    bolt.transform.SetParent(panHead, true);
                    bolt.transform.position = strip.transform.TransformPoint(SliceModelBuilder.BoltLocal(n, b.size.x / 2f, b.max.y));
                    bolt.transform.rotation = panHead.rotation;
                    bolt.GetComponent<FastenerBehaviour>().Configure($"ip.carbon_strip_{side}.bolt_{n}", $"{sideName}滑板 螺栓 {n}");
                    Track(bolt, $"ip.carbon_strip_{side}.bolt_{n}");
                    slots[n - 1] = bolt.transform;
                }
                Track(strip, $"part.carbon_strip_{side}");
                var removable = strip.GetComponent<RemovableBehaviour>();
                var visual = strip.transform.Find("Visual");
                removable.Setup(visual.Find("Holes").Cast<Transform>().ToArray(), slots, visual);

                var bracket = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bracket.name = $"Bracket_{side}";
                bracket.transform.SetParent(panHead, false);
                bracket.transform.localPosition = localPos + new Vector3(0, b.min.y + 0.0015f, 0);
                bracket.transform.localRotation = Quaternion.Euler(90, 0, 0);
                bracket.transform.localScale = new Vector3(b.size.x, b.size.z * 0.95f, 1);
                Object.DestroyImmediate(bracket.GetComponent<Collider>());
                var bc = bracket.AddComponent<BoxCollider>(); bc.size = new Vector3(1, 1, 0.004f);
                var r = bracket.GetComponent<Renderer>(); r.sharedMaterial = mats["dirt"];
                var dirt = bracket.AddComponent<DirtMask>();
                dirt.Configure($"ip.carbon_strip_{side}.bracket", $"{sideName}托架接觸面");
                dirt.Setup(r, $"part.carbon_strip_{side}");
                bracket.AddComponent<Highlightable>();
                sb.AppendLine($"場景：{sideName}滑板 {strip.transform.position:F3}、4 支螺栓、托架遮罩");
            }

            // ---- 弓頭接地掛點 ----
            var fastenerL = panHead.Find("Fastener_L_Bolt");
            var clamp = SliceModelBuilder.Prim(PrimitiveType.Cube, "GroundClampPoint", panHead, Vector3.zero, new Vector3(0.05f, 0.03f, 0.05f), mats["ground"]);
            clamp.transform.position = (fastenerL != null ? fastenerL.position : panHead.position) + new Vector3(0, -0.07f, 0);
            var clampFitting = SliceModelBuilder.Prim(PrimitiveType.Cube, "ClampAttached", clamp.transform, new Vector3(0, -0.9f, 0), new Vector3(0.8f, 0.8f, 0.8f), mats["groundStripe"]);
            Object.DestroyImmediate(clampFitting.GetComponent<Collider>());
            var clampTerm = clamp.AddComponent<GroundTerminal>();
            clampTerm.Configure(SimIds.GroundClamp, "弓頭接地掛點");
            clampTerm.Setup(clampFitting, null, null);
            Track(clampFitting, SimIds.GroundClamp, ".rod");   // 接地棒掛在弓頭端的夾具

            // ---- 降弓位置檢測：弓頭頂面中心的檢測點 → 降弓停放位置的綠框（量測座標寫回 PC_PanHeadLower） ----
            var pcDef = AssetDatabase.LoadAssetAtPath<PositionCheckDefinition>($"{StepJsonImporter.PositionCheckFolder}/PC_PanHeadLower.asset");
            var liftSettings = AssetDatabase.LoadAssetAtPath<LiftSettings>(StepJsonImporter.LiftSettingsPath);
            var headBounds = panHead.GetComponentsInChildren<Renderer>().Select(x => x.bounds).Aggregate((a, b2) => { a.Encapsulate(b2); return a; });
            var checkWorld = new Vector3(headBounds.center.x, headBounds.max.y + 0.015f, headBounds.center.z);
            var checkPoint = SliceModelBuilder.Prim(PrimitiveType.Sphere, "LowerCheckPoint", panHead, Vector3.zero, Vector3.one * 0.035f, mats["checkPoint"]);
            checkPoint.transform.position = checkWorld;
            Object.DestroyImmediate(checkPoint.GetComponent<Collider>());
            checkPoint.AddComponent<InteractionPointMarker>().Configure(pcDef.CheckMarkerId, "弓頭檢測點");
            pcDef.checkPointLocal = panHead.InverseTransformPoint(checkWorld);
            pcDef.targetLocal = rig.transform.InverseTransformPoint(checkWorld);   // 現在是降到底的姿態
            mech.SetLift(1f);
            float travelCm = Vector3.Distance(checkPoint.transform.position, checkWorld) * 100f;
            mech.SetLift(0f);
            liftSettings.travelCm = Mathf.Round(travelCm);
            EditorUtility.SetDirty(pcDef); EditorUtility.SetDirty(liftSettings);
            AssetDatabase.SaveAssets();

            var target = new GameObject("LowerTarget");
            target.transform.SetParent(rig.transform, false);
            target.transform.position = checkWorld;
            target.AddComponent<InteractionPointMarker>().Configure(pcDef.TargetMarkerId, "降弓位置（綠框）");
            var disc = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "Disc", target.transform, Vector3.zero, new Vector3(0.16f, 0.002f, 0.16f), mats["targetZone"]);
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            foreach (var (pos, scl) in new[] { (new Vector3(0, 0, 0.08f), new Vector3(0.17f, 0.006f, 0.01f)), (new Vector3(0, 0, -0.08f), new Vector3(0.17f, 0.006f, 0.01f)),
                                               (new Vector3(0.08f, 0, 0), new Vector3(0.01f, 0.006f, 0.17f)), (new Vector3(-0.08f, 0, 0), new Vector3(0.01f, 0.006f, 0.17f)) })
            {
                var bar = SliceModelBuilder.Prim(PrimitiveType.Cube, "Frame", target.transform, pos, scl, mats["targetFrame"]);
                Object.DestroyImmediate(bar.GetComponent<Collider>());
            }
            rig.AddComponent<PositionCheck>().Setup(pcDef, checkPoint.transform, rig.transform, target.transform, checkPoint.GetComponent<Renderer>(), disc.GetComponent<Renderer>());
            sb.AppendLine($"降弓位置檢測：檢測點 {checkWorld:F3}、容許 {pcDef.toleranceCm} cm、全程行程 {travelCm:F0} cm");

            // ---- R1 作業環境（T1）：車頂段、檢修平台、樓梯、駕駛室、背景與設備，全部讀 RoofSceneDef／StationSet ----
            var c = panHead.GetComponent<Renderer>().bounds.center;
            mech.SetLift(1f);
            var rigBounds = RendererBounds(rig);
            mech.SetLift(0f);
            rigBounds.Encapsulate(RendererBounds(rig));
            var panRoot = PantographAssemblyBuilder.Build(rig, sb);   // R1.5a a1：集電弓整組 PAN_Root（世界位置不變）
            var tools = StepJsonImporter.LoadTools();
            var site = RoofSiteBuilder.Build(roofDef, stationSet, mats, rig, panHead, clamp.transform, tools, sb);
            var env = site.env;
            PantographAssemblyBuilder.WriteReport(panRoot);   // R1.5a a5：Logs/PantographAssemblyReport.txt
            var heightProfile = R1Data.Height;
            heightProfile.measuredStripTop = Mathf.Round(site.stripTop * 1000f) / 1000f;
            EditorUtility.SetDirty(heightProfile);
            File.AppendAllText("Logs/SliceBuildReport.txt", $"\n## R1 高度與站點（{System.DateTime.Now:yyyy-MM-dd HH:mm}）\nstripTopAboveRoof={site.stripTop:0.000}\n共用車頂站點 ({site.roofStation.x:0.00}, {site.roofStation.z:0.00})；肘側最遠螺栓 {stationSet.measuredFarBoltL:0.000} m、外側 {stationSet.measuredFarBoltR:0.000} m\n");
            new GameObject("RoofAccess").AddComponent<RoofAccess>().Setup(R1Data.RoofAccess);
            // CSR_11 安裝到位判定：每條滑板的檢測點（頂面中心）→ 安裝位置（相對 PanHead），容許 0.5 cm
            foreach (var side in new[] { "L", "R" })
            {
                var strip = panHead.Find($"CarbonStrip_{side}");
                var def = AssetDatabase.LoadAssetAtPath<PositionCheckDefinition>($"{StepJsonImporter.PositionCheckFolder}/PC_StripInstall_{side}.asset");
                if (strip == null || def == null) { sb.AppendLine($"⚠ 缺少 PC_StripInstall_{side}"); continue; }
                var sb2 = strip.GetComponentsInChildren<Renderer>().Select(x => x.bounds).Aggregate((a, b2) => { a.Encapsulate(b2); return a; });
                var cp = new GameObject("InstallCheck");
                cp.transform.SetParent(strip, false);
                cp.transform.position = new Vector3(sb2.center.x, sb2.max.y, sb2.center.z);
                cp.AddComponent<InteractionPointMarker>().Configure(def.CheckMarkerId, (side == "L" ? "肘側" : "外側") + "新滑板");
                var tgt = new GameObject("InstallTarget_" + side);
                tgt.transform.SetParent(panHead, false);
                tgt.transform.position = cp.transform.position;
                tgt.AddComponent<InteractionPointMarker>().Configure(def.TargetMarkerId, (side == "L" ? "肘側" : "外側") + "滑板安裝位置");
                def.checkPointLocal = strip.InverseTransformPoint(cp.transform.position);
                def.targetLocal = panHead.InverseTransformPoint(cp.transform.position);
                EditorUtility.SetDirty(def);
                tgt.AddComponent<PositionCheck>().Setup(def, cp.transform, panHead, null, null, null);
            }
            AssetDatabase.SaveAssets();

            // ---- 擺放檢查：各場景件與集電弓（升降全程）不可相交 ----
            var layout = SceneLayoutCheck.Check(rigBounds, site.layout.ToArray());
            sb.AppendLine(layout.report);
            File.AppendAllText("Logs/SliceBuildReport.txt", "\n## 場景擺放檢查（" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "）\n" + layout.report + "\n");

            // ---- 攝影機與互動：開場就近、從斜上方看弓頭 ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f; cam.fieldOfView = 50f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            camGo.AddComponent<AudioListener>();
            var orbit = camGo.AddComponent<OrbitCameraController>();
            orbit.SetRotateWithLeftButton(false);
            SetHome(orbit, new Bounds(c + new Vector3(0, 0, 0.3f), Vector3.one), cam);
            var interaction = new GameObject("Interaction");
            interaction.AddComponent<Picker>();
            interaction.AddComponent<DragPlace>();
            interaction.AddComponent<InspectMode>().Setup(orbit);
            interaction.AddComponent<StepFocus>().Setup(orbit);

            // ---- 工具列 ----
            var belt = new GameObject("ToolBelt");
            foreach (var t in tools)
            {
                var tg = new GameObject("Tool_" + t.displayName);
                tg.transform.SetParent(belt.transform, false);
                ((ToolBase)tg.AddComponent(ToolTypes[t.toolId])).Setup(t, t.toolId);
            }
            belt.AddComponent<ToolBelt>();
            belt.AddComponent<ToolTutorialTrigger>();
            belt.AddComponent<ToolInventory>();

            rig.AddComponent<LiftDriver>().Setup(liftSettings);   // 升降：介面／鍵盤寫 ctrl.lift.cmd，機構端讀取驅動
            interaction.AddComponent<LiftKeys>();
            new GameObject("SliceUI").AddComponent<SliceUI>();
            BuildRecording(sb);
            var boot = new GameObject("SliceBootstrap").AddComponent<SliceBootstrap>();
            boot.Configure(StepJsonImporter.LoadProcedure(),
                StepJsonImporter.LoadAll<SafetyRuleDefinition>(StepJsonImporter.SafetyFolder).OrderBy(r => r.ruleId).ToArray(),
                StepJsonImporter.LoadAll<ScenarioDefinition>(StepJsonImporter.ScenarioFolder).OrderBy(s => s.scenarioId == "SCN_Default" ? "" : s.scenarioId).ToArray(),
                tools.ToArray());

            // 所有情境都從升弓開始：場景存成升弓姿態，開場（任務卡）看到的就是升起的弓
            var mechSo = new SerializedObject(mech);
            mechSo.FindProperty("startRaised").boolValue = true;
            mechSo.ApplyModifiedPropertiesWithoutUndo();
            mech.SetLift(1f);

            try { BeforeSave?.Invoke(sb); }
            catch (Exception e) { sb.AppendLine("⚠ 存檔前擴充失敗：" + e.Message); Debug.LogException(e); }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            PlayerSettings.runInBackground = true;   // 視窗沒有焦點時 Play 也繼續跑（自動截圖、同時操作其他視窗）
            EditorSceneManager.SaveScene(scene, ScenePath);
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            sb.AppendLine("場景已存檔：" + ScenePath + "（Build Settings 第 0 位）");

            var ids = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Select(x => x.Id).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            sb.AppendLine($"場景互動物件 {ids.Count} 個：" + string.Join(", ", ids.OrderBy(x => x)));
            DataValidator.Validate(out var validation, ids);
            sb.AppendLine("資料檢查：\n" + validation);

            RenderPreviews(sb, panHead, c);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SliceSceneReport.txt", sb.ToString());
            return sb.ToString();
        }

        public const string RecordingSettingsPath = "Assets/_Project/Settings/RecordingSettings.asset";

        /// <summary>重播要擺位置的物件：只放 ID（取自 ISimObject 或工具 ID）。</summary>
        static void Track(GameObject go, string id, string suffix = "")
        {
            var t = go.GetComponent<ReplayTracked>() ?? go.AddComponent<ReplayTracked>();
            t.Configure(id, suffix);
        }

        /// <summary>紀錄服務、錄影入口、旁觀鏡頭（VR 錄影用，平常不畫）。</summary>
        static void BuildRecording(StringBuilder sb)
        {
            var settings = AssetDatabase.LoadAssetAtPath<RecordingSettings>(RecordingSettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecordingSettingsPath));
                settings = ScriptableObject.CreateInstance<RecordingSettings>();
                AssetDatabase.CreateAsset(settings, RecordingSettingsPath);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("Recording");
            go.AddComponent<RecordingService>().Setup(settings);
            go.AddComponent<VideoCaptureService>();
            var spec = new GameObject("SpectatorCamera");
            spec.transform.SetParent(go.transform, false);
            spec.AddComponent<Camera>().enabled = false;
            spec.AddComponent<SpectatorCamera>().Setup(settings.spectatorDistance);
            int tracked = Object.FindObjectsOfType<ReplayTracked>(true).Length;
            sb.AppendLine($"紀錄：RecordingService（根目錄 {settings.Root}）、VideoCaptureService、SpectatorCamera；ReplayTracked {tracked} 個");
        }

        public static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is LineRenderer)).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>工具架：背板＋9 個掛位（每格一個掛鉤框與簡易工具模型），掛位順序同 v5（1 掛牌組…9 標記筆）。</summary>
        static Transform BuildToolRack(Transform env, Vector3 pos, List<ToolDefinition> tools, Dictionary<string, Material> mats)
        {
            var root = new GameObject("ToolRack");
            root.transform.SetParent(env, false);
            root.transform.position = pos;
            var board = SliceModelBuilder.Prim(PrimitiveType.Cube, "Board", root.transform, new Vector3(0, 0.95f, -0.04f), new Vector3(1.5f, 0.75f, 0.04f), mats["shelf"]);
            board.AddComponent<ToolRackBoard>().Configure("env.tool_rack", "工具架（點一下查看九個掛位）");
            for (int i = 0; i < 2; i++)
            {
                var post = SliceModelBuilder.Prim(PrimitiveType.Cube, "Post", root.transform, new Vector3(i == 0 ? -0.72f : 0.72f, 0.66f, -0.04f), new Vector3(0.05f, 1.32f, 0.05f), mats["steel"]);
                Object.DestroyImmediate(post.GetComponent<Collider>());
            }
            foreach (var t in tools.OrderBy(t => t.hotkey))
            {
                int k = t.hotkey;
                int col = (k - 1) % 5, row = (k - 1) / 5;
                var slotPos = new Vector3(-0.6f + 0.3f * col, 1.15f - 0.38f * row, 0f);
                var slot = SliceModelBuilder.Prim(PrimitiveType.Cube, $"Slot_{k}", root.transform, slotPos, new Vector3(0.26f, 0.32f, 0.03f), mats["panel"]);
                var model = BuildToolModel(t.toolId, slot.transform, mats);
                Track(model, t.toolId);   // 工具（ID＝工具 ID）
                var comp = slot.AddComponent<ToolRackSlot>();
                comp.Configure($"env.tool_rack.slot_{k}", $"工具架：{t.displayName}");
                comp.Setup(k, model, slot.GetComponent<Renderer>());
                slot.AddComponent<Highlightable>();
            }
            return root.transform;
        }

        public static GameObject BuildToolModelPublic(string toolId, Transform slot, Dictionary<string, Material> mats) => BuildToolModel(toolId, slot, mats);

        /// <summary>簡易工具模型（primitive 組合），放在掛位前方。</summary>
        static GameObject BuildToolModel(string toolId, Transform slot, Dictionary<string, Material> mats)
        {
            var root = new GameObject("Model_" + toolId);
            root.transform.SetParent(slot, false);
            root.transform.localPosition = new Vector3(0, 0, 1.6f);
            root.transform.localScale = new Vector3(1f / slot.localScale.x, 1f / slot.localScale.y, 1f / slot.localScale.z);
            GameObject P(PrimitiveType type, Vector3 p, Vector3 s, string mat, Vector3? rot = null)
            {
                var g = SliceModelBuilder.Prim(type, "Part", root.transform, p, s, mats[mat]);
                if (rot.HasValue) g.transform.localRotation = Quaternion.Euler(rot.Value);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                return g;
            }
            switch (toolId)
            {
                case "tool.voltage_detector": P(PrimitiveType.Cylinder, new Vector3(0, 0, 0), new Vector3(0.04f, 0.1f, 0.04f), "groundStripe"); P(PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0), new Vector3(0.015f, 0.03f, 0.015f), "mark"); break;
                case "tool.grounding_rod": P(PrimitiveType.Cylinder, new Vector3(0, 0, 0), new Vector3(0.02f, 0.13f, 0.02f), "ground"); P(PrimitiveType.Cube, new Vector3(0, 0.13f, 0), new Vector3(0.08f, 0.02f, 0.03f), "steel"); break;
                case "tool.caliper": P(PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(0.025f, 0.22f, 0.008f), "steel"); P(PrimitiveType.Cube, new Vector3(0.04f, 0.08f, 0), new Vector3(0.06f, 0.015f, 0.008f), "steel"); P(PrimitiveType.Cube, new Vector3(0.015f, -0.02f, 0.006f), new Vector3(0.04f, 0.03f, 0.006f), "panel"); break;
                case "tool.socket_wrench": P(PrimitiveType.Cube, new Vector3(0, -0.02f, 0), new Vector3(0.02f, 0.2f, 0.015f), "steel"); P(PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0.015f), new Vector3(0.035f, 0.02f, 0.035f), "steel", new Vector3(90, 0, 0)); break;
                case "tool.torque_wrench": P(PrimitiveType.Cube, new Vector3(0, -0.02f, 0), new Vector3(0.025f, 0.24f, 0.02f), "steel"); P(PrimitiveType.Cylinder, new Vector3(0, -0.1f, 0), new Vector3(0.03f, 0.04f, 0.03f), "mark"); P(PrimitiveType.Cube, new Vector3(0, 0.11f, 0), new Vector3(0.04f, 0.03f, 0.03f), "steel"); break;
                case "tool.cleaning_cloth": P(PrimitiveType.Cube, new Vector3(0, 0, 0), new Vector3(0.16f, 0.16f, 0.01f), "paper", new Vector3(0, 0, 8)); break;
                case "tool.marker": P(PrimitiveType.Cylinder, new Vector3(0, 0, 0), new Vector3(0.018f, 0.08f, 0.018f), "mark"); break;
                case "tool.lockout_tag": P(PrimitiveType.Cube, new Vector3(0, -0.02f, 0), new Vector3(0.07f, 0.06f, 0.03f), "mark"); P(PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(0.05f, 0.01f, 0.05f), "steel", new Vector3(90, 0, 0)); P(PrimitiveType.Cube, new Vector3(0, -0.09f, 0), new Vector3(0.06f, 0.07f, 0.004f), "groundStripe"); break;
                case "tool.safety_harness": P(PrimitiveType.Cube, new Vector3(-0.04f, 0, 0), new Vector3(0.02f, 0.22f, 0.01f), "tool"); P(PrimitiveType.Cube, new Vector3(0.04f, 0, 0), new Vector3(0.02f, 0.22f, 0.01f), "tool"); P(PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(0.1f, 0.02f, 0.01f), "tool"); break;
            }
            return root;
        }

        static void SetHome(OrbitCameraController orbit, Bounds focus, Camera cam)
        {
            var so = new SerializedObject(orbit);
            so.FindProperty("homeTarget").vector3Value = focus.center;
            so.FindProperty("homeDistance").floatValue = 1.7f;
            so.FindProperty("homeYaw").floatValue = 200f;
            so.FindProperty("homePitch").floatValue = 40f;
            so.FindProperty("minDistance").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var rot = Quaternion.Euler(40f, 200f, 0f);
            cam.transform.SetPositionAndRotation(focus.center - rot * Vector3.forward * 1.7f, rot);
        }

        // ---------- 預覽圖 ----------
        static void RenderPreviews(StringBuilder sb, Transform panHead, Vector3 c)
        {
            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f); cam.nearClipPlane = 0.01f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            var temps = new List<GameObject>();
            try
            {
                Shot(cam, rt, "rig_slice_scene", c + new Vector3(0, 0.1f, 0.6f), Quaternion.Euler(30, 200, 0), 3.6f, 50f);
                Shot(cam, rt, "rig_slice_shelf", new Vector3(c.x, 0.45f, c.z - 1.5f), Quaternion.Euler(15, 180, 0), 1.9f, 45f);
                var stripL = panHead.Find("CarbonStrip_L");
                Shot(cam, rt, "rig_slice_strip_L", stripL.position, Quaternion.Euler(35, 160, 0), 0.9f, 45f);
                // 螺栓編號：暫時加上數字標籤做俯視圖
                foreach (var side in new[] { "L", "R" })
                    for (int n = 1; n <= 4; n++)
                    {
                        var bolt = panHead.Find($"Bolt_{side}_{n}");
                        var g = new GameObject("tmpNum");
                        g.transform.position = bolt.position + new Vector3(0, 0.02f, 0);
                        g.transform.rotation = Quaternion.Euler(90, 0, 0);
                        g.transform.localScale = Vector3.one * 0.004f;
                        var tm = g.AddComponent<TextMesh>();
                        tm.text = $"{side}{n}"; tm.fontSize = 60; tm.anchor = TextAnchor.MiddleCenter; tm.color = side == "L" ? new Color(1f, 0.5f, 0f) : Color.cyan;
                        temps.Add(g);
                    }
                var headCenter = panHead.GetComponent<Renderer>().bounds.center;
                Shot(cam, rt, "rig_slice_bolts", headCenter, Quaternion.Euler(90, 0, 0), 1.9f, 50f);
                sb.AppendLine("預覽圖：Logs/rig_slice_scene.png、rig_slice_strip_L.png、rig_slice_bolts.png");
            }
            catch (Exception e) { sb.AppendLine("預覽圖失敗：" + e.Message); }
            finally
            {
                foreach (var t in temps) Object.DestroyImmediate(t);
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
            }
        }

        static void Shot(Camera cam, RenderTexture rt, string name, Vector3 target, Quaternion rot, float dist, float fov)
        {
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes($"Logs/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}