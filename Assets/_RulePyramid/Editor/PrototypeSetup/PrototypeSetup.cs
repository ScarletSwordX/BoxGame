using System.IO;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RulePyramid.Editor
{
    public static class PrototypeSetup
    {
        const string Root = "Assets/_RulePyramid";

        [MenuItem("Tools/RulePyramid/Create or Repair Prototype")]
        public static void CreateOrRepair()
        {
            Directory.CreateDirectory("Assets/_RulePyramid/Config");
            Directory.CreateDirectory("Assets/_RulePyramid/Scenes");
            var visual = LoadOrCreate<VisualConfig>(Root + "/Config/VisualConfig.asset");
            EnsureMaterials(visual);
            var catalog = LoadOrCreate<LevelCatalog>(Root + "/Config/LevelCatalog.asset");
            catalog.levels = new[]
            {
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L01.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L02.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L03.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L04.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L05.json"),
                AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Content/Levels/L06.json")
            };
            EditorUtility.SetDirty(catalog);
            EnsureScene(visual, catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[RulePyramid] Prototype scene and configs repaired.");
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void EnsureMaterials(VisualConfig visual)
        {
            visual.terrainMaterial = MakeMat("Terrain", new Color(0.45f, 0.45f, 0.48f));
            visual.redMaterial = MakeMat("Red", new Color(0.85f, 0.28f, 0.22f));
            visual.blueMaterial = MakeMat("Blue", new Color(0.25f, 0.45f, 0.9f));
            visual.pinkMaterial = MakeMat("Pink", new Color(0.9f, 0.4f, 0.7f));
            visual.pinkHollowMaterial = MakeMat("PinkHollow", new Color(0.95f, 0.55f, 0.8f, 0.35f), true);
            visual.textMaterial = MakeMat("Text", new Color(0.92f, 0.88f, 0.6f));
            visual.anchoredTextMaterial = MakeMat("TextAnchored", new Color(0.75f, 0.7f, 0.4f));
            EditorUtility.SetDirty(visual);
        }

        static Material MakeMat(string name, Color color, bool transparent = false)
        {
            var path = Root + "/Config/Mat_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (transparent)
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureScene(VisualConfig visual, LevelCatalog catalog)
        {
            var scenePath = Root + "/Scenes/Prototype.unity";
            var scene = File.Exists(scenePath)
                ? EditorSceneManager.OpenScene(scenePath)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
            }
            if (Object.FindObjectOfType<Light>() == null)
            {
                var light = new GameObject("Directional Light");
                var l = light.AddComponent<Light>();
                l.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            var bootstrapGo = GameObject.Find("GameBootstrap") ?? new GameObject("GameBootstrap");
            var bootstrap = bootstrapGo.GetComponent<GameBootstrap>() ?? bootstrapGo.AddComponent<GameBootstrap>();
            var worldGo = GameObject.Find("WorldView") ?? new GameObject("WorldView");
            var world = worldGo.GetComponent<WorldView>() ?? worldGo.AddComponent<WorldView>();
            world.config = visual;
            var anim = worldGo.GetComponent<EventAnimator>() ?? worldGo.AddComponent<EventAnimator>();
            anim.worldView = world;
            var cam = Camera.main;
            var slots = cam.GetComponent<CameraSlotsController>() ?? cam.gameObject.AddComponent<CameraSlotsController>();
            slots.config = visual;
            var input = bootstrapGo.GetComponent<InputAdapter>() ?? bootstrapGo.AddComponent<InputAdapter>();
            input.cameraSlots = slots;
            var hudGo = GameObject.Find("HUD") ?? new GameObject("HUD");
            var ui = hudGo.GetComponent<UIDocument>() ?? hudGo.AddComponent<UIDocument>();
            ui.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/UIToolkit/Hud.uxml");
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/UIToolkit/PanelSettings.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, Root + "/UIToolkit/PanelSettings.asset");
            }
            ui.panelSettings = panel;
            var hud = hudGo.GetComponent<HudController>() ?? hudGo.AddComponent<HudController>();
            hud.document = ui;
            bootstrap.catalog = catalog;
            bootstrap.visualConfig = visual;
            bootstrap.worldView = world;
            bootstrap.animator = anim;
            bootstrap.cameraSlots = slots;
            bootstrap.inputAdapter = input;
            bootstrap.hud = hud;
            EditorSceneManager.SaveScene(scene, scenePath);
        }
    }
}
