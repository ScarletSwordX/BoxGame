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

        static LevelDefinition Shifted(LevelDefinition map, int x, int z)
        {
            var shifted = map.Clone();
            var delta = new GridCell(x, 0, z);
            shifted.bounds.min = shifted.bounds.min.Add(delta);
            shifted.bounds.max = shifted.bounds.max.Add(delta);
            foreach (var box in shifted.terrain) { box.min = box.min.Add(delta); box.max = box.max.Add(delta); }
            foreach (var entity in shifted.entities) entity.cell = entity.cell.Add(delta);
            return shifted;
        }

        [Test]
        public void UpperRightInheritanceAlignsDifferentOriginsAndKeepsChildOverride()
        {
            var parent = Map();
            var child = Shifted(parent, 10, 20);
            child.bounds.min.x -= 2;
            Player(child).cell.z++;
            child = StageMapInheritance.Link(parent, child, "L9P1.json");
            Assert.AreEqual(2, StageMapInheritance.Version(child));
            var third = StageMapInheritance.Link(child, Shifted(child, -30, -40), "L9P2.json");
            var before = child.Clone();
            child = StageMapInheritance.Resolve(parent, child);
            Assert.AreEqual(before.bounds.min, child.bounds.min);
            Assert.AreEqual(before.bounds.max, child.bounds.max);
            Assert.AreEqual(Player(before).cell, Player(child).cell);
            Player(parent).cell.x--;
            Player(parent).cell.z--;
            AuthoringOperations.RemoveTerrain(parent, new GridCell(6, 0, 6), new GridCell(6, 0, 6));
            child = StageMapInheritance.Resolve(parent, child);
            third = StageMapInheritance.Resolve(child, third);
            Assert.AreEqual(new GridCell(12, 1, 24), Player(child).cell);
            Assert.AreEqual(EditorCoordinates.Display(child.bounds, Player(child).cell), EditorCoordinates.Display(third.bounds, Player(third).cell));
            Assert.IsFalse(LevelCloner.ExpandTerrain(child.terrain).Contains(new GridCell(16, 0, 26)));
            Assert.IsFalse(LevelCloner.ExpandTerrain(third.terrain).Contains(new GridCell(-14, 0, -14)));
        }

        [Test]
        public void InheritedSizeExpansionPreservesChildUpperRightAndStoredContent()
        {
            var parent = Map();
            var child = StageMapInheritance.Link(parent, Shifted(parent, 10, 20), "L9P1.json");
            var cell = Player(child).cell;
            var corner = child.bounds.max;
            parent.bounds.min.x -= 2;
            parent.bounds.min.z -= 3;
            child = StageMapInheritance.Resolve(parent, child);
            Assert.AreEqual(corner, child.bounds.max);
            Assert.AreEqual(new GridCell(8, 0, 17), child.bounds.min);
            Assert.AreEqual(cell, Player(child).cell);
        }

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
        public void BlockedMoveKeepsOtherFieldsAndCascadeUsesAcceptedLayout()
        {
            var parent = Map();
            var child = parent.Clone();
            child.entities = child.entities.Concat(new[] { new EntityDefinition { id = "local", kind = "Object", subject = "ROCK", cell = new GridCell(4, 1, 3) } }).ToArray();
            child = StageMapInheritance.Link(parent, child, "L9P1.json");
            var third = StageMapInheritance.Link(child, child.Clone(), "L9P2.json");
            Player(parent).cell.x++;
            Player(parent).color = "Red";
            child = StageMapInheritance.Resolve(parent, child);
            Assert.AreEqual(new GridCell(3, 1, 3), Player(child).cell);
            Assert.AreEqual("Red", Player(child).color);
            third = StageMapInheritance.Resolve(child, third);
            Assert.AreEqual(Player(child).cell, Player(third).cell);
            Assert.AreEqual("Red", Player(third).color);
            Assert.IsFalse(LevelValidator.ValidateStageSave(child).HasStructureErrors);
        }

        [Test]
        public void OutOfBoundsAdditionAndClippingSizeAreSkipped()
        {
            var parent = Map();
            parent.entities = parent.entities.Concat(new[] { new EntityDefinition { id = "outside", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 5) } }).ToArray();
            var child = Map();
            AuthoringOperations.RemoveTerrain(child, new GridCell(0, 0, 0), new GridCell(1, 0, 7));
            child.bounds.min.x = 2;
            child.entities = child.entities.Where(e => e.cell.x >= 2).ToArray();
            var basis = Map();
            child = StageMapInheritance.Link(basis, child, "L9P1.json");
            var result = StageMapInheritance.Resolve(parent, child);
            Assert.IsFalse(result.entities.Any(e => e.id == "outside"));
            var full = Map();
            var linked = StageMapInheritance.Link(full, full.Clone(), "L9P1.json");
            AuthoringOperations.RemoveTerrain(full, new GridCell(0, 0, 0), new GridCell(1, 0, 7));
            full.bounds.min.x = 2;
            full.entities = full.entities.Where(e => e.cell.x >= 2).ToArray();
            linked.entities = linked.entities.Concat(new[] { new EntityDefinition { id = "edge", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 5) } }).ToArray();
            var shrunk = StageMapInheritance.Resolve(full, linked);
            Assert.AreEqual(0, shrunk.bounds.min.x);
            Assert.IsTrue(shrunk.entities.Any(e => e.id == "edge"));
        }

        [Test]
        public void NewTerrainSkipsOccupiedCellButKeepsOtherCells()
        {
            var parent = Map();
            var child = StageMapInheritance.Link(parent, parent.Clone(), "L9P1.json");
            Player(child).cell = new GridCell(4, 1, 2);
            parent.terrain = parent.terrain.Concat(new[] { new GridCellBox { id = "blocked", appearance = "Stone", min = new GridCell(4, 1, 2), max = new GridCell(5, 1, 2) } }).ToArray();
            var result = StageMapInheritance.Resolve(parent, child);
            var cells = LevelCloner.ExpandTerrain(result.terrain);
            Assert.IsFalse(cells.Contains(new GridCell(4, 1, 2)));
            Assert.IsTrue(cells.Contains(new GridCell(5, 1, 2)));
            Assert.IsFalse(LevelValidator.ValidateStageSave(result).HasStructureErrors);
        }

        [Test]
        public void BusyOrUnreadableDescendantDoesNotBlockParentSave()
        {
            string root = Path.Combine(Path.GetTempPath(), "BoxGame-inheritance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(root, "L9P" + i + ".json")).ToArray();
            try
            {
                var parent = Map();
                var child = StageMapInheritance.Link(parent, parent.Clone(), "L9P1.json");
                var third = StageMapInheritance.Link(child, child.Clone(), "L9P2.json");
                StageMapInheritance.WriteBatch(new System.Collections.Generic.Dictionary<string, LevelDefinition> { [paths[0]] = parent, [paths[1]] = child, [paths[2]] = third });
                string before = File.ReadAllText(paths[1]);
                Player(parent).cell.z++;
                var warnings = new System.Collections.Generic.List<string>();
                var batch = StageMapInheritance.BuildSave(paths[0], parent, paths, warnings, new[] { paths[1] });
                CollectionAssert.AreEquivalent(new[] { paths[0] }, batch.Keys);
                StageMapInheritance.WriteBatch(batch);
                Assert.AreEqual(before, File.ReadAllText(paths[1]));
                Assert.IsTrue(warnings.Any(w => w.Contains("窗口")));
                File.WriteAllText(paths[1], "invalid json");
                batch = StageMapInheritance.BuildSave(paths[0], parent, paths, warnings);
                CollectionAssert.AreEquivalent(new[] { paths[0] }, batch.Keys);
                Assert.IsTrue(warnings.Any(w => w.Contains("无法读取")));
                File.WriteAllText(paths[1], before.Replace("L9P1.json", "missing.json"));
                batch = StageMapInheritance.BuildSave(paths[1], LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[1])), paths, warnings);
                Assert.IsTrue(batch.ContainsKey(paths[1]));
                Assert.IsTrue(warnings.Any(w => w.Contains("父阶段")));
            }
            finally
            {
                foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
                Directory.Delete(root);
            }
        }

        [Test]
        public void NoOpResolvePreservesChildEntityOrder()
        {
            var parent = Map();
            var child = parent.Clone();
            child.entities = child.entities.Reverse().ToArray();
            var linked = StageMapInheritance.Link(parent, child, "L9P1.json");
            var resolved = StageMapInheritance.Resolve(parent, linked);
            CollectionAssert.AreEqual(child.entities.Select(e => e.id), resolved.entities.Select(e => e.id));
            CollectionAssert.AreEqual(child.entities.Select(e => e.cell), resolved.entities.Select(e => e.cell));
        }

        [Test]
        public void ConflictingDescendantSkipsOnlyBlockedEntityAndSavesOthers()
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
                Player(parent).cell.z++;
                var warnings = new System.Collections.Generic.List<string>();
                var batch = StageMapInheritance.BuildSave(paths[0], parent, paths, warnings);
                StageMapInheritance.WriteBatch(batch);
                var saved = LevelJsonSerializer.FromDraftJson(File.ReadAllText(paths[1]));
                Assert.AreEqual(4, Player(saved).cell.z);
                Assert.IsTrue(saved.entities.Any(e => e.id == "child_only"));
                Assert.IsFalse(saved.entities.Any(e => e.id == "collision"));
                Assert.IsTrue(warnings.Any(w => w.Contains("collision")));
                Assert.AreNotEqual(before, File.ReadAllText(paths[0]));
                // 跳过不会被记录成永久覆盖；移走子阶段障碍后可以继承。
                saved.entities.First(e => e.id == "child_only").cell.x++;
                var retried = StageMapInheritance.Resolve(parent, saved);
                Assert.IsTrue(retried.entities.Any(e => e.id == "collision"));
            }
            finally
            {
                foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
                Directory.Delete(root);
            }
        }
    }
}
