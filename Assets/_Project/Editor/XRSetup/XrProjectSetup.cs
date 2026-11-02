using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PantographSim.Editor.XR
{
    /// <summary>
    /// VR 專案設定（Meta Quest 3S 經 Quest Link，PC Standalone）：XR Management 啟用 OpenXR、Initialize XR on Startup；
    /// OpenXR 勾 Oculus Touch 與 Meta Quest Touch Plus 兩個 Interaction Profile、Single Pass Instanced；
    /// Input System 在 Editor 不看視窗焦點（戴上頭盔時 Unity 視窗常常不是焦點）；匯入 XRI Starter Assets（預設輸入動作）。
    /// 結果寫到 Logs/XRSetupReport.txt（套件版本、OpenXR 設定、Quest Link 安裝與目前 OpenXR runtime）。
    /// 觸發：Logs/xr-setup.request 或選單 PantographSim/XR/Configure OpenXR (Quest Link)。可重跑。
    /// </summary>
    [InitializeOnLoad]
    public static class XrProjectSetup
    {
        const string RequestPath = "Logs/xr-setup.request";
        const string ReportPath = "Logs/XRSetupReport.txt";
        const string InputSettingsPath = "Assets/_Project/Settings/InputSystem.inputsettings.asset";
        static double nextPoll;

        static XrProjectSetup() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(RequestPath);
            try { Run(); }
            catch (Exception e) { File.WriteAllText(ReportPath, "設定失敗：" + e); Debug.LogException(e); }
        }

        [MenuItem("PantographSim/XR/Configure OpenXR (Quest Link)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("VR 設定報告　" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Unity " + Application.unityVersion);

            // ---- 1. XR Plug-in Management：PC（Standalone）啟用 OpenXR ----
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            const BuildTargetGroup pc = BuildTargetGroup.Standalone;
            if (!perTarget.HasSettingsForBuildTarget(pc)) perTarget.CreateDefaultSettingsForBuildTarget(pc);
            if (!perTarget.HasManagerSettingsForBuildTarget(pc)) perTarget.CreateDefaultManagerSettingsForBuildTarget(pc);
            var general = perTarget.SettingsForBuildTarget(pc);
            general.InitManagerOnStart = true;
            bool assigned = XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", pc);
            EditorUtility.SetDirty(general); EditorUtility.SetDirty(perTarget);
            if (general.Manager != null) EditorUtility.SetDirty(general.Manager);
            sb.AppendLine($"XR Management（PC）：Initialize XR on Startup = {general.InitManagerOnStart}；Loader = {string.Join(", ", general.Manager.activeLoaders.Select(l => l.GetType().Name))}（{(assigned ? "已指定" : "原本就有")}）");

            // ---- 2. OpenXR：兩個控制器設定檔、Single Pass Instanced ----
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(pc);
            if (openxr != null)
            {
                openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                foreach (var f in openxr.GetFeatures<OpenXRInteractionFeature>())
                    if (f is OculusTouchControllerProfile || f is MetaQuestTouchPlusControllerProfile) f.enabled = true;
                EditorUtility.SetDirty(openxr);
                sb.AppendLine("OpenXR（PC）：Render Mode = " + openxr.renderMode);
                foreach (var f in openxr.GetFeatures<OpenXRInteractionFeature>().Where(f => f.enabled))
                    sb.AppendLine("  ✔ Interaction Profile：" + f.GetType().Name);
            }
            else sb.AppendLine("⚠ 找不到 OpenXR 設定（OpenXR 套件可能還沒裝好）");

            // ---- 3. Input System：Editor 中不看視窗焦點 ----
            var input = InputSystem.settings;
            if (input == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(input)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputSettings>(InputSettingsPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<InputSettings>();
                    Directory.CreateDirectory(Path.GetDirectoryName(InputSettingsPath));
                    AssetDatabase.CreateAsset(asset, InputSettingsPath);
                }
                InputSystem.settings = asset;
                input = asset;
            }
            input.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            input.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EditorUtility.SetDirty(input);
            sb.AppendLine("Input System：背景不停止輸入、Editor 中所有裝置輸入都進 Game 視窗（" + AssetDatabase.GetAssetPath(input) + "）");

            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var prop = so.FindProperty("activeInputHandler");
                sb.AppendLine("Active Input Handling = " + (prop == null ? "?" : prop.intValue == 2 ? "Both" : prop.intValue == 1 ? "Input System" : "Input Manager（舊）"));
            }

            // ---- 4. XRI Starter Assets（預設輸入動作）----
            var xri = PackageInfo.FindForAssembly(typeof(UnityEngine.XR.Interaction.Toolkit.XRBaseInteractable).Assembly);
            if (xri != null)
            {
                var starter = Sample.FindByPackage(xri.name, xri.version).FirstOrDefault(s => s.displayName == "Starter Assets");
                if (starter.displayName == null) sb.AppendLine("⚠ 找不到 XRI Starter Assets 範例");
                else
                {
                    if (!starter.isImported) starter.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow);
                    sb.AppendLine("XRI Starter Assets：" + (starter.isImported ? "已匯入 " : "匯入中 ") + starter.importPath);
                }
            }

            AssetDatabase.SaveAssets();

            // ---- 5. 套件版本 ----
            sb.AppendLine();
            sb.AppendLine("## 套件版本");
            foreach (var p in PackageInfo.GetAllRegisteredPackages().Where(p => p.name.StartsWith("com.unity.xr") || p.name == "com.unity.inputsystem").OrderBy(p => p.name))
                sb.AppendLine($"  {p.name} {p.version}");

            // ---- 6. Quest Link 與目前的 OpenXR runtime ----
            sb.AppendLine();
            sb.AppendLine("## 電腦上的 OpenXR runtime");
            string active = Reg(@"HKLM\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime");
            sb.AppendLine("  目前（ActiveRuntime）：" + (string.IsNullOrEmpty(active) ? "（未設定）" : active));
            bool link = active.IndexOf("oculus", StringComparison.OrdinalIgnoreCase) >= 0 || active.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) >= 0;
            sb.AppendLine("  " + (link ? "✔ 已是 Meta Quest Link" : "✘ 不是 Meta Quest Link：到 Meta Quest Link app → 設定 → 一般 → OpenXR Runtime 按「設為使用中」"));
            string questJson = @"C:\Program Files\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json";
            string oldJson = @"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json";
            sb.AppendLine("  Quest Link 安裝：" + (File.Exists(questJson) ? "✔ " + questJson : File.Exists(oldJson) ? "✔ " + oldJson : "✘ 找不到 oculus_openxr_64.json"));
            string avail = RegAll(@"HKLM\SOFTWARE\Khronos\OpenXR\1\AvailableRuntimes");
            if (avail.Length > 0) sb.AppendLine("  已安裝的 runtime：\n" + avail);

            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log("[XrProjectSetup]\n" + sb);
        }

        static string RunReg(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("reg.exe", args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return o;
            }
            catch { return ""; }
        }

        static string Reg(string key, string value)
        {
            foreach (var line in RunReg($"query \"{key}\" /v {value} /reg:64").Split('\n'))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"REG_(?:EXPAND_)?SZ\s+(.*)$");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return "";
        }

        static string RegAll(string key) =>
            string.Join("\n", RunReg($"query \"{key}\" /reg:64").Split('\n').Where(l => l.Contains("REG_DWORD")).Select(l => "    " + l.Trim()));
    }
}
