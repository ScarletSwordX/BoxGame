using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class L03MechanicsTests
    {
        static GridCell C(int x, int y = 1, int z = 6) => new GridCell(x, y, z);

        static EntityDefinition Object(string id, string subject, GridCell cell) => new EntityDefinition
        {
            id = id, kind = "Object", subject = subject, token = "", cell = cell
        };

        static EntityDefinition Word(string id, string token, GridCell cell) => new EntityDefinition
        {
            id = id, kind = "Text", subject = "", token = token, cell = cell
        };

        static LevelDefinition Level(EntityDefinition[] objects, params string[] sentences)
        {
            var entities = new List<EntityDefinition>(objects);
            for (int row = 0; row < sentences.Length; row++)
            {
                var tokens = sentences[row].Split(' ');
                for (int x = 0; x < tokens.Length; x++)
                    entities.Add(Word("rule_" + row + "_" + x, tokens[x], new GridCell(x, 1, row)));
            }
            return new LevelDefinition
            {
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "L03-mechanics",
                title = "L03 mechanics",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(12, 7, 9) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(12, 0, 9) } },
                entities = entities.ToArray(),
                fixedRules = Array.Empty<FixedRuleData>(),
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

        static WorldModel World(EntityDefinition[] objects, params string[] sentences) =>
            WorldModel.FromLevel(Level(objects, sentences));

        static void Move(WorldModel world, string command)
        {
            Assert.IsTrue(world.TryCommand(command, out var reason), command + ": " + reason);
        }

        [Test]
        public void FormingSelfWinWinsImmediatelyAndUndoRestoresRuleAndControl()
        {
            var level = Level(new[] { Object("robot", "ROBOT", C(2, 1, 3)), Word("loose_win", "WIN", C(2, 1, 2)) },
                "ROBOT IS YOU", "ROBOT IS");
            var session = new GameSession(level);
            var initial = session.World.Fingerprint();
            Assert.IsFalse(session.Won);
            Assert.IsTrue(session.TryExecute("S"));
            Assert.IsTrue(session.Won);
            Assert.AreEqual("robot", session.World.WinRecord.YouId);
            Assert.AreEqual("robot", session.World.WinRecord.WinId);
            session.Undo();
            Assert.AreEqual(initial, session.World.Fingerprint());
            Assert.IsTrue(session.TryExecute("S"));
            Assert.IsTrue(session.Won);
            session.Restart();
            Assert.AreEqual(initial, session.World.Fingerprint());
        }

        [Test]
        public void InitialSelfWinIsDetectedForBothOptionLabels()
        {
            var level = Level(new[] { Object("robot", "ROBOT", C(5)) }, "ROBOT IS YOU", "ROBOT IS WIN");
            foreach (var label in new[] { "DistinctEntitiesSameCell", "YouAndWinSameCell" })
            {
                level.options.winMode = label;
                var world = WorldModel.FromLevel(level);
                Assert.IsTrue(world.WonLatched, label);
                Assert.AreEqual(world.WinRecord.YouId, world.WinRecord.WinId);
            }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void SideEntryNeedsBothHotAndMelt(bool melt, bool survives)
        {
            var rules = new List<string> { "ROBOT IS YOU", "LAVA IS HOT" };
            if (melt) rules.Add("ROBOT IS MELT");
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("lava", "LAVA", C(2)) }, rules.ToArray());
            Move(world, "E");
            Assert.AreEqual(survives, world.Entity("robot") != null);
            Assert.AreEqual(survives ? MotionPhase.Grounded : MotionPhase.NoControl, world.Phase);
            Assert.IsNotNull(world.Entity("lava"));
        }

        [Test]
        public void MeltWithoutHotDoesNotDestroyEntrant()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("lava", "LAVA", C(2)) },
                "ROBOT IS YOU", "ROBOT IS MELT");
            Move(world, "E");
            Assert.AreEqual(C(2), world.Entity("robot").Cell);
            Assert.AreEqual(MotionPhase.Grounded, world.Phase);
        }

        [Test]
        public void TopFallOntoHotBouncySurvivesWithoutEnteringEvenIfSurfaceIsNonSolid()
        {
            var level = Level(new[] { Object("robot", "ROBOT", C(1, 3)), Object("lava", "LAVA", C(2)) },
                "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT AND BOUNCY");
            var world = WorldModel.FromLevel(level);
            world.Terrain.Add(C(1, 2));
            Assert.IsFalse(world.Solid(world.Entity("lava")), "测试必须覆盖非实体弹面");
            Move(world, "E");
            Assert.IsNotNull(world.Entity("robot"));
            Assert.IsTrue(world.Apex.ContainsKey("robot"), "真实下落接触应进入弹跳顶点");
            Assert.Greater(world.Entity("robot").Cell.y, world.Entity("lava").Cell.y);
            Assert.IsFalse(world.Log.Any(e => e.Kind == "Melted" && e.EntityId == "robot"));
        }

        [Test]
        public void TopFallIntoHotWithoutBouncyMelts()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1, 3)), Object("lava", "LAVA", C(2)) },
                "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT");
            world.Terrain.Add(C(1, 2));
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
            Assert.IsTrue(world.Log.Any(e => e.Kind == "Melted" && e.EntityId == "robot"));
        }

        [Test]
        public void NonYouMeltObjectAlsoDiesOnEnteringHot()
        {
            var world = World(new[]
                {
                    Object("robot", "ROBOT", C(1, 1, 7)), Object("rock", "ROCK", C(2, 3)),
                    Object("lava", "LAVA", C(2))
                }, "ROBOT IS YOU", "ROCK IS MELT", "LAVA IS HOT");
            Move(world, "E");
            Assert.IsNull(world.Entity("rock"));
            Assert.IsNotNull(world.Entity("robot"));
            Assert.AreEqual(MotionPhase.Grounded, world.Phase);
        }

        [Test]
        public void BounceRiseEnteringAnotherHotCellMeltsWithoutLeavingApex()
        {
            var world = World(new[]
                {
                    Object("robot", "ROBOT", C(1, 3)), Object("surface", "LAVA", C(2)),
                    Object("overhead", "LAVA", C(2, 4))
                }, "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT AND BOUNCY");
            world.Terrain.Add(C(1, 2));
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.IsFalse(world.Apex.ContainsKey("robot"));
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
        }

        [Test]
        public void SideEntryIntoHotBouncyStillMeltsAndHotWinsBeforeWin()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("lava", "LAVA", C(2)) },
                "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT AND BOUNCY AND WIN");
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.IsFalse(world.WonLatched);
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
            Assert.IsFalse(world.Apex.ContainsKey("robot"));
        }

        [Test]
        public void IndependentYouMovesWhenOtherIsBlockedAndOrderDoesNotMatter()
        {
            foreach (bool reverse in new[] { false, true })
            {
                var objects = new[] { Object("a", "ROBOT", C(1)), Object("b", "ROBOT", C(5)) };
                var level = Level(reverse ? objects.Reverse().ToArray() : objects, "ROBOT IS YOU");
                var world = WorldModel.FromLevel(level);
                world.Terrain.Add(C(2));
                Move(world, "E");
                Assert.AreEqual(C(1), world.Entity("a").Cell);
                Assert.AreEqual(C(6), world.Entity("b").Cell);
                Assert.AreEqual(2, world.Actors().Count);
            }
        }

        [Test]
        public void FrontYouMovesFirstAndSharedPushChainMovesOnce()
        {
            foreach (bool reverse in new[] { false, true })
            {
                var objects = new[]
                {
                    Object("rear", "ROBOT", C(1)), Object("front", "ROBOT", C(2)),
                    Object("rock", "ROCK", C(3))
                };
                var world = World(reverse ? objects.Reverse().ToArray() : objects,
                    "ROBOT IS YOU", "ROCK IS PUSH");
                Move(world, "E");
                Assert.AreEqual(C(2), world.Entity("rear").Cell);
                Assert.AreEqual(C(3), world.Entity("front").Cell);
                Assert.AreEqual(C(4), world.Entity("rock").Cell, "同一推链一条命令只走一格");
            }
        }

        [Test]
        public void BlockedApexYouKeepsItsTurnWhileAnotherYouMoves()
        {
            var world = World(new[] { Object("apex", "ROBOT", C(1, 3)), Object("ground", "ROBOT", C(5)) },
                "ROBOT IS YOU");
            world.Terrain.Add(C(1, 2));
            world.Terrain.Add(C(2, 3));
            world.Apex["apex"] = new BounceApexState
            {
                SurfaceId = "surface", ContactPos = C(1, 2), ApexPos = C(1, 3), RiseCells = 1
            };
            Move(world, "E");
            Assert.AreEqual(C(1, 3), world.Entity("apex").Cell);
            Assert.AreEqual(C(6), world.Entity("ground").Cell);
            Assert.IsTrue(world.Apex.ContainsKey("apex"), "受阻顶点玩家仍应等待下一输入");
            Assert.IsFalse(world.ForcedFall.Contains("apex"));
        }

        [Test]
        public void PartialAndCompleteMeltPreserveControlUntilLastYouAndUndoRestoresAll()
        {
            var world = World(new[]
                {
                    Object("first", "ROBOT", C(1)), Object("second", "ROBOT", C(1, 1, 7)),
                    Object("lava_a", "LAVA", C(2)), Object("lava_b", "LAVA", C(3, 1, 7))
                }, "ROBOT IS YOU", "ROBOT IS MELT", "LAVA IS HOT");
            string start = world.Fingerprint();
            Move(world, "E");
            Assert.IsNull(world.Entity("first"));
            Assert.AreEqual(C(2, 1, 7), world.Entity("second").Cell);
            Assert.AreEqual(MotionPhase.Grounded, world.Phase);
            Move(world, "E");
            Assert.IsNull(world.Entity("second"));
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
            Assert.IsTrue(world.Undo());
            Assert.IsNull(world.Entity("first"));
            Assert.AreEqual(C(2, 1, 7), world.Entity("second").Cell);
            Assert.AreEqual(MotionPhase.Grounded, world.Phase);
            Assert.IsTrue(world.Undo());
            Assert.AreEqual(start, world.Fingerprint());
            Assert.AreEqual(2, world.Actors().Count);
            var session = new GameSession(world.Spec);
            Assert.IsTrue(session.TryExecute("E"), session.LastRejectReason);
            Assert.AreEqual(1, session.TurnCount);
            session.Restart();
            Assert.AreEqual(start, session.World.Fingerprint());
            Assert.AreEqual(0, session.TurnCount);
        }

        [Test]
        public void NewYouCreatedByPushedAndSentenceActsOnNextInput()
        {
            var level = Level(new[] { Object("robot", "ROBOT", C(6)), Object("rock", "ROCK", C(9)) },
                "ROBOT IS YOU");
            var entities = level.entities.ToList();
            var sentence = new[] { "ROBOT", "AND", "ROCK", "IS" };
            for (int x = 0; x < sentence.Length; x++)
                entities.Add(Word("joint_" + x, sentence[x], C(x)));
            entities.Add(Word("joint_you", "YOU", C(5)));
            level.entities = entities.ToArray();
            var world = WorldModel.FromLevel(level);
            Assert.AreEqual(1, world.Actors().Count);
            Move(world, "W");
            Assert.AreEqual(C(4), world.Entity("joint_you").Cell);
            Assert.AreEqual(C(9), world.Entity("rock").Cell, "新 YOU 不应响应令规则成立的这次输入");
            Assert.AreEqual(2, world.Actors().Count);
            Move(world, "E");
            Assert.AreEqual(C(10), world.Entity("rock").Cell);
        }

        static LevelDefinition RuleDesignDraft(int stage) => LevelJsonSerializer.FromJson(File.ReadAllText(
            Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/L3P" + stage + ".json")));

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RuleDesignCannotWinBeforeRequiredRule(int stage)
        {
            var initial = WorldModel.FromLevel(RuleDesignDraft(stage));
            var pending = new Queue<WorldModel>();
            var seen = new HashSet<string> { initial.Fingerprint() };
            pending.Enqueue(initial);
            int targetTransitions = 0;
            while (pending.Count > 0)
            {
                Assert.Less(seen.Count, 20000, "搜索必须穷尽；超出预算不能当作无绕解证明");
                var current = pending.Dequeue();
                foreach (string command in new[] { "N", "E", "S", "W", "J", "WAIT" })
                {
                    var next = current.CloneLive();
                    if (!next.TryCommand(command, out _)) continue;
                    bool learned = stage == 1
                        ? next.Rules.Has("ROBOT", "YOU") && next.Rules.Has("ROBOT", "WIN")
                        : stage == 2 ? next.Rules.Has("LAVA", "HOT") && next.Rules.Has("LAVA", "BOUNCY")
                        : next.Entities.Count(e => e.Kind == EntityKind.Object && e.Id.StartsWith("wall_") && e.Subject == "ROBOT") == 8;
                    if (learned) { targetTransitions++; continue; }
                    Assert.IsFalse(next.WonLatched, "存在未使用目标规则的胜利路径");
                    if (next.Phase != MotionPhase.NoControl && seen.Add(next.Fingerprint())) pending.Enqueue(next);
                }
            }
            Assert.Greater(targetTransitions, 0, "目标规则必须可达");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RuleDesignReplayUndoRestartAndRuleSources(int stage)
        {
            var level = RuleDesignDraft(stage);
            Assert.AreEqual("OK", LevelValidator.ValidateForPlaytest(level).ToString());
            var session = new GameSession(level);
            string initial = session.World.Fingerprint();
            var fingerprints = new List<string> { initial };
            foreach (var command in level.referenceSolutions[0].commands)
            {
                Assert.IsTrue(session.TryExecute(command), session.LastRejectReason);
                fingerprints.Add(session.World.Fingerprint());
            }
            Assert.IsTrue(session.Won);
            if (stage < 3)
            {
                string subject = stage == 1 ? "ROBOT" : "LAVA";
                string first = stage == 1 ? "YOU" : "HOT";
                string second = stage == 1 ? "WIN" : "BOUNCY";
                var left = session.World.PropertySources.Single(s => s.Subject == subject && s.Property == first);
                var right = session.World.PropertySources.Single(s => s.Subject == subject && s.Property == second);
                CollectionAssert.AreEquivalent(new[] { stage == 1 ? "control_robot" : "lava_hot_1" }, left.TextIds.Intersect(right.TextIds));
            }
            for (int step = fingerprints.Count - 2; step >= 0; step--)
            {
                Assert.IsTrue(session.Undo());
                Assert.AreEqual(fingerprints[step], session.World.Fingerprint());
            }
            Assert.IsFalse(session.Undo());
            foreach (var command in level.referenceSolutions[0].commands) Assert.IsTrue(session.TryExecute(command));
            session.Restart();
            Assert.AreEqual(initial, session.World.Fingerprint());
        }

        [Test]
        public void WholeWallRingTransformsInPlaceAndAllNineBodiesAreYou()
        {
            var session = new GameSession(RuleDesignDraft(3));
            var walls = session.World.Entities.Where(e => e.Subject == "WALL" && e.Kind == EntityKind.Object)
                .ToDictionary(e => e.Id, e => e.Cell);
            Assert.AreEqual(8, walls.Count);
            Assert.IsTrue(session.TryExecute("N"));
            Assert.IsFalse(session.Won, "转换当步新角色保持原位，下一次输入才移动");
            Assert.AreEqual(9, session.World.Actors().Count);
            foreach (var pair in walls)
            {
                var converted = session.World.Entity(pair.Key);
                Assert.AreEqual("ROBOT", converted.Subject);
                Assert.AreEqual(pair.Value, converted.Cell);
                Assert.IsTrue(session.World.Actors().Any(e => e.Id == pair.Key));
                Assert.IsFalse(session.World.Rules.Has(converted.Subject, "STOP"));
            }
            Assert.IsTrue(session.TryExecute("S"));
            Assert.IsTrue(session.Won);
            Assert.IsTrue(walls.ContainsKey(session.World.WinRecord.YouId));
            Assert.AreEqual("goal", session.World.WinRecord.WinId);
        }

        [TestCase("L1P1")]
        [TestCase("L1P2")]
        [TestCase("L1P3")]
        [TestCase("L2P1")]
        [TestCase("L2P2")]
        [TestCase("L2P3")]
        [TestCase("L3P1")]
        [TestCase("L3P2")]
        [TestCase("L3P3")]
        public void DraftReferenceWins(string name)
        {
            string path = Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts", name + ".json");
            Assert.IsTrue(File.Exists(path), path);
            var level = LevelJsonSerializer.FromJson(File.ReadAllText(path));
            Assert.IsNotEmpty(level.referenceSolutions, name);
            foreach (var solution in level.referenceSolutions)
            {
                var session = new GameSession(level);
                for (int step = 0; step < solution.commands.Length; step++)
                    Assert.IsTrue(session.TryExecute(solution.commands[step]),
                        name + "/" + solution.id + " step " + (step + 1) + " " + solution.commands[step] +
                        ": " + session.LastRejectReason);
                Assert.IsTrue(session.Won, name + "/" + solution.id + " did not win after " + solution.commands.Length + " steps");
                var result = ReplayRunner.Run(level, solution);
                Assert.IsTrue(result.Won, name + "/" + solution.id + ": " + result.FailReason);
            }
        }
    }
}
