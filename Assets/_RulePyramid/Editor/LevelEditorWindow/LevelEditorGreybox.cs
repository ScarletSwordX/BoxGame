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
        // 保留旧入口兼容，但打开实际独立的 P1 地图。
        [MenuItem("Tools/规则工坊/L01 三阶段灰盒")]
        public static void OpenL01Greybox() { OpenL01Stage(1); }

        [MenuItem("Tools/规则工坊/L01 独立地图/P1（7×3）")]
        public static void OpenL01P1() { OpenL01Stage(1); }

        [MenuItem("Tools/规则工坊/L01 独立地图/P2（7×7）")]
        public static void OpenL01P2() { OpenL01Stage(2); }

        [MenuItem("Tools/规则工坊/L01 独立地图/P3（12×10）")]
        public static void OpenL01P3() { OpenL01Stage(3); }

        static void OpenL01Stage(int stage)
        {
            var id = "L1P" + stage;
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/" + id + ".json");
            var draft = LevelJsonSerializer.FromDraftJson(File.ReadAllText(path, Encoding.UTF8));
            // 使用独立窗口，保留当前编辑器中可能尚未保存的工作。
            var window = CreateInstance<LevelEditorWindow>();
            window.titleContent = new GUIContent(id + " 独立地图");
            window._session = new LevelEditSession(draft, path);
            window._recoveredDirty = false;
            window.ResetWorkspace();
            window.Show();
        }
    }
}
