using NUnit.Framework;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorCommandsTests
    {
        [TestCase(KeyCode.V, EditorCommand.Select)]
        [TestCase(KeyCode.B, EditorCommand.Terrain)]
        [TestCase(KeyCode.G, EditorCommand.Box)]
        [TestCase(KeyCode.Q, EditorCommand.Object)]
        [TestCase(KeyCode.T, EditorCommand.Text)]
        [TestCase(KeyCode.E, EditorCommand.Erase)]
        [TestCase(KeyCode.R, EditorCommand.Eyedropper)]
        [TestCase(KeyCode.Delete, EditorCommand.Delete)]
        [TestCase(KeyCode.Backspace, EditorCommand.Delete)]
        [TestCase(KeyCode.F, EditorCommand.Focus)]
        [TestCase(KeyCode.Escape, EditorCommand.Cancel)]
        public void ResolvesCanvasShortcut(KeyCode key, EditorCommand expected)
        {
            Assert.AreEqual(expected, EditorCommands.Resolve(key, true, false, false, false, false));
        }

        [TestCase(KeyCode.E, EditorCommand.Delete)]
        [TestCase(KeyCode.F, EditorCommand.FrameAll)]
        public void ShiftCombinesRelatedLeftHandActions(KeyCode key, EditorCommand expected)
        {
            Assert.AreEqual(expected, EditorCommands.Resolve(key, true, false, false, false, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, true, true, false, false, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, false, false, false, false, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, true, false, false, true, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, true, false, true, false, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, true, false, false, false, true, true));
        }

        [TestCase(KeyCode.O)]
        [TestCase(KeyCode.I)]
        [TestCase(KeyCode.Home)]
        [TestCase(KeyCode.Y)]
        [TestCase(KeyCode.X)]
        public void RetiredKeysDoNotTriggerEditingCommands(KeyCode key)
        {
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(key, true, false, false, false, false));
        }

        [Test]
        public void ShiftDoesNotAccidentallySelectAnotherTool()
        {
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Q, true, false, false, false, false, true));
            Assert.AreEqual(EditorCommand.Erase, EditorCommands.Resolve(KeyCode.E, true, false, false, false, false));
            Assert.AreEqual(EditorCommand.Focus, EditorCommands.Resolve(KeyCode.F, true, false, false, false, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.R, true, false, false, true, false));
        }

        [Test]
        public void IgnoresShortcutsWithoutCanvasFocusOrWhileTyping()
        {
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.E, false, false, false, false, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.B, true, true, false, false, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Escape, true, true, false, false, false));
        }

        [Test]
        public void IgnoresShortcutsWithModifiersOrDuringPlay()
        {
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.B, true, false, false, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.E, true, false, false, true, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Escape, true, false, false, true, false));
        }

        [Test]
        public void DraggingAllowsOnlyEscape()
        {
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.E, true, false, true, false, false));
            Assert.AreEqual(EditorCommand.Cancel, EditorCommands.Resolve(KeyCode.Escape, true, false, true, false, true));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Escape, false, false, true, false, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Escape, true, true, true, false, false));
            Assert.AreEqual(EditorCommand.None, EditorCommands.Resolve(KeyCode.Escape, true, false, true, true, false));
        }
    }
}
