#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Tests.PlayMode
{
    public class GameAppearanceRuntimeTests
    {
        static VisualConfig Config() => AssetDatabase.LoadAssetAtPath<VisualConfig>("Assets/_RulePyramid/Config/VisualConfig.asset");
        static LevelDefinition Level(EntityDefinition[] entities)
        {
            var list = new List<EntityDefinition>(entities);
            if (!list.Exists(e => e.kind == "Object" && e.subject == "ROBOT"))
                list.Add(new EntityDefinition { id = "actor", kind = "Object", subject = "ROBOT", cell = new GridCell(29, 1, 1) });
            foreach (var token in new[] { "ROBOT", "IS", "YOU" })
                list.Add(new EntityDefinition { id = "control-" + token, kind = "Text", token = token,
                    cell = new GridCell(token == "ROBOT" ? 0 : token == "IS" ? 1 : 2, 1, 3) });
            return new LevelDefinition
        {
            schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "appearance",
            bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(30, 3, 4) },
            terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(30, 0, 4) } },
            entities = list.ToArray()
        };
        }

        [Test]
        public void EveryIdentityHasDistinctSingleMeshAndTransformationKeepsRootAndPosition()
        {
            var config = Config(); var entities = new List<EntityDefinition>();
            foreach (var subject in Tokens.Subjects) entities.Add(new EntityDefinition
                { id = subject, kind = "Object", subject = subject, cell = new GridCell(entities.Count * 2, 1, 1) });
            var world = WorldModel.FromLevel(Level(entities.ToArray()));
            var host = new GameObject("身份模型测试"); var view = host.AddComponent<WorldView>(); view.config = config;
            try
            {
                view.Rebuild(world); view.RefreshOcclusion(null); var unique = new HashSet<Mesh>();
                foreach (var subject in Tokens.Subjects)
                {
                    Assert.IsTrue(view.TryGetView(subject, out var root));
                    var entry = config.AppearanceFor(subject); Assert.IsNotNull(entry.mesh); Assert.IsTrue(unique.Add(entry.mesh));
                    Assert.AreSame(entry.mesh, root.GetComponent<MeshFilter>().sharedMesh);
                    Assert.AreSame(entry.material, root.GetComponent<Renderer>().sharedMaterial);
                    Assert.LessOrEqual(entry.mesh.bounds.size.x, 1.001f);
                    Assert.LessOrEqual(entry.mesh.bounds.size.y, 1.001f);
                    Assert.LessOrEqual(entry.mesh.bounds.size.z, 1.001f);
                    Assert.AreEqual(1, entry.mesh.subMeshCount);
                    foreach (var collider in root.GetComponentsInChildren<Collider>()) Assert.IsFalse(collider.enabled);
                }
                Assert.AreEqual(.5f, config.AppearanceFor("SPRING").mesh.bounds.max.y, .001f);
                Assert.AreEqual(.5f, config.AppearanceFor("ROCK").mesh.bounds.max.y, .001f);
                view.TryGetView("ROCK", out var original); var position = original.position;
                world.Entity("ROCK").Subject = "FLAG"; view.AlignToState(world);
                view.TryGetView("ROCK", out var transformed); Assert.AreSame(original, transformed);
                Assert.AreEqual(position, transformed.position);
                Assert.AreSame(config.AppearanceFor("FLAG").mesh, transformed.GetComponent<MeshFilter>().sharedMesh);
                world.Entity("ROCK").Subject = "ROCK"; view.AlignToState(world);
                Assert.AreSame(config.AppearanceFor("ROCK").mesh, original.GetComponent<MeshFilter>().sharedMesh);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void AllWordsKeepOriginalTextMeshAppearanceAndTokens()
        {
            var config = Config(); var entities = new List<EntityDefinition>();
            var words = new List<string>(); words.AddRange(Tokens.Subjects); words.AddRange(Tokens.Props); words.AddRange(Tokens.Operators); words.Add("UNKNOWN");
            for (int i = 0; i < words.Count; i++) entities.Add(new EntityDefinition
                { id = "word" + i, kind = "Text", token = words[i], cell = new GridCell(i, 1, 1) });
            var world = WorldModel.FromLevel(Level(entities.ToArray()));
            var host = new GameObject("词牌字形测试"); var view = host.AddComponent<WorldView>(); view.config = config;
            try
            {
                view.Rebuild(world);
                for (int i = 0; i < words.Count; i++)
                {
                    view.TryGetView("word" + i, out var root);
                    var text = root.GetComponentInChildren<TextMesh>();
                    Assert.IsNotNull(text, words[i]);
                    Assert.AreEqual(words[i], text.text);
                    var ink = QualitySettings.activeColorSpace == ColorSpace.Linear ? config.wordInk.linear : config.wordInk;
                    for (int channel = 0; channel < 4; channel++) Assert.AreEqual(ink[channel], text.color[channel], 1f / 255f);
                    Assert.AreEqual(FontStyle.Normal, text.fontStyle);
                    Assert.AreEqual(.12f, text.characterSize);
                    Assert.AreEqual(32, text.fontSize);
                    Assert.AreEqual(TextAnchor.MiddleCenter, text.anchor);
                    Assert.AreEqual(TextAlignment.Center, text.alignment);
                }
                view.TryGetView("word" + words.IndexOf("LAVA"), out var lava);
                Assert.AreSame(config.lavaMaterial, lava.GetComponent<Renderer>().sharedMaterial);
                Assert.AreNotSame(config.AppearanceFor("LAVA").material, config.lavaMaterial);
                view.AlignToState(world); view.TryGetView("word0", out var first);
                Assert.AreEqual(1, first.GetComponentsInChildren<TextMesh>().Length);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
#endif
