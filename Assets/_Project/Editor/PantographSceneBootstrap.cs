using System.IO;
using System.Linq;
using PantographSim.App;
using PantographSim.Interaction;
using PantographSim.Parts.Mechanism;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PantographSim.Editor
{
    /// <summary>
    /// 建立維修模擬器的起始場景：地面、燈光、攝影機、可動集電弓與預覽互動元件。
    /// 編輯器載入後自動檢查：可動模型不存在就建立；場景不存在就建立；舊版場景就升級。
    /// 之後可從選單 Tools/集電弓/重建起始場景 重新產生。
    /// </summary>
    [InitializeOnLoad]
    public static class PantographSceneBootstrap
    {
        const string ScenePath = "Assets/_Project/Content/Scenes/PantographWorkshop.unity";
        const string RootName = "Pantograph";

        static PantographSceneBootstrap()
        {
            EditorApplication.delayCall += EnsureScene;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += EnsureScene;
            };
        }

        static void EnsureScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool rebuilt = false;
            if (PantographRigBuilder.NeedsBuild())
            {
                if (!PantographRigBuilder.Build()) return;
                rebuilt = true;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateScene();
                return;
            }

            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath && (rebuilt || Object.FindObjectOfType<PantographMechanism>() == null))
            {
                bool wasDirty = active.isDirty;
                UpgradeScene();
                EditorSceneManager.MarkSceneDirty(active);
                if (!wasDirty) EditorSceneManager.SaveScene(active);
                Debug.Log("[PantographSceneBootstrap] 場景已改用可動集電弓");
            }
        }

        [MenuItem("Tools/集電弓/重建起始場景")]
        public static void CreateSceneFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateScene();
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";

            var root = new GameObject(RootName);
            var bounds = AddRig(root, ground);

            var cam = Camera.main;
            if (cam != null)
            {
                float size = Mathf.Max(bounds.size.magnitude, 1f);
                cam.transform.position = bounds.center + new Vector3(1f, 0.6f, -1f).normalized * size * 1.2f;
                cam.transform.LookAt(bounds.center);
            }
            AddInteraction(root);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (buildScenes.All(s => s.path != ScenePath))
            {
                buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(bounds, false);
            Debug.Log($"[PantographSceneBootstrap] 已建立場景 {ScenePath}");
        }

        /// <summary>舊場景（靜態 FBX＋升降弓切換）→ 可動集電弓；保留使用者自己加的其他物件。</summary>
        static void UpgradeScene()
        {
            var root = GameObject.Find(RootName) ?? new GameObject(RootName);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var oldController = GameObject.Find("DemoController");
            if (oldController != null) Object.DestroyImmediate(oldController);

            AddRig(root, GameObject.Find("Ground"));
            AddInteraction(root);
        }

        /// <summary>放入可動集電弓，底架置中貼地；回傳升弓時的範圍。</summary>
        static Bounds AddRig(GameObject root, GameObject ground)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PantographRigBuilder.PrefabPath);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
            rig.transform.SetParent(root.transform, false);

            // Prefab 靜止姿態就是升弓姿態；以底架置中貼地
            var baseRenderer = rig.transform.Find("Base")?.GetComponent<Renderer>();
            var baseBounds = baseRenderer != null ? baseRenderer.bounds : Bounds(rig);
            rig.transform.position -= new Vector3(baseBounds.center.x, baseBounds.min.y, baseBounds.center.z);

            var bounds = Bounds(rig);
            if (ground != null) ground.transform.localScale = Vector3.one * Mathf.Max(1f, bounds.size.magnitude * 0.3f);
            return bounds;
        }

        static void AddInteraction(GameObject root)
        {
            OrbitCameraController orbit = null;
            var cam = Camera.main;
            if (cam != null)
            {
                orbit = cam.GetComponent<OrbitCameraController>();
                if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCameraController>();
                cam.nearClipPlane = 0.05f;
            }
            var controllerGo = new GameObject("DemoController");
            controllerGo.AddComponent<PantographDemoController>().Bind(orbit, root.GetComponentInChildren<PantographMechanism>());
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
