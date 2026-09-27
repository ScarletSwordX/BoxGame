using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using RulePyramid.Runtime;

namespace RulePyramid.Tests.EditorPreview
{
    public class StageMapInheritanceTests
    {
        static LevelDefinition Map() => (LevelDefinition)typeof(LevelEditorWindow).GetMethod("CreateEmpty", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        static EntityDefinition Player(LevelDefinition map) => map.entities.First(e => e.id == "player");

        [Test]
        public void ParentEditsFlowThroughThreeStagesAndPreserveChildFields()
        {
            var first = Map();
            var second = first.Clone();
            Player(second).cell.x++;
            second = StageMapInheritance.Link(first, second, "L9P1.json");
            var third = StageMapInheritance.Link(second, second.Clone(), "L9P2.json");
            Player(first).cell.z++;
            var nextSecond = StageMapInheritance.Resolve(first, second);
            var nextThird = StageMapInheritance.Resolve(nextSecond, third);
            Assert.AreEqual(Player(second).cell.x, Player(nextThird).cell.x);
            Assert.AreEqual(Player(first).cell.z, Player(nextThird).cell.z);
            Player(nextSecond).cell.y++;
            nextThird = StageMapInheritance.Resolve(nextSecond, nextThird);
            Assert.AreEqual(Player(nextSecond).cell.y, Player(nextThird).cell.y);
        }

        [Test]
        public void OverrideSurvivesParentTemporarilyMatchingItAndJsonRoundTrip()
        {
            var parent = Map();
            var child = parent.Clone();
            Player(child).cell.x++;
            child = StageMapInheritance.Link(parent, child, "L9P1.json");
            Player(parent).cell.x++;
            child = StageMapInheritance.Resolve(parent, child);
            child = LevelJsonSerializer.FromDraftJson(LevelJsonSerializer.ToJson(child));
            Player(parent).cell.x++;
            child = StageMapInheritance.Resolve(parent, child);
            Assert.AreEqual(Player(parent).cell.x - 1, Player(child).cell.x);
        }

        [Test]
        public void ChildDeletionAndAdditionSurviveAndParentDeletionKeepsModifiedChild()
        {
            var parent = Map();
            var child = parent.Clone();
            string removed = child.entities.First(e => e.id != "player").id;
            child.entities = child.entities.Where(e => e.id != removed).ToArray();
            Player(child).cell.x++;
            child.entities = child.entities.Concat(new[] { new EntityDefinition { id = "local", kind = "Object", subject = "ROCK", cell = new GridCell(2, 1, 2) } }).ToArray();
            child = StageMapInheritance.Link(parent, child, "L9P1.json");
            parent.entities = parent.entities.Where(e => e.id != "player").ToArray();
            child = StageMapInheritance.Resolve(parent, child);
            Assert.IsFalse(child.entities.Any(e => e.id == removed));
            Assert.IsTrue(child.entities.Any(e => e.id == "local"));
            Assert.AreEqual("ROBOT", Player(child).subject);
        }

        [Test]
        public void TerrainInheritanceUsesCellsInsteadOfBoxIds()
        {
            var parent = Map();
            parent.terrain = new[] { new GridCellBox { id = "floor", min = new GridCell(0, 0, 0), max = new GridCell(3, 0, 0), appearance = "Stone" } };
            var child = parent.Clone();
            AuthoringOperations.RemoveTerrain(child, new GridCell(1, 0, 0), new GridCell(1, 0, 0));
            child = StageMapInheritance.Link(parent, child, "L9P1.json");
            parent.terrain[0].id = "renamed";
            parent.terrain[0].max.x = 4;
            child = StageMapInheritance.Resolve(parent, child);
            var cells = LevelCloner.ExpandTerrain(child.terrain);
            Assert.IsFalse(cells.Contains(new GridCell(1, 0, 0)));
            Assert.IsTrue(cells.Contains(new GridCell(4, 0, 0)));
        }

        [Test]
        public void SaveCascadeUndoAndChildSaveKeepIndependentOverrides()
        {
            string root = Path.Combine(Path.GetTempPath(), "BoxGame-inheritance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(root, "L9P" + i + ".json")).ToArray();
            try
            {
                var first = Map();
                var second = first.Clone();
                Player(second).cell.x++;
                second = StageMapInheritance.Link(first, second, "L9P1.json");
                var third = StageMapInheritance.Link(second, second.Clone(), "L9P2.json");
                StageMapInheritance.WriteBatch(new System.Collections.Generic.Dictionary<string, LevelDefinition> { [paths[0]] = first, [paths[1]] = second, [paths[2]] = third });
                var session = new LevelEditSession(LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[0])), paths[0]);
                session.Edit("移动", d => Player(d).cell.z++);
                StageMapInheritance.WriteBatch(StageMapInheritance.BuildSave(paths[0], session.Draft, paths));
                Assert.AreEqual(4, Player(LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[2]))).cell.z);
                Assert.IsTrue(session.Undo());
                StageMapInheritance.WriteBatch(StageMapInheritance.BuildSave(paths[0], session.Draft, paths));
                var restored = LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[2]));
                Assert.AreEqual(new GridCell(4, 1, 3), Player(restored).cell);
                second = LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[1]));
                Player(second).cell.z = 5;
                var batch = StageMapInheritance.BuildSave(paths[1], second, paths);
                Assert.IsFalse(batch.ContainsKey(paths[0]));
                StageMapInheritance.WriteBatch(batch);
                Assert.AreEqual(5, Player(LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[2]))).cell.z);
                foreach (string path in paths)
                {
                    var bytes = File.ReadAllBytes(path);
                    Assert.IsFalse(bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191);
                    for (int i = 0; i < bytes.Length; i++) if (bytes[i] == 10) Assert.AreEqual(13, bytes[i - 1]);
                }
            }
            finally
            {
                foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
                Directory.Delete(root);
            }
        }

        [Test]
        public void ConflictingDescendantPreventsWritingAnyMap()
        {
            string root = Path.Combine(Path.GetTempPath(), "BoxGame-inheritance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var paths = new[] { Path.Combine(root, "L9P1.json"), Path.Combine(root, "L9P2.json") };
            try
            {
                var parent = Map();
                var child = parent.Clone();
                child.entities = child.entities.Concat(new[] { new EntityDefinition { id = "child_only", kind = "Object", subject = "ROCK", cell = new GridCell(5, 1, 5) } }).ToArray();
                child = StageMapInheritance.Link(parent, child, "L9P1.json");
                File.WriteAllText(paths[0], LevelJsonSerializer.ToJson(parent));
                File.WriteAllText(paths[1], LevelJsonSerializer.ToJson(child));
                string before = File.ReadAllText(paths[0]);
                parent.entities = parent.entities.Concat(new[] { new EntityDefinition { id = "collision", kind = "Object", subject = "ROCK", cell = new GridCell(5, 1, 5) } }).ToArray();
                var error = Assert.Throws<InvalidOperationException>(() => StageMapInheritance.BuildSave(paths[0], parent, paths));
                StringAssert.Contains("L9P2.json", error.Message);
                Assert.AreEqual(before, File.ReadAllText(paths[0]));
            }
            finally
            {
                foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
                Directory.Delete(root);
            }
        }
    }
}
