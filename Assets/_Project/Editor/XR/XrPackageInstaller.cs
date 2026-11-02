using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// VR（Meta Quest 3S，OpenXR）需要的套件安裝：XR Plugin Management、OpenXR、Input System、XR Interaction Toolkit 2.6.x。
    /// 版本固定（Unity 2022.3 可用、XRI 停在 2.6.x 系列），一次加入（AddAndRemove，避免中途重新編譯把安裝清單弄丟）。
    /// 安裝前先把 Active Input Handling 設成 Both（新舊輸入系統並用：XRI 用新的，現有鍵盤滑鼠程式用舊的），
    /// 這樣 Input System 套件不會跳出「要不要重新啟動」的對話框；設定在下次重新啟動 Unity 時生效。
    /// 觸發：建立 Logs/install-xr.request，或選單 PantographSim/XR/Install VR Packages。結果寫到 Logs/install-xr.txt。
    /// </summary>
    [InitializeOnLoad]
    public static class XrPackageInstaller
    {
        const string RequestPath = "Logs/install-xr.request";
        const string ResultPath = "Logs/install-xr.txt";
        public static readonly string[] Packages =
        {
            "com.unity.xr.management@4.5.3",
            "com.unity.xr.openxr@1.14.3",
            "com.unity.inputsystem@1.11.2",
            "com.unity.xr.interaction.toolkit@2.6.5",
        };

        static AddAndRemoveRequest current;
        static readonly StringBuilder log = new StringBuilder();
        static double nextPoll;

        static XrPackageInstaller() => EditorApplication.update += Tick;

        [MenuItem("PantographSim/XR/Install VR Packages")]
        public static void Install()
        {
            if (current != null) return;
            log.Clear();
            log.AppendLine("VR 套件安裝 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            SetInputHandlingBoth();
            log.AppendLine("安裝 " + string.Join("、", Packages) + " …");
            File.WriteAllText(ResultPath, log.ToString());
            current = Client.AddAndRemove(Packages, null);
        }

        static void SetInputHandlingBoth()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings.Length == 0) return;
            var so = new SerializedObject(settings[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == 2) { log.AppendLine("Active Input Handling 已是 Both"); return; }
            prop.intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            log.AppendLine("Active Input Handling → Both（下次重新啟動 Unity 生效）");
        }

        static void Tick()
        {
            if (current == null)
            {
                if (EditorApplication.timeSinceStartup < nextPoll) return;
                nextPoll = EditorApplication.timeSinceStartup + 2.0;
                if (File.Exists(RequestPath) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    File.Delete(RequestPath);
                    Install();
                }
                return;
            }
            if (!current.IsCompleted) return;
            if (current.Status == StatusCode.Success)
                foreach (var p in current.Result.Where(p => Packages.Any(x => x.StartsWith(p.name + "@"))))
                    log.AppendLine($"  ✔ {p.name} {p.version}");
            else log.AppendLine($"  ✘ {current.Error?.message}");
            current = null;
            log.AppendLine("完成");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ResultPath, log.ToString());
            Debug.Log("[XrPackageInstaller]\n" + log);
        }
    }
}
