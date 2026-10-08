using System;
using System.Collections.Generic;
using System.Linq;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public static class GameAppearanceSetup
    {
        const string Root = "Assets/_RulePyramid/Art/Objects";

        [MenuItem("Tools/规则工坊/接入游戏造型")]
        public static void Apply()
        {
            var config = AssetDatabase.LoadAssetAtPath<VisualConfig>("Assets/_RulePyramid/Config/VisualConfig.asset");
            Ensure(config);
            AssetDatabase.SaveAssets();
        }

        public static void Ensure(VisualConfig config)
        {
            if (config == null) return;
            System.IO.Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var atlas = Texture(Root + "/IdentityAtlas.png", false);
            var lava = Texture(Root + "/LavaCrust.png", true);
            var emission = Texture(Root + "/LavaEmission.png", true);
            var appearances = config.objectAppearances == null ? new List<VisualConfig.ObjectAppearance>()
                : config.objectAppearances.Where(a => a != null).ToList();
            foreach (var subject in new[] { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA" })
            {
                var entry = appearances.Find(a => a.subject == subject);
                if (entry == null) { entry = new VisualConfig.ObjectAppearance { subject = subject }; appearances.Add(entry); }
                if (entry.mesh == null) entry.mesh = ModelMesh(subject);
                if (entry.material == null)
                {
                    var path = Root + "/Mat_" + subject + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        var identity = Identity(config, subject);
                        material = identity != null ? new Material(identity) : new Material(Shader.Find("Standard"));
                        material.name = "Object " + subject;
                        material.mainTexture = atlas;
                        material.SetFloat("_Glossiness", subject == "SPRING" ? .55f : .20f);
                        material.SetFloat("_Metallic", subject == "SPRING" ? .48f : 0f);
                        if (subject == "LAVA")
                        {
                            material.shader = Shader.Find("RuleWorkshop/PlayerCutaway");
                            material.color = Color.white;
                            material.mainTexture = lava;
                            material.SetTexture("_EmissionMap", emission);
                            material.SetColor("_EmissionColor", new Color(1.1f, .35f, .04f));
                            material.SetVector("_FlowSpeed", new Vector4(.012f, .007f, 0, 0));
                            material.EnableKeyword("_EMISSION");
                        }
                        AssetDatabase.CreateAsset(material, path);
                    }
                    entry.material = material;
                }
            }
            config.objectAppearances = appearances.ToArray();
            if (config.lavaMaterial == null)
            {
                var path = Root + "/Mat_LavaWord.mat";
                config.lavaMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (config.lavaMaterial == null)
                {
                    config.lavaMaterial = new Material(Shader.Find("Standard"));
                    config.lavaMaterial.color = new Color(.92f, .22f, .06f);
                    AssetDatabase.CreateAsset(config.lavaMaterial, path);
                }
            }
            EditorUtility.SetDirty(config);
        }

        static Texture2D Texture(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少造型贴图：" + path);
            if (importer.wrapMode != (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp)
                || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("Tools/规则工坊/重载导出的游戏模型")]
        public static void RebuildImportedMeshes()
        {
            foreach (var subject in new[] { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA" }) ModelMesh(subject, true);
            AssetDatabase.SaveAssets();
        }

        static Mesh ModelMesh(string subject, bool rebuild = false)
        {
            var path = Root + "/" + subject + "_Mesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && !rebuild) return existing;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/" + subject + ".fbx");
            if (model == null) throw new InvalidOperationException("缺少模型：" + subject);
            var filter = model.GetComponentInChildren<MeshFilter>();
            var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            mesh.name = subject + " Game Mesh";
            // 包含模型根节点的 FBX 轴与单位换算，让运行时网格直接使用 Unity 的 Y 向上。
            var matrix = filter.transform.localToWorldMatrix;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++) normals[i] = matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
            mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            // 仅消除导入单位倍率；不按属性或受控状态缩放对象。
            float unit = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = unit > 2f || unit < .1f ? 1f / unit : 1f;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - bounds.center) * scale;
            mesh.vertices = vertices; mesh.RecalculateBounds();
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Identity(VisualConfig c, string subject)
        {
            switch (subject)
            {
                case "ROBOT": return c.redMaterial;
                case "ROCK": return c.blueMaterial;
                case "CLOUD": return c.cloudMaterial;
                case "SPRING": return c.springMaterial;
                case "FLAG": return c.pinkMaterial;
                case "WALL": return c.wallMaterial;
                default: return c.lavaMaterial;
            }
        }
    }
}
