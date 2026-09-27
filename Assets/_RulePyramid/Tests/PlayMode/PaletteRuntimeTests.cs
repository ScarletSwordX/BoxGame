using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class PaletteRuntimeTests
    {
        static Material MakeMaterial()
        {
            return new Material(Shader.Find("Standard"));
        }

        static EntityDefinition Object(string id, string subject, int x, int z) =>
            new EntityDefinition { id = id, kind = "Object", subject = subject, cell = new GridCell(x, 1, z) };

        static EntityDefinition Word(string id, string token, int x, int z, bool anchored = false) =>
            new EntityDefinition { id = id, kind = "Text", token = token,
                cell = new GridCell(x, 1, z), anchored = anchored };

        static LevelDefinition Level(int maxX, params EntityDefinition[] entities) => new LevelDefinition
        {
            schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "palette-test", title = "配色测试",
            bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(maxX, 3, 6) },
            terrain = new[] { new GridCellBox { id = "floor", min = new GridCell(0, 0, 0),
                max = new GridCell(maxX, 0, 6) } },
            entities = entities
        };

        static Material ViewMaterial(WorldView view, string id)
        {
            Assert.IsTrue(view.TryGetView(id, out var entity), id);
            return entity.GetComponent<Renderer>().sharedMaterial;
        }

        [Test]
        public void SixSubjectsAndEveryWordClassUseIdentityAndInkWithoutChangingTextSize()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var materials = new List<Material>();
            Material Add() { var material = MakeMaterial(); materials.Add(material); return material; }
            config.redMaterial = Add(); config.blueMaterial = Add(); config.cloudMaterial = Add();
            config.springMaterial = Add(); config.pinkMaterial = Add(); config.wallMaterial = Add();
            config.lavaMaterial = Add();
            config.nounTextMaterial = Add(); config.operatorTextMaterial = Add();
            config.propertyTextMaterial = Add(); config.textMaterial = Add();
            config.anchoredTextMaterial = Add();
            config.wordInk = new Color(0.17f, 0.23f, 0.31f);
            var entities = new List<EntityDefinition>();
            string[] subjects = { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL" };
            Material[] subjectMaterials = { config.redMaterial, config.blueMaterial, config.cloudMaterial,
                config.springMaterial, config.pinkMaterial, config.wallMaterial };
            for (int i = 0; i < subjects.Length; i++)
                entities.Add(Object("object-" + subjects[i], subjects[i], i * 2, 2));
            string[] words = { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL",
                "IS", "AND", "YOU", "PUSH", "STOP", "HOVER", "FLY", "WIN", "BOUNCY", "UNKNOWN" };
            for (int i = 0; i < words.Length; i++)
                entities.Add(Word("word-" + i, words[i], i * 2, 5, i == 7));
            entities.Add(Object("object-LAVA", "LAVA", 14, 2));
            entities.Add(Word("word-LAVA", "LAVA", 14, 3));
            entities.Add(Word("control-noun", "ROBOT", 0, 0));
            entities.Add(Word("control-is", "IS", 1, 0));
            entities.Add(Word("control-you", "YOU", 2, 0));
            var host = new GameObject("Palette subjects and words");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                view.Rebuild(WorldModel.FromLevel(Level(32, entities.ToArray())));
                view.RefreshOcclusion(null);
                Assert.AreSame(config.lavaMaterial, ViewMaterial(view, "object-LAVA"));
                Assert.AreSame(ViewMaterial(view, "object-LAVA"), ViewMaterial(view, "word-LAVA"));
                for (int i = 0; i < subjects.Length; i++)
                {
                    Assert.AreSame(subjectMaterials[i], ViewMaterial(view, "object-" + subjects[i]), subjects[i]);
                    Assert.IsTrue(view.TryGetView("object-" + subjects[i], out var subjectView));
                    Assert.IsTrue(subjectView.GetComponent<Renderer>().receiveShadows, subjects[i]);
                }
                for (int i = 0; i < words.Length; i++)
                {
                    var expected = i < 6 ? subjectMaterials[i] : i < 8 ? config.operatorTextMaterial
                        : i < 15 ? config.propertyTextMaterial : config.textMaterial;
                    Assert.AreSame(expected, ViewMaterial(view, "word-" + i), words[i]);
                    Assert.IsTrue(view.TryGetView("word-" + i, out var word));
                    Assert.IsFalse(word.GetComponent<Renderer>().receiveShadows, words[i]);
                    var label = word.GetComponentInChildren<TextMesh>();
                    Assert.AreEqual(words[i], label.text);
                    var expectedInk = QualitySettings.activeColorSpace == ColorSpace.Linear
                        ? config.wordInk.linear : config.wordInk;
                    // TextMesh 顶点色会量化为 Color32；逐分量容许一个 8-bit 色阶误差。
                    for (int channel = 0; channel < 4; channel++)
                        Assert.AreEqual(expectedInk[channel], label.color[channel], 1f / 255f, words[i]);
                    Assert.AreEqual(32, label.fontSize);
                    Assert.AreEqual(0.12f, label.characterSize);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config);
                foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void PushingNounIntoTransformationAndUndoUpdateTheSameObjectsIdentityColor()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            config.redMaterial = MakeMaterial();
            config.blueMaterial = MakeMaterial();
            config.pinkMaterial = MakeMaterial();
            config.nounTextMaterial = MakeMaterial();
            config.operatorTextMaterial = MakeMaterial();
            config.propertyTextMaterial = MakeMaterial();
            var host = new GameObject("Palette transformation and undo");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var session = new GameSession(Level(6,
                    Object("actor", "ROBOT", 3, 1), Object("target", "ROCK", 5, 5),
                    Word("you-noun", "ROBOT", 0, 0), Word("you-is", "IS", 1, 0),
                    Word("you-property", "YOU", 2, 0),
                    Word("transform-noun", "ROCK", 1, 3), Word("transform-is", "IS", 2, 3),
                    Word("transform-target", "FLAG", 3, 2)));
                view.Rebuild(session.World);
                view.RefreshOcclusion(null);
                Assert.AreEqual("ROCK", session.World.Entity("target").Subject);
                Assert.AreSame(config.blueMaterial, ViewMaterial(view, "target"));
                Assert.IsTrue(session.TryExecute("N"), session.LastRejectReason);
                Assert.AreEqual("FLAG", session.World.Entity("target").Subject);
                view.AlignToState(session.World);
                view.RefreshOcclusion(null);
                Assert.AreSame(config.pinkMaterial, ViewMaterial(view, "target"));
                Assert.AreSame(ViewMaterial(view, "transform-target"), ViewMaterial(view, "target"));
                Assert.AreSame(config.blueMaterial, ViewMaterial(view, "transform-noun"));
                Assert.IsTrue(session.Undo());
                Assert.AreEqual("ROCK", session.World.Entity("target").Subject);
                view.AlignToState(session.World);
                view.RefreshOcclusion(null);
                Assert.AreSame(config.blueMaterial, ViewMaterial(view, "target"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config.redMaterial);
                UnityEngine.Object.DestroyImmediate(config.blueMaterial);
                UnityEngine.Object.DestroyImmediate(config.pinkMaterial);
                UnityEngine.Object.DestroyImmediate(config.nounTextMaterial);
                UnityEngine.Object.DestroyImmediate(config.operatorTextMaterial);
                UnityEngine.Object.DestroyImmediate(config.propertyTextMaterial);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void MissingNewMaterialsKeepLegacySubjectWordAndStructureFallbacks()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            config.redMaterial = MakeMaterial(); config.blueMaterial = MakeMaterial();
            config.pinkMaterial = MakeMaterial();
            config.terrainMaterial = MakeMaterial(); config.textMaterial = MakeMaterial();
            config.anchoredTextMaterial = MakeMaterial();
            var host = new GameObject("Palette legacy fallback");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var level = Level(8, Object("actor", "ROBOT", 8, 1),
                    Word("control-noun", "ROBOT", 0, 0), Word("control-is", "IS", 1, 0),
                    Word("control-you", "YOU", 2, 0), Object("cloud", "CLOUD", 0, 2),
                    Object("spring", "SPRING", 2, 2), Object("wall", "WALL", 4, 2),
                    Word("noun", "ROCK", 0, 5), Word("operator", "AND", 2, 5, true),
                    Word("property", "BOUNCY", 4, 5), Word("unknown", "UNKNOWN", 6, 5));
                level.terrain = new[]
                {
                    level.terrain[0],
                    new GridCellBox { id = "raised", min = new GridCell(7, 1, 2),
                        max = new GridCell(7, 1, 2) }
                };
                view.Rebuild(WorldModel.FromLevel(level));
                view.RefreshOcclusion(null);
                Assert.AreSame(config.blueMaterial, ViewMaterial(view, "cloud"));
                Assert.AreSame(config.pinkMaterial, ViewMaterial(view, "spring"));
                Assert.AreSame(config.terrainMaterial, ViewMaterial(view, "wall"));
                Assert.AreSame(config.blueMaterial, ViewMaterial(view, "noun"));
                foreach (var id in new[] { "operator", "property", "unknown" })
                    Assert.AreSame(config.textMaterial, ViewMaterial(view, id), id);
                Assert.AreSame(config.terrainMaterial,
                    host.transform.Find("Terrain raised").GetComponent<Renderer>().sharedMaterial);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config.redMaterial);
                UnityEngine.Object.DestroyImmediate(config.blueMaterial);
                UnityEngine.Object.DestroyImmediate(config.pinkMaterial);
                UnityEngine.Object.DestroyImmediate(config.terrainMaterial);
                UnityEngine.Object.DestroyImmediate(config.textMaterial);
                UnityEngine.Object.DestroyImmediate(config.anchoredTextMaterial);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void FixedCameraUsesConfiguredBackgroundAndFirstSlot()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            config.backgroundColor = new Color(0.8f, 0.7f, 0.6f, 1f);
            var host = new GameObject("Palette camera");
            var camera = host.AddComponent<Camera>();
            var slots = host.AddComponent<CameraSlotsController>();
            slots.config = config;
            try
            {
                slots.slot = 3;
                slots.FrameLevel(new GridCellBox { min = new GridCell(0, 0, 0),
                    max = new GridCell(4, 2, 4) }, true);
                slots.Snap();
                Assert.AreEqual(0, slots.Slot);
                Assert.AreEqual(0, slots.slot);
                Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
                Assert.AreEqual(config.backgroundColor, camera.backgroundColor);
                Assert.IsTrue(camera.orthographic);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [UnityTest]
        public IEnumerator RaisedBoxesAndExpansionTilesUseStructureColorWhileBaseFloorStaysGround()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            config.terrainMaterial = MakeMaterial();
            config.structureMaterial = MakeMaterial();
            var host = new GameObject("Palette structure expansion");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            LevelDefinition Map(int width, bool raised)
            {
                var level = Level(width, Object("actor", "ROBOT", 3, 1),
                    Word("control-noun", "ROBOT", 0, 0), Word("control-is", "IS", 1, 0),
                    Word("control-you", "YOU", 2, 0));
                level.terrain = raised ? new[]
                {
                    new GridCellBox { id = "floor", min = new GridCell(0, 0, 0),
                        max = new GridCell(width, 0, 6) },
                    new GridCellBox { id = "raised", min = new GridCell(width, 1, 2),
                        max = new GridCell(width, 1, 2) }
                } : level.terrain;
                return level;
            }
            try
            {
                var previous = WorldModel.FromLevel(Map(3, false));
                var next = WorldModel.FromLevel(Map(4, true));
                view.Rebuild(previous);
                Assert.AreSame(config.terrainMaterial,
                    host.transform.Find("Terrain floor").GetComponent<Renderer>().sharedMaterial);
                var expansion = view.ExpandTo(next, 0.12f);
                Assert.IsTrue(expansion.MoveNext());
                var tile = host.transform.Find("Added Terrain (3,1,2)");
                Assert.IsNotNull(tile);
                Assert.AreSame(config.structureMaterial, tile.GetComponent<Renderer>().sharedMaterial);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (expansion.MoveNext())
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline);
                    yield return expansion.Current;
                }
                yield return null;
                Assert.AreSame(config.terrainMaterial,
                    host.transform.Find("Terrain floor").GetComponent<Renderer>().sharedMaterial);
                Assert.AreSame(config.structureMaterial,
                    host.transform.Find("Terrain raised").GetComponent<Renderer>().sharedMaterial);
            }
            finally
            {
                UnityEngine.Object.Destroy(host);
                UnityEngine.Object.Destroy(config.terrainMaterial);
                UnityEngine.Object.Destroy(config.structureMaterial);
                UnityEngine.Object.Destroy(config);
            }
        }
    }
}
