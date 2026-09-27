using System;
using System.Collections.Generic;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public sealed class EditorPickingTests
    {
        static readonly GridCell Cell = new GridCell(1, 1, 1);

        static LevelDefinition Level()
        {
            return new LevelDefinition
            {
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 4, 4) },
                terrain = new[] { new GridCellBox { id = "ground", min = Cell, max = Cell, appearance = "Stone" } },
                entities = new[]
                {
                    new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = Cell },
                    new EntityDefinition { id = "word", kind = "Text", token = "WIN", cell = Cell }
                }
            };
        }

        [Test]
        public void CategoryMasksAllowOnlyRequestedKinds()
        {
            Assert.IsFalse(EditorPicking.Allows(EditorCategory.None, EntityKind.Object));
            Assert.IsFalse(EditorPicking.Allows(EditorCategory.Terrain, EntityKind.Text));
            Assert.IsTrue(EditorPicking.Allows(EditorCategory.Object, EntityKind.Object));
            Assert.IsFalse(EditorPicking.Allows(EditorCategory.Object, EntityKind.Text));
            Assert.IsTrue(EditorPicking.Allows(EditorCategory.Text, EntityKind.Text));
            Assert.IsTrue(EditorPicking.Allows(EditorCategory.All, EntityKind.Object));
            Assert.IsTrue(EditorPicking.Allows(EditorCategory.All, EntityKind.Text));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        [TestCase(6, 2)]
        [TestCase(7, 3)]
        public void CandidatesHonorEveryCategoryMask(int bits, int expectedCount)
        {
            var mask = (EditorCategory)bits;
            var samples = EditorPicking.Candidates(Level(), Cell, mask, null);
            Assert.AreEqual(expectedCount, samples.Count);
            var categories = new HashSet<EditorCategory>();
            foreach (var sample in samples)
            {
                Assert.AreNotEqual(EditorCategory.None, mask & sample.Category);
                Assert.AreEqual(Cell, sample.Cell);
                Assert.IsTrue(categories.Add(sample.Category));
            }
            Assert.AreEqual((mask & EditorCategory.Terrain) != 0, categories.Contains(EditorCategory.Terrain));
            Assert.AreEqual((mask & EditorCategory.Object) != 0, categories.Contains(EditorCategory.Object));
            Assert.AreEqual((mask & EditorCategory.Text) != 0, categories.Contains(EditorCategory.Text));
        }

        [TestCase("Stone", "Stone")]
        [TestCase("TransparentGlass", "TransparentGlass")]
        [TestCase(null, "Stone")]
        [TestCase("", "Stone")]
        public void TerrainSampleUsesAppearanceOrLegacyDefault(string appearance, string expected)
        {
            var level = Level();
            level.entities = Array.Empty<EntityDefinition>();
            level.terrain[0].appearance = appearance;
            var samples = EditorPicking.Candidates(level, Cell, EditorCategory.Terrain, null);
            Assert.AreEqual(1, samples.Count);
            Assert.AreEqual(expected, samples[0].Value);
            Assert.AreEqual(appearance, level.terrain[0].appearance);
        }

        [Test]
        public void EveryKnownSubjectAndWordSamplesItsInitialValue()
        {
            var level = Level();
            level.terrain = Array.Empty<GridCellBox>();
            foreach (var subject in Tokens.Subjects)
            {
                level.entities = new[] { new EntityDefinition
                    { id = "object", kind = "Object", subject = subject, cell = Cell } };
                var samples = EditorPicking.Candidates(level, Cell, EditorCategory.Object, null);
                Assert.AreEqual(1, samples.Count, subject);
                Assert.AreEqual(subject, samples[0].Value);
                Assert.AreEqual(subject, level.entities[0].subject);
            }

            var words = new HashSet<string>(Tokens.Subjects);
            words.UnionWith(Tokens.Props);
            words.UnionWith(Tokens.Operators);
            foreach (var word in words)
            {
                level.entities = new[] { new EntityDefinition
                    { id = "text", kind = "Text", token = word, cell = Cell } };
                var samples = EditorPicking.Candidates(level, Cell, EditorCategory.Text, null);
                Assert.AreEqual(1, samples.Count, word);
                Assert.AreEqual(word, samples[0].Value);
                Assert.AreEqual(word, level.entities[0].token);
            }
            Assert.AreEqual(15, words.Count);
        }

        [Test]
        public void CandidatesCarryInitialValuesAndDoNotMutateLevel()
        {
            var level = Level();
            var locked = new HashSet<string> { "rock" };
            var samples = EditorPicking.Candidates(level, Cell, EditorCategory.All, locked);
            Assert.AreEqual(2, samples.Count);
            Assert.AreEqual(EditorCategory.Terrain, samples[0].Category);
            Assert.AreEqual("Stone", samples[0].Value);
            Assert.IsNull(samples[0].EntityId);
            Assert.AreEqual(Cell, samples[0].Cell);
            Assert.AreEqual(EditorCategory.Text, samples[1].Category);
            Assert.AreEqual("WIN", samples[1].Value);
            Assert.AreEqual("word", samples[1].EntityId);

            var objectOnly = EditorPicking.Candidates(level, Cell, EditorCategory.Object, null);
            Assert.AreEqual(1, objectOnly.Count);
            Assert.AreEqual("ROCK", objectOnly[0].Value);
            Assert.AreEqual("rock", objectOnly[0].EntityId);
            Assert.AreEqual("Stone", level.terrain[0].appearance);
            Assert.AreEqual("ROCK", level.entities[0].subject);
            Assert.AreEqual("WIN", level.entities[1].token);
            Assert.AreEqual(0, EditorPicking.Candidates(level, Cell, EditorCategory.None, null).Count);
        }

        [Test]
        public void FilteredAndLockedHitsPassThroughToEligibleEntity()
        {
            var level = Level();
            level.terrain = Array.Empty<GridCellBox>();
            level.entities[0].cell = new GridCell(2, 1, 2);
            level.entities[1].cell = new GridCell(1, 2, 1);
            using (var preview = new AuthoringPreview3D())
            {
                preview.RotateSlot(0);
                preview.Focus(level.entities[0].cell);
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(level.entities[0].cell);
                Assert.IsTrue(preview.TryPickFiltered(mouse, EditorCategory.Object, null, out _, out var objectId));
                Assert.AreEqual("rock", objectId);
                Assert.IsTrue(preview.TryPickFiltered(mouse, EditorCategory.All,
                    new HashSet<string> { "word" }, out _, out var unlockedId));
                Assert.AreEqual("rock", unlockedId);
            }
        }

        [Test]
        public void EligibleTerrainOccludesUnlessThroughSelectIsEnabled()
        {
            var level = Level();
            level.terrain[0].min = new GridCell(0, 1, 0);
            level.terrain[0].max = new GridCell(0, 3, 0);
            level.entities = new[] { new EntityDefinition { id = "rock", kind = "Object",
                subject = "ROCK", cell = Cell } };
            using (var preview = new AuthoringPreview3D())
            {
                preview.Focus(Cell);
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(Cell);
                Assert.IsTrue(preview.TryPickFiltered(mouse, EditorCategory.All, null, out _, out var obscuredId));
                Assert.IsNull(obscuredId);
                Assert.IsTrue(preview.TryPickFiltered(mouse, EditorCategory.Object, null, out _, out var filteredId));
                Assert.AreEqual("rock", filteredId);
                preview.ThroughSelect = true;
                Assert.IsTrue(preview.TryPickFiltered(mouse, EditorCategory.All, null, out _, out var throughId));
                Assert.AreEqual("rock", throughId);
            }
        }

        [Test]
        public void FrameFitsWideBoundsInNarrowPreview()
        {
            var level = Level();
            level.bounds.max = new GridCell(12, 3, 12);
            using (var preview = new AuthoringPreview3D())
            {
                var rect = new Rect(0, 0, 180, 500);
                preview.UpdatePicking(rect, level, null, 1, false, false, false);
                preview.Frame(level.bounds);
                preview.UpdatePicking(rect, level, null, 1, false, false, false);
                foreach (var cell in new[]
                {
                    new GridCell(0, 0, 0), new GridCell(0, 3, 12),
                    new GridCell(12, 0, 12), new GridCell(12, 3, 0)
                })
                    Assert.IsTrue(rect.Contains(preview.ProjectCellCenter(cell)), cell.ToString());
            }
        }
    }
}
