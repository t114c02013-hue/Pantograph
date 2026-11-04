using System.IO;
using PantographSim.XR;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PantographSim.Editor.XR
{
    /// <summary>VR 試玩修正 第 5 點：把 Quest Touch Plus 模型掛到控制器：Model Parent／Visual_Offset／CTRL_TouchPlus_*，Transform 全部 0；方向不對只調 Visual_Offset。</summary>
    public static class ControllerVisualBuilder
    {
        public const string Folder = "Assets/_Project/Interaction/XR/ControllerVisual";
        /// <summary>Unity 匯入 Blender FBX 後模型前後相反（根節點歸零時 +Z 朝後），在 Visual_Offset 轉回來，錨點才會和報告一致；頭盔實測再微調。</summary>
        const float ImportedYawFix = 180f;

        /// <summary>掛上模型，把控制器上的 Attach_Tool、Poke_Tip 由呼叫端依 anchor 放到模型的錨點。</summary>
        public static GameObject Mount(Transform controller, bool left, ActionBasedController ctrl)
        {
            string side = left ? "L" : "R";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/Models/CTRL_TouchPlus_{side}.fbx");
            if (fbx == null) throw new System.Exception("找不到控制器模型 CTRL_TouchPlus_" + side);
            var parent = new GameObject("Model Parent").transform; parent.SetParent(controller, false);
            var offset = new GameObject("Visual_Offset").transform; offset.SetParent(parent, false);
            offset.localRotation = Quaternion.Euler(0f, ImportedYawFix, 0f);
            var model = Object.Instantiate(fbx, offset, false);
            model.name = fbx.name;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            var mat = Material(side);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            model.AddComponent<XrControllerVisual>().Setup(left);
            ctrl.modelParent = parent; ctrl.model = model.transform;
            return model;
        }

        /// <summary>Standard 材質，貼圖沿用模型原檔的 BaseColor。</summary>
        static Material Material(string side)
        {
            Directory.CreateDirectory(Folder + "/Materials");
            string path = $"{Folder}/Materials/MAT_TouchPlus_{side}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Textures/CTRL_TouchPlus_{side}_BaseColor.png");
            mat.SetFloat("_Glossiness", 0.35f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    /// <summary>控制器模型匯入設定：不用檔案內嵌的材質與貼圖、不要 collider、不要動畫。</summary>
    public class ControllerModelPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(ControllerVisualBuilder.Folder)) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f; m.useFileUnits = true; m.addCollider = false; m.isReadable = false;
            m.importCameras = false; m.importLights = false; m.importAnimation = false; m.animationType = ModelImporterAnimationType.None;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
