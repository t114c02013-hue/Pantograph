using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5a a4：作業區（zone.arrival 進站區、zone.maintenance 地面維修區）。範圍是 XZ 矩形，floorY＝地板高度；兩區之間用傳送移動。
    /// 座標系同 RoofSceneDef（原點在弓頭中心正下方的車頂面；+Z 朝肘側；平台在 −X 側；單位 m）。EX。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/R1.5/Zone")]
    public class ZoneDef : ScriptableObject
    {
        public const string Arrival = "zone.arrival", Maintenance = "zone.maintenance";

        public string id;
        public string label;
        public Vector2 minXZ, maxXZ;
        public float floorY;

        public bool Contains(Vector3 p) => p.x >= minXZ.x && p.x <= maxXZ.x && p.z >= minXZ.y && p.z <= maxXZ.y;
    }
}
