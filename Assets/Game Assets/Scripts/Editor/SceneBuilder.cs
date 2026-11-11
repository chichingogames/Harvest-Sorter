using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the base structure of Game.unity (skeleton.md §1, §9.1, §10). Re-runnable: missing roots are
/// created, existing ones are kept, and only "GamePlay/Level Art" is regenerated from the art layout JSON.
/// </summary>
public static class SceneBuilder
{
    private const string ScenePath = "Assets/Game Assets/Scenes/Game.unity";
    private const string ModelsPath = "Assets/Game Assets/Models/";
    private const string LayoutPath = "Assets/Game Assets/Art Data/Level_{0:00}_Art_Layout.json";
    private const string LevelArtName = "Level Art";

    // Only the static board is placed; "order cue" and "preview state" rows are gameplay state.
    private static readonly string[] _staticGroups = { "board", "dressing" };

    private static readonly Color _background = new Color32(0xD8, 0xAF, 0x77, 0xFF); // "Warm sand" swatch

    [Serializable] private class Layout { public int level; public LayoutCamera camera; public Placement[] placements; }
    [Serializable] private class LayoutCamera { public float height; public float[] position; public float[] target; }
    [Serializable] private class Placement { public string asset; public string label; public float[] position; public float yawDegrees; public float[] scale; public string group; }

    [MenuItem("Harvest Sorter/Build Base Scene")]
    public static void BuildBaseScene() => Build(1);

    [MenuItem("Harvest Sorter/Rebuild Level Art/Level 1")]
    public static void BuildLevel1Art() => Build(1);

    [MenuItem("Harvest Sorter/Rebuild Level Art/Level 2")]
    public static void BuildLevel2Art() => Build(2);

    public static void Build(int level)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }

        var layout = LoadLayout(level);

        GetOrCreateRoot("Managers");
        var gamePlay = GetOrCreateRoot("GamePlay");
        BuildLevelArt(gamePlay.transform, layout);
        SetupCamera(layout.camera);
        SetupLight();
        BuildCanvas();
        BuildEventSystem();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[SceneBuilder] Built base scene with level {level} art.");
    }

    private static Layout LoadLayout(int level)
    {
        var path = string.Format(LayoutPath, level);
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(path));
        if (layout?.placements == null) throw new InvalidDataException($"Could not read placements from {path}");
        return layout;
    }

    // Art Data is Blender space (Z up). The models are exported so that Blender (x, y, z) == Unity (x, z, y),
    // which mirrors handedness, so a Blender yaw about +Z is the negative Unity yaw about +Y.
    private static Vector3 ToUnity(float[] p) => new Vector3(p[0], p[2], p[1]);

    private static void BuildLevelArt(Transform gamePlay, Layout layout)
    {
        var old = gamePlay.Find(LevelArtName);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(LevelArtName).transform;
        root.SetParent(gamePlay, false);

        foreach (var p in layout.placements)
        {
            if (Array.IndexOf(_staticGroups, p.group) < 0) continue;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelsPath + p.asset + ".fbx");
            if (model == null)
            {
                Debug.LogWarning($"[SceneBuilder] Missing model {p.asset}.fbx for placement {p.label}");
                continue;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            go.name = p.label;
            go.transform.localPosition = ToUnity(p.position);
            go.transform.localRotation = Quaternion.Euler(0f, -p.yawDegrees, 0f);
            go.transform.localScale = ToUnity(p.scale);
            go.isStatic = true;
        }
    }

    private static void SetupCamera(LayoutCamera layoutCamera)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }

        cam.transform.SetParent(null);
        cam.transform.position = ToUnity(layoutCamera.position);
        cam.transform.LookAt(ToUnity(layoutCamera.target), Vector3.forward);
        cam.orthographic = true;
        cam.orthographicSize = layoutCamera.height * 0.5f; // layout height is the full vertical extent
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = _background;
    }

    private static void SetupLight()
    {
        var light = UnityEngine.Object.FindFirstObjectByType<Light>();
        if (light == null || light.type != LightType.Directional)
        {
            light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
        }

        // Key light from the upper left of the screen, as in the level previews.
        light.transform.rotation = Quaternion.Euler(55f, 135f, 0f);
        light.color = new Color32(0xFF, 0xF1, 0xDC, 0xFF);
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.6f;
    }

    private static void BuildCanvas()
    {
        var canvasGo = GetOrCreateRoot("Canvas");
        var canvas = GetOrAdd<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = GetOrAdd<CanvasScaler>(canvasGo);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        GetOrAdd<GraphicRaycaster>(canvasGo);
        canvasGo.layer = LayerMask.NameToLayer("UI");

        var container = GetOrCreateChild(canvasGo.transform, "Container");   // SafeArea goes here
        GetOrCreateChild(container, "InGame");
        GetOrCreateChild(container, "PopUps");

        var dark = GetOrCreateChild(canvasGo.transform, "Dark Area", out var createdDark);
        if (createdDark)
        {
            var image = dark.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            dark.gameObject.SetActive(false);
        }

        var loading = GetOrCreateChild(canvasGo.transform, "Loading Screen", out var createdLoading);
        if (createdLoading) loading.gameObject.SetActive(false);
        var transition = GetOrCreateChild(canvasGo.transform, "Transition Screen", out var createdTransition);
        if (createdTransition) transition.gameObject.SetActive(false);
    }

    private static void BuildEventSystem()
    {
        var go = GetOrCreateRoot("EventSystem");
        GetOrAdd<EventSystem>(go);
        var legacy = go.GetComponent<StandaloneInputModule>();
        if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
        GetOrAdd<InputSystemUIInputModule>(go);
    }

    private static GameObject GetOrCreateRoot(string name)
    {
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return new GameObject(name);
    }

    private static Transform GetOrCreateChild(Transform parent, string name) => GetOrCreateChild(parent, name, out _);

    private static Transform GetOrCreateChild(Transform parent, string name, out bool created)
    {
        var child = parent.Find(name);
        created = child == null;
        if (!created) return child;

        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.gameObject.layer = LayerMask.NameToLayer("UI");
        return rt;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();
}
