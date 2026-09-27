using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;

namespace RulePyramid.Tests.EditMode
{
    public class AuthoringSerializationTests
    {
        static JObject Draft()
        {
            return new JObject
            {
                ["schemaVersion"] = 9,
                ["mechanicsVersion"] = "RW-v0.9",
                ["id"] = "unfinished",
                ["title"] = "尚未拼出 YOU",
                ["bounds"] = new JObject
                {
                    ["min"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 },
                    ["max"] = new JObject { ["x"] = 5, ["y"] = 5, ["z"] = 5 }
                },
                ["terrain"] = new JArray(),
                ["entities"] = new JArray
                {
                    new JObject
                    {
                        ["id"] = "robot",
                        ["kind"] = "Object",
                        ["subject"] = "ROBOT",
                        ["cell"] = new JObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 }
                    }
                },
                ["tutorial"] = new JObject
                {
                    ["objective"] = "走到标记处",
                    ["regions"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = "entry",
                            ["name"] = "入口",
                            ["text"] = "欢迎",
                            ["bounds"] = new JObject
                            {
                                ["min"] = new JObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 },
                                ["max"] = new JObject { ["x"] = 2, ["y"] = 2, ["z"] = 2 }
                            },
                            ["durationSeconds"] = 2.5f,
                            ["enabled"] = true,
                            ["futureEditorColor"] = "violet"
                        }
                    }
                },
                ["designerNotes"] = new JObject { ["intent"] = "尚未完成" }
            };
        }

        [Test]
        public void DraftWithoutYouRoundTripsAndKeepsUnknownDesignMetadata()
        {
            var source = Draft();
            var level = LevelJsonSerializer.FromDraftJson(source.ToString());
            Assert.AreEqual(1, level.entities.Length);
            Assert.AreEqual("ROBOT", level.entities[0].subject);

            var saved = JObject.Parse(LevelJsonSerializer.ToJson(level));
            Assert.IsNull(saved["fixedRules"]);
            Assert.IsNull(saved["authoringSourceJson"]);
            Assert.AreEqual("尚未完成", saved["designerNotes"]?["intent"]?.Value<string>());
            Assert.AreEqual("violet", saved["tutorial"]?["regions"]?[0]?["futureEditorColor"]?.Value<string>());

            var reopened = LevelJsonSerializer.FromDraftJson(saved.ToString());
            Assert.AreEqual(0, reopened.referenceSolutions.Length, "不能凭空恢复空白旧参考解");
            Assert.AreEqual("欢迎", reopened.tutorial.regions[0].text);
            Assert.AreEqual(2.5f, reopened.tutorial.regions[0].durationSeconds, 0.001f);
            Assert.IsTrue(reopened.tutorial.regions[0].enabled);
        }

        [Test]
        public void LegacyFixedRulesFieldIsRejectedEvenWhenEmpty()
        {
            var source = Draft();
            source["fixedRules"] = new JArray();
            Assert.Throws<InvalidOperationException>(() => LevelJsonSerializer.FromDraftJson(source.ToString()));
        }

        [Test]
        public void RegionDataAndBoundsAreIndependentAfterClone()
        {
            var source = LevelJsonSerializer.FromDraftJson(Draft().ToString());
            var clone = source.Clone();
            clone.tutorial.regions[0].name = "已改名";
            clone.tutorial.regions[0].bounds.max = new GridCell(4, 4, 4);

            Assert.AreEqual("入口", source.tutorial.regions[0].name);
            Assert.AreEqual(new GridCell(2, 2, 2), source.tutorial.regions[0].bounds.max);
            var saved = JObject.Parse(LevelJsonSerializer.ToJson(clone));
            Assert.AreEqual("已改名", saved["tutorial"]?["regions"]?[0]?["name"]?.Value<string>());
            Assert.AreEqual(4, saved["tutorial"]?["regions"]?[0]?["bounds"]?["max"]?["x"]?.Value<int>());
            Assert.AreEqual("violet", saved["tutorial"]?["regions"]?[0]?["futureEditorColor"]?.Value<string>());
        }
    }
}
