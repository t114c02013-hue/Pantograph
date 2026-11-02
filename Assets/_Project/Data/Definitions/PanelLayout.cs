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
        }

        public Panel[] panels =
        {
            new Panel { panelId = "board", offset = new Vector3(-0.55f, 1.35f, 1.05f) },    // 步驟提示面板：站點左前方約 1.2 m，上緣略低於視線
            new Panel { panelId = "dialog", offset = new Vector3(0f, 1.52f, 1.0f), openInFront = true },   // 錯誤視窗等對話框：頭前 1 m
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
