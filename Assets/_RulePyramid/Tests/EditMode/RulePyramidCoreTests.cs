using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class RuleWorkshopCoreTests
    {
        static string LevelsDir
        {
            get
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels"));
                return root;
            }
        }

        static LevelDefinition Load(string id)
        {
            var path = Path.Combine(LevelsDir, id + ".json");
            Assert.IsTrue(File.Exists(path), "Missing " + path);
            return LevelJsonSerializer.FromJson(File.ReadAllText(path));
        }

        /// <summary>
        /// v0.9 Fixture：规则仅来自世界 Text（含 ROBOT IS YOU），禁止 fixedRules。
        /// </summary>
        static WorldModel Fixture(GridCell playerPos, EntityDefinition[] extras = null, string[] extraSentences = null)
        {
            var entities = new List<EntityDefinition>
            {
                new EntityDefinition
                {
                    id = "player", kind = "Object", subject = "ROBOT", token = "", cell = playerPos, anchored = false
                }
            };

            // 预留区放置 ROBOT IS YOU（沿 +X）
            const int ruleX = -2;
            const int ruleY = 1;
            const int ruleZ = -2;
            entities.Add(Tx("fx_you_s", "ROBOT", new GridCell(ruleX, ruleY, ruleZ)));
            entities.Add(Tx("fx_you_is", "IS", new GridCell(ruleX + 1, ruleY, ruleZ)));
            entities.Add(Tx("fx_you_p", "YOU", new GridCell(ruleX + 2, ruleY, ruleZ)));

            if (extras != null) entities.AddRange(extras);

            if (extraSentences != null)
            {
                for (int s = 0; s < extraSentences.Length; s++)
                {
                    var parts = extraSentences[s].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int z = ruleZ + 1 + s;
                    for (int t = 0; t < parts.Length; t++)
                        entities.Add(Tx("fx_s" + s + "_" + t, parts[t], new GridCell(ruleX + t, ruleY, z)));
                }
            }

            var level = new LevelDefinition
            {
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                id = "fixture",
                title = "fixture",
                bounds = new GridCellBox { min = new GridCell(-2, 0, -2), max = new GridCell(8, 8, 8) },
                terrain = new[]
                {
                    new GridCellBox { min = new GridCell(-2, 0, -2), max = new GridCell(8, 0, 8) }
                },
                entities = entities.ToArray(),
                fixedRules = Array.Empty<FixedRuleData>(),
                options = new OptionsData
                {
                    actionMode = "MoveClimbHoldPush",
                    winMode = "DistinctEntitiesSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly",
                    collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText",
                    supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo",
                    bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly",
                    textMobilityMode = "AllWordsMovable_GeometryAccess"
                },
                designContract = new DesignContractData
                {
                    requireActiveInteraction = false,
                    minimumSolutionFamilies = 1,
                    interactionIsAuthoringConstraint = true
                },
                referenceSolutions = new[]
                {
                    new ReferenceSolutionData { id = "A", name = "A", family = "A", commands = new string[0] }
                }
            };
            return WorldModel.FromLevel(level);
        }

        static EntityDefinition Ob(string id, string subject, GridCell cell)
        {
            return new EntityDefinition
            {
                id = id, kind = "Object", subject = subject, token = "", cell = cell, anchored = false
            };
        }

        static EntityDefinition Tx(string id, string token, GridCell cell, bool anchored = false)
        {
            return new EntityDefinition
            {
                id = id, kind = "Text", subject = "", token = token, cell = cell, anchored = anchored
            };
        }

        [Test]
        public void Schema_Tokens_Are_V09()
        {
            Assert.AreEqual(9, Tokens.SchemaVersion);
            Assert.AreEqual("RW-v0.9", Tokens.MechanicsVersion);
            Assert.IsTrue(Tokens.Commands.Contains("PE"));
            Assert.IsTrue(Tokens.Subjects.Contains("ROBOT"));
            Assert.IsTrue(Tokens.Props.Contains("BOUNCY"));
            Assert.IsFalse(Tokens.Props.Contains("BLOCK"));
        }

        [Test]
        public void Solid_Requires_YouPushStop_Or_Text()
        {
            var m = Fixture(new GridCell(0, 1, 0), new[] { Ob("rock", "ROCK", new GridCell(1, 1, 0)) },
                new[] { "ROCK IS PUSH" });
            Assert.IsTrue(m.Solid(m.Entity("player")));
            Assert.IsTrue(m.Solid(m.Entity("rock")));
            m = Fixture(new GridCell(0, 1, 0), new[] { Ob("rock", "ROCK", new GridCell(1, 1, 0)) });
            Assert.IsFalse(m.Solid(m.Entity("rock")));
        }

        [Test]
        public void DirectionPushesRockAndUndoRestoresBothObjects()
        {
            var m = Fixture(new GridCell(0, 1, 0), new[] { Ob("rock", "ROCK", new GridCell(1, 1, 0)) },
                new[] { "ROCK IS PUSH" });
            string initial = m.Fingerprint();
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.AreEqual(new GridCell(1, 1, 0), m.Entity("player").Cell);
            Assert.AreEqual(new GridCell(2, 1, 0), m.Entity("rock").Cell);
            Assert.IsTrue(m.Undo());
            Assert.AreEqual(initial, m.Fingerprint());
        }

        [Test]
        public void DirectionPushesMovableSolidsAndNeverClimbsBlockedSolids(
            [Values("E", "W", "N", "S")] string command,
            [Values("ROCK_PUSH", "ROCK_STOP", "WALL_STOP", "TEXT", "TERRAIN")] string obstacle)
        {
            var start = new GridCell(3, 1, 3);
            WorldDirections.TryParse(command, out var direction);
            var destination = start.Add(WorldDirections.ToOffset(direction));
            EntityDefinition[] extras;
            string[] rules;
            if (obstacle == "TEXT")
            {
                extras = new[] { Tx("blocker", "WIN", destination) };
                rules = null;
            }
            else if (obstacle == "TERRAIN")
            {
                extras = null;
                rules = null;
            }
            else
            {
                var parts = obstacle.Split('_');
                extras = new[] { Ob("blocker", parts[0], destination) };
                rules = new[] { parts[0] + " IS " + parts[1] };
            }
            var m = Fixture(start, extras, rules);
            if (obstacle == "TERRAIN") m.Terrain.Add(destination);
            Assert.Greater(m.Spec.bounds.max.y, start.y + 1, "本测试不能靠低顶边界阻止登攀");
            if (obstacle == "ROCK_PUSH" || obstacle == "TEXT")
            {
                var target = destination.Add(WorldDirections.ToOffset(direction));
                Assert.IsTrue(m.TryCommand(command, out _), "普通方向必须推动物件与词牌");
                Assert.AreEqual(destination, m.Actor().Cell);
                Assert.AreEqual(target, m.Entity("blocker").Cell);
                Assert.IsTrue(m.Undo());
                m.Terrain.Add(target); // 堵住推链末端，上方仍有空间，禁止退回翻越。
            }
            string initial = m.Fingerprint();
            int logCount = m.Log.Count;
            Assert.IsFalse(m.TryCommand(command, out _), obstacle + "/" + command);
            Assert.AreEqual(initial, m.Fingerprint());
            Assert.AreEqual(start, m.Actor().Cell);
            Assert.AreEqual(0, m.History.Count);
            Assert.AreEqual(logCount, m.Log.Count);
            if (obstacle == "TERRAIN" || obstacle.EndsWith("_STOP"))
            {
                Assert.IsFalse(m.TryCommand("P" + command, out _), "不可推物不能退回登攀");
                Assert.AreEqual(initial, m.Fingerprint());
            }
        }

        [Test]
        public void PushChainRemainsAtomicAndBlockedPushNeverClimbs()
        {
            var m = Fixture(new GridCell(0, 1, 0), new[]
                {
                    Ob("first", "ROCK", new GridCell(1, 1, 0)),
                    Ob("second", "ROCK", new GridCell(2, 1, 0))
                }, new[] { "ROCK IS PUSH" });
            m.Terrain.Add(new GridCell(3, 1, 0));
            string initial = m.Fingerprint();
            Assert.IsFalse(m.TryCommand("E", out _));
            Assert.AreEqual(initial, m.Fingerprint());
            Assert.AreEqual(0, m.History.Count);
            m.Terrain.Remove(new GridCell(3, 1, 0));
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.AreEqual(new GridCell(1, 1, 0), m.Actor().Cell);
            Assert.AreEqual(new GridCell(2, 1, 0), m.Entity("first").Cell);
            Assert.AreEqual(new GridCell(3, 1, 0), m.Entity("second").Cell);
        }

        [Test]
        public void NonRobotYouAlsoCannotClimbButCanEnterNonSolidObjects()
        {
            var m = Fixture(new GridCell(0, 1, 0),
                new[] { Ob("wall", "WALL", new GridCell(1, 1, 0)) }, new[] { "WALL IS STOP" });
            m.Entity("player").Subject = "CLOUD";
            m.Entity("fx_you_s").Token = "CLOUD";
            m.Refresh();
            Assert.AreEqual("CLOUD", m.Actor().Subject);
            Assert.IsFalse(m.TryCommand("E", out _));
            m.Entity("fx_s0_2").Token = "WIN";
            m.Refresh();
            Assert.IsTrue(m.TryCommand("E", out _));
            Assert.AreEqual(new GridCell(1, 1, 0), m.Actor().Cell);
            Assert.IsTrue(m.WonLatched);
        }

        [TestCase("E", "PE")]
        [TestCase("W", "PW")]
        [TestCase("N", "PN")]
        [TestCase("S", "PS")]
        public void LegacyPushCommandsParseAndRecordAsOrdinaryDirections(string direction, string legacy)
        {
            Assert.IsTrue(SimCommand.TryParse(legacy, out var parsed, out _));
            Assert.AreEqual(CommandKind.Move, parsed.Kind);
            Assert.AreEqual(direction, parsed.Raw);
            WorldDirections.TryParse(direction, out var worldDirection);
            var start = new GridCell(3, 1, 3);
            var destination = start.Add(WorldDirections.ToOffset(worldDirection));
            var model = Fixture(start, new[] { Ob("rock", "ROCK", destination) }, new[] { "ROCK IS PUSH" });
            var normal = new GameSession(model.Spec);
            var compatible = new GameSession(model.Spec);
            var recorder = new PlaytestRecorder(compatible);
            Assert.IsTrue(normal.TryExecute(direction));
            Assert.IsTrue(recorder.TryExecute(legacy));
            Assert.AreEqual(normal.World.Fingerprint(), compatible.World.Fingerprint());
            CollectionAssert.AreEqual(new[] { direction }, recorder.Commands);
            Assert.IsTrue(recorder.Undo());
            Assert.AreEqual(start, compatible.World.Actor().Cell);
            Assert.AreEqual(0, recorder.CommandCount);
        }

        [Test]
        public void Transform_Changes_Subject_Not_Id()
        {
            var m = Fixture(new GridCell(0, 1, 0),
                new[]
                {
                    Ob("rock", "ROCK", new GridCell(3, 1, 0)),
                    Tx("w_rock", "ROCK", new GridCell(1, 1, 1)),
                    Tx("w_is", "IS", new GridCell(2, 1, 1)),
                    Tx("w_flag", "FLAG", new GridCell(3, 1, 1))
                },
                new[] { "FLAG IS WIN" });
            Assert.AreEqual("ROCK", m.Entity("rock").Subject);
            try { m.ResolveRules("Test"); }
            catch (VictoryCommittedException) { }
            Assert.AreEqual("FLAG", m.Entity("rock").Subject);
            Assert.AreEqual("rock", m.Entity("rock").Id);
        }

        [Test]
        public void AllTwelveLevels_LoadAndValidate()
        {
            for (int i = 1; i <= 12; i++)
            {
                var level = Load("L" + i.ToString("00"));
                var report = LevelValidator.ValidateStructure(level);
                Assert.IsFalse(report.HasStructureErrors, level.id + ": " + report);
                Assert.AreEqual(9, level.schemaVersion);
                Assert.AreEqual("RW-v0.9", level.mechanicsVersion);
                Assert.IsNotNull(level.referenceSolutions);
                Assert.GreaterOrEqual(level.referenceSolutions.Length, 1);
            }
        }

        [Test]
        public void AllEighteenReferenceSolutions_Win()
        {
            int total = 0;
            for (int i = 1; i <= 12; i++)
            {
                var level = Load("L" + i.ToString("00"));
                foreach (var sol in level.referenceSolutions)
                {
                    total++;
                    var result = ReplayRunner.Run(level, sol);
                    Assert.IsTrue(result.Won, level.id + "/" + sol.id + ": " + result.FailReason);
                    var session = new GameSession(level);
                    foreach (var cmd in sol.commands)
                        Assert.IsTrue(session.TryExecute(cmd), cmd);
                    Assert.IsTrue(session.Won);
                    while (session.Undo()) { }
                    Assert.IsFalse(session.Won);
                    Assert.AreEqual(0, session.TurnCount);
                }
            }
            Assert.AreEqual(18, total);
        }

        [Test]
        public void L01_NoInteractionAudit_Exhausted()
        {
            var level = Load("L01");
            Assert.IsTrue(level.designContract.requireActiveInteraction);
            var audit = InteractionAudit.AuditNoInteraction(level, 20000);
            Assert.AreEqual("EXHAUSTED_NO_INTERACTION_WIN", audit.Status, audit.Status + " states=" + audit.States);
        }

        [Test]
        public void L02_NoInteractionAudit_Exhausted()
        {
            var level = Load("L02");
            Assert.IsTrue(level.designContract.requireActiveInteraction);
            var audit = InteractionAudit.AuditNoInteraction(level, 20000);
            Assert.AreEqual("EXHAUSTED_NO_INTERACTION_WIN", audit.Status, audit.Status + " states=" + audit.States);
        }

        [Test]
        public void L01_ReferencePushesRockWithoutChangingRules_AlternativeCanChangeRules()
        {
            var level = Load("L01");
            Assert.IsTrue(level.designContract.requireActiveInteraction);
            var result = ReplayRunner.Run(level, level.referenceSolutions[0]);
            Assert.IsTrue(result.Won, result.FailReason);
            Assert.IsTrue(result.EventKinds.Contains("ActiveInteraction"));
            Assert.IsFalse(result.EventKinds.Contains("RulesChanged"));
            Assert.AreEqual(new GridCell(7, 1, 0), ReplayRockAfter(level, level.referenceSolutions[0].commands));

            // 规则词牌保持可改写，作为主教学解以外的合法路线。
            var alternative = new GameSession(level);
            foreach (var command in new[] { "S", "S", "E", "PS", "N", "W", "N", "N", "E", "E", "E", "E", "E" })
                Assert.IsTrue(alternative.TryExecute(command), command + ": " + alternative.LastRejectReason);
            Assert.IsTrue(alternative.Won);
            Assert.AreEqual(new GridCell(3, 1, 0), alternative.World.Entity("rock").Cell);
            Assert.IsFalse(alternative.World.Solid(alternative.World.Entity("rock")));
            Assert.IsTrue(InteractionAudit.IsActiveInteraction(alternative.World.Log));
        }

        [Test]
        public void L02_ReferenceBreaksStopAndPassesThroughUnmovedRock()
        {
            var level = Load("L02");
            var session = new GameSession(level);
            Assert.IsTrue(session.World.Props(session.World.Entity("rock")).Contains("STOP"));
            Assert.IsTrue(session.World.Solid(session.World.Entity("rock")));
            Assert.IsFalse(session.World.Movable(session.World.Entity("rock")));
            Assert.IsTrue(session.TryExecute("E"));
            Assert.IsFalse(session.TryExecute("E"), "完整 STOP 规则不能直接穿过岩块");
            Assert.AreEqual(new GridCell(3, 1, 0), session.World.Entity("rock").Cell);
            session.Restart();

            bool crossedRock = false;
            foreach (var command in level.referenceSolutions[0].commands)
            {
                Assert.IsTrue(session.TryExecute(command), command + ": " + session.LastRejectReason);
                if (session.World.Actor().Cell == session.World.Entity("rock").Cell)
                    crossedRock = true;
            }
            Assert.IsTrue(session.Won);
            Assert.IsTrue(crossedRock, "参考解应真正与失去 STOP 的岩块共享一格");
            Assert.AreEqual(new GridCell(3, 1, 0), session.World.Entity("rock").Cell);
            Assert.IsFalse(session.World.Props(session.World.Entity("rock")).Contains("STOP"));
            Assert.IsFalse(session.World.Solid(session.World.Entity("rock")));
            var result = ReplayRunner.Run(level, level.referenceSolutions[0]);
            Assert.IsTrue(result.Won, result.FailReason);
            Assert.IsTrue(result.EventKinds.Contains("RulesChanged"));
        }

        [Test]
        public void L03_StartsWithCloudIsYou_AndCloudWins()
        {
            var level = Load("L03");
            var world = WorldModel.FromLevel(level);
            Assert.AreEqual("cloud_01", world.ActorId);
            Assert.AreEqual("CLOUD", world.Actor().Subject);
            Assert.IsTrue(world.Rules.Get("CLOUD").Contains("YOU"));
            Assert.IsFalse(world.Rules.Get("ROBOT").Contains("YOU"));
            var result = ReplayRunner.Run(level, level.referenceSolutions[0]);
            Assert.IsTrue(result.Won, result.FailReason);
            Assert.AreEqual("cloud_01", result.ActorIdAtWin);
            Assert.AreEqual("goal", result.WinId);
        }

        [Test]
        public void FirstThreeLevels_ShowOnePromptAtSpawn()
        {
            for (int i = 1; i <= 3; i++)
            {
                var level = Load("L" + i.ToString("00"));
                var session = new GameSession(level);
                var prompts = new RegionTutorialSession(level.tutorial);
                prompts.Observe(session);
                Assert.IsNotNull(prompts.Current, level.id + " 出生时应立即出现提示");
                Assert.AreEqual(5f, prompts.Current.durationSeconds, level.id);
                prompts.Tick(5.1);
                Assert.IsNull(prompts.Current, level.id + " 提示展示结束后应消失");
            }
        }

        static GridCell ReplayRockAfter(LevelDefinition level, string[] commands)
        {
            var session = new GameSession(level);
            foreach (var command in commands)
                Assert.IsTrue(session.TryExecute(command), command + ": " + session.LastRejectReason);
            Assert.IsTrue(session.Won);
            return session.World.Entity("rock").Cell;
        }

        [Test]
        public void ControlTransfer_Changes_ActorId()
        {
            var level = new LevelDefinition
            {
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                id = "xfer",
                title = "xfer",
                bounds = new GridCellBox { min = new GridCell(-2, 0, -2), max = new GridCell(7, 7, 5) },
                terrain = new[] { new GridCellBox { min = new GridCell(-2, 0, -2), max = new GridCell(7, 0, 5) } },
                entities = new[]
                {
                    Ob("body_a", "ROBOT", new GridCell(1, 1, 1)),
                    Ob("body_b", "FLAG", new GridCell(5, 1, 0)),
                    Tx("subject_a", "ROBOT", new GridCell(0, 1, 2)),
                    Tx("connector", "IS", new GridCell(1, 1, 2)),
                    Tx("you_a", "YOU", new GridCell(2, 1, 2)),
                    Tx("subject_b", "FLAG", new GridCell(0, 1, 3)),
                    Tx("you_b", "YOU", new GridCell(2, 1, 3)),
                    Tx("win_flag_s", "FLAG", new GridCell(4, 1, 4)),
                    Tx("win_flag_is", "IS", new GridCell(5, 1, 4)),
                    Tx("win_flag_p", "WIN", new GridCell(6, 1, 4)),
                    Tx("win_robot_s", "ROBOT", new GridCell(4, 1, 5)),
                    Tx("win_robot_is", "IS", new GridCell(5, 1, 5)),
                    Tx("win_robot_p", "WIN", new GridCell(6, 1, 5))
                },
                fixedRules = Array.Empty<FixedRuleData>(),
                options = new OptionsData
                {
                    actionMode = "MoveClimbHoldPush",
                    winMode = "DistinctEntitiesSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly",
                    collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText",
                    supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo",
                    bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly",
                    textMobilityMode = "AllWordsMovable_GeometryAccess"
                }
            };
            var m = WorldModel.FromLevel(level);
            Assert.AreEqual("body_a", m.ActorId);
            Assert.IsTrue(m.TryCommand("PN", out _), m.LastRejection);
            Assert.AreEqual("body_b", m.ActorId);
            Assert.IsFalse(m.WonLatched);
        }

        [Test]
        public void EditSession_Playtest_DoesNotMutateDraft()
        {
            var level = Load("L01");
            var session = new LevelEditSession(level);
            var before = session.Draft.entities[0].cell;
            Assert.IsTrue(session.TryStartPlaytest(out var play, out var err), err);
            foreach (var cmd in level.referenceSolutions[0].commands)
                play.TryExecute(cmd);
            Assert.IsTrue(play.Won);
            session.StopPlaytest();
            Assert.AreEqual(before, session.Draft.entities[0].cell);
        }
    }
}
