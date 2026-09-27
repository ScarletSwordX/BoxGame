using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        [SerializeField] string _savedStageId;
        sealed class LinkedStage { public string Path; public string Label; }
        LinkedStage[] _linkedStages;
        static readonly Regex MapName = new Regex(@"^L([0-9]{1,9})P([0-9]{1,9})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        StageDefinition[] Stages => _session?.Draft.stagePlan?.stages ?? Array.Empty<StageDefinition>();
        int ActiveStageIndex => Array.FindIndex(Stages, s => s.id == _session.ActiveStageId);

        string StageLabel(string id)
        {
            int index = Array.FindIndex(Stages, s => s.id == id);
            return index < 0 ? "独立地图" : "P" + (index + 1) + " · " + Stages[index].name;
        }

        void DrawStageToolbar()
        {
            if (Stages.Length == 0)
            {
                if (_linkedStages == null) RefreshLinkedStages();
                if (_linkedStages.Length < 2) return;
                using (new EditorGUI.DisabledScope(Playing || _dragging))
                {
                    int current = Array.FindIndex(_linkedStages, s => SamePath(s.Path, _session.SourcePath));
                    if (current < 0) return;
                    int next = EditorGUILayout.Popup("游玩阶段", current, _linkedStages.Select(s => s.Label).ToArray());
                    if (next != current) SwitchLinkedStage(_linkedStages[next].Path);
                }
                return;
            }
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
            GUILayout.Label("每张地图独立保存尺寸、地形、物件、词牌、教学提示与参考解。", EditorStyles.wordWrappedLabel);
            GUILayout.Label("启用继承后，保存父阶段会自动更新后续阶段的地图尺寸、地形和实体；子阶段的局部修改保留。名称、提示与参考解各阶段独立，玩家残局不继承。", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(Playing || _dragging))
                if (GUILayout.Button("刷新关联阶段")) { RefreshLinkedStages(); _message = "已刷新关联阶段。"; }
            if (Stages.Length == 0)
            {
                string parent = StageMapInheritance.Parent(_session.Draft);
                GUILayout.Label(string.IsNullOrEmpty(parent) ? "当前阶段：无父阶段" : "继承自：" + parent, EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(Playing || _dragging || Dirty))
                    if (GUILayout.Button("建立整组顺序继承（保留各图现状）")) EnableStageInheritance();
                if (Dirty) GUILayout.Label("请先保存当前地图，再建立继承。", EditorStyles.wordWrappedMiniLabel);
            }
            if (Stages.Length == 0)
            {
                if (_linkedStages == null) RefreshLinkedStages();
                GUILayout.Label(_linkedStages.Length > 1 ? "当前地图关联 " + _linkedStages.Length + " 个游玩阶段，可从上方切换。" : "当前地图没有其他关联阶段。", EditorStyles.wordWrappedLabel);
                return;
            }
            EditorGUILayout.HelpBox("当前打开的是旧版多阶段草稿。可在上方切换阶段，并将当前阶段导出为独立地图。导出使用该阶段原有边界，不修改旧草稿。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Playing || _dragging))
                if (GUILayout.Button("导出当前阶段为独立地图")) ExportStageMap();
        }

        void EnableStageInheritance()
        {
            try
            {
                RefreshLinkedStages();
                if (_linkedStages.Length < 2) throw new InvalidOperationException("至少需要两张关联阶段地图");
                var maps = new System.Collections.Generic.Dictionary<string, LevelDefinition>();
                LevelDefinition parent = null;
                string previous = null;
                foreach (var linked in _linkedStages)
                {
                    var map = LevelJsonSerializer.FromDraftJson(File.ReadAllText(linked.Path, Encoding.UTF8));
                    if (previous != null && !SamePath(Path.GetDirectoryName(previous), Path.GetDirectoryName(linked.Path)))
                        throw new InvalidOperationException("建立顺序继承需要阶段地图位于同一目录");
                    if (parent != null && string.IsNullOrEmpty(StageMapInheritance.Parent(map)))
                        map = StageMapInheritance.Link(parent, map, Path.GetFileName(previous));
                    maps[linked.Path] = map;
                    parent = map; previous = linked.Path;
                }
                CheckOpenStageWindows(maps.Keys);
                StageMapInheritance.WriteBatch(maps);
                ReloadSavedStageWindows(maps.Keys);
                AssetDatabase.Refresh();
                _message = "已建立阶段顺序继承；当前各图布局保留，后续保存自动向子阶段传播。";
            }
            catch (Exception ex) { _message = "建立继承失败：" + ex.Message; }
        }

        void CheckOpenStageWindows(System.Collections.Generic.IEnumerable<string> paths)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<LevelEditorWindow>())
                if (window != this && paths.Any(p => SamePath(p, window._session?.SourcePath)) && (window.Dirty || window.Playing))
                    throw new InvalidOperationException("关联阶段窗口有未保存编辑或正在试玩，请先保存或退出：" + window._session.SourcePath);
        }

        void ReloadSavedStageWindows(System.Collections.Generic.IEnumerable<string> paths)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<LevelEditorWindow>())
            {
                if (!paths.Any(p => SamePath(p, window._session?.SourcePath))) continue;
                string path = window._session.SourcePath;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (window == this)
                {
                    // 保留本窗口撤销历史，同时更新持久化的继承元数据。
                    window._session.Draft.authoringSourceJson = json;
                    continue;
                }
                window._session = new LevelEditSession(LevelJsonSerializer.FromDraftJson(json), path);
                window._recoveredDirty = false;
                window.ResetWorkspace();
            }
        }

        static bool SamePath(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        static bool IdMatchesFile(string path)
        {
            try
            {
                var map = LevelJsonSerializer.FromDraftJson(File.ReadAllText(path, Encoding.UTF8));
                return string.Equals(map.id, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // The published sequence is authoritative. Unlisted drafts use exact numeric LxPy names in one directory.
        static LinkedStage[] FindLinkedStages(string sourcePath, string mapId, string contentRoot)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(contentRoot)) return Array.Empty<LinkedStage>();
            string catalogPath = Path.Combine(contentRoot, "Levels", "catalog.json");
            if (File.Exists(catalogPath))
            {
                var catalog = JsonUtility.FromJson<LevelCatalogManifest>(File.ReadAllText(catalogPath, Encoding.UTF8));
                foreach (var sequence in catalog?.stageSequences ?? Array.Empty<LevelStageManifest>())
                {
                    if (sequence?.maps == null) continue;
                    var paths = sequence.maps.Select(m => Path.GetFullPath(Path.Combine(contentRoot, m.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
                    if (!paths.Any(p => SamePath(p, sourcePath))) continue;
                    return paths.Select((p, i) => new { Path = p, Index = i }).Where(x => File.Exists(x.Path))
                        .Select(x => new LinkedStage { Path = x.Path, Label = "P" + (x.Index + 1) + " · " + Path.GetFileNameWithoutExtension(x.Path) }).ToArray();
                }
            }
            var current = MapName.Match(Path.GetFileNameWithoutExtension(sourcePath));
            if (!current.Success || !string.Equals(current.Value, mapId, StringComparison.OrdinalIgnoreCase)) return Array.Empty<LinkedStage>();
            string directory = Path.GetDirectoryName(sourcePath);
            return Directory.GetFiles(directory, "*.json")
                .Select(p => new { Path = p, Match = MapName.Match(Path.GetFileNameWithoutExtension(p)) })
                .Where(x => x.Match.Success && x.Match.Groups[1].Value.TrimStart('0') == current.Groups[1].Value.TrimStart('0') && IdMatchesFile(x.Path))
                .OrderBy(x => long.Parse(x.Match.Groups[2].Value))
                .Select(x => new LinkedStage { Path = x.Path, Label = "P" + x.Match.Groups[2].Value + " · " + Path.GetFileNameWithoutExtension(x.Path) }).ToArray();
        }

        void RefreshLinkedStages()
        {
            try { _linkedStages = FindLinkedStages(_session?.SourcePath, _session?.Draft.id, Path.Combine(Application.dataPath, "_RulePyramid", "Content")); }
            catch (Exception ex) { _linkedStages = Array.Empty<LinkedStage>(); _message = "关联阶段读取失败：" + ex.Message; }
        }

        void SwitchLinkedStage(string path)
        {
            if (Playing || _dragging || SamePath(path, _session.SourcePath)) return;
            if (!CanReplace()) return;
            try
            {
                var level = LevelJsonSerializer.FromDraftJson(File.ReadAllText(path, Encoding.UTF8));
                if (level.bounds == null || level.stagePlan != null) throw new InvalidOperationException("目标不是独立阶段地图");
                CancelDrag();
                _session = new LevelEditSession(level, path);
                _recoveredDirty = false;
                _linkedStages = null;
                ResetWorkspace();
                _session.CurrentY = Mathf.Clamp(level.entities?.FirstOrDefault(e => e.kind == "Object" && e.subject == "ROBOT")?.cell.y ?? level.bounds.min.y, level.bounds.min.y, level.bounds.max.y);
                _message = "已打开阶段地图：" + Path.GetFileName(path);
            }
            catch (Exception ex) { _message = "切换阶段失败：" + ex.Message; }
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
