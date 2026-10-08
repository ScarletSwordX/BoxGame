using System;
using System.Collections.Generic;
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

        [MenuItem("Tools/规则工坊/创建或修复原型")]
        public static void CreateOrRepair()
        {
            Directory.CreateDirectory("Assets/_RulePyramid/Config");
            Directory.CreateDirectory("Assets/_RulePyramid/Scenes");
            var visual = LoadOrCreate<VisualConfig>(Root + "/Config/VisualConfig.asset");
            EnsureMaterials(visual);
            GameAppearanceSetup.Ensure(visual);
            var catalog = LoadOrCreate<LevelCatalog>(Root + "/Config/LevelCatalog.asset");
            ApplyCatalogManifest(catalog, ReadCatalogManifest());
            EnsureScene(visual, catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[规则工坊] Prototype 场景与关卡目录已修复，共 " + catalog.Count + " 关。");
        }

        public const string CatalogManifestPath = Root + "/Content/Levels/catalog.json";

        public static LevelCatalogManifest ReadCatalogManifest()
        {
            var manifest = JsonUtility.FromJson<LevelCatalogManifest>(File.ReadAllText(CatalogManifestPath));
            if (manifest == null || manifest.levels == null || manifest.levels.Length == 0)
                throw new InvalidOperationException("关卡目录没有有效章节。");
            return manifest;
        }

        [MenuItem("Tools/规则工坊/同步正式关卡目录")]
        public static void UpdateCatalog()
        {
            var catalog = LoadOrCreate<LevelCatalog>(Root + "/Config/LevelCatalog.asset");
            ApplyCatalogManifest(catalog, ReadCatalogManifest());
            AssetDatabase.SaveAssets();
            Debug.Log("[规则工坊] 已按 catalog.json 同步 " + catalog.Count + " 关。");
        }

        public static void ApplyCatalogManifest(LevelCatalog catalog, LevelCatalogManifest manifest)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (manifest?.levels == null || manifest.levels.Length == 0)
                throw new InvalidOperationException("关卡目录没有有效章节。");
            // 先完整解析，再更新资产，避免缺失阶段导致半份目录被保存。
            var levels = new TextAsset[manifest.levels.Length];
            var chapters = new HashSet<TextAsset>();
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = LoadCatalogMap(manifest.levels[i]);
                if (!chapters.Add(levels[i])) throw new InvalidOperationException("章节入口重复：" + manifest.levels[i]);
            }
            var sequences = new List<LevelStageSequence>();
            var sequenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedMaps = new HashSet<TextAsset>();
            foreach (var sequence in manifest.stageSequences ?? Array.Empty<LevelStageManifest>())
            {
                if (sequence == null || string.IsNullOrWhiteSpace(sequence.id) ||
                    sequence.maps == null || sequence.maps.Length == 0)
                    throw new InvalidOperationException("阶段组必须有名称和地图。");
                if (!sequenceIds.Add(sequence.id)) throw new InvalidOperationException("阶段组名称重复：" + sequence.id);
                var maps = new TextAsset[sequence.maps.Length];
                for (int i = 0; i < maps.Length; i++)
                {
                    maps[i] = LoadCatalogMap(sequence.maps[i]);
                    if (!claimedMaps.Add(maps[i])) throw new InvalidOperationException("阶段地图重复关联：" + sequence.maps[i]);
                    if (i > 0 && chapters.Contains(maps[i]))
                        throw new InvalidOperationException("后续阶段不能同时作为章节入口：" + sequence.maps[i]);
                }
                if (!chapters.Contains(maps[0])) throw new InvalidOperationException("阶段组首图不在章节目录：" + sequence.id);
                sequences.Add(new LevelStageSequence { id = sequence.id, maps = maps });
            }
            catalog.levels = levels;
            catalog.stageSequences = sequences.ToArray();
            EditorUtility.SetDirty(catalog);
        }

        static TextAsset LoadCatalogMap(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidOperationException("阶段地图路径为空。");
            var relative = relativePath.Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Contains(".."))
                throw new InvalidOperationException("阶段地图必须位于 Content 内：" + relativePath);
            if (relative.StartsWith("levels/", StringComparison.OrdinalIgnoreCase))
                relative = "Levels/" + relative.Substring(7);
            var path = Root + "/Content/" + relative;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) throw new InvalidOperationException("缺少阶段地图：" + path);
            return asset;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        [MenuItem("Tools/规则工坊/应用暖砂配色")]
        public static void ApplyWarmPalette()
        {
            var visual = LoadOrCreate<VisualConfig>(Root + "/Config/VisualConfig.asset");
            EnsureMaterials(visual, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[规则工坊] 已应用 VA-v1.1 暖砂配色，保留关卡、玻璃与既有反馈。");
        }

        static void EnsureMaterials(VisualConfig visual, bool resetPalette = false)
        {
            bool apply = resetPalette || visual.paletteVersion < 1;
            EnsurePaletteMaterial(ref visual.terrainMaterial, "Terrain", new Color32(231, 216, 197, 255), apply);
            EnsurePaletteMaterial(ref visual.structureMaterial, "Structure", new Color32(191, 179, 214, 255), apply);
            EnsurePaletteMaterial(ref visual.redMaterial, "Red", new Color32(186, 116, 99, 255), apply);
            EnsurePaletteMaterial(ref visual.blueMaterial, "Blue", new Color32(123, 137, 152, 255), apply);
            EnsurePaletteMaterial(ref visual.pinkMaterial, "Pink", new Color32(194, 143, 162, 255), apply);
            EnsurePaletteMaterial(ref visual.cloudMaterial, "Cloud", new Color32(183, 210, 216, 255), apply);
            EnsurePaletteMaterial(ref visual.springMaterial, "Spring", new Color32(158, 141, 183, 255), apply);
            EnsurePaletteMaterial(ref visual.wallMaterial, "Wall", new Color32(157, 148, 134, 255), apply);
            EnsurePaletteMaterial(ref visual.textMaterial, "Text", new Color32(212, 215, 221, 255), apply);
            EnsurePaletteMaterial(ref visual.nounTextMaterial, "TextNoun", new Color32(121, 188, 179, 255), apply);
            EnsurePaletteMaterial(ref visual.operatorTextMaterial, "TextOperator", new Color32(246, 238, 221, 255), apply);
            EnsurePaletteMaterial(ref visual.propertyTextMaterial, "TextProperty", new Color32(236, 198, 107, 255), apply);
            // 玻璃沿用作者的材质与透明参数，不作为身份色一并重置。
            if (visual.pinkHollowMaterial == null)
                visual.pinkHollowMaterial = MakeMat("PinkHollow", new Color(0.95f, 0.55f, 0.8f, 0.35f), true);
            if (visual.anchoredTextMaterial == null) visual.anchoredTextMaterial = visual.textMaterial;
            if (apply)
            {
                visual.backgroundColor = new Color32(243, 239, 230, 255);
                visual.wordInk = new Color32(41, 50, 70, 255);
                visual.paletteVersion = 1;
            }
            EditorUtility.SetDirty(visual);
        }

        static void EnsurePaletteMaterial(ref Material material, string name, Color color, bool apply)
        {
            if (material == null) material = MakeMat(name, color);
            if (!apply) return;
            material.color = color;
            EditorUtility.SetDirty(material);
        }

        static Material MakeMat(string name, Color color, bool transparent = false)
        {
            var path = Root + "/Config/Mat_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            if (created) mat.color = color;
            if (created && transparent)
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
            if (UnityEngine.Object.FindObjectOfType<Light>() == null)
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
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = visual.backgroundColor;
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
