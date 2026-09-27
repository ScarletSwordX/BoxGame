using System;
using System.IO;
using System.Linq;
using System.Text;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        [SerializeField] string _savedStageId;
        StageDefinition[] Stages => _session?.Draft.stagePlan?.stages ?? Array.Empty<StageDefinition>();
        int ActiveStageIndex => Array.FindIndex(Stages, s => s.id == _session.ActiveStageId);

        string StageLabel(string id)
        {
            int index = Array.FindIndex(Stages, s => s.id == id);
            return index < 0 ? "独立地图" : "P" + (index + 1) + " · " + Stages[index].name;
        }

        void DrawStageToolbar()
        {
            if (Stages.Length == 0) return;
            using (new EditorGUI.DisabledScope(Playing || _dragging))
            {
                int current = Math.Max(0, ActiveStageIndex);
                int next = EditorGUILayout.Popup("旧草稿阶段", current, Stages.Select(s => StageLabel(s.id)).ToArray());
                if (next != current) SwitchEditorStage(Stages[next].id);
            }
        }

        void DrawStages()
        {
            GUILayout.Label("每个游玩阶段使用一张独立地图，例如 L1P1.json、L1P2.json。", EditorStyles.boldLabel);
            GUILayout.Label("使用新建或另存建立地图，在关卡设置填写对应 ID 和标题。每张地图独立保存尺寸、地形、物件、词牌、教学提示与参考解。", EditorStyles.wordWrappedLabel);
            GUILayout.Label("下一阶段保留哪些结构由设计决定，可另存当前地图后扩建和重新布置；地图之间不自动共享编辑，也不继承玩家残局。", EditorStyles.wordWrappedLabel);
            if (Stages.Length == 0) return;
            EditorGUILayout.HelpBox("当前打开的是旧版多阶段草稿。可在上方切换阶段，并将当前阶段导出为独立地图。导出使用该阶段原有边界，不修改旧草稿。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Playing || _dragging))
                if (GUILayout.Button("导出当前阶段为独立地图")) ExportStageMap();
        }

        void SwitchEditorStage(string id)
        {
            if (Playing) return;
            CancelDrag();
            try
            {
                _session.SwitchStage(id);
                _savedStageId = _session.ActiveStageId;
                _selected.Clear(); _selectedTerrain.Clear(); _locked.Clear();
                _regionIndex = _solutionIndex = -1;
                Changed();
                _message = "正在查看旧草稿 " + StageLabel(id) + "；可导出为独立地图。";
            }
            catch (Exception ex) { _message = "无法切换阶段：" + ex.Message; }
        }

        void ExportStageMap()
        {
            if (Playing || _dragging || Stages.Length == 0) return;
            string suggested = _session.Draft.id + "P" + (ActiveStageIndex + 1);
            var path = EditorUtility.SaveFilePanel("导出独立阶段地图", Application.dataPath + "/_RulePyramid/Content/LevelDrafts", suggested + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            if (!string.IsNullOrEmpty(_session.SourcePath) && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_session.SourcePath), StringComparison.OrdinalIgnoreCase))
            { _message = "请选择新的地图文件，不能覆盖当前多阶段草稿。"; return; }
            try
            {
                var level = _session.BuildStandaloneMap(Path.GetFileNameWithoutExtension(path));
                var report = LevelValidator.ValidateStageSave(level);
                if (report.HasStructureErrors) throw new InvalidOperationException(report.ToString());
                WriteDraftFile(path, level);
                AssetDatabase.Refresh();
                _message = "已导出独立地图：" + path + "；使用打开继续编辑该地图。";
            }
            catch (Exception ex) { _message = "导出失败：" + ex.Message; }
        }

        static void WriteDraftFile(string path, LevelDefinition level)
        {
            var content = LevelJsonSerializer.ToJson(level).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
