using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;

namespace RulePyramid.Tests.EditMode
{
    public class RuleReadingDirectionTests
    {
        static EntityState Word(string id, string token, GridCell cell)
        {
            return new EntityState { Id = id, Kind = EntityKind.Text, Token = token, Cell = cell };
        }

        static WorldModel Read(params EntityState[] words)
        {
            var world = new WorldModel();
            world.Entities.AddRange(words);
            world.Refresh();
            return world;
        }

        [Test]
        public void WorldRulesReadNounIsPropertyFromLeftAndTopOnly()
        {
            var horizontal = Read(
                Word("left", "ROBOT", new GridCell(0, 1, 4)),
                Word("middle", "IS", new GridCell(1, 1, 4)),
                Word("right", "YOU", new GridCell(2, 1, 4)));
            Assert.IsTrue(horizontal.Rules.Has("ROBOT", "YOU"));
            CollectionAssert.AreEqual(new[] { "left", "middle", "right" }, horizontal.PropertySources.Single().TextIds);

            var vertical = Read(
                Word("top", "WALL", new GridCell(4, 1, 5)),
                Word("middle", "IS", new GridCell(4, 1, 4)),
                Word("bottom", "STOP", new GridCell(4, 1, 3)));
            Assert.IsTrue(vertical.Rules.Has("WALL", "STOP"));
            CollectionAssert.AreEqual(new[] { "top", "middle", "bottom" }, vertical.PropertySources.Single().TextIds);

            var rightToLeft = Read(
                Word("noun", "ROBOT", new GridCell(2, 1, 4)),
                Word("is", "IS", new GridCell(1, 1, 4)),
                Word("property", "YOU", new GridCell(0, 1, 4)));
            Assert.IsFalse(rightToLeft.Rules.Has("ROBOT", "YOU"));
            Assert.IsEmpty(rightToLeft.PropertySources);

            var bottomToTop = Read(
                Word("noun", "WALL", new GridCell(4, 1, 3)),
                Word("is", "IS", new GridCell(4, 1, 4)),
                Word("property", "STOP", new GridCell(4, 1, 5)));
            Assert.IsFalse(bottomToTop.Rules.Has("WALL", "STOP"));
            Assert.IsEmpty(bottomToTop.PropertySources);
        }

        [Test]
        public void DownwardNounConversionAndAndSourcesKeepTheirMeaning()
        {
            var conversion = Read(
                Word("noun", "ROBOT", new GridCell(2, 1, 7)),
                Word("is", "IS", new GridCell(2, 1, 6)),
                Word("target", "FLAG", new GridCell(2, 1, 5)));
            Assert.AreEqual("FLAG", conversion.Transforms["ROBOT"]);
            var source = conversion.TransformSources.Single();
            Assert.AreEqual("ROBOT", source.Source);
            Assert.AreEqual("FLAG", source.Target);
            CollectionAssert.AreEqual(new[] { "noun", "is", "target" }, (List<string>)source.Origin);

            var reversedNouns = Read(
                Word("noun", "ROBOT", new GridCell(3, 1, 5)),
                Word("is", "IS", new GridCell(3, 1, 6)),
                Word("target", "FLAG", new GridCell(3, 1, 7)));
            Assert.IsFalse(reversedNouns.Transforms.ContainsKey("ROBOT"));
            Assert.AreEqual("ROBOT", reversedNouns.Transforms["FLAG"]);
            CollectionAssert.AreEqual(new[] { "target", "is", "noun" },
                (List<string>)reversedNouns.TransformSources.Single().Origin);

            var words = new[] { "ROBOT", "AND", "WALL", "IS", "YOU", "AND", "PUSH" };
            var line = new EntityState[words.Length];
            for (int i = 0; i < words.Length; i++)
                line[i] = Word("word_" + i, words[i], new GridCell(5, 1, 9 - i));
            var world = Read(line);
            Assert.IsTrue(world.Rules.Has("ROBOT", "YOU"));
            Assert.IsTrue(world.Rules.Has("ROBOT", "PUSH"));
            Assert.IsTrue(world.Rules.Has("WALL", "YOU"));
            Assert.IsTrue(world.Rules.Has("WALL", "PUSH"));
            var fullLineSources = world.PropertySources.Where(s => s.TextIds[0] == "word_0").ToArray();
            Assert.AreEqual(4, fullLineSources.Length);
            foreach (var propertySource in fullLineSources)
                CollectionAssert.AreEqual(line.Select(e => e.Id).ToArray(), propertySource.TextIds);
        }

        [Test]
        public void CompatibilityParserUsesTheSameReadingDirections()
        {
            var top = new[]
            {
                Word("noun", "FLAG", new GridCell(0, 1, 3)),
                Word("is", "IS", new GridCell(0, 1, 2)),
                Word("property", "WIN", new GridCell(0, 1, 1))
            };
            Assert.IsTrue(RuleParser.Parse(null, top).Has("FLAG", "WIN"));
            var bottom = new[]
            {
                Word("noun", "FLAG", new GridCell(0, 1, 1)),
                Word("is", "IS", new GridCell(0, 1, 2)),
                Word("property", "WIN", new GridCell(0, 1, 3))
            };
            Assert.IsFalse(RuleParser.Parse(null, bottom).Has("FLAG", "WIN"));
        }
    }
}
