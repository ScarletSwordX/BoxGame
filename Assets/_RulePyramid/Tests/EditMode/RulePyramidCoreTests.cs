using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class RulePyramidCoreTests
    {
        static LevelDefinition Load(string id)
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/" + id + ".json");
            return LevelJsonSerializer.FromJson(File.ReadAllText(path));
        }

        static EntityDefinition Color(string id, string color, GridCell cell, bool anchored = false)
        {
            return new EntityDefinition { id = id, kind = "Color", color = color, token = "", cell = cell, anchored = anchored };
        }

        static EntityDefinition Word(string id, string token, GridCell cell, bool anchored = false)
        {
            return new EntityDefinition { id = id, kind = "Text", color = "", token = token, cell = cell, anchored = anchored };
        }

        static LevelDefinition Fixture(GridCell player, EntityDefinition[] extras = null, string[] extraRules = null, GridCellBox[] boxes = null)
        {
            var entities = new List<EntityDefinition> { Color("player", "RED", player) };
            if (extras != null) entities.AddRange(extras);
            var rules = new List<FixedRuleData>
            {
                new FixedRuleData { id = "r0", tokens = new[] { "RED", "IS", "YOU" } }
            };
            if (extraRules != null)
            {
                for (int i = 0; i < extraRules.Length; i++)
                    rules.Add(new FixedRuleData { id = "r" + (i + 1), tokens = extraRules[i].Split(' ') });
            }
            var terrain = new List<GridCellBox>
            {
                new GridCellBox { min = new GridCell(-4, 0, -4), max = new GridCell(7, 0, 4) }
            };
            if (boxes != null) terrain.AddRange(boxes);
            return new LevelDefinition
            {
                schemaVersion = 5,
                mechanicsVersion = "RP-v0.5",
                id = "fixture",
                title = "fixture",
                bounds = new GridCellBox { min = new GridCell(-4, 0, -4), max = new GridCell(7, 12, 4) },
                terrain = terrain.ToArray(),
                entities = entities.ToArray(),
                fixedRules = rules.ToArray(),
                options = Options()
            };
        }

        static OptionsData Options()
        {
            return new OptionsData
            {
                supportMode = "StrictBelow",
                jumpMode = "LandingBounce3",
                bounceRiseCells = 3,
                decisionMode = "GroundedOrBounceApex",
                ruleAxes = new[] { "PositiveX", "PositiveZ" },
                winMode = "DistinctEntitiesSameCell",
                winCheckMode = "AfterAtomicLogicChange",
                actionMode = "FourWayMoveInPlaceJumpApexSteer",
                gravityMode = "WorldDownExceptHoverOrFly",
                playerBlockMode = "ImplicitFromYou"
            };
        }

        [Test]
        public void R01_OnlyExplicitYou_IsSolidAndDown()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0)));
            var p = m.Entity("player");
            Assert.IsTrue(m.Rules.Has("RED", "YOU"));
            Assert.IsTrue(PropertyResolver.IsSolid(p, m.Rules));
            Assert.AreEqual(GravityMode.Down, PropertyResolver.ResolveGravity(p, m.Rules));
        }

        [Test]
        public void R02_MovableTextFallsWithoutFallWord()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Word("t", "AND", new GridCell(2, 4, 0)) }));
            m.Settle();
            Assert.AreEqual(new GridCell(2, 1, 0), m.Entity("t").Cell);
        }

        [Test]
        public void R03_HollowColorFalls()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("h", "PINK", new GridCell(2, 4, 0)) }));
            m.Settle();
            Assert.IsFalse(PropertyResolver.IsSolid(m.Entity("h"), m.Rules));
            Assert.AreEqual(new GridCell(2, 1, 0), m.Entity("h").Cell);
        }

        [Test]
        public void R04_AnchoredTextDoesNotFall()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Word("t", "AND", new GridCell(2, 4, 0), true) }));
            m.Settle();
            Assert.AreEqual(new GridCell(2, 4, 0), m.Entity("t").Cell);
            Assert.AreEqual(GravityMode.Anchored, PropertyResolver.ResolveGravity(m.Entity("t"), m.Rules));
        }

        [Test]
        public void R05_HoverKeepsHeightWithoutBlock()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("h", "BLUE", new GridCell(2, 4, 0)) }, new[] { "BLUE IS HOVER" }));
            m.Settle();
            Assert.AreEqual(new GridCell(2, 4, 0), m.Entity("h").Cell);
            Assert.IsFalse(PropertyResolver.IsSolid(m.Entity("h"), m.Rules));
        }

        [Test]
        public void R06_FlyOutranksHoverOutranksDown()
        {
            var level = Fixture(new GridCell(0, 1, 0), new[] { Color("h", "BLUE", new GridCell(2, 4, 0)) }, new[] { "BLUE IS HOVER AND FLY" });
            var m = WorldModel.FromLevel(level);
            Assert.AreEqual(GravityMode.Up, PropertyResolver.ResolveGravity(m.Entity("h"), m.Rules));
            m.Spec.fixedRules = new[]
            {
                new FixedRuleData { id = "r0", tokens = new[] { "RED", "IS", "YOU" } },
                new FixedRuleData { id = "r1", tokens = new[] { "BLUE", "IS", "HOVER" } }
            };
            m.Refresh();
            Assert.AreEqual(GravityMode.Hover, PropertyResolver.ResolveGravity(m.Entity("h"), m.Rules));
            m.Spec.fixedRules = new[] { new FixedRuleData { id = "r0", tokens = new[] { "RED", "IS", "YOU" } } };
            m.Refresh();
            Assert.AreEqual(GravityMode.Down, PropertyResolver.ResolveGravity(m.Entity("h"), m.Rules));
        }

        [Test]
        public void R07_ImplicitBlockFollowsYouNotColor()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0)));
            var p = m.Entity("player");
            m.Spec.fixedRules = new FixedRuleData[0];
            m.Refresh();
            Assert.IsFalse(PropertyResolver.IsSolid(p, m.Rules));
            m.Spec.fixedRules = new[] { new FixedRuleData { id = "r", tokens = new[] { "RED", "IS", "BLOCK" } } };
            m.Refresh();
            Assert.IsTrue(PropertyResolver.IsSolid(p, m.Rules));
        }

        [Test]
        public void R08_IncompleteAndKeepsPrefix()
        {
            var extras = new[]
            {
                Word("a", "BLUE", new GridCell(0, 2, 2), true),
                Word("b", "IS", new GridCell(1, 2, 2), true),
                Word("c", "BLOCK", new GridCell(2, 2, 2), true),
                Word("d", "FLY", new GridCell(4, 2, 2), true)
            };
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), extras));
            Assert.IsTrue(m.Rules["BLUE"].SetEquals(new[] { "BLOCK" }));
            m.Entities.Add(new EntityState { Id = "e", Kind = EntityKind.Text, Token = "AND", Cell = new GridCell(3, 2, 2), Anchored = true });
            m.Refresh();
            Assert.IsTrue(m.Rules["BLUE"].SetEquals(new[] { "BLOCK", "FLY" }));
        }

        [Test]
        public void R09_OnlyPositiveXAndZParse()
        {
            var axes = new[]
            {
                (new GridCell(1, 0, 0), true),
                (new GridCell(0, 0, 1), true),
                (new GridCell(-1, 0, 0), false),
                (new GridCell(0, 1, 0), false),
                (new GridCell(1, 1, 0), false)
            };
            foreach (var (vector, expected) in axes)
            {
                var baseCell = new GridCell(2, 4, 2);
                var words = new[] { "BLUE", "IS", "BLOCK" };
                var extras = new List<EntityDefinition>();
                for (int i = 0; i < words.Length; i++)
                {
                    extras.Add(Word(i.ToString(), words[i], new GridCell(
                        baseCell.x + i * vector.x, baseCell.y + i * vector.y, baseCell.z + i * vector.z), true));
                }
                var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), extras.ToArray()));
                Assert.AreEqual(expected, m.Rules.Has("BLUE", "BLOCK"), vector.ToString());
            }
        }

        [Test]
        public void R10_HeadBumpedTextFallsBack()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Word("t", "AND", new GridCell(0, 2, 0)) }));
            Assert.IsTrue(m.TryCommand("J", out _));
            Assert.AreEqual(new GridCell(0, 1, 0), m.Entity("player").Cell);
            Assert.AreEqual(new GridCell(0, 2, 0), m.Entity("t").Cell);
            Assert.IsTrue(m.Log.Exists(e => e.Kind == "HeadBump"));
            Assert.IsTrue(m.Log.Exists(e => e.Kind == "GravityFall" && e.EntityId == "t"));
        }

        [Test]
        public void R11_HeadBumpedHoverKeepsHeight()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("h", "BLUE", new GridCell(0, 2, 0)) }, new[] { "BLUE IS BLOCK AND HOVER" }));
            Assert.IsTrue(m.TryCommand("J", out _));
            Assert.AreEqual(new GridCell(0, 3, 0), m.Entity("h").Cell);
            Assert.AreEqual(new GridCell(0, 1, 0), m.Entity("player").Cell);
        }

        [Test]
        public void R12_LandingPressMovesHoverSolid()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 5, 0), new[] { Color("h", "BLUE", new GridCell(0, 2, 0)) }, new[] { "BLUE IS BLOCK AND HOVER" }));
            m.Settle();
            Assert.AreEqual(new GridCell(0, 1, 0), m.Entity("h").Cell);
            Assert.AreEqual(1, m.Log.FindAll(e => e.Kind == "LandingPress" && e.EntityId == "player").Count);
        }

        [Test]
        public void R13_JumpBeatsPressAndRisesThree()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 5, 0), new[] { Color("h", "BLUE", new GridCell(0, 2, 0)) }, new[] { "BLUE IS BLOCK AND HOVER AND JUMP" }));
            m.Settle();
            Assert.AreEqual(new GridCell(0, 2, 0), m.Entity("h").Cell);
            Assert.AreEqual(new GridCell(0, 6, 0), m.Entity("player").Cell);
            Assert.AreEqual(MotionPhase.BounceApex, m.Phase);
            Assert.IsFalse(m.Log.Exists(e => e.Kind == "LandingPress"));
        }

        [Test]
        public void R14_NoAutoClimbAndJumpStaysInPlace()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), null, null, new[]
            {
                new GridCellBox { min = new GridCell(1, 1, 0), max = new GridCell(1, 1, 0) }
            }));
            var before = m.Fingerprint();
            Assert.IsFalse(m.TryCommand("E", out _));
            Assert.AreEqual(before, m.Fingerprint());
            Assert.IsTrue(m.TryCommand("J", out _));
            Assert.AreEqual(new GridCell(0, 1, 0), m.Entity("player").Cell);
        }

        [Test]
        public void R15_LegacyJumpsRejected()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0)));
            var before = m.Fingerprint();
            foreach (var c in new[] { "JE", "JW", "JN", "JS" })
                Assert.IsFalse(m.TryCommand(c, out _));
            Assert.AreEqual(before, m.Fingerprint());
            Assert.AreEqual(0, m.History.Count);
        }

        [Test]
        public void R16_NeighborsDoNotWin()
        {
            var deltas = new[]
            {
                GridCell.East, GridCell.West, GridCell.North, GridCell.South, GridCell.Up, GridCell.Down, new GridCell(1, 1, 1)
            };
            foreach (var d in deltas)
            {
                var goal = new GridCell(0, 3, 0).Add(d);
                var m = WorldModel.FromLevel(Fixture(new GridCell(0, 3, 0), new[] { Color("goal", "PINK", goal) }, new[] { "PINK IS WIN" }));
                Assert.IsFalse(m.WonLatched, d.ToString());
            }
        }

        [Test]
        public void R17_EnterHollowWinAndUndo()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("goal", "PINK", new GridCell(1, 1, 0)) }, new[] { "PINK IS WIN" }));
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.IsTrue(m.WonLatched);
            Assert.IsTrue(m.Undo());
            Assert.IsFalse(m.WonLatched);
            Assert.AreEqual(new GridCell(0, 1, 0), m.Entity("player").Cell);
        }

        [Test]
        public void R18_HollowWinCanFallOntoPlayer()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("goal", "PINK", new GridCell(0, 4, 0)) }, new[] { "PINK IS WIN" }));
            m.Settle();
            Assert.IsTrue(m.WonLatched);
            Assert.AreEqual(new GridCell(0, 1, 0), m.WinRecord.Cell);
        }

        [Test]
        public void R19_InactiveAndSelfWinDoNotCount()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("goal", "PINK", new GridCell(1, 1, 0)) }));
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.IsFalse(m.WonLatched);
            m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), extraRules: new[] { "RED IS WIN" }));
            Assert.IsFalse(m.WonLatched);
        }

        [Test]
        public void R20_WinDoesNotCancelBlock()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 1, 0), new[] { Color("goal", "PINK", new GridCell(1, 1, 0)) }, new[] { "PINK IS BLOCK AND WIN" }));
            Assert.IsFalse(m.TryCommand("E", out _));
            Assert.IsFalse(m.WonLatched);
        }

        [Test]
        public void R21_MidBounceWinStopsBeforeApex()
        {
            var m = WorldModel.FromLevel(Fixture(new GridCell(0, 3, 0), new[]
            {
                Color("spring", "BLUE", new GridCell(0, 1, 0)),
                Color("goal", "PINK", new GridCell(0, 4, 0))
            }, new[] { "BLUE IS BLOCK AND JUMP", "PINK IS HOVER AND WIN" }));
            m.Settle();
            Assert.IsTrue(m.WonLatched);
            Assert.AreEqual(new GridCell(0, 4, 0), m.Entity("player").Cell);
            Assert.IsFalse(m.Apex.ContainsKey("player"));
        }

        [Test]
        public void R22_RejectedApexSteerPreservesApex()
        {
            var m = WorldModel.FromLevel(Load("L04"));
            Assert.IsTrue(m.TryCommand("N", out _));
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.AreEqual(MotionPhase.BounceApex, m.Phase);
            m.Terrain.Add(new GridCell(4, 5, 1));
            var before = m.Fingerprint();
            int n = m.History.Count;
            Assert.IsFalse(m.TryCommand("E", out _));
            Assert.AreEqual(before, m.Fingerprint());
            Assert.AreEqual(n, m.History.Count);
        }

        [Test]
        public void R23_BreakingHoverRestoresFallAndUndo()
        {
            var m = WorldModel.FromLevel(Load("L03"));
            Assert.AreEqual(new GridCell(3, 5, 2), m.Entity("bridge").Cell);
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.IsTrue(m.TryCommand("N", out _));
            Assert.IsFalse(m.Rules.Has("BLUE", "HOVER"));
            Assert.AreEqual(new GridCell(3, 2, 2), m.Entity("bridge").Cell);
            Assert.IsTrue(m.Undo());
            Assert.AreEqual(new GridCell(3, 5, 2), m.Entity("bridge").Cell);
            Assert.IsTrue(m.Rules.Has("BLUE", "HOVER"));
        }

        [Test]
        public void R24_AndSupportedWhileFlyCarriesPlayer()
        {
            var spec = Load("L05");
            var m = WorldModel.FromLevel(spec);
            foreach (var c in spec.referenceSolution.commands)
            {
                if (c == spec.referenceSolution.commands[3]) break;
                Assert.IsTrue(m.TryCommand(c, out var msg), msg);
            }
            Assert.AreEqual(new GridCell(3, 4, 0), m.Entity("lift_front").Cell);
            Assert.AreEqual(new GridCell(3, 5, 0), m.Entity("player").Cell);
            Assert.AreEqual(new GridCell(3, 2, -1), m.Entity("t_and").Cell);
            Assert.IsTrue(m.Rules.Has("BLUE", "FLY"));
        }

        [Test]
        public void SixLevels_ReferenceSolutionsWinAndUndo()
        {
            foreach (var id in new[] { "L01", "L02", "L03", "L04", "L05", "L06" })
            {
                var spec = Load(id);
                var report = LevelValidator.ValidateForPlaytest(spec);
                Assert.IsTrue(report.CanPlaytest, id + " " + report);
                var m = WorldModel.FromLevel(spec);
                var before = m.Fingerprint();
                m.Settle();
                Assert.AreEqual(before, m.Fingerprint(), id + " unstable");
                Assert.IsFalse(m.WonLatched, id + " already won");
                foreach (var cmd in spec.referenceSolution.commands)
                {
                    Assert.IsFalse(m.WonLatched, id + " early win before " + cmd);
                    Assert.IsTrue(m.TryCommand(cmd, out var msg), id + " " + cmd + " " + msg);
                }
                Assert.IsTrue(m.WonLatched, id + " not won");
                Assert.IsTrue(m.Undo(), id + " undo");
                Assert.IsFalse(m.WonLatched, id + " undo win");
            }
        }

        [Test]
        public void JsonRoundtrip_L01()
        {
            var spec = Load("L01");
            var json = LevelJsonSerializer.ToJson(spec);
            var again = LevelJsonSerializer.FromJson(json);
            Assert.AreEqual(spec.id, again.id);
            Assert.AreEqual(spec.entities.Length, again.entities.Length);
            Assert.AreEqual(spec.entities[0].cell, again.entities[0].cell);
        }

        [Test]
        public void EditSession_StopDoesNotMutateDraft()
        {
            var spec = Load("L01");
            var edit = new LevelEditSession(spec);
            Assert.IsTrue(edit.TryStartPlaytest(out var session, out var error), error);
            session.TryExecute("E");
            Assert.AreNotEqual(spec.entities[0].cell, session.World.Entity("player").Cell);
            edit.StopPlaytest();
            Assert.AreEqual(spec.entities[0].cell, edit.Draft.entities[0].cell);
        }
    }
}
