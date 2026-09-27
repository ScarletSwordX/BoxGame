using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorCoordinatesTests
    {
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static GridCellBox Bounds() => new GridCellBox { min = new GridCell(-5, -2, 3), max = new GridCell(9, 6, 13) };

        [Test]
        public void UpperRightIsZeroAndRoundTripPreservesEveryStoredCell()
        {
            var bounds = Bounds();
            Assert.AreEqual(new GridCell(0, 0, 0), EditorCoordinates.Display(bounds, new GridCell(9, 0, 13)));
            Assert.AreEqual(new GridCell(14, -2, 10), EditorCoordinates.Display(bounds, bounds.min));
            for (int x = -5; x <= 9; x++)
                for (int y = -2; y <= 6; y++)
                    for (int z = 3; z <= 13; z++)
                    {
                        var stored = new GridCell(x, y, z);
                        Assert.AreEqual(stored, EditorCoordinates.Stored(bounds, EditorCoordinates.Display(bounds, stored)));
                    }
        }

        [Test]
        public void PositiveDisplayedOffsetMovesLeftDownAndUpWithoutChangingHeightConvention()
        {
            Assert.AreEqual(new GridCell(-2, 3, -4), EditorCoordinates.Delta(new GridCell(2, 3, 4)));
            Assert.AreEqual(new Vector3(-1, 0, 0), EditorCoordinates.Direction(Vector3.right));
            Assert.AreEqual(new Vector3(0, 0, -1), EditorCoordinates.Direction(Vector3.forward));
        }

        [Test]
        public void GrowingMapKeepsUpperRightAndExistingDisplayPositionsFixed()
        {
            var map = new LevelDefinition { bounds = Bounds() };
            var cell = new GridCell(4, 1, 8);
            var before = EditorCoordinates.Display(map.bounds, cell);
            var size = new GridCell(20, 9, 15);
            var minimum = EditorCoordinates.ResizeMinimum(new GridCell(9, -2, 13), size);
            MapResize.Resize(map, minimum, size);
            Assert.AreEqual(new GridCell(-10, -2, -1), map.bounds.min);
            Assert.AreEqual(new GridCell(9, 6, 13), map.bounds.max);
            Assert.AreEqual(before, EditorCoordinates.Display(map.bounds, cell));
        }

        [Test]
        public void InspectorCoordinateEditWritesStoredCoordinateAndUndoes()
        {
            var window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                var session = window.Session;
                var player = session.Draft.entities.First(e => e.id == "player");
                var initial = player.cell;
                ((HashSet<string>)typeof(LevelEditorWindow).GetField("_selected", Flags).GetValue(window)).Add(player.id);
                var target = new GridCell(1, 1, 2);
                typeof(LevelEditorWindow).GetMethod("MoveSelectionToDisplay", Flags).Invoke(window, new object[] { target });
                Assert.AreEqual(EditorCoordinates.Stored(session.Draft.bounds, target), session.Draft.entities.First(e => e.id == player.id).cell);
                Assert.IsTrue(session.Undo());
                Assert.AreEqual(initial, session.Draft.entities.First(e => e.id == player.id).cell);
            }
            finally { window.DiscardChanges(); Object.DestroyImmediate(window); }
        }

        [Test]
        public void DisplayingInheritedMapsNeverChangesSerializedDataOrCreatesOverrides()
        {
            var window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                var parent = window.Session.Draft.Clone();
                var child = parent.Clone();
                child.bounds.max.x++;
                child = StageMapInheritance.Link(parent, child, "L9P1.json");
                string before = LevelJsonSerializer.ToJson(child);
                foreach (var e in child.entities) EditorCoordinates.Display(child.bounds, e.cell);
                Assert.AreEqual(before, LevelJsonSerializer.ToJson(child));
                parent.entities.First(e => e.id == "player").cell.z++;
                var updated = StageMapInheritance.Resolve(parent, child);
                Assert.AreEqual(parent.entities.First(e => e.id == "player").cell, updated.entities.First(e => e.id == "player").cell);
            }
            finally { window.DiscardChanges(); Object.DestroyImmediate(window); }
        }
    }
}
