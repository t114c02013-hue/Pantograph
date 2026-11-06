using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// T3 可移動面板的預設位置（以目前站點為基準：x＝右、y＝高、z＝前，單位 m；EX）。只列可以抓把手移動的面板；
    /// 升降操作盒是設備、作業單在左手夾板，都不在這張表裡（沒有把手）。錯誤視窗一律開在頭前 dialogDistance。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1/Panel Layout")]
    public class PanelLayout : ScriptableObject
    {
        public const string ResourceName = "PanelLayout";

        [Serializable]
        public class Panel
        {
            public string panelId;
            [Tooltip("相對站點的預設位置（站點的 panelOffset 有值時，提示板優先用站點的）")] public Vector3 offset;
            [Tooltip("開啟時一律放到頭前（錯誤視窗）")] public bool openInFront;
            [Tooltip("試玩修正 第 3 點：可以叫出來、放到想要位置的面板（作業單）：第一次打開放在眼前 frontDistance、眼下 frontBelow、往下傾 frontTiltDeg，之後記住學員放的位置，換站跟著站點走；沒內容時藏把手")] public bool carried;
            public float frontDistance, frontBelow, frontTiltDeg;
        }

        [Tooltip("VR 開場（XrStartupSequencer）：等頭盔追蹤最多幾秒、頭離地多高才算追蹤有效、逾時用的眼高（m）")] public float startupTimeoutSec = 5f, trackedMinHeadY = 0.5f;
        [Tooltip("任務卡、對話框、錯誤視窗（試玩修正 8.5）：頭前方距離、比眼睛低多少（m）；只跟頭的水平朝向，擺好就固定")] public float taskCardDistance = 1.0f, taskCardBelowEye = 0.15f;

        [Tooltip("選好模式後把人轉向站點正面：任務卡在站點背面，提示板和工作目標在正面")] public bool faceWorkOnStart = false;

        [Tooltip("terry 試玩：開場背對站點面向目標（升降盒），任務卡在升降盒對面、正面朝學員")] public bool startFacesOpposite = true;
        [Tooltip("terry 試玩：作業單打開時，離視線超過這個角度（度）或這個距離（m）就移到眼前")] public float carriedMaxAngle = 50f, carriedMaxDistance = 0.8f;
        [Tooltip("terry 試玩：工具說明小視窗貼在手上工具旁邊：x＝往使用者右邊、y＝往上、z＝往使用者靠近（m）")] public Vector3 toolCardOffset = new Vector3(0.2f, 0.1f, 0.12f);
        [Tooltip("工具說明小視窗每 unit 幾公尺（620×520 unit；字高約＝字級×這個值）")] public float toolCardScale = 0.0005f;
        [Tooltip("射線指到工具時的摘要小框，比一般資訊卡縮小成幾倍")] public float toolSummaryScale = 0.7f;
        public Panel[] panels =
        {
            new Panel { panelId = "board", offset = new Vector3(-0.55f, 1.35f, 1.05f) },    // 步驟提示面板：站點左前方約 1.2 m，上緣略低於視線
            new Panel { panelId = "dialog", offset = new Vector3(0f, 1.52f, 1.0f), openInFront = true },   // 錯誤視窗等對話框：頭前 1 m
            new Panel { panelId = "workorder", carried = true, frontDistance = 0.5f, frontBelow = 0.15f, frontTiltDeg = 15f },   // 作業單（第 8.5 點的位置）
        };
        public float dialogDistance = 1.0f, recallDistance = 1.2f, recallMaxAngle = 30f;
        public Vector3 handleSize = new Vector3(0.20f, 0.03f, 0.03f);
        public Color handleColor = new Color(1f, 0.55f, 0.1f);

        public Panel Find(string id) => Array.Find(panels, p => p.panelId == id);

        public static PanelLayout LoadDefault()
        {
            var t = Resources.Load<PanelLayout>(ResourceName);
            return t != null ? t : CreateInstance<PanelLayout>();
        }
    }
}
