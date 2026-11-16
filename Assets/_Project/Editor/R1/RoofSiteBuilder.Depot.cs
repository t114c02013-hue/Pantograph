using System.Collections.Generic;
using System.Linq;
using System.Text;
using PantographSim.Core;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantographSim.Editor
{
    /// <summary>
    /// terry 2026-11-13：捷運／輕軌機廠的場域擺設（只有外觀，不加站點、不影響判定；碰撞只擋不能進去的檢查坑）。
    /// 依新北捷運局設計論文與其他車廠的公開資料類推；牌面用語、尺寸多為推估（見 Docs 報告）。不放任何公司標誌、不做架空線。
    /// </summary>
    public static partial class RoofSiteBuilder
    {
        public const string FontPath = "Assets/_Project/Content/Fonts/Resources/NotoSansTC.ttf";

        /// <summary>柱式檢查機坑的範圍（推估）：|X| ≤ PitHalfWidth、Z 在 ±PitHalfLength；軌內深 1.6 m、軌外深 1.05 m。</summary>
        const float PitHalfWidth = 1.75f, PitHalfLength = 21f, PitDeep = 1.6f, PitSide = 1.05f, PitInner = 0.62f;

        /// <summary>地面（廠房底板）挖掉檢查坑那一塊。</summary>
        static void GroundWithPit(Transform parent, float x0, float x1, float z0, float z1, float gy, Material m)
        {
            void G(float ax, float bx, float az, float bz) { if (bx - ax > 0.01f && bz - az > 0.01f) Box("Ground", parent, new Vector3((ax + bx) / 2f, gy - 0.05f, (az + bz) / 2f), new Vector3(bx - ax, 0.1f, bz - az), m, collider: false); }
            G(x0, -PitHalfWidth, z0, z1);
            G(PitHalfWidth, x1, z0, z1);
            G(-PitHalfWidth, PitHalfWidth, z0, -PitHalfLength);
            G(-PitHalfWidth, PitHalfWidth, PitHalfLength, z1);
        }

        static TextMesh Label(string name, Transform parent, string text, Vector3 pos, Quaternion rot, float charSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var tm = go.AddComponent<TextMesh>();
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font != null) { tm.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            tm.text = text; tm.fontSize = 64; tm.characterSize = charSize; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = color;
            return tm;
        }

        /// <summary>牌子：底板＋字（字貼在 +normal 那面）。</summary>
        static Transform Sign(string name, Transform parent, string text, Vector3 center, Vector3 normal, Vector2 size, Material board, Color ink, float charSize = 0.012f)
        {
            var root = Child(parent, name);
            var rot = Quaternion.LookRotation(-normal, Vector3.up);   // TextMesh 正面朝 −Z
            var b = Box("Board", root, center, Vector3.one, board, collider: false);
            b.transform.rotation = rot; b.transform.localScale = new Vector3(size.x, size.y, 0.02f);
            Label("Text", root, text, center + normal * 0.012f, rot, charSize, ink);
            return root;
        }

        /// <summary>鋼格板貼圖（程式產生 64×64 格子，存成資產；已存在就沿用）。</summary>
        static Texture2D GratingTexture()
        {
            const string path = "Assets/_Project/Content/Textures/TX_Grating.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) return t;
            var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    bool bar = x % 16 < 3 || y % 8 < 2;
                    float v = bar ? 0.62f : 0.18f;
                    tex.SetPixel(x, y, new Color(v, v, v * 1.03f));
                }
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void BuildDepot(Result res, RoofSceneDef d, Dictionary<string, Material> mats, Material mWarn, StringBuilder sb)
        {
            var depot = Child(res.env, "ENV_Depot");
            float gy = d.groundY, py = d.platformFloorY;
            var mBlack = Mat("hazardBlack", new Color(0.08f, 0.08f, 0.08f));
            var mConcrete = Mat("pitConcrete", new Color(0.52f, 0.53f, 0.54f));
            var mWhite = Mat("signWhite", new Color(0.95f, 0.95f, 0.93f));
            var mRed = Mat("signRed", new Color(0.80f, 0.12f, 0.10f));
            var mGreen = Mat("signGreen", new Color(0.15f, 0.62f, 0.30f));
            var mBlue = Mat("signBlue", new Color(0.12f, 0.32f, 0.62f));
            var mSteel = mats["steel"];
            var mLight = Mat("lightFixture", new Color(1f, 1f, 0.96f), unlit: true);
            var mGlow = Mat("statusGreen", new Color(0.3f, 1f, 0.45f), unlit: true);
            var mCab = Mat("cabGlass", new Color(0.12f, 0.18f, 0.24f));

            // ---- 柱式檢查機坑（只當背景；碰撞擋住不能進去） ----
            var pit = Child(depot, "InspectionPit");
            float pz0 = -PitHalfLength, pz1 = PitHalfLength, pl = pz1 - pz0;
            Box("PitFloor_Inner", pit, new Vector3(0, gy - PitDeep, 0), new Vector3(2 * PitInner, 0.1f, pl), mConcrete, collider: false);
            foreach (float s in new[] { -1f, 1f })
            {
                float xs = s * (PitInner + PitHalfWidth) / 2f;
                Box("PitFloor_Side", pit, new Vector3(xs, gy - PitSide, 0), new Vector3(PitHalfWidth - PitInner, 0.1f, pl), mConcrete, collider: false);
                Box("PitWall_Inner", pit, new Vector3(s * PitInner, gy - (PitDeep + PitSide) / 2f, 0), new Vector3(0.1f, PitDeep - PitSide, pl), mConcrete, collider: false);
                Box("PitWall_Outer", pit, new Vector3(s * (PitHalfWidth + 0.05f), gy - PitSide / 2f, 0), new Vector3(0.1f, PitSide, pl), mConcrete, collider: false);
                Box("PitLightStrip", pit, new Vector3(s * (PitHalfWidth - 0.02f), gy - 0.35f, 0), new Vector3(0.03f, 0.04f, pl - 1f), mLight, collider: false);
                Box("PitLightStrip_Low", pit, new Vector3(s * (PitInner - 0.02f), gy - PitSide - 0.25f, 0), new Vector3(0.03f, 0.04f, pl - 1f), mLight, collider: false);
                HazardStripe("PitEdge", pit, new Vector3(s * (PitHalfWidth + 0.08f), gy, pz0), new Vector3(s * (PitHalfWidth + 0.08f), gy, pz1), gy + 0.004f, 0.12f, mWarn, mBlack);
                // 軌道架在柱上：每 1.2 m 一根
                for (float z = pz0 + 0.6f; z < pz1; z += 1.2f)
                    Box("RailColumn", pit, new Vector3(s * 0.7175f, gy - PitSide / 2f, z), new Vector3(0.18f, PitSide, 0.18f), mConcrete, collider: false);
                // 坑邊護欄：+X 整段，−X 避開作業平台那一段
                if (s > 0) GuardRail("PitRail", pit, new Vector3(PitHalfWidth + 0.2f, gy + 0.55f, 0), new Vector3(0.04f, 1.1f, pl), mats["steel"]);
                else foreach (var (a, b) in new[] { (pz0, d.platformZMin - 0.5f), (d.platformZMax + 4.5f, pz1) })
                        GuardRail("PitRail", pit, new Vector3(-PitHalfWidth - 0.2f, gy + 0.55f, (a + b) / 2f), new Vector3(0.04f, 1.1f, b - a), mats["steel"]);
                // 擋住不能走進坑裡（傳送也到不了：沒有 Walkable）
                Col("PitBlocker", pit, new Vector3(s > 0 ? PitHalfWidth : -PitHalfWidth - 0.05f, gy, pz0), new Vector3(s > 0 ? PitHalfWidth + 0.05f : -PitHalfWidth, gy + 1.1f, pz1), SceneLayers.LocomotionBlocker);
            }
            foreach (float e in new[] { pz0, pz1 })   // terry 2026-11-13：坑的兩端也畫黃黑邊線（坑邊一圈都有明顯邊線）
                HazardStripe("PitEdge_End", pit, new Vector3(-PitHalfWidth - 0.08f, gy, e + Mathf.Sign(e) * 0.08f), new Vector3(PitHalfWidth + 0.08f, gy, e + Mathf.Sign(e) * 0.08f), gy + 0.004f, 0.12f, mWarn, mBlack);
            foreach (float e in new[] { pz0, pz1 })   // 兩端階梯
                for (int i = 0; i < 6; i++)
                {
                    float h = PitSide * (i + 1) / 7f;
                    Box("PitStep", pit, new Vector3(-(PitInner + PitHalfWidth) / 2f, gy - h, e - Mathf.Sign(e) * (0.15f + 0.28f * i)), new Vector3(PitHalfWidth - PitInner - 0.1f, 0.05f, 0.28f), i == 0 ? mWarn : mConcrete, collider: false);
                }

            // ---- 地面標線：黃色人行通道 1.2 m（兩條邊線）、柱腳黃黑斜紋 ----
            var marks = Child(depot, "FloorMarkings");
            foreach (var (cx, z0, z1) in new[] { (-4.4f, -45f, 45f), (4.6f, -45f, 45f) })
                foreach (float off in new[] { -0.6f, 0.6f })
                    Box("WalkwayEdge", marks, new Vector3(cx + off, gy + 0.004f, (z0 + z1) / 2f), new Vector3(0.08f, 0.004f, z1 - z0), mWarn, collider: false);
            for (float z = -50f; z <= 60.01f; z += 6f)
                foreach (float x in new[] { -17.7f + 0.3f, 7.7f - 0.3f })
                {
                    var collar = Child(marks, "ColumnCollar");
                    for (int k = 0; k < 4; k++)
                        Box("Band", collar, new Vector3(x, gy + 0.125f * k + 0.0625f, z), new Vector3(0.46f, 0.125f, 0.46f), k % 2 == 0 ? mWarn : mBlack, collider: false);
                }

            // ---- 廠房天花板燈具（長條燈） ----
            var lights = Child(depot, "CeilingLights");
            for (float z = -45f; z <= 55f; z += 6f)
                foreach (float x in new[] { -12f, -4f, 3.5f })
                    Box("Fixture", lights, new Vector3(x, d.shedTrussY - 0.35f, z + 3f), new Vector3(0.25f, 0.06f, 2.4f), mLight, collider: false);

            // ---- 隔離管制點（上平台樓梯頂；牌面用語推估） ----
            var iso = Child(depot, "IsolationControlPoint");
            float sx = d.PlatformOuterX + 0.12f, sz = d.platformZMax - 0.35f;
            var n = Vector3.right;
            var rotText = Quaternion.LookRotation(-n, Vector3.up);   // TextMesh 正面朝 −Z
            Transform N(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Renderer R(GameObject root, string name) => root.GetComponentsInChildren<Renderer>(true).First(r => r.name == name);
            // RA-29 A：建模師 A 的燈號 IsolationStatusLight 站在原柱子位置（取代柱子、燈箱、燈、文字牌；原點在立柱底板底面中心，正面局部 −Z 朝 +X 平台）
            var light = Fbx("ENV_IsolationStatusLight", null, iso, new Vector3(sx, py, sz), yaw: -90f, rename: "IsolationStatusLight");
            var statusBoard = R(light, "StatusText_Board");
            var statusText = Label("Text", N(light, "StatusText"), "隔離未完成　禁止登車頂", N(light, "StatusText").position + n * 0.003f, rotText, 0.005f, Color.white);
            // 掛牌板 LotoBoard：掛在旁邊平台護欄外側（往 −Z 0.7 m，板底離地 1.0 m），4 張掛牌用 Hook_Tag_1～4，5、6 留空（備用位）
            var board = Fbx("ENV_LotoBoard", null, iso, new Vector3(d.PlatformOuterX - 0.03f, py + 1.0f, sz - 0.7f), yaw: -90f, rename: "LotoBoard");
            Label("Text", N(board, "Label_Title"), "隔離確認", N(board, "Label_Title").position + n * 0.003f, rotText, 0.006f, Color.white);
            string[] tags = { "降弓\n確認", "外接電源\n切離・上鎖", "儲能隔離\n上鎖", "驗電・接地\n完成" };   // 資深鐵道工程師 2026-11-13（掛牌窄，分兩行）
            for (int i = 0; i < 6; i++)
            {
                var sil = R(board, "TagSilhouette_" + (i + 1));
                if (i >= tags.Length) { sil.enabled = false; continue; }   // 5、6 號掛勾留空
                if (i % 2 == 0) sil.sharedMaterial = mWarn;
                Label("Tag_" + (i + 1), sil.transform, "■ " + tags[i], sil.bounds.center + n * 0.003f, rotText, 0.0016f, Color.black);
            }
            for (int i = 0; i < (d.lotoSlotNames ?? new string[0]).Length && i < 4; i++)   // RA-30 第 2 點：名牌 0.07 × 0.03 寫簡稱（2.5 cm 放不下，約 1.5 cm），5、6 留空
            {
                var slot = N(board, "Label_Slot_" + (i + 1));
                Label("Text", slot, d.lotoSlotNames[i], slot.position + n * 0.003f, rotText, d.lotoSlotCharSize, Color.black);
            }
            Sign("RoofWorkerCount", iso, "車頂作業人數　1", new Vector3(sx + 0.06f, py + 0.92f, sz), n, new Vector2(0.5f, 0.14f), mWhite, Color.black, 0.009f);   // 燈號立柱 0.9 m
            // 燈號只讀車頂開放鍵 roof.open（隔離四格都完成＝SI_04、SI_05、SI_06 d 完成；WC_02 b 之後關閉），預設紅燈；燈罩換材質，點光源（LampLight）掛在燈罩上
            var mRedGlow = Mat("statusRed", new Color(1f, 0.25f, 0.2f), unlit: true);
            var lampG = R(light, "StatusLamp_Green_Lens"); var lampR = R(light, "StatusLamp_Red_Lens");
            lampG.sharedMaterial = mBlack; lampR.sharedMaterial = mRedGlow; statusBoard.sharedMaterial = mRed;
            iso.gameObject.AddComponent<PantographSim.Worksite.IsolationStatusLight>().Setup(lampG, lampR, statusText, statusBoard, mGlow, mBlack, mGreen, mRed, mRedGlow);
            // 自動回關矮門（樓梯口）與驗電棒架
            var gateC = new Vector3((d.PlatformOuterX + d.PlatformInnerX) / 2f, py, d.platformZMax + 0.08f);
            Box("SelfClosingGate_Bar", iso, gateC + new Vector3(0, 0.85f, 0), new Vector3(0.85f, 0.05f, 0.04f), mWarn, collider: false);
            Box("SelfClosingGate_Bar", iso, gateC + new Vector3(0, 0.45f, 0), new Vector3(0.85f, 0.05f, 0.04f), mWarn, collider: false);
            Box("DetectorRack", iso, new Vector3(sx + 0.06f, py + 0.5f, sz - 0.45f), new Vector3(0.1f, 0.9f, 0.18f), mRed, collider: false);

            // ---- 外接直流電源箱（柱邊）＋收好的電纜（外觀） ----
            var power = Child(depot, "ExternalPowerBox");
            Box("Cabinet", power, new Vector3(7.2f, gy + 0.8f, -6.5f), new Vector3(0.4f, 1.6f, 0.9f), mWhite, collider: false);
            Sign("Label", power, "外接直流電源　750 V", new Vector3(6.99f, gy + 1.35f, -6.5f), Vector3.left, new Vector2(0.7f, 0.12f), mWarn, Color.black, 0.008f);
            for (int k = 0; k < 3; k++)
                SliceModelBuilder.Prim(PrimitiveType.Cylinder, "CableCoil", power, Vector3.zero, Vector3.one, mBlack).transform.SetPositionAndRotation(new Vector3(6.95f, gy + 0.6f + k * 0.06f, -5.7f), Quaternion.Euler(0, 0, 90));
            foreach (Transform t in power) if (t.name == "CableCoil") { t.localScale = new Vector3(0.5f, 0.03f, 0.5f); Object.DestroyImmediate(t.GetComponent<Collider>()); }
            Box("ClearanceFrame", power, new Vector3(6.4f, gy + 0.004f, -6.5f), new Vector3(1.0f, 0.004f, 1.2f), mRed, collider: false);

            // ---- 股道編號吊牌、作業中禁止移動牌＋輪擋、止衝擋 ----
            var track = Child(depot, "TrackSigns");
            foreach (float z in new[] { -24f, 24f })
                Sign("TrackNumber", track, "第 2 股道", new Vector3(0f, d.shedTrussY - 1.6f, z), z < 0 ? Vector3.back : Vector3.forward, new Vector2(1.2f, 0.45f), mWhite, Color.black, 0.03f);
            float trainEnd = d.carLength / 2f;
            foreach (float s in new[] { -1f, 1f })
            {
                var c = new Vector3(0f, gy, s * (trainEnd + 1.2f));
                Box("StandLeg", track, c + new Vector3(0, 0.45f, 0), new Vector3(0.05f, 0.9f, 0.05f), mSteel, collider: false);
                Sign("DoNotMove", track, "作業中\n禁止移動車輛", c + new Vector3(0, 1.15f, 0), new Vector3(0, 0, s), new Vector2(0.7f, 0.5f), mRed, Color.white, 0.02f);
                foreach (float rx in new[] { -0.7175f, 0.7175f })
                    Box("WheelChock", track, new Vector3(rx, gy + 0.22f, s * (trainEnd - 2.6f - 0.9f - 0.4f)), new Vector3(0.12f, 0.12f, 0.2f), mWarn, collider: false);
            }
            var stop = Child(track, "BufferStop");
            Box("Frame", stop, new Vector3(0, gy + 0.6f, -46f), new Vector3(2.2f, 1.2f, 0.4f), mWarn, collider: false);
            for (int k = 0; k < 5; k++) Box("Stripe", stop, new Vector3(-0.9f + 0.45f * k, gy + 0.6f, -45.79f), new Vector3(0.18f, 1.2f, 0.02f), mBlack, collider: false);

            // ---- 維修區靠牆一排器材（適中、走道淨空；簡單造型，常近看的之後做細緻版） ----
            var wall = Child(depot, "WallEquipment");
            float wx = -16.6f;
            void Cart(string name, float z, Material top)
            {
                var r = Child(wall, name);
                Box("Top", r, new Vector3(wx, gy + 0.9f, z), new Vector3(0.6f, 0.05f, 1.0f), top, collider: false);
                Box("Shelf", r, new Vector3(wx, gy + 0.35f, z), new Vector3(0.6f, 0.04f, 1.0f), mSteel, collider: false);
                foreach (float a in new[] { -1f, 1f }) foreach (float b in new[] { -1f, 1f })
                        Box("Leg", r, new Vector3(wx + a * 0.27f, gy + 0.45f, z + b * 0.47f), new Vector3(0.04f, 0.9f, 0.04f), mSteel, collider: false);
                Box("Drawers", r, new Vector3(wx, gy + 0.65f, z), new Vector3(0.55f, 0.4f, 0.9f), mRed, collider: false);
            }
            Cart("ToolCart_1", -9f, mSteel);
            Cart("ToolCart_2", -7.5f, mSteel);
            var rack = Child(wall, "CarbonStripRack");   // 新碳滑板零件架
            for (int k = 0; k < 4; k++) Box("Shelf", rack, new Vector3(wx, gy + 0.3f + 0.45f * k, -4.5f), new Vector3(0.5f, 0.03f, 1.6f), mSteel, collider: false);
            foreach (float b in new[] { -0.78f, 0.78f }) Box("Upright", rack, new Vector3(wx, gy + 0.9f, -4.5f + b), new Vector3(0.05f, 1.8f, 0.05f), mBlue, collider: false);
            for (int k = 0; k < 3; k++) Box("NewStrip", rack, new Vector3(wx, gy + 0.78f + 0.45f * (k % 2), -4.5f - 0.3f + 0.3f * k), new Vector3(0.12f, 0.03f, 1.1f), mBlack, collider: false);
            Sign("RackLabel", rack, "新品碳滑板", new Vector3(wx + 0.27f, gy + 1.95f, -4.5f), Vector3.right, new Vector2(0.6f, 0.15f), mWhite, Color.black, 0.01f);
            foreach (var (z, col, label) in new[] { (-1.5f, mWarn, "金屬廢料"), (-0.8f, mBlue, "一般廢棄物"), (-0.1f, mRed, "油污廢料") })
            {
                SliceModelBuilder.Prim(PrimitiveType.Cylinder, "WasteBin", wall, new Vector3(wx, gy + 0.45f, z), new Vector3(0.5f, 0.45f, 0.5f), col);
                Sign("BinLabel", wall, label, new Vector3(wx + 0.26f, gy + 0.6f, z), Vector3.right, new Vector2(0.4f, 0.1f), mWhite, Color.black, 0.007f);
            }
            var reel = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "AirHoseReel", wall, Vector3.zero, Vector3.one, mWarn);
            reel.transform.SetPositionAndRotation(new Vector3(-17.35f, gy + 1.6f, 1.2f), Quaternion.Euler(0, 0, 90)); reel.transform.localScale = new Vector3(0.45f, 0.1f, 0.45f);
            Box("SocketBox", wall, new Vector3(-17.38f, gy + 1.2f, 2.2f), new Vector3(0.15f, 0.4f, 0.5f), mBlue, collider: false);
            Sign("SocketLabel", wall, "插座箱　AC 220 V", new Vector3(-17.29f, gy + 1.5f, 2.2f), Vector3.right, new Vector2(0.5f, 0.08f), mWhite, Color.black, 0.006f);
            foreach (float z in new[] { 3.4f, -10.5f })
            {
                SliceModelBuilder.Prim(PrimitiveType.Cylinder, "FireExtinguisher", wall, new Vector3(-17.3f, gy + 0.35f, z), new Vector3(0.18f, 0.3f, 0.18f), mRed);
                Box("ClearanceFrame_Red", wall, new Vector3(-16.9f, gy + 0.004f, z), new Vector3(0.8f, 0.004f, 0.8f), mRed, collider: false);
                Box("ClearanceFrame_White", wall, new Vector3(-16.9f, gy + 0.005f, z), new Vector3(0.6f, 0.004f, 0.6f), mWhite, collider: false);
                Sign("FireLabel", wall, "滅火器", new Vector3(-17.29f, gy + 1.2f, z), Vector3.right, new Vector2(0.35f, 0.12f), mRed, Color.white, 0.009f);
            }
            Sign("SafetyBoard", wall, "安全第一\n確認隔離　確認驗電\n確認接地　確認人數", new Vector3(-17.75f, gy + 1.8f, 5.2f), Vector3.right, new Vector2(1.6f, 1.0f), mGreen, Color.white, 0.022f);
            var helmets = Child(wall, "HelmetHarnessRack");
            Box("Board", helmets, new Vector3(-17.75f, gy + 1.6f, 7.2f), new Vector3(0.04f, 0.9f, 1.4f), mSteel, collider: false);
            for (int k = 0; k < 4; k++)
            {
                SliceModelBuilder.Prim(PrimitiveType.Sphere, "Helmet", helmets, new Vector3(-17.6f, gy + 1.85f, 6.7f + 0.33f * k), new Vector3(0.24f, 0.16f, 0.26f), k % 2 == 0 ? mWarn : mWhite);
                Box("Harness", helmets, new Vector3(-17.66f, gy + 1.35f, 6.7f + 0.33f * k), new Vector3(0.05f, 0.45f, 0.18f), mBlack, collider: false);
            }
            Sign("RackLabel", helmets, "安全帽　安全帶", new Vector3(-17.72f, gy + 2.15f, 7.2f), Vector3.right, new Vector2(0.7f, 0.12f), mWhite, Color.black, 0.008f);
            // 背景：轉向架推車（+X 側）
            var bc = Child(depot, "BogieTrolley");
            Box("Trolley", bc, new Vector3(5.6f, gy + 0.25f, 12f), new Vector3(1.6f, 0.15f, 2.6f), mWarn, collider: false);
            Box("BogieFrame", bc, new Vector3(5.6f, gy + 0.65f, 12f), new Vector3(1.5f, 0.3f, 1.9f), mBlack, collider: false);
            foreach (float a in new[] { -1f, 1f }) foreach (float b in new[] { -1f, 1f })
                {
                    var w = SliceModelBuilder.Prim(PrimitiveType.Cylinder, "Wheel", bc, Vector3.zero, Vector3.one, mSteel);
                    w.transform.SetPositionAndRotation(new Vector3(5.6f + a * 0.72f, gy + 0.65f, 12f + b * 0.9f), Quaternion.Euler(0, 0, 90)); w.transform.localScale = new Vector3(0.66f, 0.06f, 0.66f);
                }
            foreach (var c in depot.GetComponentsInChildren<Collider>(true)) if (c.gameObject.layer != L(SceneLayers.LocomotionBlocker)) Object.DestroyImmediate(c);
            NoShadow(depot);
            sb.AppendLine($"機廠擺設（推估）：檢查坑 |X|≤{PitHalfWidth} Z ±{PitHalfLength}（軌內深 {PitDeep}、軌外 {PitSide}）、隔離管制點、外接電源箱、股道吊牌、禁止移動牌＋輪擋、止衝擋、靠牆器材、天花板燈");
        }

        /// <summary>駕駛室外觀（terry 2026-11-13）：前擋風與側窗、儀表台、駕駛座；控制盤和站點不動。</summary>
        static void DressCab(Transform cabVisual, RoofSceneDef d)
        {
            var cc = d.cabCenter; float half = d.cabSize / 2f, cy = d.cabFloorY;
            var mGlass = Mat("cabGlass", new Color(0.12f, 0.18f, 0.24f));
            var mDash = Mat("cabDash", new Color(0.18f, 0.19f, 0.21f));
            var mSeat = Mat("cabSeat", new Color(0.15f, 0.25f, 0.40f));
            Box("Windshield", cabVisual, new Vector3(cc.x, cy + 1.45f, cc.z - half + 0.01f), new Vector3(d.cabSize - 0.2f, 0.9f, 0.01f), mGlass, collider: false);
            Box("SideWindow", cabVisual, new Vector3(cc.x + half - 0.01f, cy + 1.45f, cc.z - 0.2f), new Vector3(0.01f, 0.7f, 0.8f), mGlass, collider: false);
            Box("Dashboard", cabVisual, new Vector3(cc.x + 0.1f, cy + 0.85f, cc.z - half + 0.2f), new Vector3(d.cabSize - 0.4f, 0.12f, 0.35f), mDash, collider: false);
            for (int k = 0; k < 4; k++) Box("DashButton", cabVisual, new Vector3(cc.x - 0.2f + 0.18f * k, cy + 0.92f, cc.z - half + 0.25f), new Vector3(0.06f, 0.02f, 0.06f), k == 3 ? Mat("signRed", new Color(0.8f, 0.12f, 0.1f)) : Mat("statusGreen", new Color(0.3f, 1f, 0.45f), unlit: true), collider: false);
            Box("SeatBase", cabVisual, new Vector3(cc.x + 0.35f, cy + 0.45f, cc.z + 0.15f), new Vector3(0.45f, 0.1f, 0.45f), mSeat, collider: false);
            Box("SeatBack", cabVisual, new Vector3(cc.x + 0.35f, cy + 0.8f, cc.z + 0.38f), new Vector3(0.45f, 0.6f, 0.08f), mSeat, collider: false);
            Box("SeatPost", cabVisual, new Vector3(cc.x + 0.35f, cy + 0.2f, cc.z + 0.15f), new Vector3(0.08f, 0.4f, 0.08f), Mat("cabSeatPost", new Color(0.4f, 0.42f, 0.45f)), collider: false);
        }
    }
}
