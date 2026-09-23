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
        public void Climb_Vs_Push_Are_Distinct()
        {
            var m = Fixture(new GridCell(0, 1, 0), new[] { Ob("rock", "ROCK", new GridCell(1, 1, 0)) },
                new[] { "ROCK IS PUSH" });
            Assert.IsTrue(m.TryCommand("E", out _)); // climb onto rock
            Assert.AreEqual(new GridCell(1, 2, 0), m.Entity("player").Cell);

            m = Fixture(new GridCell(0, 1, 0), new[] { Ob("rock", "ROCK", new GridCell(1, 1, 0)) },
                new[] { "ROCK IS PUSH" });
            Assert.IsTrue(m.TryCommand("PE", out _));
            Assert.AreEqual(new GridCell(1, 1, 0), m.Entity("player").Cell);
            Assert.AreEqual(new GridCell(2, 1, 0), m.Entity("rock").Cell);
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
        public void L01_RequiresActiveInteraction_AndReferenceWins()
        {
            var level = Load("L01");
            Assert.IsTrue(level.designContract.requireActiveInteraction);
            var result = ReplayRunner.Run(level, level.referenceSolutions[0]);
            Assert.IsTrue(result.Won, result.FailReason);
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
