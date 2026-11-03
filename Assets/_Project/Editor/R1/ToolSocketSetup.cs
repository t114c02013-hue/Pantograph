using System.IO;
using System.Linq;
using System.Text;
using PantographSim.Data;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// a6 增補 7（鐵道 D39，torque-wrench-sockets.md v1.3 §6.5、§7）：套筒目錄 SocketCatalog、兩支扭力扳手與套筒扳手的 ToolDefinition 欄位、
    /// 螺栓用哪顆套筒（FastenerDef.socketId 的對照）、tool_sockets.json（Blender 讀的單一來源）＋雜湊。可以重跑（每次寫同樣的值）：每次建置都覆寫套筒目錄與這三支扳手的 ToolDefinition 欄位，在 Inspector 手改會被蓋掉，改數值要改這裡。
    /// 數值是規格表的值（EX 的照抄），之後只改這裡的資料或資產。
    /// shortcut: tool.torque_wrench_l 的 ToolDefinition 放在 Assets/_Project/Tools（不在 R1 的工具清單裡，不進工具列、不影響 25 步）, R1.5b b4c 把它接進工具袋 slot_13 時搬進工具清單
    /// </summary>
    public static class ToolSocketSetup
    {
        public const string CatalogPath = "Assets/_Project/Tools/Data/SocketCatalog.asset";
        public const string LargeWrenchPath = "Assets/_Project/Tools/TorqueWrench/Data/TL_torque_wrench_l.asset";
        public const string SocketsJsonPath = "Assets/_Project/Worksite/Data/tool_sockets.json";
        public const string SocketsHashPath = "Assets/_Project/Worksite/Data/tool_sockets.hash.txt";

        static T Ensure<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        public static SocketCatalog Catalog => AssetDatabase.LoadAssetAtPath<SocketCatalog>(CatalogPath);
        public static ToolDefinition LargeWrench => AssetDatabase.LoadAssetAtPath<ToolDefinition>(LargeWrenchPath);

        public static string Apply()
        {
            var cat = Ensure<SocketCatalog>(CatalogPath);
            SocketDef S(string id, string prefab, float drive, int size, float h, float dia, string src, params string[] fits) =>
                new SocketDef { socketId = id, prefab = prefab, driveIn = drive, sizeMm = size, heightM = h, outerDiaM = dia, fits = fits, source = src };
            cat.sockets = new[]
            {
                S("socket.10", "TL_Socket_10", 0.5f, 10, 0.038f, 0.022f, "EX（v1.3 §7.2，錯誤選項）"),
                S("socket.13", "TL_Socket_13", 0.5f, 13, 0.038f, 0.022f, "EX（v1.3 §7.2）", "ip.carbon_strip_*.bolt_*"),
                S("socket.17", "TL_Socket_17", 0.5f, 17, 0.038f, 0.024f, "EX（v1.3 §7.2，錯誤選項）"),
                S("socket.19", "TL_Socket_19", 0.5f, 19, 0.035f, 0.028f, "D38", "part.pan_assy.hv_bolt_*"),
                S("socket.24", "TL_Socket_24", 0.5f, 24, 0.040f, 0.033f, "D38", "part.pan_base.bolt_*"),
                S("socket.13_38", "TL_Socket_13_38", 0.375f, 13, 0.030f, 0.019f, "EX（D39）", "ip.carbon_strip_*.bolt_*"),
            };
            cat.fasteners = new[]
            {
                new FastenerSocket { fastenerPattern = "part.pan_base.bolt_*", socketId = "socket.24" },
                new FastenerSocket { fastenerPattern = "part.pan_assy.hv_bolt_*", socketId = "socket.19" },
                new FastenerSocket { fastenerPattern = "ip.carbon_strip_*.bolt_*", socketId = "socket.13", estimated = true },   // 鬆／鎖用套筒扳手 13；上扭力（CSR_13）用小扭力扳手的 13_38
            };
            EditorUtility.SetDirty(cat);

            var tools = StepJsonImporter.LoadTools();
            void Wrench(ToolDefinition t, string prefab, float drive, Vector2 range, float handle, string def, string[] socks, ToolCalibration cal)
            {
                t.prefab = prefab; t.driveIn = drive; t.rangeNm = range; t.handleLengthM = handle; t.socketMountNode = "Socket_Mount";
                t.defaultSocketId = def; t.socketIds = socks; t.socketStateKey = t.toolId + ".socket"; t.calibration = cal ?? new ToolCalibration();
                EditorUtility.SetDirty(t);
            }
            var small = tools.First(t => t.toolId == "tool.torque_wrench");
            Wrench(small, "PF_TorqueWrench", 0.375f, new Vector2(10, 60), 0.28f, "socket.13_38", new[] { "socket.13_38" }, new ToolCalibration { labelId = "TW-S-001", dueDate = "2027-05-31", source = "示例值" });
            var large = Ensure<ToolDefinition>(LargeWrenchPath);
            large.toolId = "tool.torque_wrench_l"; large.displayName = "大扭力扳手（1/2\"）"; large.hotkey = 0;
            Wrench(large, "PF_TorqueWrench_L", 0.5f, new Vector2(40, 200), 0.45f, "socket.24", new[] { "socket.24", "socket.19" }, new ToolCalibration { labelId = "TW-L-001", dueDate = "2027-05-31", source = "示例值" });
            var sw = tools.First(t => t.toolId == "tool.socket_wrench");
            float swHandle = float.TryParse(sw.Param("handleLength"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0f;   // 套筒扳手讀它自己現在的值
            Wrench(sw, "PF_SocketWrench", 0.5f, Vector2.zero, swHandle, "socket.13", new[] { "socket.10", "socket.13", "socket.17", "socket.19", "socket.24" }, null);

            // tool_sockets.json：Blender tools.build_socket 只讀這份（單一來源）；雜湊給建置檢查 §4-4
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("0.####", ci);
            var json = "{\n  \"sockets\": [\n" + string.Join(",\n", cat.sockets.Select(s =>
                $"    {{ \"socketId\": \"{s.socketId}\", \"prefab\": \"{s.prefab}\", \"driveIn\": {F(s.driveIn)}, \"sizeMm\": {s.sizeMm}, \"heightM\": {F(s.heightM)}, \"outerDiaM\": {F(s.outerDiaM)} }}")) + "\n  ]\n}\n";
            File.WriteAllText(SocketsJsonPath, json, new UTF8Encoding(false));
            File.WriteAllText(SocketsHashPath, Hash(json), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(SocketsJsonPath); AssetDatabase.ImportAsset(SocketsHashPath);
            AssetDatabase.SaveAssets();
            return $"套筒目錄 {cat.sockets.Length} 顆、螺栓對照 {cat.fasteners.Length} 筆；扳手 tool.torque_wrench（3/8\"、10–60、0.28）、tool.torque_wrench_l（1/2\"、40–200、0.45）、tool.socket_wrench（1/2\"、把手 {swHandle}）";
        }

        public static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")))).Replace("-", "").Substring(0, 16);
        }
    }
}
