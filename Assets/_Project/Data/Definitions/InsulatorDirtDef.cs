using System;
using UnityEngine;

namespace PantographSim.Data
{
    /// <summary>
    /// R1.5b b4（D43，model-spec v3.12 §7.7.1c）：絕緣子髒汙的計分格。遮罩 cols × rows（16 × 6），16 行繞圓周。
    /// 計分格＝面向子站點的 faceColumns 行裡、從跪姿肩點（站點上方 shoulderY）往外 reach 內的手部樣點打得到（沒被吊耳、高壓端子、底架擋住）的格；
    /// 建置時烘焙成 scoreMask[n-1]（'1'＝計分格），每支至少 minScoreCells 格。清除率只算計分格，達 cleanRatio 時整支變乾淨（播 wrapSeconds 的拉一圈動作）；
    /// 背面不計分。「已目視繞看一圈」＝面向的那幾行每行至少擦到 1 格。VR、桌面同一套規則。
    /// </summary>
    [CreateAssetMenu(menuName = "PantographSim/Insulator Dirt")]
    public class InsulatorDirtDef : ScriptableObject
    {
        public const string ResourcePath = "InsulatorDirt";
        public int cols = 16, rows = 6;
        [Tooltip("面向子站點的行數（16 行裡取 8）")] public int faceColumns = 8;
        [Tooltip("跪姿肩點：站點上方高度（m）")] public float shoulderY = 0.9f;
        [Tooltip("手部樣點離肩點最遠（m）")] public float reach = 0.8f;
        [Tooltip("每支至少幾個計分格（不到就讓建置失敗）")] public int minScoreCells = 32;
        [Tooltip("計分格清除率門檻")] public float cleanRatio = 0.95f;
        [Tooltip("達門檻時播「用布包住絕緣子拉一圈」的秒數")] public float wrapSeconds = 0.5f;
        [Tooltip("建置時烘焙：每支的計分格（cols × rows 個 0／1，列優先），[n-1]＝絕緣子 n")] public string[] scoreMask = Array.Empty<string>();
        [Tooltip("建置時烘焙：每支面向子站點的行（逗號分隔）")] public string[] faceMask = Array.Empty<string>();

        public static InsulatorDirtDef Load() => Resources.Load<InsulatorDirtDef>(ResourcePath);
    }
}
