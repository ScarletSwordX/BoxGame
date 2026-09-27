using System.IO;
using System.Text;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        [MenuItem("Tools/规则工坊/L02 地图原型/P1 使用弹性")]
        public static void OpenL02P1() { OpenL02Prototype(1); }

        [MenuItem("Tools/规则工坊/L02 地图原型/P2 转移弹性")]
        public static void OpenL02P2() { OpenL02Prototype(2); }

        [MenuItem("Tools/规则工坊/L02 地图原型/P3 转移控制")]
        public static void OpenL02P3() { OpenL02Prototype(3); }

        static void OpenL02Prototype(int stage)
        {
            var id = "L2P" + stage;
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/" + id + ".json");
            var draft = LevelJsonSerializer.FromDraftJson(File.ReadAllText(path, Encoding.UTF8));
            // 每次打开独立窗口，保留其它窗口尚未保存的工作。
            var window = CreateInstance<LevelEditorWindow>();
            window.titleContent = new GUIContent(id + " 地图原型");
            window._session = new LevelEditSession(draft, path);
            window._recoveredDirty = false;
            window.ResetWorkspace();
            window._session.CurrentY = stage == 3 ? 1 : 3;
            window.Show();
        }
    }
}
