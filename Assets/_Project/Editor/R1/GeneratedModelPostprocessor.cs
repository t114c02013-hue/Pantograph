using System.IO;
using UnityEditor;
using UnityEngine;

namespace PantographSim.Editor
{
    /// <summary>
    /// R1.5a a2：只套用在 Worksite/Models/Generated 與 PantographAssembly/Models 的模型：
    /// Scale Factor 1、Convert Units 開、不產生 collider、Read/Write 關、Normals 用 Import、材質改用外部 Materials/Worksite（7 個，Base Map＝Palette.png）。
    /// Palette.png：點取樣、不產生 mipmap（每格一個顏色，不能混色）。
    /// </summary>
    public class GeneratedModelPostprocessor : AssetPostprocessor
    {
        public const string MaterialFolder = "Assets/_Project/Materials/Worksite";
        public const string PalettePath = "Assets/_Project/Worksite/Models/Generated/Palette.png";
        public static readonly string[] MaterialNames = { "Paint", "Roof", "Walkway", "Glass", "Dark", "Metal", "Insulator" };

        public static bool Applies(string path) =>
            path.Replace('\\', '/').Contains("/Worksite/Models/Generated/") || path.Replace('\\', '/').Contains("/PantographAssembly/Models/");

        void OnPreprocessModel()
        {
            if (!Applies(assetPath)) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.bakeAxisConversion = true;   // Blender 的 Z 上、Y 前轉成 Unity 的 Y 上、Z 前，整個階層一起轉（子物件的位置也轉）
            m.addCollider = false;
            m.isReadable = false;
            m.importNormals = ModelImporterNormals.Import;
            m.importAnimation = false;
            m.importCameras = false;
            m.importLights = false;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var n in MaterialNames)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{n}.mat");
                if (mat != null) m.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mat);
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').EndsWith("/Generated/Palette.png")) return;
            var t = (TextureImporter)assetImporter;
            t.filterMode = FilterMode.Point;
            t.mipmapEnabled = false;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = TextureWrapMode.Clamp;
        }

        /// <summary>建立 7 個外部材質（Standard、Base Map＝Palette.png）；已存在就只更新貼圖。要在匯入模型之前呼叫（匯入中不能建資產）。</summary>
        public static void EnsureMaterials()
        {
            Directory.CreateDirectory(MaterialFolder);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            foreach (var n in MaterialNames)
            {
                string path = $"{MaterialFolder}/{n}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Standard")) { name = n }; AssetDatabase.CreateAsset(mat, path); }
                mat.mainTexture = tex;
                mat.color = Color.white;
                mat.SetFloat("_Glossiness", n == "Glass" ? 0.8f : n == "Metal" ? 0.45f : 0.2f);
                mat.SetFloat("_Metallic", n == "Metal" ? 0.6f : 0f);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/Worksite/Reimport Generated Models")]
        public static void ReimportGenerated()
        {
            AssetDatabase.ImportAsset(PalettePath, ImportAssetOptions.ForceUpdate);
            EnsureMaterials();
            foreach (var dir in new[] { "Assets/_Project/Worksite/Models/Generated", "Assets/_Project/Parts/PantographAssembly/Models" })
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }
    }
}
