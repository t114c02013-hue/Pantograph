using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using PantographSim.Parts.Mechanism;
using PantographSim.XR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using Object = UnityEngine.Object;

namespace PantographSim.Editor.XR
{
    /// <summary>
    /// 切片場景的 VR 部分（SliceSceneBuilder 存檔前呼叫）：XR Origin（Floor 追蹤原點、頭盔攝影機、左右控制器、右手射線）、
    /// 傳送（XRI TeleportationProvider＋6 個固定傳送點）、作業平台扶手與升降面板掛點、每個互動點的 XRInteractableBridge。
    /// XR Origin 存成停用；執行時 XrModeSwitch 偵測到頭盔才啟用（沒有頭盔就是原本的桌面模式）。
    /// </summary>
    [InitializeOnLoad]
    public static class XrRigBuilder
    {
        const string StarterActionsGlob = "XRI Default Input Actions t:InputActionAsset";

        static XrRigBuilder() { SliceSceneBuilder.BeforeSave -= Build; SliceSceneBuilder.BeforeSave += Build; }

        public static void Build(StringBuilder sb)
        {
            var objects = Object.FindObjectsOfType<MonoBehaviour>(true).OfType<ISimObject>().Where(o => !string.IsNullOrEmpty(o.Id))
                .GroupBy(o => o.Id).ToDictionary(g => g.Key, g => ((Component)g.First()).transform);
            Transform T(string id) => objects.TryGetValue(id, out var t) ? t : null;
            var mats = SliceModelBuilder.Materials();
            var lineMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

            // ---- 位置基準：降弓綠框、集電弓升降全程範圍 ----
            var mech = Object.FindObjectOfType<PantographMechanism>();
            float lift = mech.Lift;
            mech.SetLift(0f); var b = SliceSceneBuilder.RendererBounds(mech.gameObject);
            mech.SetLift(1f); b.Encapsulate(SliceSceneBuilder.RendererBounds(mech.gameObject));
            mech.SetLift(lift);
            var green = T("ip.pan_head.lower_target");
            var target = green != null ? green.position : b.center;
            // R1：平台、護欄與升降面板掛點（xr.mount.lift_panel）由 RoofSiteBuilder 建好，這裡不再另建
            var stationSet = R1Data.Stations;
            sb.AppendLine($"按鍵表：{R1Data.InputBindings.rows.Length} 列（{R1Data.InputBindingsPath}）");

            // ---- XR 根物件、XR Origin ----
            var xrRoot = new GameObject("XR");
            var managerGo = new GameObject("XR Interaction Manager");
            managerGo.transform.SetParent(xrRoot.transform, false);
            managerGo.AddComponent<XRInteractionManager>();

            var rigGo = new GameObject("XR Origin (XR Rig)");
            rigGo.transform.SetParent(xrRoot.transform, false);
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(rigGo.transform, false);
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset, false);
            camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 200f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            camGo.AddComponent<AudioListener>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(Action("Head Position", "Vector3", "<XRHMD>/centerEyePosition"));
            tpd.rotationInput = new InputActionProperty(Action("Head Rotation", "Quaternion", "<XRHMD>/centerEyeRotation"));
            tpd.trackingStateInput = new InputActionProperty(Action("Head Tracking State", "Integer", "<XRHMD>/trackingState"));

            var origin = rigGo.AddComponent<XROrigin>();
            origin.Origin = rigGo;
            origin.CameraFloorOffsetObject = offset.gameObject;
            origin.Camera = cam;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;   // B 段 2.3：Floor 追蹤，不加固定身高偏移

            var starter = StarterActions();
            if (starter != null)
            {
                var mgr = rigGo.AddComponent<InputActionManager>();
                mgr.actionAssets = new List<InputActionAsset> { starter };
            }
            sb.AppendLine("VR：XR Origin（Floor）＋頭盔攝影機" + (starter != null ? "，控制器使用 XRI Starter Assets 輸入動作 " + AssetDatabase.GetAssetPath(starter) : "，控制器使用內建輸入動作（找不到 Starter Assets）"));

            var left = Controller("Left Controller", "LeftHand", offset, starter, mats, out var leftCtrl);
            var rightT = Controller("Right Controller", "RightHand", offset, starter, mats, out var rightCtrl);

            // 右手射線（XRI，介面點擊與指物件）
            var ray = rightT.gameObject.AddComponent<XRRayInteractor>();
            ray.maxRaycastDistance = 30f;
            ray.enableUIInteraction = true;
            var line = rightT.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMat; line.widthMultiplier = 0.004f; line.numCornerVertices = 2;
            var visual = rightT.gameObject.AddComponent<XRInteractorLineVisual>();
            visual.lineWidth = 0.004f;
            ray.hideControllerOnSelect = false;   // T2-a：拿著工具時控制器維持顯示
            // T2-a：工具握持點 Attach_Tool（握把前下方，不擋扳機與按鍵）；左手 Poke_Tip（控制器前端 r 0.8 cm）按工具上的撥桿、± 鈕、ZERO
            var toolAnchor = new GameObject("Attach_Tool").transform;
            toolAnchor.SetParent(rightT, false);
            toolAnchor.localPosition = new Vector3(0, -0.01f, 0.06f);
            new GameObject("Attach_Tool").transform.SetParent(left, false);
            var pokeTip = new GameObject("Poke_Tip");
            pokeTip.transform.SetParent(left, false);
            pokeTip.transform.localPosition = new Vector3(0, -0.01f, 0.05f);
            var tipCol = pokeTip.AddComponent<SphereCollider>(); tipCol.radius = 0.008f; tipCol.isTrigger = true;
            // T3：面板把手只有左手 XRDirectInteractor 抓得到（Interaction Layer PanelHandle）；右手射線不抓把手
            int handleMask = InteractionLayerMask.GetMask(XrPanels.HandleLayerName);
            var panelGrab = new GameObject("PanelGrab");
            panelGrab.transform.SetParent(left, false);
            var pgCol = panelGrab.AddComponent<SphereCollider>(); pgCol.radius = 0.06f; pgCol.isTrigger = true;
            panelGrab.AddComponent<XRDirectInteractor>().interactionLayers = handleMask;
            ray.interactionLayers = ~handleMask;

            // 傳送：LocomotionSystem＋TeleportationProvider，左手拋物線
            var loco = rigGo.AddComponent<LocomotionSystem>();
            loco.xrOrigin = origin;
            var provider = rigGo.AddComponent<TeleportationProvider>();
            provider.system = loco;
            var arcGo = new GameObject("TeleportArc");
            arcGo.transform.SetParent(left, false);
            var arc = arcGo.AddComponent<LineRenderer>();
            arc.sharedMaterial = lineMat; arc.widthMultiplier = 0.012f; arc.useWorldSpace = true; arc.enabled = false;

            // R1 站點（StationSet）：x、z 讀資料，y 往下打 Walkable，朝向對 facingTargetId
            var points = BuildTeleportPoints(xrRoot.transform, mats, lineMat, stationSet.stations.Where(s => s != null).Select(s =>
            {
                var anchor = GameObject.Find("StationAnchor_" + s.id);   // RoofSiteBuilder 產生（共用車頂點已移位）
                var pos = anchor != null ? anchor.transform.position : new Vector3(s.positionXZ.x, 0f, s.positionXZ.y);
                if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out var hit, 30f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
                var look = s.facingDirection.sqrMagnitude > 1e-6f ? pos + s.facingDirection : T(s.facingTargetId) is Transform ft ? ft.position : target;
                return (s, pos, look);
            }).ToArray());

            // 淡黑轉場（傳送、切換樓層）：攝影機前 0.08 m 的黑色方塊
            var fadeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fadeQuad.name = "Fader";
            NoCollider(fadeQuad);
            fadeQuad.transform.SetParent(camGo.transform, false);
            fadeQuad.transform.localPosition = new Vector3(0, 0, 0.08f);
            fadeQuad.transform.localScale = Vector3.one * 0.5f;
            fadeQuad.GetComponent<Renderer>().sharedMaterial = lineMat;
            var fader = camGo.AddComponent<XrFader>();
            fader.Setup(fadeQuad.GetComponent<Renderer>(), R1Data.Height.floorChangeFadeSeconds);

            var rig = rigGo.AddComponent<XrRig>();
            rig.Setup(cam, left, rightT, rightT, toolAnchor);
            var teleport = rigGo.AddComponent<XrTeleport>();
            teleport.Setup(origin, provider, left, arc, points, stationSet.startStationId, fader);
            teleport.SetRule(R1Data.Teleport);   // R1.5b b2

            // 教學模式的地面箭頭（指向目前小動作的站點）
            var arrow = new GameObject("StationArrow").transform;
            arrow.SetParent(xrRoot.transform, false);
            NoCollider(SliceModelBuilder.Prim(PrimitiveType.Cube, "Shaft", arrow, new Vector3(0, 0.01f, 0), new Vector3(0.06f, 0.01f, 0.3f), mats["checkPoint"]));
            var head = SliceModelBuilder.Prim(PrimitiveType.Cube, "Head", arrow, new Vector3(0, 0.01f, 0.17f), new Vector3(0.14f, 0.01f, 0.14f), mats["checkPoint"]);
            head.transform.localRotation = Quaternion.Euler(0, 45, 0);
            NoCollider(head);
            arrow.gameObject.SetActive(false);
            rigGo.AddComponent<XrStations>().Setup(teleport, arrow, camGo.transform);
            rigGo.AddComponent<XrToolWidgets>().Setup(toolAnchor, pokeTip.transform, leftCtrl);
            rigGo.AddComponent<XrPanels>().Setup(R1Data.Panels, camGo.transform, teleport);
            // R1.5b b1：天車遙控器（左握把在掛座旁拿起／掛回，右手 Poke_Tip 或射線＋扳機按鍵）
            var rightPoke = new GameObject("Poke_Tip_Right");
            rightPoke.transform.SetParent(rightT, false);
            rightPoke.transform.localPosition = new Vector3(0, -0.01f, 0.05f);
            var pendantT = T("env.crane_pendant"); var dockGo = GameObject.Find("PendantDock");
            if (pendantT != null && dockGo != null) rigGo.AddComponent<XrCranePendant>().Setup(rig, R1Data.Crane, pendantT, dockGo.transform, rightPoke.transform);
            else sb.AppendLine("⚠ 天車遙控器：找不到 env.crane_pendant 或 PendantDock");

            // 試玩修正 第 1 點：沒有平滑移動與重力；移動靠傳送與真實走動，撞邊界由 HeadBoundaryPusher 擋
            var loco6 = R1Data.Locomotion;
            rigGo.AddComponent<HeadBoundaryPusher>().Setup(origin);
            rigGo.AddComponent<XrLocomotion>().Setup(loco6, camGo.transform, rigGo.transform, teleport);
            BuildBoundary(sb);
            sb.AppendLine($"VR：安全網低於地板 {loco6.recoverBelowFloorM} m");
            rigGo.AddComponent<XrViewHeight>().Setup(origin, R1Data.Height, R1Data.Locomotion, fader);
            var coach = rigGo.AddComponent<XrCoach>();
            coach.Setup(rig);
            var player = rigGo.AddComponent<XrPlayer>();
            player.Setup(rig, origin, ray, rightCtrl, teleport, coach);
            var sw = xrRoot.AddComponent<XrModeSwitch>();
            sw.Setup(rigGo, rig, teleport, coach);
            // D30：上鎖掛牌組的隨身腰包 env.waist_pouch（右腰；工具袋在左腰，選單可對調是 R1.5b）。不是站點；抓取與收納的程式是 R1.5b
            var pouch = SliceModelBuilder.Prim(PrimitiveType.Cube, "WaistPouch", rigGo.transform, R1Data.RoofScene.waistPouchLocal, new Vector3(0.12f, 0.14f, 0.06f), mats["panel"]);
            pouch.GetComponent<BoxCollider>().isTrigger = true;
            pouch.AddComponent<PantographSim.Parts.Common.InteractionPointMarker>().Configure("env.waist_pouch", "腰包（上鎖掛牌組）");
            sb.AppendLine($"VR：腰包 env.waist_pouch 在 XR Origin 局部 {R1Data.RoofScene.waistPouchLocal:F2}（右腰）");
            // R1.5b b3：工具袋、腰包跟著頭的 yaw（同一個隨身容器元件）；工具袋可改掛左手腕
            var rd = R1Data.RoofScene;
            var bagT = T("env.tool_bag");
            if (bagT != null)
                rigGo.AddComponent<XrCarryContainer>().Setup(rig, bagT, rd.toolBagHeadOffset, true, rd.carryOpenRange, bagT.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("Label_")).Select(x => x.gameObject).ToArray());
            rigGo.AddComponent<XrCarryContainer>().Setup(rig, pouch.transform, rd.waistPouchHeadOffset, false, rd.carryOpenRange, null);
            // R1.5b b4（D31）：安全帶胸前收納扣環、安全帶工具環（同樣跟著頭的 yaw；位置、大小、握把半徑都在 RoofSceneDef）
            foreach (var (id, name, label, headOffset) in new[] { (BodyRings.ChestRing, "ChestRing", "胸前收納扣環（安全帶掛鉤）", rd.chestRingHeadOffset), (BodyRings.ToolRing, "HarnessToolRing", "安全帶工具環（短接地線）", rd.toolRingHeadOffset) })
            {
                var ring = SliceModelBuilder.Prim(PrimitiveType.Cube, name, rigGo.transform, Vector3.zero, Vector3.one * rd.bodyRingSize, mats["panel"]);
                ring.GetComponent<BoxCollider>().isTrigger = true;
                ring.AddComponent<PantographSim.Parts.Common.InteractionPointMarker>().Configure(id, label);
                rigGo.AddComponent<XrCarryContainer>().Setup(rig, ring.transform, headOffset, false, rd.bodyRingRadius, null);
                sb.AppendLine($"VR：{label} {id}：頭部偏移 {headOffset:F2}、邊長 {rd.bodyRingSize}、握把半徑 {rd.bodyRingRadius}");
            }
            player.SetBodyRingRadius(rd.bodyRingRadius);
            rigGo.SetActive(false);
            BuildToCabMarker(xrRoot.transform, mats, lineMat, sb);
            sb.AppendLine("VR：站點 " + string.Join("、", points.Select(p => p.Label + (string.IsNullOrEmpty(p.LockKey) ? "" : "（車頂開放才解鎖）"))));

            // StationLayoutCheck（B 段 3.5）：不合格就建置失敗
            mech.SetLift(0f);
            // 第21項（增補 7）：StationSet.wrenchSweepTools 逐支讀 ToolDefinition（handleLengthM、socketIds）＋套筒目錄
            var wrench = new StationLayoutCheck.WrenchSweepInput { tools = StepJsonImporter.LoadTools().Append(ToolSocketSetup.LargeWrench).Where(t => t != null).ToList(), catalog = ToolSocketSetup.Catalog };
            var check = StationLayoutCheck.CheckScene(stationSet, R1Data.RoofScene, points, T, PanGeometry.SweptFootprints(mech), wrench,
                new StationLayoutCheck.InspectInput { rigging = R1Data.Rigging, clearance = R1Data.Crane.inspectClearanceM });   // v3.10 查看站間距
            mech.SetLift(lift);
            // 增補 4 第 9 點：摘要「N 過／M 待定／K 不合格」，有待定或不合格就不寫「全過」
            string summary = $"結果：{points.Length} 站，{check.Summary}" + (check.Ok && check.pending.Count == 0 ? "（全過）" : "");
            System.IO.File.WriteAllText("Logs/StationLayoutCheck.txt", summary + "\n" + check + "\n");
            sb.AppendLine("站點檢查：" + summary + "\n" + check);
            if (!check.Ok) throw new System.Exception("StationLayoutCheck 不合格：\n" + string.Join("\n", check.bad));

            // ---- 每個互動點加 XRInteractableBridge（只收自己的 Collider）----
            int bridges = 0;
            foreach (var inter in Object.FindObjectsOfType<MonoBehaviour>(true).OfType<IInteractable>().Cast<MonoBehaviour>().ToList())
            {
                if (inter.GetComponent<XRInteractableBridge>() != null) continue;
                var own = inter.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponentInParent<IInteractable>() as MonoBehaviour == inter).ToList();
                if (own.Count == 0) continue;
                var bridge = inter.gameObject.AddComponent<XRInteractableBridge>();
                bridge.Configure(((IInteractable)inter).Id, own);
                bridges++;
            }
            sb.AppendLine($"VR：{bridges} 個互動點加上 XRInteractableBridge");
        }

        /// <summary>a6 第一節 4：「去駕駛室」地面光圈 mk.platform.to_cab（外觀沿用 TP_Marker、藍色）。標記，不是站點；按鈕和傳送是 R1.5b。</summary>
        /// <summary>試玩修正 第 1 點：頭的活動範圍由場景的 Walkable 產生（外緣往內縮 BoundaryDef.inset）。跨板只算在 open 那一組。</summary>
        public static void BuildBoundary(StringBuilder sb)
        {
            var def = R1Data.Boundary;
            var walk = Object.FindObjectsOfType<BoxCollider>(true).Where(c => c.gameObject.layer == LayerMask.NameToLayer(SceneLayers.Walkable)).ToList();
            def.open = BoundaryLogic.Erode(walk.Select(BoundaryLogic.WalkRect).ToList(), def.inset);
            def.closed = BoundaryLogic.Erode(walk.Where(c => c.name != "Walkable_Bridge").Select(BoundaryLogic.WalkRect).ToList(), def.inset);
            EditorUtility.SetDirty(def);
            sb.AppendLine($"VR：頭的活動範圍 {walk.Count} 塊 Walkable → 內縮 {def.inset} m：閘門開 {def.open.Length} 塊、關 {def.closed.Length} 塊");
        }

        static void BuildToCabMarker(Transform parent, Dictionary<string, Material> mats, Material lineMat, StringBuilder sb)
        {
            var d = R1Data.RoofScene;
            var pos = new Vector3(d.toCabMarkerXZ.x, d.platformFloorY, d.toCabMarkerXZ.y);
            if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out var hit, 30f, SceneLayers.WalkableMask, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoofSiteBuilder.PrefabFolder + "/TP_Marker.prefab");
            var mk = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            mk.name = "mk.platform.to_cab";
            mk.transform.position = pos;
            mk.transform.localScale = Vector3.one * (d.toCabMarkerRadius / 0.25f);
            mk.GetComponentInChildren<Text>(true).text = "去駕駛室";
            var disc = mk.transform.Find("Visual/Disc").GetComponent<Renderer>();
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", XrTeleportPoint.ColorOf(XrTeleportPoint.Look.Available, 0f)); disc.SetPropertyBlock(block);
            sb.AppendLine($"平台：去駕駛室光圈 mk.platform.to_cab {pos:F3}（r {d.toCabMarkerRadius}）");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static void NoCollider(GameObject g) { var c = g.GetComponent<Collider>(); if (c != null) Object.DestroyImmediate(c); }

        static InputAction Action(string name, string type, params string[] paths)
        {
            var a = new InputAction(name, type == "Button" ? InputActionType.Button : InputActionType.Value, expectedControlType: type);
            foreach (var p in paths) a.AddBinding(p);
            return a;
        }

        static InputActionAsset StarterActions()
        {
            var guid = AssetDatabase.FindAssets(StarterActionsGlob).FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }

        /// <summary>Starter Assets 的動作參照（例 "XRI RightHand/Position"）；找不到就用內建動作。</summary>
        static InputActionProperty Prop(InputActionAsset asset, string mapAction, InputAction fallback)
        {
            if (asset != null)
            {
                var r = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)).OfType<InputActionReference>()
                    .FirstOrDefault(x => x.action != null && $"{x.action.actionMap.name}/{x.action.name}" == mapAction);
                if (r != null) return new InputActionProperty(r);
            }
            return new InputActionProperty(fallback);
        }

        static Transform Controller(string name, string hand, Transform parent, InputActionAsset starter, Dictionary<string, Material> mats, out ActionBasedController ctrl)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(hand == "LeftHand" ? -0.2f : 0.2f, 1.1f, 0.3f);
            ctrl = go.AddComponent<ActionBasedController>();
            string map = hand == "LeftHand" ? "XRI LeftHand" : "XRI RightHand";
            string dev = $"<XRController>{{{hand}}}";
            ctrl.positionAction = Prop(starter, map + "/Position", Action("Position", "Vector3", dev + "/devicePosition"));
            ctrl.rotationAction = Prop(starter, map + "/Rotation", Action("Rotation", "Quaternion", dev + "/deviceRotation"));
            ctrl.trackingStateAction = Prop(starter, map + "/Tracking State", Action("Tracking State", "Integer", dev + "/trackingState"));
            ctrl.selectAction = Prop(starter, map + " Interaction/Select", Action("Select", "Button", dev + "/{GripButton}"));
            ctrl.selectActionValue = Prop(starter, map + " Interaction/Select Value", Action("Select Value", "Axis", dev + "/{Grip}"));
            ctrl.activateAction = Prop(starter, map + " Interaction/Activate", Action("Activate", "Button", dev + "/{TriggerButton}"));
            ctrl.activateActionValue = Prop(starter, map + " Interaction/Activate Value", Action("Activate Value", "Axis", dev + "/{Trigger}"));
            ctrl.uiPressAction = Prop(starter, map + " Interaction/UI Press", Action("UI Press", "Button", dev + "/{TriggerButton}"));
            ctrl.uiPressActionValue = Prop(starter, map + " Interaction/UI Press Value", Action("UI Press Value", "Axis", dev + "/{Trigger}"));
            ctrl.hapticDeviceAction = Prop(starter, map + "/Haptic Device", new InputAction("Haptic Device", InputActionType.PassThrough, dev + "/{Haptic}"));
            // 簡易控制器外觀（沒有 Collider，不擋射線）
            var body = SliceModelBuilder.Prim(PrimitiveType.Cube, "Model", go.transform, new Vector3(0, -0.015f, -0.03f), new Vector3(0.035f, 0.03f, 0.11f), mats["panel"]);
            NoCollider(body);
            var ring = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "Ring", go.transform, new Vector3(0, 0.005f, 0.01f), new Vector3(0.06f, 0.006f, 0.06f), mats[hand == "LeftHand" ? "measure" : "tool"]);
            NoCollider(ring);
            return go.transform;
        }

        static XrTeleportPoint[] BuildTeleportPoints(Transform parent, Dictionary<string, Material> mats, Material lineMat, (StationDef s, Vector3 pos, Vector3 lookAt)[] specs)
        {
            var root = new GameObject("TeleportPoints").transform;
            root.SetParent(parent, false);
            var markerPrefab = BuildMarkerPrefab(mats, lineMat);
            var list = new List<XrTeleportPoint>();
            foreach (var (s, pos, lookAt) in specs)
            {
                string id = s.id, label = s.label, key = s.unlockKey;
                var go = new GameObject("TP_" + label);
                go.transform.SetParent(root, false);
                var dir = Flat(lookAt - pos);
                go.transform.SetPositionAndRotation(pos, dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity);
                var marker = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab, go.transform);
                marker.name = "Marker";
                var disc = marker.transform.Find("Visual/Disc").GetComponent<Renderer>();
                var text = marker.GetComponentInChildren<Text>(true);
                text.text = label;
                var lockIcon = marker.transform.Find("Visual/Lock").gameObject;
                marker.SetActive(false);
                var p = go.AddComponent<XrTeleportPoint>();
                bool shared = !string.IsNullOrEmpty(s.sharedWith);   // 共用同一點：地上只留另一站的標記
                p.Configure(id, label, key, shared ? null : marker, disc, text);
                p.ConfigureStation(s.floor.ToString(), s.lockedMessage, s.sharedWith, shared ? null : lockIcon);
                if (shared) Object.DestroyImmediate(marker);
                list.Add(p);
            }
            return list.ToArray();
        }

        /// <summary>TP_Marker（B 段 2.2）：r＝0.25 m 圓盤＋腳印方向箭頭，貼在地面上 0.005 m；鎖頭與名稱。存成 prefab，每站一個實例。</summary>
        static GameObject BuildMarkerPrefab(Dictionary<string, Material> mats, Material lineMat)
        {
            var root = new GameObject("TP_Marker");
            Transform Kid(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }
            var visual = Kid(root.transform, "Visual"); Kid(root.transform, "Walkable"); Kid(root.transform, "Collision");
            var anchors = Kid(root.transform, "Anchors");
            Kid(anchors, "Anchor_Feet");
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disc.name = "Disc";
            NoCollider(disc);
            disc.transform.SetParent(visual, false);
            disc.transform.localPosition = new Vector3(0, 0.005f, 0);
            disc.transform.localRotation = Quaternion.Euler(90, 0, 0);
            disc.transform.localScale = Vector3.one * 0.5f;
            disc.GetComponent<Renderer>().sharedMaterial = lineMat;
            foreach (float x in new[] { -0.06f, 0.06f })   // 腳印（朝本地 +Z＝面向目標）
                NoCollider(SliceModelBuilder.Prim(PrimitiveType.Cube, "Footprint", visual, new Vector3(x, 0.007f, 0.02f), new Vector3(0.07f, 0.002f, 0.16f), mats["panel"]));
            var tip = SliceModelBuilder.Prim(PrimitiveType.Cube, "Arrow", visual, new Vector3(0, 0.007f, 0.17f), new Vector3(0.07f, 0.002f, 0.07f), mats["panel"]);
            tip.transform.localRotation = Quaternion.Euler(0, 45, 0);
            NoCollider(tip);
            var lockIcon = SliceModelBuilder.Prim(PrimitiveType.Cube, "Lock", visual, new Vector3(0, 0.12f, 0), new Vector3(0.08f, 0.1f, 0.04f), mats["steel"]);
            NoCollider(lockIcon);
            lockIcon.SetActive(false);
            var canvasGo = new GameObject("Label", typeof(RectTransform));
            canvasGo.transform.SetParent(visual, false);
            canvasGo.transform.localPosition = new Vector3(0, 0.45f, 0);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)canvasGo.transform;
            crt.sizeDelta = new Vector2(600, 200);
            crt.localScale = Vector3.one * 0.0015f;
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.Load<Font>("NotoSansTC"); text.fontSize = 56; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false;
            textGo.AddComponent<Outline>().effectColor = Color.black;
            var trt = (RectTransform)textGo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            System.IO.Directory.CreateDirectory(RoofSiteBuilder.PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoofSiteBuilder.PrefabFolder + "/TP_Marker.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
