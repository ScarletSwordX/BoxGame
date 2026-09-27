using UnityEngine;

namespace RulePyramid.Editor
{
    public enum EditorCommand
    {
        None,
        Select,
        Terrain,
        Box,
        Object,
        Text,
        Erase,
        Eyedropper,
        Delete,
        Focus,
        FrameAll,
        Cancel
    }

    public static class EditorCommands
    {
        public static EditorCommand Resolve(KeyCode key, bool canvasFocus, bool textFocus, bool dragging, bool playing, bool modifiers, bool shift = false)
        {
            if (!canvasFocus || textFocus || playing) return EditorCommand.None;
            if (dragging) return key == KeyCode.Escape ? EditorCommand.Cancel : EditorCommand.None;
            if (modifiers) return EditorCommand.None;
            if (shift)
            {
                if (key == KeyCode.E) return EditorCommand.Delete;
                if (key == KeyCode.F) return EditorCommand.FrameAll;
                return EditorCommand.None;
            }

            switch (key)
            {
                case KeyCode.V: return EditorCommand.Select;
                case KeyCode.B: return EditorCommand.Terrain;
                case KeyCode.G: return EditorCommand.Box;
                case KeyCode.Q: return EditorCommand.Object;
                case KeyCode.T: return EditorCommand.Text;
                case KeyCode.E: return EditorCommand.Erase;
                case KeyCode.R: return EditorCommand.Eyedropper;
                case KeyCode.Delete:
                case KeyCode.Backspace: return EditorCommand.Delete;
                case KeyCode.F: return EditorCommand.Focus;
                case KeyCode.Escape: return EditorCommand.Cancel;
                default: return EditorCommand.None;
            }
        }
    }
}
