using System.Collections.Generic;
using System.Linq;
using PantographSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.UI
{
    /// <summary>
    /// VR 介面配置：把 SliceUI 各面板搬進世界空間的小 Canvas（1 unit = 1 m）：
    /// ・提示板（步驟面板）：前方 1.2 m、略低於視線；第一次出現在正前方，3 秒後先出現箭頭，再用 1 秒移到左前方。
    /// ・對話框（錯誤三段式、任務卡、教學、選單、說明…）：視線正前方約 1 m，頭轉超過 35° 才慢慢跟過來。
    /// ・左手腕面板（手上工具、設定值、上一步、收起提示板、工具面板）、左手夾板（作業單）、右控制器按鍵標籤。
    /// ・升降面板：固定在作業平台扶手（場景位置 xr.mount.lift_panel）。・資訊卡：物件靠玩家一側、面向頭盔。
    /// 桌面專用的面板（場景標籤、底部工具列、操作列、右上角列）收進停用的容器。所有 Canvas 都是 World Space。
    /// </summary>
    public class XrPanelLayout : MonoBehaviour
    {
        public const string LiftMountId = "xr.mount.lift_panel";
        // 每 unit 幾公尺（字高約 = 字級 × 比例；步驟面板 22 級 ≈ 2 cm）
        public const float BoardScale = 0.0009f, DialogScale = 0.0007f, WristScale = 0.00042f, LiftScale = 0.0009f, HoverScale = 0.001f, ClipScale = 0.00036f, LabelScale = 0.00022f;
        const float BoardDistance = 1.2f, DialogDistance = 1.0f;

        static readonly string[] DialogNames =
        {
            "Tray", "Toast", "ControlPanel_Dim", "Removal_Dim", "Spare_Dim", "TaskCard_Dim", "Tutorial_Dim",
            "Result_Dim", "Help_Dim", "XrHelp_Dim", "XrMenu_Dim", "XrTutorial_Dim", "RecordsList_Dim", "VideoNotice", "Error_Dim"
        };
        /// <summary>只給頭盔看的圖層（旁觀鏡頭不畫，影片裡不會出現 REC 紅點）；與紀錄模組 SpectatorCamera.HmdOnlyLayer 相同。</summary>
        public const int HmdOnlyLayer = 30;
        static readonly string[] HiddenNames = { "Markers", "ToolBar", "Controls", "TopBar" };

        IXrRig rig;
        RectTransform safe;
        Canvas board, dialog, wrist, lift, hover, clip, labels;
        RectTransform stepPanel, wristView, gesture, toolPanel, hoverCard, labelsRoot, boardArrow;
        Transform hidden;
        readonly List<Canvas> canvases = new List<Canvas>();
        float boardYaw, boardShownAt = -1f;
        float lastRecenter = -1f;
        bool boardPlaced;

        public IReadOnlyList<Canvas> Canvases => canvases;
        public Canvas Dialog => dialog;
        public Canvas Board => board;
        public Canvas Lift => lift;
        public Canvas Hover => hover;
        public Canvas Wrist => wrist;

        public void Setup(Canvas mainCanvas, RectTransform safeArea, IXrRig xrRig, HoverCardView hoverCardView)
        {
            rig = xrRig; safe = safeArea; hoverView = hoverCardView;
            hidden = new GameObject("XR_HiddenDesktopPanels").transform;
            hidden.SetParent(transform, false);
            hidden.gameObject.SetActive(false);

            board = MakeCanvas("XR_Board", new Vector2(540, 1000), BoardScale, null);
            dialog = MakeCanvas("XR_Dialog", new Vector2(1240, 1000), DialogScale, null);
            lift = MakeCanvas("XR_Lift", new Vector2(360, 900), LiftScale, null);
            hover = MakeCanvas("XR_Hover", new Vector2(460, 420), HoverScale, null);
            wrist = MakeCanvas("XR_Wrist", new Vector2(720, 1100), WristScale, rig != null ? rig.LeftHand : null);
            clip = MakeCanvas("XR_Clipboard", new Vector2(1160, 980), ClipScale, null);   // 試玩修正 第 3 點：世界中的面板，可以抓（XrPanels）
            labels = MakeCanvas("XR_ControllerLabels", new Vector2(420, 360), LabelScale, rig != null ? rig.RightHand : null);
            if (rig != null)
            {
                wrist.transform.localPosition = new Vector3(0f, 0.09f, 0.03f);
                wrist.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
                labels.transform.localPosition = new Vector3(0.07f, 0.03f, 0.0f);
                labels.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            }

            stepPanel = Move("StepPanel", board, new Vector2(0.5f, 1f));
            boardArrow = Ui.Text(board.transform, "◀ 提示板移到左邊", 40, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform;
            Ui.Place(boardArrow, new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(520, 80));
            boardArrow.gameObject.AddComponent<Outline>().effectColor = Color.black;
            boardArrow.gameObject.SetActive(false);

            // 對話框照原本的上下層順序搬
            foreach (Transform child in safe.Cast<Transform>().ToList())
                if (DialogNames.Contains(child.name)) Move(child.name, dialog, new Vector2(0.5f, 0.5f));
            var tray = dialog.transform.Find("Tray") as RectTransform;
            if (tray != null) PlaceIn(tray, new Vector2(1f, 1f), new Vector2(-10, -10));
            var toast = dialog.transform.Find("Toast") as RectTransform;
            if (toast != null) PlaceIn(toast, new Vector2(0.5f, 0f), new Vector2(0, 30));

            Move("WorkOrder_Dim", clip, new Vector2(0.5f, 0.5f));
            liftPanel = Move("LiftPanel", lift, new Vector2(0.5f, 1f));
            hoverCard = Move("HoverCard", hover, new Vector2(0.5f, 0f));
            wristView = Move("XrWrist", wrist, new Vector2(0.5f, 1f));
            toolPanel = Move("ToolPanel", wrist, new Vector2(0.5f, 1f));
            gesture = Move("XrGesture", wrist, new Vector2(0.5f, 0f));
            labelsRoot = Move("XrLabels", labels, new Vector2(0.5f, 0.5f));
            foreach (var n in HiddenNames) { var t = safe.Find(n); if (t != null) t.SetParent(hidden, false); }
            if (toolPanel != null) toolPanel.sizeDelta = new Vector2(700, toolPanel.sizeDelta.y);

            // 重播控制：前方偏下的一塊板子（時間軸在上、錯誤／步驟清單在下）
            replay = MakeCanvas("XR_Replay", new Vector2(1240, 1000), DialogScale, null);
            Move("ReplayBar", replay, new Vector2(0.5f, 1f));
            var lists = Move("ReplayLists", replay, new Vector2(0.5f, 0f));
            if (lists != null) lists.sizeDelta = new Vector2(1200, lists.sizeDelta.y);
            PlaceReplay();

            // 錄影中的 REC 紅點：固定在頭盔視野右上角（眼前約 1 m、偏右上約 25°），只在頭盔圖層
            if (rig != null && rig.Head != null)
            {
                recDot = MakeCanvas("XR_RecDot", new Vector2(320, 70), 0.0009f, rig.Head);
                var dir = Quaternion.Euler(-16f, 19f, 0f) * Vector3.forward;
                recDot.transform.localPosition = dir * 1.0f;
                recDot.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                var bg = Ui.Panel(recDot.transform, "Bg", new Color(0, 0, 0, 0.45f), false, false);
                Ui.Stretch(bg.rectTransform);
                recDotText = Ui.Text(recDot.transform, "", 40, new Color(1f, 0.2f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
                Ui.Stretch(recDotText.rectTransform);
                foreach (var t in recDot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = HmdOnlyLayer;
                recDot.sortingOrder = 50;
                recDot.gameObject.SetActive(false);
            }

            // 原本的 Canvas 也改成 World Space（裡面已經沒有要顯示的東西）
            mainCanvas.renderMode = RenderMode.WorldSpace;
            mainCanvas.worldCamera = rig != null ? rig.Eye : null;
            var scaler = mainCanvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            mainCanvas.transform.localScale = Vector3.one * 0.001f;
            mainCanvas.transform.position = new Vector3(0, -50f, 0);
            canvases.Add(mainCanvas);

            PlaceDialog(1f);
            PlaceLift();
        }

        RectTransform liftPanel;
        HoverCardView hoverView;
        Canvas replay, recDot;
        Text recDotText;

        void PlaceReplay()
        {
            if (replay == null) return;
            var fwd = FlatForward();
            var pos = HeadPos + fwd * 1.1f + Vector3.down * 0.45f;
            replay.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - HeadPos, Vector3.up));
        }

        Canvas MakeCanvas(string name, Vector2 size, float metersPerUnit, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : transform, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = rig != null ? rig.Eye : null;
            c.sortingOrder = 10;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * metersPerUnit;
            canvases.Add(c);
            return c;
        }

        RectTransform Move(string name, Canvas target, Vector2 anchor)
        {
            var t = safe.Find(name) as RectTransform;
            if (t == null) return null;
            t.SetParent(target.transform, false);
            if (name.EndsWith("_Dim"))
            {
                // 遮罩只蓋住這塊板子（半透明），卡片置中
                Ui.Stretch(t);
                var img = t.GetComponent<Image>();
                if (img != null) img.color = name == "WorkOrder_Dim" ? new Color(0, 0, 0, 0f) : new Color(0, 0, 0, 0.35f);
            }
            else PlaceIn(t, anchor, Vector2.zero);
            return t;
        }

        static void PlaceIn(RectTransform t, Vector2 anchor, Vector2 pos)
        {
            t.anchorMin = t.anchorMax = t.pivot = anchor;
            t.anchoredPosition = pos;
        }

        Vector3 HeadPos => rig != null && rig.Head != null ? rig.Head.position : Vector3.up * 1.6f;

        Vector3 FlatForward()
        {
            var f = rig != null && rig.Head != null ? rig.Head.forward : Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        /// <summary>提示板：頭的水平方向轉 yaw 度、前方 1.2 m、比眼睛低 0.25 m，面向頭。</summary>
        void PlaceBoard(float yaw)
        {
            var dir = Quaternion.Euler(0, yaw, 0) * FlatForward();
            var pos = HeadPos + dir * BoardDistance + Vector3.down * 0.25f;
            board.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - HeadPos, Vector3.up));
        }

        void PlaceDialog(float t)
        {
            var fwd = FlatForward();
            var st = Sim.State;   // 距離、高度是資料（PanelLayout，XrStartupSequencer 寫進黑板；UI 只能引用 Core）
            var pos = HeadPos + fwd * (st?.GetFloat("xr.dialog.distance", DialogDistance) ?? DialogDistance) + Vector3.down * (st?.GetFloat("xr.dialog.below", 0.08f) ?? 0.08f);
            dialog.transform.position = Vector3.Lerp(dialog.transform.position, pos, t);
            dialog.transform.rotation = Quaternion.Slerp(dialog.transform.rotation, Quaternion.LookRotation(fwd, Vector3.up), t);
        }

        void PlaceLift()
        {
            if (Sim.Registry != null && Sim.Registry.TryGetTransform(LiftMountId, out var mount) && mount != null)
                lift.transform.SetPositionAndRotation(mount.position, mount.rotation);
            else
                lift.transform.SetPositionAndRotation(HeadPos + FlatForward() * 0.8f + Vector3.down * 0.5f + Quaternion.Euler(0, 90, 0) * FlatForward() * 0.4f, Quaternion.LookRotation(FlatForward()));
        }

        /// <summary>傳送、轉向或置中之後重新擺放跟著玩家的面板。</summary>
        public void Recenter()
        {
            PlaceReplay();
            if (Managed) return;   // T3：面板固定在世界中，由 XrPanels 依站點擺放
            PlaceDialog(1f);
            if (boardPlaced) PlaceBoard(boardYaw);
        }

        /// <summary>T3：XR 的 XrPanels 接手提示板與對話框（固定在世界、把手移動、各站點記位置）。</summary>
        static bool Managed => Sim.State != null && Sim.State.GetBool("xr.panels.managed");
        bool registered, dialogWasOpen;

        bool DialogOpen() { foreach (Transform c in dialog.transform) if (c.gameObject.activeSelf && c.name.EndsWith("_Dim")) return true; return false; }

        void LateUpdate()
        {
            var s = Sim.State;
            if (s == null || rig == null) return;
            if (s.GetFloat("xr.recenter", -1f) != lastRecenter) { lastRecenter = s.GetFloat("xr.recenter", -1f); Recenter(); PlaceLift(); }
            if (!registered && Sim.Registry != null)
            {
                registered = true;
                Sim.Registry.Register("xr.panel.board", board.transform);
                Sim.Registry.Register("xr.panel.dialog", dialog.transform);
                Sim.Registry.Register("xr.panel.workorder", clip.transform);
            }

            // REC 紅點（提示板收起或轉身時也看得到，因為它跟著頭）
            if (recDot != null)
            {
                string rec = Ui.RecText(s);
                if (recDot.gameObject.activeSelf != (rec.Length > 0)) recDot.gameObject.SetActive(rec.Length > 0);
                if (rec.Length > 0) recDotText.text = rec;
            }

            // 開場的擺放由 XrStartupSequencer 負責；開場之後才打開的對話框（錯誤視窗）開在頭前 1 m，開著時固定不跟頭，可以抓把手移動
            bool open = DialogOpen();
            if (open && !dialogWasOpen && s.GetBool("xr.ready")) PlaceDialog(1f);
            dialogWasOpen = open;

            bool managed = Managed;
            // 提示板：第一次出現在正前方 → 3 秒後箭頭 → 1 秒移到左前方（−35°）
            board.gameObject.SetActive(!s.GetBool("ui.board_hidden"));
            bool stepVisible = stepPanel != null && stepPanel.gameObject.activeSelf;
            if (stepVisible && boardShownAt < 0f && !managed) { boardShownAt = Time.unscaledTime; boardYaw = 0f; PlaceBoard(0f); boardPlaced = true; }
            if (boardShownAt >= 0f && !managed)
            {
                float t = Time.unscaledTime - boardShownAt;
                boardArrow.gameObject.SetActive(t > 3f && t < 4f);
                if (t > 4f && t < 5.2f)
                {
                    boardYaw = -35f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t - 4f));
                    PlaceBoard(boardYaw);
                }
            }

            // 手腕面板：工具面板接在腕面板下面；手勢小卡在上面
            if (wristView != null && toolPanel != null)
                toolPanel.anchoredPosition = new Vector2(0, -(wristView.gameObject.activeSelf ? wristView.rect.height + 12f : 0f));
            if (gesture != null) { gesture.anchorMin = gesture.anchorMax = new Vector2(0.5f, 1f); gesture.pivot = new Vector2(0.5f, 0f); gesture.anchoredPosition = new Vector2(0, 12f); }

            // 資訊卡：物件靠玩家一側、面向頭盔，大小隨距離（看起來一樣大）
            if (hoverCard != null && hoverCard.gameObject.activeSelf)
            {
                string id = hoverView != null ? hoverView.ShownId : "";
                if (Sim.Registry != null && Sim.Registry.TryGetPosition(id, out var p))
                {
                    var toHead = HeadPos - p;
                    float dist = toHead.magnitude;
                    var pos = p + toHead.normalized * Mathf.Min(0.2f, dist * 0.3f) + Vector3.up * 0.1f;
                    hover.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - HeadPos, Vector3.up));
                    hover.transform.localScale = Vector3.one * HoverScale * Mathf.Clamp(Vector3.Distance(pos, HeadPos), 0.5f, 3f);
                }
            }

            // 右控制器標籤：低頭看控制器（控制器在視線中央附近、距離 0.7 m 內）才出現
            if (labels != null && rig.RightHand != null && rig.Head != null)
            {
                var to = rig.RightHand.position - rig.Head.position;
                bool look = to.magnitude < 0.7f && Vector3.Dot(rig.Head.forward, to.normalized) > 0.82f;
                labels.gameObject.SetActive(look);
            }
        }
    }
}
