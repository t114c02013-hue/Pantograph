using System.Collections.Generic;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b1（crane-software v0.5 §2.2，crane-procedure 2.1、6.1）：天車三軸的速度、行程、加減速，紅區、晃動、遙控器掛座半徑。標 EX 的是示例值。
    /// 位置都是世界座標（原點 O）：小車 X、橋架 Z、吊鉤 Y。hookYMax 由建置從模型量測寫回。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Crane Profile")]
    public class CraneProfile : ScriptableObject
    {
        [Header("速度（m/min）：高速／微動")]
        public float hoistFast = 5f, hoistFine = 0.5f;
        [Tooltip("橫行（小車 X），微動 EX")] public float traverseFast = 15f, traverseFine = 1.5f;
        [Tooltip("走行（橋架 Z），微動 EX")] public float travelFast = 20f, travelFine = 2f;
        [Header("行程（世界座標）")]
        public float trolleyXMin = -15f, trolleyXMax = 2f;
        public float bridgeZMin = -15f, bridgeZMax = 15f;
        [Tooltip("吊鉤最高 Y（上限位，Crane_HookBlock 的 pivot；建置時依 D40 從模型量測寫回：吊鉤組頂點最高點到台車／捲揚機底面要留 hookTopClearance）。唯一的吊鉤最高點數值，6.35 與 RoofSceneDef.craneHookTopSpecY 作廢")] public float hookYMax = 5.85f;
        [Tooltip("D40（鐵道）：吊鉤升到上限位時，吊鉤組頂面（頂點最高點）到台車底面（Crane_Hoist／Crane_Trolley 頂點最低點）的最小間距，避免過捲")] public float hookTopClearance = 0.30f;
        [Tooltip("吊鉤最低 Y（EX，地面維修區台架以下）")] public float hookYMin = -3.0f;
        [Tooltip("起動、停止的平滑加減速秒數（EX）")] public float accelSeconds = 0.3f;
        [Tooltip("part.crane.hook.pos 變化超過這個值（m）才寫入")] public float hookPosWriteThreshold = 0.01f;
        [Header("紅區、晃動、遙控器")]
        [Tooltip("紅區＝吊物水平投影外擴（m，D18）")] public float redZoneMargin = 1.0f;
        [Tooltip("視覺晃動最大角度（度）與停車後衰減到 0 的秒數")] public float swayMaxDeg = 3f, swayDecaySeconds = 1.5f;
        [Tooltip("Btn_Speed2 的發光顏色與標籤：微動／高速（軟體工程裁定）")] public Color speedFineColor = new Color(0.2f, 0.85f, 0.3f), speedFastColor = new Color(1f, 0.55f, 0.1f);
        public string speedFineLabel = "微動", speedFastLabel = "高速";
        [Tooltip("左握把在掛座這個半徑（m）內才是拿起／掛回")] public float pendantDockRadius = 0.3f;
        [Tooltip("遙控器按鍵的中文標籤（按鍵節點名 → 文字）")] public string[] buttonLabels =
        {
            "Btn_Up=上升", "Btn_Down=下降", "Btn_East=橫行 +X", "Btn_West=橫行 −X", "Btn_North=走行 +Z", "Btn_South=走行 −Z",
            "Btn_Speed2=高速", "Btn_EStop=緊急停止", "Key_Power=電源",
        };
    }

    /// <summary>天車命令的軸與方向。</summary>
    public enum CraneAxis { None, Hoist, Traverse, Travel }

    /// <summary>天車的純計算（EditMode 測試用；CraneMechanism 每幀呼叫）。</summary>
    public static class CraneLogic
    {
        public const string Up = "Up", Down = "Down", East = "E", West = "W", North = "N", South = "S", None = "None";

        /// <summary>ctrl.crane.cmd → 軸與方向（+1／−1）。E＝+X、W＝−X、N＝+Z、S＝−Z。</summary>
        public static (CraneAxis axis, int dir) Parse(string cmd)
        {
            switch (cmd)
            {
                case Up: return (CraneAxis.Hoist, 1);
                case Down: return (CraneAxis.Hoist, -1);
                case East: return (CraneAxis.Traverse, 1);
                case West: return (CraneAxis.Traverse, -1);
                case North: return (CraneAxis.Travel, 1);
                case South: return (CraneAxis.Travel, -1);
                default: return (CraneAxis.None, 0);
            }
        }

        /// <summary>按鍵節點名 → 命令（方向鍵才有）。</summary>
        public static string CmdOfButton(string button)
        {
            switch (button)
            {
                case "Btn_Up": return Up;
                case "Btn_Down": return Down;
                case "Btn_East": return East;
                case "Btn_West": return West;
                case "Btn_North": return North;
                case "Btn_South": return South;
                default: return null;
            }
        }

        /// <summary>目前按住的方向鍵 → 命令：剛好一個才有效，同時按兩個（或沒按）就是 None（deadman）。</summary>
        public static string Resolve(IEnumerable<string> pressedButtons)
        {
            string cmd = None; int n = 0;
            foreach (var b in pressedButtons) { var c = CmdOfButton(b); if (c == null) continue; if (++n > 1) return None; cmd = c; }
            return cmd;
        }

        /// <summary>軸速度（m/s）。</summary>
        public static float Speed(CraneProfile p, CraneAxis axis, bool fast)
        {
            float mpm = axis == CraneAxis.Hoist ? (fast ? p.hoistFast : p.hoistFine)
                : axis == CraneAxis.Traverse ? (fast ? p.traverseFast : p.traverseFine)
                : axis == CraneAxis.Travel ? (fast ? p.travelFast : p.travelFine) : 0f;
            return mpm / 60f;
        }

        /// <summary>
        /// 一軸平滑加減速：velocity 以「全速／accelSeconds」的加速度趨近 target，位置夾在 [min,max]。
        /// 回傳 true＝這一步碰到行程端點（速度歸零）。
        /// </summary>
        public static bool StepAxis(ref float pos, ref float velocity, float target, float fullSpeed, float accelSeconds, float min, float max, float dt)
        {
            float accel = Mathf.Max(fullSpeed, 1e-4f) / Mathf.Max(0.01f, accelSeconds);
            velocity = Mathf.MoveTowards(velocity, target, accel * dt);
            pos += velocity * dt;
            if (pos >= max) { pos = max; if (velocity > 0f) { velocity = 0f; return true; } }
            if (pos <= min) { pos = min; if (velocity < 0f) { velocity = 0f; return true; } }
            return false;
        }

        /// <summary>紅區（D18、D19）：頭部水平投影在吊物水平投影外擴 margin 的範圍內（和高度無關）。</summary>
        public static bool InRedZone(Bounds loadWorld, float margin, Vector3 head) =>
            head.x >= loadWorld.min.x - margin && head.x <= loadWorld.max.x + margin && head.z >= loadWorld.min.z - margin && head.z <= loadWorld.max.z + margin;

        /// <summary>
        /// 視覺晃動（兩個水平軸各一個阻尼擺，只是外觀）：吊鉤水平加速度推動擺角，夾在 ±maxDeg，以時間常數 decay/3 指數衰減（約 decay 秒到 0）。
        /// shortcut: 晃動只是視覺，不影響判定, 教授要訓練「停車防擺」時再改成會影響判定的模型
        /// </summary>
        public static Vector2 StepSway(Vector2 angleDeg, Vector2 horizontalAccel, float maxDeg, float decaySeconds, float dt)
        {
            const float degPerAccel = 20f;   // 1 m/s² 的加速度約推 20°（只取外觀，之後被 maxDeg 夾住）
            angleDeg += -horizontalAccel * degPerAccel * dt;
            angleDeg = Vector2.ClampMagnitude(angleDeg, maxDeg);
            float k = Mathf.Exp(-dt * 3f / Mathf.Max(0.01f, decaySeconds));
            return angleDeg * k;
        }
    }
}
