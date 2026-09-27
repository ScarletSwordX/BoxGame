using System.Collections.Generic;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.PlayMode
{
    public class L03PresentationTests
    {
        static EntityDefinition Object(string id, string subject, int x) => new EntityDefinition
        {
            id = id, kind = "Object", subject = subject, cell = new GridCell(x, 1, 6)
        };

        static LevelDefinition Level(EntityDefinition[] objects, params string[] sentences)
        {
            var entities = new List<EntityDefinition>(objects);
            for (int row = 0; row < sentences.Length; row++)
            {
                var words = sentences[row].Split(' ');
                for (int x = 0; x < words.Length; x++)
                    entities.Add(new EntityDefinition
                    {
                        id = "rule_" + row + "_" + x, kind = "Text", token = words[x],
                        cell = new GridCell(x, 1, row)
                    });
            }
            return new LevelDefinition
            {
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "L03-presentation", title = "表现测试",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(8, 7, 8) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(8, 0, 8) } },
                entities = entities.ToArray(),
                options = new OptionsData
                {
                    actionMode = "MoveAutoPush", winMode = "DistinctEntitiesSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly", collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText", supportMode = "StrictBelow",
                    controlMode = "MultiYouIndependentBlocking_NoControlUndo", bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly", textMobilityMode = "AllWordsMovable_GeometryAccess"
                }
            };
        }

        [Test]
        public void LavaFallbackMarksEveryYouAndUndoRestoresMeltedView()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var host = new GameObject("L03 表现测试");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var world = WorldModel.FromLevel(Level(new[]
                    {
                        Object("first", "ROBOT", 1), Object("second", "ROBOT", 5),
                        Object("lava", "LAVA", 2)
                    }, "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT"));
                view.Rebuild(world);
                Assert.IsTrue(view.TryGetView("lava", out var lava));
                var lavaMaterial = lava.GetComponent<Renderer>().sharedMaterial;
                Assert.IsNotNull(lavaMaterial);
                Assert.Greater(lavaMaterial.color.r, lavaMaterial.color.g);
                Assert.Greater(lavaMaterial.color.g, lavaMaterial.color.b);
                foreach (var id in new[] { "first", "second" })
                {
                    Assert.IsTrue(view.TryGetView(id, out var actor));
                    var marker = actor.Find("YouMarker");
                    Assert.IsNotNull(marker, id);
                    Assert.IsTrue(marker.gameObject.activeSelf, id);
                    Assert.AreEqual("YOU", marker.GetComponent<TextMesh>().text);
                }

                view.HideEntity("first");
                Assert.IsTrue(view.TryGetView("first", out var hidden));
                Assert.IsFalse(hidden.gameObject.activeSelf);
                view.AlignToState(world);
                Assert.IsTrue(hidden.gameObject.activeSelf, "撤销或中断动画后仍存活的 YOU 应恢复。");

                Assert.IsTrue(world.TryCommand("E", out var reason), reason);
                Assert.IsNull(world.Entity("first"));
                view.HideEntity("first");
                view.AlignToState(world);
                Assert.IsFalse(view.TryGetView("first", out _));
                Assert.IsTrue(world.Undo());
                view.AlignToState(world);
                Assert.IsTrue(view.TryGetView("first", out var restored));
                Assert.IsTrue(restored.gameObject.activeSelf);
                Assert.IsTrue(restored.Find("YouMarker").gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ApexCueFollowsApexYouWhenAnotherYouIsGrounded()
        {
            var session = new GameSession(Level(new[]
                {
                    Object("a-ground", "ROBOT", 5), Object("z-apex", "ROBOT", 1)
                }, "ROBOT IS YOU"));
            session.World.Apex["z-apex"] = new BounceApexState
            {
                SurfaceId = "spring", ContactPos = new GridCell(1, 0, 6),
                ApexPos = session.World.Entity("z-apex").Cell, RiseCells = 1
            };
            Assert.AreEqual(MotionPhase.BounceApex, session.Phase);
            Assert.AreEqual("z-apex", session.World.ActorId);
            var cue = new BounceApexCueState();
            cue.Tick(session, false, 1f, 0f);
            Assert.IsTrue(cue.Waiting);
            Assert.AreEqual("z-apex", cue.ActorId);
        }
    }
}
