using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorEfficiencyWorkflowTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        LevelEditorWindow window;
        LevelEditSession session;

        static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
        HashSet<string> Selected => (HashSet<string>)Get(window, "_selected");
        HashSet<string> Locked => (HashSet<string>)Get(window, "_locked");
        HashSet<GridCell> SelectedTerrain => (HashSet<GridCell>)Get(window, "_selectedTerrain");

        [SetUp]
        public void SetUp()
        {
            window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            session = new LevelEditSession(new LevelDefinition
            {
                id = "efficiency", schemaVersion = 9, mechanicsVersion = "RW-v0.9",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 7, 7) },
                terrain = new[]
                {
                    new GridCellBox { id = "stone", min = new GridCell(1, 0, 1), max = new GridCell(2, 0, 1), appearance = "Stone" }
                },
                entities = new[]
                {
                    new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 1), anchored = true, color = "legacy" },
                    new EntityDefinition { id = "word", kind = "Text", token = "PUSH", cell = new GridCell(2, 1, 1) },
                    new EntityDefinition { id = "locked", kind = "Object", subject = "WALL", cell = new GridCell(3, 1, 1) },
                    new EntityDefinition { id = "high", kind = "Object", subject = "CLOUD", cell = new GridCell(4, 3, 4) }
                }
            });
            Set(window, "_session", session);
            Call(window, "RefreshInspection");
        }

        [TearDown]
        public void TearDown()
        {
            window.DiscardChanges();
            UnityEngine.Object.DestroyImmediate(window);
        }

        [Test]
        public void CategoryChangePrunesSelectionWithoutEditingDraft()
        {
            Selected.UnionWith(new[] { "rock", "word", "locked" });
            SelectedTerrain.Add(new GridCell(1, 0, 1));
            Locked.Add("locked");

            Call(window, "SetCategories", EditorCategory.Text);

            CollectionAssert.AreEquivalent(new[] { "word" }, Selected);
            Assert.IsEmpty(SelectedTerrain);
            Assert.AreEqual(EditorCategory.Text, Get(window, "_categories"));
            StringAssert.Contains("_categories", EditorJsonUtility.ToJson(window));
            Assert.IsFalse(session.Dirty);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void CategoryAndSavedWorkspaceStateSurviveEditorSerialization()
        {
            Call(window, "SetCategories", EditorCategory.Object);
            Set(window, "_brush", EditorBrush.Erase);
            Set(window, "_savedY", 3);
            Set(window, "_savedSelection", new[] { "rock" });
            Set(window, "_savedLocks", new[] { "locked" });
            Set(window, "_savedTerrain", new[] { new GridCell(1, 0, 1) });
            Set(window, "_savedView", new AuthoringPreview3D.ViewState { Yaw = 123f, Pitch = 42f, Slot = 2, HasFrame = true });
            var json = EditorJsonUtility.ToJson(window);
            var restored = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, restored);
                Assert.AreEqual(EditorCategory.Object, Get(restored, "_categories"));
                Assert.AreEqual(EditorBrush.Erase, Get(restored, "_brush"));
                Assert.AreEqual(3, Get(restored, "_savedY"));
                CollectionAssert.AreEqual(new[] { "rock" }, (string[])Get(restored, "_savedSelection"));
                CollectionAssert.AreEqual(new[] { "locked" }, (string[])Get(restored, "_savedLocks"));
                CollectionAssert.AreEqual(new[] { new GridCell(1, 0, 1) }, (GridCell[])Get(restored, "_savedTerrain"));
                var view = (AuthoringPreview3D.ViewState)Get(restored, "_savedView");
                Assert.AreEqual(123f, view.Yaw);
                Assert.AreEqual(42f, view.Pitch);
                Assert.AreEqual(2, view.Slot);
            }
            finally
            {
                restored.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void EmptyCategoryMaskPreventsEraseWithoutCreatingUndo()
        {
            Call(window, "SetCategories", EditorCategory.None);
            Call(window, "EraseCells", new[] { new GridCell(1, 1, 1), new GridCell(1, 0, 1) });
            Assert.AreEqual(4, session.Draft.entities.Length);
            Assert.AreEqual(2, LevelCloner.ExpandTerrain(session.Draft.terrain).Count);
            Assert.IsFalse(session.Dirty);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void EraseStrokeRespectsCategoryAndLockAndUsesOneUndo()
        {
            Locked.Add("locked");
            Call(window, "SetCategories", EditorCategory.Object | EditorCategory.Terrain);
            Call(window, "EraseCells", new[] { new GridCell(1, 1, 1), new GridCell(2, 1, 1), new GridCell(3, 1, 1), new GridCell(1, 0, 1), new GridCell(2, 0, 1) });

            CollectionAssert.AreEquivalent(new[] { "word", "locked", "high" }, session.Draft.entities.Select(e => e.id));
            Assert.IsEmpty(LevelCloner.ExpandTerrain(session.Draft.terrain));
            Assert.IsTrue(session.Dirty);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(4, session.Draft.entities.Length);
            Assert.AreEqual(2, LevelCloner.ExpandTerrain(session.Draft.terrain).Count);
            Assert.IsFalse(session.CanUndo);
            Assert.IsFalse(session.Dirty);
        }

        [Test]
        public void BlankSampleKeepsBrushAndParameters()
        {
            Set(window, "_brush", EditorBrush.Eyedropper);
            Set(window, "_subject", "WALL");
            Set(window, "_word", "WIN");
            Call(window, "SampleCell", new GridCell(7, 7, 7));

            Assert.AreEqual(EditorBrush.Eyedropper, Get(window, "_brush"));
            Assert.AreEqual("WALL", Get(window, "_subject"));
            Assert.AreEqual("WIN", Get(window, "_word"));
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void SingleSamplesSwitchBrushAndPlacementCreatesFreshEntity()
        {
            Call(window, "SetCategories", EditorCategory.Object);
            Call(window, "SampleCell", new GridCell(1, 1, 1));
            Assert.AreEqual(EditorBrush.Object, Get(window, "_brush"));
            Assert.AreEqual("ROCK", Get(window, "_subject"));
            Assert.IsFalse(session.Dirty);

            Set(window, "_hover", new GridCell(5, 1, 5));
            Call(window, "CommitGesture", (object)null);
            var placed = session.Draft.entities.Single(e => e.cell == new GridCell(5, 1, 5));
            Assert.AreEqual("ROCK", placed.subject);
            Assert.AreNotEqual("rock", placed.id);
            Assert.IsFalse(placed.anchored);
            Assert.IsTrue(string.IsNullOrEmpty(placed.color));
            Assert.IsTrue(session.Undo());
            Assert.IsFalse(session.CanUndo);

            Call(window, "SetCategories", EditorCategory.Text);
            Call(window, "SampleCell", new GridCell(2, 1, 1));
            Assert.AreEqual(EditorBrush.Text, Get(window, "_brush"));
            Assert.AreEqual("PUSH", Get(window, "_word"));
            Call(window, "SetCategories", EditorCategory.Terrain);
            Call(window, "SampleCell", new GridCell(1, 0, 1));
            Assert.AreEqual(EditorBrush.Terrain, Get(window, "_brush"));
            Assert.AreEqual("Stone", Get(window, "_appearance"));
            Assert.IsFalse((bool)Get(window, "_subtract"));
        }

        [Test]
        public void DeleteSelectionPreservesLockedAndFilteredContent()
        {
            Selected.UnionWith(new[] { "rock", "word", "locked" });
            SelectedTerrain.Add(new GridCell(1, 0, 1));
            Locked.Add("locked");
            Call(window, "SetCategories", EditorCategory.Object | EditorCategory.Terrain);
            Call(window, "DeleteSelection");

            CollectionAssert.AreEquivalent(new[] { "word", "locked", "high" }, session.Draft.entities.Select(e => e.id));
            var terrain = LevelCloner.ExpandTerrain(session.Draft.terrain);
            Assert.IsFalse(terrain.Contains(new GridCell(1, 0, 1)));
            Assert.IsTrue(terrain.Contains(new GridCell(2, 0, 1)));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(4, session.Draft.entities.Length);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void FocusSelectionKeepsVisibleYOtherwiseUsesLowestAndPreservesOrientation()
        {
            var preview = new AuthoringPreview3D();
            try
            {
                preview.RestoreView(new AuthoringPreview3D.ViewState { Yaw = 123f, Pitch = 42f, Size = 20f, Slot = 2, HasFrame = true });
                Set(window, "_preview", preview);
                Set(window, "_gridViewport", new Rect(0, 0, 600, 400));
                Selected.UnionWith(new[] { "rock", "high" });
                session.CurrentY = 3;
                Call(window, "FocusSelection");
                Assert.AreEqual(3, session.CurrentY);
                Assert.AreEqual(new Vector3(3f, 2.5f, 3f), preview.CaptureView().Target);
                Assert.AreEqual(123f, preview.CaptureView().Yaw);
                Assert.AreEqual(42f, preview.CaptureView().Pitch);
                Assert.AreEqual(2, preview.CaptureView().Slot);
                session.CurrentY = 7;
                Call(window, "FocusSelection");
                Assert.AreEqual(1, session.CurrentY);
                Assert.IsFalse((bool)Get(window, "_autoFit"));
            }
            finally
            {
                Set(window, "_preview", null);
                preview.Dispose();
            }
        }

        [Test]
        public void CommandsAreGuardedWhileDraggingOrPlaying()
        {
            Set(window, "_brush", EditorBrush.Select);
            Set(window, "_dragging", true);
            Call(window, "ExecuteEditorCommand", EditorCommand.Erase);
            Assert.AreEqual(EditorBrush.Select, Get(window, "_brush"));
            Call(window, "ExecuteEditorCommand", EditorCommand.Cancel);
            Assert.IsFalse((bool)Get(window, "_dragging"));

            var empty = (LevelDefinition)typeof(LevelEditorWindow)
                .GetMethod("CreateEmpty", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Set(session, "_playtest", new GameSession(empty));
            Call(window, "ExecuteEditorCommand", EditorCommand.Erase);
            Assert.AreEqual(EditorBrush.Select, Get(window, "_brush"));
            Assert.IsFalse(session.CanUndo);
        }
    }
}
