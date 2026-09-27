using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RulePyramid.Editor
{
    public enum EditorBrush { Select, Terrain, Object, Text, Erase, Box, Region, Sentence, Eyedropper }

    public partial class LevelEditorWindow : EditorWindow
    {
        LevelEditSession _session;
        AuthoringPreview3D _preview;
        [SerializeField] EditorBrush _brush;
        [SerializeField] EditorCategory _categories = EditorCategory.All;
        [SerializeField] bool _autoFit = true;
        Rect _gridViewport;
        [SerializeField] GridCellBox _focusBounds;
        [SerializeField] bool _focusPending;
        readonly HashSet<string> _selected = new HashSet<string>();
        readonly HashSet<string> _locked = new HashSet<string>();
        readonly HashSet<GridCell> _stroke = new HashSet<GridCell>();
        readonly List<string> _sentence = new List<string> { "ROBOT", "IS", "YOU" };
        [SerializeField] string _word = "WIN", _subject = "ROCK", _appearance = "Stone", _message = "先选择工具，再在二维或三维画布操作。";
        Vector2 _leftScroll, _rightScroll, _bottomScroll;
        [SerializeField] Vector2 _pan;
        Vector3Int _resizeOrigin, _resizeSize;
        bool _resizeEditing;
        [SerializeField] float _zoom = 34, _split = .5f;
        [SerializeField] bool _adjacent = true, _clip, _surface, _subtract;
        bool _dragging, _drag3D, _append, _inputFocus;
        GridCell _start, _hover, _moveOffset;
        GridCellBox _ghost;
        [SerializeField] int _topY = 1, _tab, _layout, _regionIndex = -1, _solutionIndex = -1, _sentenceAxis;
        [SerializeField] int _savedY;
        [SerializeField] string[] _savedSelection, _savedLocks;
        [SerializeField] GridCell[] _savedTerrain;
        [SerializeField] bool _savedThroughSelect;
        [SerializeField] AuthoringPreview3D.ViewState _savedView;
        WorldModel _inspection;
        string _inspectionError;
        PlaytestRecorder _recorder;
        RegionTutorialSession _tutorial;
        AuthoringPreview3D.ViewState _editView;
        string[] _editSelection;
        int _editY;
        bool _recording, _autoReplay, _discarded;
        int _recordTargetIndex = -1;
        ReferenceSolutionData _replay;
        int _replayStep;
        double _lastTick, _nextReplay;
        string _recordName = "新参考解", _recordFamily = "";
        readonly Dictionary<string, string> _replayResults = new Dictionary<string, string>();
        [SerializeField] string _recoveryJson, _recoveryPath;
        [SerializeField] bool _recoveryWasDirty;
        bool _recoveredDirty;
        ValidationReport _validation;
        public LevelEditSession Session => _session;
        bool Playing => _session?.Playtest != null;
        bool Dirty => _session != null && (_session.Dirty || _recoveredDirty);
        WorldModel Inspect => Playing ? _session.Playtest.World : _inspection;
        static readonly string[] Words = { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA", "IS", "AND", "YOU", "PUSH", "STOP", "HOVER", "FLY", "BOUNCY", "WIN", "HOT", "MELT" };
        static readonly string[] Subjects = { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA" };

        [MenuItem("Tools/规则工坊/关卡编辑器")]
        public static void Open() => GetWindow<LevelEditorWindow>("规则工坊编辑器");

        void OnEnable()
        {
            if (!Enum.IsDefined(typeof(EditorBrush), _brush)) _brush = EditorBrush.Select;
            minSize = new Vector2(1000, 680);
            wantsMouseMove = true;
            if (_session == null)
            {
                _recoveredDirty = _recoveryWasDirty || string.IsNullOrEmpty(_recoveryJson);
                try { _session = new LevelEditSession(string.IsNullOrEmpty(_recoveryJson) ? CreateEmpty() : LevelJsonSerializer.FromDraftJson(_recoveryJson), _recoveryPath); }
                catch (Exception ex) { _session = new LevelEditSession(CreateEmpty()); _message = "恢复草稿失败：" + ex.Message; }
            }
            if (!string.IsNullOrEmpty(_savedStageId) && Stages.Any(s => s.id == _savedStageId)) _session.SwitchStage(_savedStageId);
            _lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            RefreshInspection();
            _session.CurrentY=Mathf.Clamp(_savedY,_session.Draft.bounds.min.y,_session.Draft.bounds.max.y);
            if(_savedSelection!=null) _selected.UnionWith(_savedSelection);
            if(_savedLocks!=null) _locked.UnionWith(_savedLocks);
            if(_savedTerrain!=null) _selectedTerrain.UnionWith(_savedTerrain);
            PruneSelection();
        }

        void OnLostFocus() { _inputFocus=false; if(_dragging) CancelDrag(); }

        void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.Add(new IMGUIContainer(DrawWorkspace) { style = { flexGrow = 1 } });
        }

        void OnDisable()
        {
            if(_dragging) CancelDrag();
            _savedY=Playing?_editY:(_session?.CurrentY??0);
            _savedSelection=Playing?_editSelection:_selected.ToArray();
            _savedLocks=_locked.ToArray(); _savedTerrain=_selectedTerrain.ToArray();
            if(_preview!=null) { _savedView=Playing?_editView:_preview.CaptureView(); _savedThroughSelect=_preview.ThroughSelect; }
            if (!_discarded) CaptureRecovery();
            EditorApplication.update -= Tick;
            _preview?.Dispose(); _preview = null;
        }

        void CaptureRecovery()
        {
            if (_session == null) return;
            _savedStageId = _session.ActiveStageId;
            _recoveryJson = LevelJsonSerializer.ToJson(_session.Draft);
            _recoveryPath = _session.SourcePath;
            _recoveryWasDirty = Dirty;
        }

        public override void SaveChanges() { if (Save(false)) base.SaveChanges(); }
        public override void DiscardChanges() { _discarded = true; _recoveredDirty = false; _recoveryWasDirty = false; _recoveryJson = null; base.DiscardChanges(); }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (Playing)
            {
                _tutorial?.Tick(now - _lastTick);
                if (_autoReplay && now >= _nextReplay) { StepReplay(); _nextReplay = now + .45; }
                Repaint();
            }
            _lastTick = now;
        }

        void DrawWorkspace()
        {
            if (_session == null) return;
            if (_preview==null)
            {
                _preview=new AuthoringPreview3D { ThroughSelect=_savedThroughSelect };
                if(_savedView.HasFrame) _preview.RestoreView(_savedView);
            }
            hasUnsavedChanges = Dirty;
            saveChangesMessage = "关卡草稿尚未保存。是否保存后关闭？";
            if (Event.current.type == EventType.MouseDown) _inputFocus = false;
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(165)))
                {
                    _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
                    DrawPalette(); EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawViewToolbar();
                    var area = GUILayoutUtility.GetRect(100, 10000, 240, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    var r2 = area; var r3 = area;
                    if (_layout == 0) { r2.width = (area.width - 6) * _split; r3.xMin = r2.xMax + 6; }
                    if (_layout != 2) DrawGrid(r2);
                    if (_layout != 1) DrawThree(r3);
                    HandleKeyboard();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(270)))
                {
                    _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                    DrawInspector(); EditorGUILayout.EndScrollView();
                }
            }
            _tab = GUILayout.Toolbar(_tab, new[] { "问题与规则", "教学提示", "参考解与回放", "地图大小", "关卡设置", "阶段地图" });
            _bottomScroll = EditorGUILayout.BeginScrollView(_bottomScroll, GUILayout.Height(205));
            if (_tab == 0) DrawDiagnostics();
            if (_tab == 1) DrawTutorial();
            if (_tab == 2) DrawSolutions();
            if (_tab == 3) DrawMapSize();
            if (_tab == 4) DrawSettings();
            if (_tab == 5) DrawStages();
            EditorGUILayout.EndScrollView();
            if (Playing)
            {
                var globalHint = GlobalStatusHint.Current(_session.Playtest);
                if (globalHint != null) EditorGUILayout.HelpBox(globalHint, MessageType.Warning);
                else if (_tutorial?.Current != null) EditorGUILayout.HelpBox(_tutorial.Current.text, MessageType.Info);
            }
            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(30));
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(Playing))
                {
                    if (GUILayout.Button("新建", EditorStyles.toolbarButton)) NewLevel();
                    if (GUILayout.Button("打开", EditorStyles.toolbarButton)) OpenLevel();
                    if (GUILayout.Button("保存 [Ctrl+S]", EditorStyles.toolbarButton)) Save(false);
                    if (GUILayout.Button("另存", EditorStyles.toolbarButton)) Save(true);
                    if (GUILayout.Button("撤销 [Ctrl+Z]", EditorStyles.toolbarButton)) { _session.Undo(); Changed(); }
                    if (GUILayout.Button("重做 [Ctrl+Shift+Z]", EditorStyles.toolbarButton)) { _session.Redo(); Changed(); }
                }
                GUILayout.Label(_session.Draft.id + " · " + _session.Draft.title + (Dirty ? "  ● 未保存" : "  已保存"));
                if (GUILayout.Button("地图大小", EditorStyles.toolbarButton)) { _tab = 3; _bottomScroll = Vector2.zero; }
                GUILayout.FlexibleSpace();
                if (!Playing && GUILayout.Button("开始试玩", EditorStyles.toolbarButton)) StartPlay(false);
                if (Playing)
                {
                    GUILayout.Label(_recording ? "● 正在录制" : _replay != null ? "参考解回放" : "试玩中");
                    if (GUILayout.Button("撤销一步", EditorStyles.toolbarButton)) UndoPlay();
                    if (GUILayout.Button("重开", EditorStyles.toolbarButton)) RestartPlay();
                    if (GUILayout.Button("退出试玩", EditorStyles.toolbarButton)) StopPlay();
                }
                if (GUILayout.Button("校验", EditorStyles.toolbarButton)) { _message = _session.Validate().ToString(); _tab = 0; }
            }
        }

        void DrawPalette()
        {
            GUILayout.Label("编辑工具", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Playing))
            {
                string[] names = { "选择 / 框选 [V]", "地形笔刷 [B]", "物体笔刷 [Q]", "词牌 [T]", "擦除 [E]", "地形长方体 [G]", "教学区域", "放置句子", "吸管 [R]" };
                for (int i = 0; i < names.Length; i++)
                    if (GUILayout.Toggle((int)_brush == i, names[i], "Button") && (int)_brush != i) SetBrush((EditorBrush)i);
                GUILayout.Space(8);
                if (_brush == EditorBrush.Terrain || _brush == EditorBrush.Box)
                {
                    _subtract = GUILayout.Toggle(_subtract, "挖除地形");
                    _appearance = EditorGUILayout.Popup("外观", _appearance == "Stone" ? 0 : 1, new[] { "石质", "透明隔墙" }) == 0 ? "Stone" : "TransparentGlass";
                }
                if (_brush == EditorBrush.Object) _subject = Subjects[EditorGUILayout.Popup(Array.IndexOf(Subjects, _subject), Subjects)];
                if (_brush == EditorBrush.Object) GUILayout.Label("左键拖动连续绘制；每格一个物体。占用格会使整次笔画取消。", EditorStyles.wordWrappedMiniLabel);
                if (_brush == EditorBrush.Object && _subject == "WALL")
                {
                    GUILayout.Label("用 WALL IS STOP 使墙壁阻挡且不可推；改为 WALL IS PUSH 后可推。",EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("准备 WALL IS STOP 句子"))
                    {
                        _sentence.Clear(); _sentence.AddRange(new[] { "WALL", "IS", "STOP" });
                        _brush = EditorBrush.Sentence;
                    }
                }
                if (_brush == EditorBrush.Text) _word = Words[EditorGUILayout.Popup(Array.IndexOf(Words, _word), Words)];
                if (_brush == EditorBrush.Box || _brush == EditorBrush.Region) _topY = EditorGUILayout.IntField("最高 Y", _topY);
                if (_brush == EditorBrush.Sentence)
                {
                    _sentenceAxis = GUILayout.Toolbar(_sentenceAxis, new[] { "向右 +X", "向下 -Z" });
                    for (int i = 0; i < _sentence.Count; i++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            _sentence[i] = Words[EditorGUILayout.Popup(Math.Max(0, Array.IndexOf(Words, _sentence[i])), Words)];
                            if (GUILayout.Button("−", GUILayout.Width(24))) { _sentence.RemoveAt(i); break; }
                        }
                    }
                    if (GUILayout.Button("添加词牌")) _sentence.Add("IS");
                }
            }
            GUILayout.Space(12);
            GUILayout.Label("左键编辑 · 中键平移\r\n滚轮缩放 · Esc 取消\r\nCtrl+Z 撤销 · Shift+E 删除\r\nF 聚焦 · Shift+F 全图\r\n三维 Alt+拖动旋转\r\nShift+拖动复制\r\nCtrl+拖空白框选", EditorStyles.wordWrappedMiniLabel);
        }

        void DrawViewToolbar()
        {
            DrawStageToolbar();
            DrawCategoryToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                _session.CurrentY = Mathf.Clamp(EditorGUILayout.IntField("当前 Y", _session.CurrentY, GUILayout.Width(175)), _session.Draft.bounds.min.y, _session.Draft.bounds.max.y);
                _layout = GUILayout.Toolbar(_layout, new[] { "双视图", "二维", "三维" });
                if (GUILayout.Button("全图 [Shift+F]", GUILayout.Width(92))) ExecuteEditorCommand(EditorCommand.FrameAll);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _adjacent = GUILayout.Toggle(_adjacent, "相邻层"); _clip = GUILayout.Toggle(_clip, "剖切上层");
                _surface = GUILayout.Toggle(_surface, "贴表面放置");
                _preview.ThroughSelect = GUILayout.Toggle(_preview.ThroughSelect, "穿透选择");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Playing))
                    for (int i = 0; i < 4; i++) if (GUILayout.Button("视角" + (i + 1), GUILayout.Width(48))) _preview.RotateSlot(i);
                GUILayout.Label("双视图比例", GUILayout.Width(70));
                _split = GUILayout.HorizontalSlider(_split, .25f, .75f, GUILayout.Width(70));
            }
        }

        void Edit(string label, Action<LevelDefinition> mutation)
        {
            if (Playing) return;
            try { _session.Edit(label, mutation); Changed(); _message = label; }
            catch (Exception ex) { _message = "未执行：" + ex.Message; }
        }

        void Changed()
        {
            _savedStageId = _session.ActiveStageId;
            _selected.RemoveWhere(id => !_session.Draft.entities.Any(e => e.id == id));
            _replayResults.Clear();
            RefreshInspection();
            _selectedTerrain.RemoveWhere(cell => Inspect?.Terrain?.Contains(cell) != true);
            CaptureRecovery(); Repaint();
        }

        void RefreshInspection()
        {
            _inspection = null; _inspectionError = null;
            _validation = _session.Validate();
            try
            {
                if (_validation.Issues.Any(i => i.Code == "BOUNDS" || i.Code == "TERRAIN_BOUNDS"))
                    throw new InvalidOperationException("地图或地形边界无效，请先修正范围。");
                // 作者草稿可以暂时没有 YOU；只解析规则，不执行环境结算。
                var world = new WorldModel { Spec = LevelCloner.Clone(_session.Draft), Terrain = LevelCloner.ExpandTerrain(_session.Draft.terrain) };
                foreach (var e in _session.Draft.entities)
                    world.Entities.Add(new EntityState { Id = e.id, Kind = EntityState.ParseKind(e.kind), Subject = e.subject, Token = e.token, Cell = e.cell, Anchored = e.anchored });
                world.Refresh(); _inspection = world;
            }
            catch (Exception ex) { _inspectionError = ex.Message; }
        }

        bool CanReplace()
        {
            if (!Dirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("未保存的关卡", "切换前是否保存当前草稿？", "保存", "取消", "不保存");
            return choice == 2 || choice == 0 && Save(false);
        }
        void NewLevel()
        {
            if (!CanReplace()) return;
            _session = new LevelEditSession(CreateEmpty()); _recoveredDirty = true; ResetWorkspace();
        }
        void OpenLevel()
        {
            if (!CanReplace()) return;
            var path = EditorUtility.OpenFilePanel("打开关卡草稿", Application.dataPath + "/_RulePyramid/Content/Levels", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { var level = LevelJsonSerializer.FromDraftJson(File.ReadAllText(path, Encoding.UTF8)); if (level.bounds == null) throw new InvalidOperationException("缺少关卡边界"); _session = new LevelEditSession(level, path); _recoveredDirty = false; ResetWorkspace(); }
            catch (Exception ex) { _message = "打开失败：" + ex.Message; }
        }
        bool Save(bool saveAs)
        {
            var occupancy=LevelValidator.ValidateStageSave(_session.Draft);
            if(occupancy.HasStructureErrors)
            { _message="不能保存，请先修正地图数据或占格冲突："+occupancy; _validation=occupancy; _tab=0; return false; }
            string path = _session.SourcePath;
            if (saveAs || string.IsNullOrEmpty(path)) path = EditorUtility.SaveFilePanel("保存关卡草稿", Application.dataPath + "/_RulePyramid/Content/Levels", _session.Draft.id + ".json", "json");
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                WriteDraftFile(path, _session.Draft);
                _session.SourcePath = path; _session.MarkSaved(); _recoveredDirty = false;
                _linkedStages = null;
                CaptureRecovery(); hasUnsavedChanges = false; _message = "已保存作者初态：" + path;
                AssetDatabase.Refresh(); return true;
            }
            catch (Exception ex) { _message = "保存失败，原文件保留：" + ex.Message; return false; }
        }
        void ResetWorkspace()
        {
            _linkedStages = null;
            _savedStageId = _session.ActiveStageId;
            _resizeEditing = false;
            CancelDrag();
            _selected.Clear(); _selectedTerrain.Clear(); _locked.Clear(); _regionIndex = _solutionIndex = -1; _ghost = null; _dragging = false;
            Changed(); FrameAll();
        }
        void FrameAll()
        {
            _autoFit = true; _focusPending=false; FitGrid(_session.Draft.bounds); _preview?.Frame(_session.Draft.bounds);
        }
        static bool Contains(GridCellBox b, GridCell c) => b != null && c.x >= b.min.x && c.x <= b.max.x && c.y >= b.min.y && c.y <= b.max.y && c.z >= b.min.z && c.z <= b.max.z;
        static GridCellBox Box(GridCell a, GridCell b) => new GridCellBox { min = new GridCell(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z)), max = new GridCell(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z)) };
        static Vector3Int Vec(GridCell c) => new Vector3Int(c.x, c.y, c.z);
        static GridCell Cell(Vector3Int v) => new GridCell(v.x, v.y, v.z);
    }
}
