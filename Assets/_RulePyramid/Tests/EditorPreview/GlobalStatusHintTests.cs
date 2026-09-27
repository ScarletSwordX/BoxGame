using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace RulePyramid.Tests.EditorPreview
{
    public class GlobalStatusHintTests
    {
        static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static EntityDefinition Word(string id, string token, int x, int z)
        {
            return new EntityDefinition { id = id, kind = "Text", token = token, cell = new GridCell(x, 1, z) };
        }
        static GameSession Session(string subject = "ROBOT", bool transfer = false)
        {
            var entities = new List<EntityDefinition>
            {
                new EntityDefinition { id = "player", kind = "Object", subject = subject, cell = new GridCell(1, 1, 1) },
                Word("subject", subject, 0, 2), Word("is", "IS", 1, 2), Word("you", "YOU", 2, 2)
            };
            if (transfer)
            {
                entities.Add(new EntityDefinition { id = "other", kind = "Object", subject = "FLAG", cell = new GridCell(4, 1, 1) });
                entities.Add(Word("other_subject", "FLAG", 0, 3));
                entities.Add(Word("other_you", "YOU", 2, 3));
            }
            return new GameSession(new LevelDefinition
            {
                id = "global-hint-" + subject, schemaVersion = 9, mechanicsVersion = "RW-v0.9",
                bounds = new GridCellBox { min = new GridCell(-1, 0, -1), max = new GridCell(5, 4, 5) },
                terrain = new[] { new GridCellBox { min = new GridCell(-1, 0, -1), max = new GridCell(5, 0, 5) } },
                entities = entities.ToArray(), options = new OptionsData { bounceRiseCells = 3 },
                tutorial = new TutorialData { hints = new[] { "手动提示" }, regions = Array.Empty<RegionTutorialData>() }
            });
        }

        [TestCase("ROBOT")]
        [TestCase("WALL")]
        public void BreakingYouShowsPersistentHintAndUndoOrRestartClearsIt(string subject)
        {
            var session = Session(subject);
            Assert.IsNull(GlobalStatusHint.Current(session));
            Assert.IsTrue(session.TryExecute("PN"), session.LastRejectReason);
            Assert.AreEqual(MotionPhase.NoControl, session.Phase);
            Assert.AreEqual(GlobalStatusHint.NoControlText, GlobalStatusHint.Current(session));
            Assert.IsFalse(session.TryExecute("E"));
            Assert.AreEqual(GlobalStatusHint.NoControlText, GlobalStatusHint.Current(session));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual("player", session.World.ActorId);
            Assert.IsNull(GlobalStatusHint.Current(session));
            Assert.IsTrue(session.TryExecute("PN"));
            Assert.AreEqual(GlobalStatusHint.NoControlText, GlobalStatusHint.Current(session));
            session.Restart();
            Assert.IsNull(GlobalStatusHint.Current(session));
        }

        [Test]
        public void MovingYouRuleToAnotherObjectDoesNotShowLossOfControlHint()
        {
            var session = Session(transfer: true);
            Assert.IsTrue(session.TryExecute("PN"), session.LastRejectReason);
            Assert.AreEqual("other", session.World.ActorId);
            Assert.IsNull(GlobalStatusHint.Current(session));
        }

        [Test]
        public void HudGivesNoControlPriorityOverRegionAndManualHintsThenRestoresNormalText()
        {
            var host = new GameObject("全局失控提示测试");
            host.SetActive(false);
            try
            {
                var session = Session();
                var bootstrap = host.AddComponent<GameBootstrap>();
                typeof(GameBootstrap).GetField("_session", Fields).SetValue(bootstrap, session);
                var hud = host.AddComponent<HudController>();
                var label = new Label();
                typeof(HudController).GetField("_hint", Fields).SetValue(hud, label);
                typeof(HudController).GetField("_bootstrap", Fields).SetValue(hud, bootstrap);
                var region = new RegionTutorialSession(new TutorialData { regions = new[]
                {
                    new RegionTutorialData { id = "region", text = "区域提示", enabled = true, durationSeconds = 10,
                        bounds = new GridCellBox { min = new GridCell(1, 1, 1), max = new GridCell(1, 1, 1) } }
                } });
                typeof(GameBootstrap).GetField("_regionTutorial", Fields).SetValue(bootstrap, region);
                region.Observe(session);
                hud.Refresh(session, null, region);
                Assert.AreEqual("区域提示", label.text);
                Assert.IsTrue(session.TryExecute("PN"));
                hud.Refresh(session, null, region);
                Assert.AreEqual(GlobalStatusHint.NoControlText, label.text);
                typeof(HudController).GetMethod("ShowNextHint", Fields).Invoke(hud, null);
                Assert.AreEqual(GlobalStatusHint.NoControlText, label.text);
                region.Tick(20);
                hud.Refresh(session, null, region);
                Assert.AreEqual(GlobalStatusHint.NoControlText, label.text);
                Assert.IsTrue(session.Undo());
                hud.Refresh(session, null, region);
                Assert.AreEqual("手动提示", label.text);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
