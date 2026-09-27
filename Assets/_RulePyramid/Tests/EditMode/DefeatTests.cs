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
    public class DefeatTests
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
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "defeat-mechanics",
                title = "DEFEAT 机制",
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

        [TestCase("DEFEAT")]
        [TestCase("DEFEAT AND WIN")]
        [TestCase("DEFEAT AND HOT")]
        public void YouEntryIsDestroyedWithoutMeltBeforeWin(string properties)
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU", "LAVA IS " + properties);
            Assert.IsNotNull(world.Entity("robot"), "相邻不会销毁");
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.IsNotNull(world.Entity("hazard"));
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
            Assert.IsFalse(world.WonLatched);
            Assert.AreEqual("hazard", world.Log.Single(e => e.Kind == "Defeated").SurfaceId);
        }

        [TestCase("STOP")]
        [TestCase("PUSH")]
        public void BlockedOrPushedHazardDoesNotCountAsEntry(string property)
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "ROCK", C(2)) },
                "ROBOT IS YOU", "ROCK IS DEFEAT AND " + property);
            bool accepted = world.TryCommand("E", out _);
            Assert.AreEqual(property == "PUSH", accepted);
            Assert.IsNotNull(world.Entity("robot"));
            Assert.AreNotEqual(world.Entity("robot").Cell, world.Entity("hazard").Cell);
            Assert.IsFalse(world.Log.Any(e => e.Kind == "Defeated"));
        }

        [Test]
        public void NonYouCanFallIntoDefeat()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1, 1, 7)),
                Object("rock", "ROCK", C(2, 3)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU", "ROCK IS MELT", "LAVA IS DEFEAT");
            Move(world, "E");
            Assert.AreEqual(C(2), world.Entity("rock").Cell);
            Assert.IsNotNull(world.Entity("robot"));
        }

        [Test]
        public void YouFallsIntoDefeatAndCleansMotionState()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1, 3)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT");
            world.Terrain.Add(C(1, 2));
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.IsFalse(world.Apex.ContainsKey("robot"));
            Assert.IsFalse(world.ForcedFall.Contains("robot"));
            Assert.IsFalse(world.Pressed.Contains("robot"));
        }

        [Test]
        public void BouncyTopDoesNotEnterDefeatCell()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1, 3)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT AND BOUNCY");
            world.Terrain.Add(C(1, 2));
            Move(world, "E");
            Assert.IsNotNull(world.Entity("robot"));
            Assert.IsTrue(world.Apex.ContainsKey("robot"));
            Assert.IsFalse(world.Log.Any(e => e.Kind == "Defeated"));
        }

        [Test]
        public void JumpIntoDefeatDestroysYou()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(1, 2)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT AND HOVER");
            Move(world, "J");
            Assert.IsNull(world.Entity("robot"));
            Assert.AreEqual(C(1, 2), world.Log.Single(e => e.Kind == "Defeated").Cell);
        }

        [Test]
        public void HazardMovingIntoStationaryYouDoesNotCountAsYouEntry()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(1, 2)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT");
            world.Settle();
            Assert.AreEqual(C(1), world.Entity("hazard").Cell);
            Assert.IsNotNull(world.Entity("robot"));
            Move(world, "E");
            Move(world, "W");
            Assert.IsNull(world.Entity("robot"));
        }

        [Test]
        public void RuleActivationOnlyDestroysOnNextEntryAndRemovalDisablesHazard()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(1)) },
                "ROBOT IS YOU", "LAVA IS HOT");
            world.Entity("rule_1_2").Token = "DEFEAT";
            world.ResolveRules("TestRuleChanged");
            Assert.IsNotNull(world.Entity("robot"), "仅规则变化，不结算原地对象");
            Move(world, "E");
            Move(world, "W");
            Assert.IsNull(world.Entity("robot"));
            Assert.IsTrue(world.Undo());
            world.Entity("rule_1_2").Token = "HOT";
            world.ResolveRules("TestRuleRemoved");
            Move(world, "W");
            Assert.IsNotNull(world.Entity("robot"));
        }

        [Test]
        public void PartialAndCompleteDefeatSupportsUndoAndRestart()
        {
            var level = Level(new[] { Object("first", "ROBOT", C(1)), Object("second", "ROBOT", C(1, 1, 7)),
                Object("hazard_a", "LAVA", C(2)), Object("hazard_b", "LAVA", C(3, 1, 7)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT");
            var session = new GameSession(level);
            var world = session.World;
            string initial = world.Fingerprint();
            Assert.IsTrue(session.TryExecute("E"), session.LastRejectReason);
            Assert.IsNull(world.Entity("first"));
            Assert.AreEqual(1, world.Actors().Count);
            Assert.AreEqual(MotionPhase.Grounded, world.Phase);
            Assert.IsTrue(session.TryExecute("E"), session.LastRejectReason);
            Assert.AreEqual(MotionPhase.NoControl, world.Phase);
            Assert.IsFalse(session.TryExecute("E"));
            Assert.IsTrue(world.Undo());
            Assert.AreEqual(1, world.Actors().Count);
            Assert.IsTrue(world.Undo());
            Assert.AreEqual(initial, world.Fingerprint());
            Assert.IsTrue(session.TryExecute("E"), session.LastRejectReason);
            session.Restart();
            Assert.AreEqual(initial, session.World.Fingerprint());
            Assert.AreEqual(0, session.TurnCount);
        }

        [Test]
        public void DefeatAndHeatProduceOneDestructionEvent()
        {
            var world = World(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU AND MELT", "LAVA IS DEFEAT AND HOT");
            Move(world, "E");
            Assert.IsNull(world.Entity("robot"));
            Assert.AreEqual(1, world.Log.Count(e => e.Kind == "Defeated" || e.Kind == "Melted"));
        }

        [Test]
        public void WordRoundTripValidationAndParsersRecognizeDefeat()
        {
            var level = Level(new[] { Object("robot", "ROBOT", C(1)), Object("hazard", "LAVA", C(2)) },
                "ROBOT IS YOU", "LAVA IS DEFEAT AND WIN");
            level = LevelJsonSerializer.FromJson(LevelJsonSerializer.ToJson(level));
            var report = LevelValidator.ValidateForPlaytest(level);
            Assert.IsTrue(report.CanPlaytest, report.ToString());
            var world = WorldModel.FromLevel(level);
            Assert.IsTrue(world.Rules.Has("LAVA", "DEFEAT"));
            Assert.IsTrue(RuleParser.Parse(null, world.Entities).Has("LAVA", "DEFEAT"));
            Assert.IsTrue(Tokens.IsProp("DEFEAT"));
        }
    }
}
