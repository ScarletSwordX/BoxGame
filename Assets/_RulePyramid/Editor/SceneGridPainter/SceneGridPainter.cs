using RulePyramid.Core;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    [InitializeOnLoad]
    public static class SceneGridPainter
    {
        static SceneGridPainter()
        {
            SceneView.duringSceneGui += OnScene;
        }

        static void OnScene(SceneView view)
        {
            var window = EditorWindow.HasOpenInstances<LevelEditorWindow>()
                ? EditorWindow.GetWindow<LevelEditorWindow>(false, null, false)
                : null;
            var session = window != null ? window.Session : null;
            if (session?.Draft?.bounds == null) return;
            var b = session.Draft.bounds;
            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            for (int x = b.min.x; x <= b.max.x + 1; x++)
                Handles.DrawLine(new Vector3(x, session.CurrentY, b.min.z), new Vector3(x, session.CurrentY, b.max.z + 1));
            for (int z = b.min.z; z <= b.max.z + 1; z++)
                Handles.DrawLine(new Vector3(b.min.x, session.CurrentY, z), new Vector3(b.max.x + 1, session.CurrentY, z));
            if (session.Draft.entities != null)
            {
                foreach (var e in session.Draft.entities)
                {
                    Handles.color = e.kind == "Text" ? Color.yellow : Color.cyan;
                    Handles.DrawWireCube(new Vector3(e.cell.x + 0.5f, e.cell.y + 0.5f, e.cell.z + 0.5f), Vector3.one);
                }
            }
        }
    }
}
