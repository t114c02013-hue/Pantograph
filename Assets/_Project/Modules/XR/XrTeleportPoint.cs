using PantographSim.Core;
using PantographSim.Data;
using UnityEngine;
using UnityEngine.UI;

namespace PantographSim.XR
{
    /// <summary>
    /// 站點（R1：依步驟設計的 8 個傳送點，資料在 StationSet）。lockKey 有值時，黑板該鍵為 true 才能去（車頂＝roof.open）。
    /// 地上一直有 TP_Marker（半徑 0.25 m 圓盤＋腳印方向箭頭）：可用＝藍、目前步驟＝綠且脈動、鎖住＝灰＋鎖頭。
    /// 兩站共用同一點（sharedWith）時只有一站有標記。
    /// </summary>
    public class XrTeleportPoint : MonoBehaviour
    {
        public enum Look { Available, Current, Locked }

        [SerializeField] string pointId;
        [SerializeField] string label;
        [SerializeField] string lockKey;
        [SerializeField] string lockedMessage = "先扣好安全帶";
        [SerializeField] string floor = "Platform";
        [SerializeField] string sharedWith = "";
        [SerializeField] GameObject marker;
        [SerializeField] Renderer disc;
        [SerializeField] Text labelText;
        [SerializeField] GameObject lockIcon;

        public string PointId => pointId;
        public string Label => label;
        public string LockKey => lockKey;
        public string LockedMessage => Rule?.BlockReason(Sim.State, pointId) ?? lockedMessage;
        /// <summary>R1.5b b2：傳送限制（TeleportRule，XrTeleport 啟動時設定）；擋下時這一站算 Locked，訊息用規則的。</summary>
        public static TeleportRule Rule;
        public string Floor => floor;
        public string SharedWith => sharedWith;
        public bool HasMarker => marker != null;

        public void Configure(string id, string name, string key, GameObject markerRoot, Renderer discRenderer, Text text)
        {
            pointId = id; label = name; lockKey = key; marker = markerRoot; disc = discRenderer; labelText = text;
        }

        public void ConfigureStation(string stationFloor, string locked, string shared, GameObject lockGo)
        {
            floor = stationFloor; sharedWith = shared ?? ""; lockIcon = lockGo;
            if (!string.IsNullOrEmpty(locked)) lockedMessage = locked;
        }

        /// <summary>同一點（自己或共用的那一站）。</summary>
        public bool Same(string id) => id == pointId || (!string.IsNullOrEmpty(sharedWith) && id == sharedWith);

        public bool Unlocked(ISimState s) => (string.IsNullOrEmpty(lockKey) || (s != null && s.GetBool(lockKey))) && Rule?.BlockReason(s, pointId) == null;

        /// <summary>B 鍵要不要傳到 target（純函式；值對應 XrTeleport.GoResult：0 Moved、1 AlreadyThere、2 Locked、3 Missing）。</summary>
        public static int Decide(XrTeleportPoint target, XrTeleportPoint current, ISimState s)
        {
            if (target == null) return 3;
            if (!target.Unlocked(s)) return 2;
            if (current != null && (current.Same(target.PointId) || target.Same(current.PointId))) return 1;
            return 0;
        }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock block;

        public static Color ColorOf(Look look, float time) =>
            look == Look.Current ? Color.Lerp(new Color(0.15f, 0.85f, 0.35f, 0.85f), new Color(0.45f, 1f, 0.6f, 0.95f), 0.5f + 0.5f * Mathf.Sin(time * 4f))
            : look == Look.Locked ? new Color(0.45f, 0.45f, 0.45f, 0.7f) : new Color(0.15f, 0.35f, 0.75f, 0.6f);

        /// <summary>平常的外觀（一直顯示在地上）。</summary>
        public void SetLook(Look look, float time)
        {
            if (marker == null) return;
            bool hidden = Rule != null && Rule.Hidden(Sim.State, pointId);   // D23：日常更換不開放 tp.platform.crane
            if (marker.activeSelf == hidden) marker.SetActive(!hidden);
            if (hidden) return;
            Paint(ColorOf(look, time));
            if (lockIcon != null && lockIcon.activeSelf != (look == Look.Locked)) lockIcon.SetActive(look == Look.Locked);
        }

        void Paint(Color c)
        {
            if (disc == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor(ColorId, c);
            disc.SetPropertyBlock(block);
        }

        /// <summary>瞄準時：目標＝綠（可去）或紅（鎖住）並顯示名稱與鎖住原因。</summary>
        public void ShowMarker(bool visible, bool isTarget, bool locked, Transform head)
        {
            if (marker == null) return;
            if (labelText != null && labelText.gameObject.activeSelf != visible) labelText.gameObject.SetActive(visible);
            if (!visible) return;
            if (isTarget) Paint(locked ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(0.2f, 1f, 0.4f, 0.9f));
            if (labelText != null)
            {
                labelText.text = locked ? $"{label}\n<color=#FF6655>{lockedMessage}</color>" : label;
                if (head != null)
                {
                    var t = labelText.canvas.transform;
                    var dir = t.position - head.position; dir.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }
    }
}
