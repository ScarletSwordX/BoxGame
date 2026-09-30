using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class AuthoringWorkflowTests
    {
        LevelEditorWindow _window;
        AuthoringPreview3D _preview;

        static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        static LevelDefinition LoadL01()
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/L01.json");
            return LevelJsonSerializer.FromJson(File.ReadAllText(path));
        }

        static object Get(object target, string name)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, "Missing field " + name);
            return field.GetValue(target);
        }

        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, "Missing field " + name);
            field.SetValue(target, value);
        }

        static void Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, PrivateInstance);
            Assert.IsNotNull(method, "Missing method " + name);
            method.Invoke(target, args);
        }

        [SetUp]
        public void SetUp()
        {
            _window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            _preview = new AuthoringPreview3D();
            Set(_window, "_preview", _preview);
            Set(_window, "_session", new LevelEditSession(LoadL01()));
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) UnityEngine.Object.DestroyImmediate(_window);
            _window = null;
            _preview = null;
        }

        [Test]
        public void DiscardedDraftIsNotCapturedWhenWindowDisables()
        {
            var session = (LevelEditSession)Get(_window, "_session");
            session.Edit("改标题", level => level.title = "未保存标题");
            Assert.IsTrue(session.Dirty);
            _window.DiscardChanges();
            Call(_window, "OnDisable");
            Assert.IsNull(Get(_window, "_recoveryJson"));
            Assert.IsFalse((bool)Get(_window, "_recoveryWasDirty"));
        }

        [Test]
        public void StopPlayRestoresSelectionHeightAndPreviewView()
        {
            var session = (LevelEditSession)Get(_window, "_session");
            var selected = (HashSet<string>)Get(_window, "_selected");
            string id = session.Draft.entities[0].id;
            selected.Add(id);
            session.CurrentY = 2;
            _preview.RotateSlot(2);
            var before = _preview.CaptureView();

            Call(_window, "StartPlay", false);
            Assert.IsNotNull(session.Playtest);
            var visual = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualConfig>("Assets/_RulePyramid/Config/VisualConfig.asset");
            Assert.IsNotNull(visual);
            Assert.AreEqual(15f, visual.cameraYaws[0]);
            var playerView = _preview.CaptureView();
            Assert.AreEqual(0, playerView.Slot);
            Assert.AreEqual(15f, playerView.Yaw, .001f);
            Assert.AreEqual(75f, playerView.Pitch, .001f);
            _preview.UpdatePicking(new Rect(0, 0, 500, 400), session.Playtest.Level, session.Playtest.World.Entities, 0, false, false, false);
            session.CurrentY = 0;
            selected.Clear();
            _preview.RotateSlot(1);
            Call(_window, "StopPlay");

            Assert.IsNull(session.Playtest);
            Assert.AreEqual(2, session.CurrentY);
            Assert.IsTrue(selected.Contains(id));
            var after = _preview.CaptureView();
            Assert.AreEqual(before.Slot, after.Slot);
            Assert.AreEqual(before.Yaw, after.Yaw);
            Assert.AreEqual(before.Pitch, after.Pitch);
            Assert.AreEqual(before.Size, after.Size);
            Assert.AreEqual(before.HasFrame, after.HasFrame);
            Assert.AreEqual(before.Target, after.Target);
        }

        [TestCase(KeyCode.W, 0, 1)]
        [TestCase(KeyCode.UpArrow, 0, 1)]
        [TestCase(KeyCode.S, 0, -1)]
        [TestCase(KeyCode.DownArrow, 0, -1)]
        [TestCase(KeyCode.A, -1, 0)]
        [TestCase(KeyCode.LeftArrow, -1, 0)]
        [TestCase(KeyCode.D, 1, 0)]
        [TestCase(KeyCode.RightArrow, 1, 0)]
        public void PlaytestKeyboardMovesAlongCameraAlignedGridAxes(KeyCode key, int dx, int dz)
        {
            var level = LoadL01();
            level.bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(6, 4, 6) };
            level.terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(6, 0, 6) } };
            level.entities = new[]
            {
                new EntityDefinition { id = "actor", kind = "Object", subject = "ROBOT", cell = new GridCell(3, 1, 3) },
                new EntityDefinition { id = "noun", kind = "Text", token = "ROBOT", cell = new GridCell(0, 1, 0) },
                new EntityDefinition { id = "is", kind = "Text", token = "IS", cell = new GridCell(1, 1, 0) },
                new EntityDefinition { id = "you", kind = "Text", token = "YOU", cell = new GridCell(2, 1, 0) }
            };
            level.designContract = null;
            level.referenceSolutions = Array.Empty<ReferenceSolutionData>();
            level.tutorial = null;
            var session = new LevelEditSession(level);
            Set(_window, "_session", session);
            bool editing = UnityEditor.EditorGUIUtility.editingTextField;
            try
            {
                Call(_window, "StartPlay", false);
                Assert.IsNotNull(session.Playtest, (string)Get(_window, "_message"));
                UnityEditor.EditorGUIUtility.editingTextField = false;
                Call(_window, "HandleKeyboardEvent", new Event { type = EventType.KeyDown, keyCode = key });
                Assert.AreEqual(new GridCell(3 + dx, 1, 3 + dz), session.Playtest.World.FindYou().Cell);
                Assert.AreEqual(1, session.Playtest.TurnCount);
                var view = _preview.CaptureView();
                var rotation = Quaternion.Euler(view.Pitch, view.Yaw, 0f);
                var axis = dz != 0 ? rotation * Vector3.forward * dz : rotation * Vector3.right * dx;
                float angle = Vector3.Angle(new Vector3(dx, 0, dz), Vector3.ProjectOnPlane(axis, Vector3.up));
                Assert.AreEqual(15f, angle, .001f);
            }
            finally
            {
                UnityEditor.EditorGUIUtility.editingTextField = editing;
                Call(_window, "StopPlay");
            }
        }

        [Test]
        public void ReRecordingUsesTargetChosenAtStartEvenIfSelectionChanges()
        {
            var level = LoadL01();
            var original = level.referenceSolutions[0];
            var second = new ReferenceSolutionData { id = "B", name = "保留的另一解", family = "other", commands = new[] { "WAIT" } };
            level.referenceSolutions = new[] { original, second };
            var session = new LevelEditSession(level);
            Set(_window, "_session", session);
            Set(_window, "_solutionIndex", 0);
            Set(_window, "_recordName", "重录的 A");
            Call(_window, "StartPlay", true);
            Assert.IsNotNull(session.Playtest);
            Assert.AreEqual(0, (int)Get(_window, "_recordTargetIndex"));

            var recorder = (PlaytestRecorder)Get(_window, "_recorder");
            foreach (var command in original.commands)
                Assert.IsTrue(recorder.TryExecute(command), recorder.Session.LastRejectReason);
            Assert.IsTrue(recorder.Won);
            Set(_window, "_solutionIndex", 1);
            Call(_window, "FinishRecording");

            Assert.AreEqual("重录的 A", session.Draft.referenceSolutions[0].name);
            Assert.AreEqual("B", session.Draft.referenceSolutions[1].id);
            Assert.AreEqual("保留的另一解", session.Draft.referenceSolutions[1].name);
            Assert.AreEqual("WAIT", session.Draft.referenceSolutions[1].commands[0]);
        }
    }
}
