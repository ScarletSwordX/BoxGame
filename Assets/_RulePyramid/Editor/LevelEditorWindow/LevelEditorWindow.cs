using System;
using System.IO;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RulePyramid.Editor
{
    public enum EditorBrush
    {
        Select,
        Terrain,
        Color,
        Text,
        Erase
    }

    public class LevelEditorWindow : EditorWindow
    {
        LevelEditSession _session;
        EditorBrush _brush = EditorBrush.Terrain;
        Label _idLabel;
        Label _selection;
        Label _diag;
        IntegerField _yField;
        TextField _tokenField;
        TextField _colorField;
        GridCanvas _canvas;

        [MenuItem("Tools/RulePyramid/Level Editor")]
        public static void Open()
        {
            GetWindow<LevelEditorWindow>("Rule Pyramid Editor");
        }

        void CreateGUI()
        {
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_RulePyramid/UIToolkit/LevelEditor.uxml");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_RulePyramid/UIToolkit/LevelEditor.uss");
            if (uxml != null) uxml.CloneTree(rootVisualElement);
            if (uss != null) rootVisualElement.styleSheets.Add(uss);

            _idLabel = rootVisualElement.Q<Label>("idLabel");
            _selection = rootVisualElement.Q<Label>("selectionLabel");
            _diag = rootVisualElement.Q<Label>("diagLabel");
            _yField = rootVisualElement.Q<IntegerField>("yField");
            _tokenField = rootVisualElement.Q<TextField>("tokenField");
            _colorField = rootVisualElement.Q<TextField>("colorField");
            var brush = rootVisualElement.Q<EnumField>("brushField");
            if (brush != null)
            {
                brush.Init(_brush);
                brush.RegisterValueChangedCallback(evt => _brush = (EditorBrush)evt.newValue);
            }
            _yField?.RegisterValueChangedCallback(evt =>
            {
                if (_session != null) _session.CurrentY = evt.newValue;
                _canvas?.MarkDirtyRepaint();
            });

            rootVisualElement.Q<Button>("newButton")?.RegisterCallback<ClickEvent>(_ => NewLevel());
            rootVisualElement.Q<Button>("openButton")?.RegisterCallback<ClickEvent>(_ => OpenLevel());
            rootVisualElement.Q<Button>("saveButton")?.RegisterCallback<ClickEvent>(_ => Save(false));
            rootVisualElement.Q<Button>("saveAsButton")?.RegisterCallback<ClickEvent>(_ => Save(true));
            rootVisualElement.Q<Button>("undoButton")?.RegisterCallback<ClickEvent>(_ => { _session?.Undo(); Refresh(); });
            rootVisualElement.Q<Button>("redoButton")?.RegisterCallback<ClickEvent>(_ => { _session?.Redo(); Refresh(); });
            rootVisualElement.Q<Button>("validateButton")?.RegisterCallback<ClickEvent>(_ => Validate());
            rootVisualElement.Q<Button>("playtestButton")?.RegisterCallback<ClickEvent>(_ => Playtest());
            rootVisualElement.Q<Button>("stopButton")?.RegisterCallback<ClickEvent>(_ => { _session?.StopPlaytest(); Refresh(); });

            var host = rootVisualElement.Q("gridHost");
            _canvas = new GridCanvas(() => _session) { style = { flexGrow = 1 } };
            _canvas.Clicked += OnCellClicked;
            host?.Add(_canvas);
            NewLevel();
        }

        void NewLevel()
        {
            var level = CreateEmpty();
            _session = new LevelEditSession(level);
            Refresh();
        }

        void OpenLevel()
        {
            var path = EditorUtility.OpenFilePanel("Open level", Application.dataPath + "/_RulePyramid/Content/Levels", "json");
            if (string.IsNullOrEmpty(path)) return;
            var json = File.ReadAllText(path);
            var level = LevelJsonSerializer.FromJson(json);
            _session = new LevelEditSession(level, path);
            Refresh();
        }

        void Save(bool saveAs)
        {
            if (_session == null) return;
            var path = _session.SourcePath;
            if (saveAs || string.IsNullOrEmpty(path))
                path = EditorUtility.SaveFilePanel("Save level", Application.dataPath + "/_RulePyramid/Content/Levels", _session.Draft.id + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            var temp = path + ".tmp";
            File.WriteAllText(temp, LevelJsonSerializer.ToJson(_session.Draft));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            _session.SourcePath = path;
            _session.MarkSaved();
            AssetDatabase.Refresh();
            Refresh();
        }

        void Validate()
        {
            if (_session == null) return;
            var report = _session.Validate();
            _diag.text = report.Issues.Count == 0 ? "校验通过" : report.ToString();
        }

        void Playtest()
        {
            if (_session == null) return;
            if (!_session.TryStartPlaytest(out _, out var error))
            {
                _diag.text = error;
                return;
            }
            _diag.text = "试玩中：在 Game View 使用 WASD/Space。Stop 返回草稿。";
            PreviewHost.Start(_session);
        }

        void OnCellClicked(GridCell cell)
        {
            if (_session == null || _session.Playtest != null) return;
            switch (_brush)
            {
                case EditorBrush.Terrain:
                    _session.Apply(new PlaceTerrainCommand { Cell = cell });
                    break;
                case EditorBrush.Erase:
                    _session.Apply(new EraseAtCommand { Cell = cell });
                    break;
                case EditorBrush.Color:
                    _session.Apply(new PlaceEntityCommand
                    {
                        Entity = new EntityDefinition
                        {
                            id = _session.NextEntityId("c"),
                            kind = "Color",
                            color = string.IsNullOrEmpty(_colorField?.value) ? "RED" : _colorField.value,
                            token = "",
                            cell = cell
                        }
                    });
                    break;
                case EditorBrush.Text:
                    _session.Apply(new PlaceEntityCommand
                    {
                        Entity = new EntityDefinition
                        {
                            id = _session.NextEntityId("t"),
                            kind = "Text",
                            color = "",
                            token = string.IsNullOrEmpty(_tokenField?.value) ? "WIN" : _tokenField.value,
                            cell = cell
                        }
                    });
                    break;
                default:
                    _selection.text = DescribeCell(cell);
                    break;
            }
            Refresh();
        }

        string DescribeCell(GridCell cell)
        {
            if (_session?.Draft.entities == null) return cell.ToString();
            var lines = cell.ToString();
            foreach (var e in _session.Draft.entities)
                if (e.cell.Equals(cell)) lines += "\n" + e.id + " " + e.kind + " " + e.color + e.token;
            return lines;
        }

        void Refresh()
        {
            if (_idLabel != null && _session != null)
                _idLabel.text = (_session.Draft.id ?? "?") + (_session.Dirty ? " *" : "");
            _canvas?.MarkDirtyRepaint();
            SceneView.RepaintAll();
        }

        static LevelDefinition CreateEmpty()
        {
            return new LevelDefinition
            {
                schemaVersion = 5,
                mechanicsVersion = Tokens.MechanicsVersion,
                id = "draft",
                title = "草稿",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 8, 7) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 0, 7) } },
                entities = Array.Empty<EntityDefinition>(),
                fixedRules = new[] { new FixedRuleData { id = "r1", tokens = new[] { "RED", "IS", "YOU" } } },
                options = new OptionsData
                {
                    supportMode = "StrictBelow",
                    jumpMode = "LandingBounce3",
                    bounceRiseCells = 3,
                    decisionMode = "GroundedOrBounceApex",
                    ruleAxes = new[] { "PositiveX", "PositiveZ" },
                    winMode = "DistinctEntitiesSameCell",
                    winCheckMode = "AfterAtomicLogicChange",
                    actionMode = "FourWayMoveInPlaceJumpApexSteer",
                    gravityMode = "WorldDownExceptHoverOrFly",
                    playerBlockMode = "ImplicitFromYou"
                },
                camera = new CameraData { initialSlot = 0, pitchDegrees = 35.264f, yawDegrees = new[] { 45f, 135f, 225f, 315f }, inputMode = "CameraRelativeGrid" },
                tutorial = new TutorialData { objective = "", hints = Array.Empty<string>() },
                referenceSolution = new ReferenceSolutionData { commands = Array.Empty<string>(), expectedFinalStatus = "Running" }
            };
        }

        public LevelEditSession Session => _session;
    }

    sealed class GridCanvas : VisualElement
    {
        readonly Func<LevelEditSession> _session;
        public event Action<GridCell> Clicked;

        public GridCanvas(Func<LevelEditSession> session)
        {
            _session = session;
            generateVisualContent += Paint;
            RegisterCallback<MouseDownEvent>(OnMouse);
        }

        void OnMouse(MouseDownEvent evt)
        {
            var session = _session();
            if (session?.Draft.bounds == null) return;
            var b = session.Draft.bounds;
            float w = resolvedStyle.width;
            int size = b.max.x - b.min.x + 1;
            int zSize = b.max.z - b.min.z + 1;
            float cell = Mathf.Min(w / Mathf.Max(1, size), resolvedStyle.height / Mathf.Max(1, zSize));
            int x = b.min.x + Mathf.FloorToInt(evt.localMousePosition.x / cell);
            int z = b.max.z - Mathf.FloorToInt(evt.localMousePosition.y / cell);
            Clicked?.Invoke(new GridCell(x, session.CurrentY, z));
        }

        void Paint(MeshGenerationContext ctx)
        {
            var session = _session();
            if (session?.Draft.bounds == null) return;
            var b = session.Draft.bounds;
            int sizeX = b.max.x - b.min.x + 1;
            int sizeZ = b.max.z - b.min.z + 1;
            float cell = Mathf.Min(resolvedStyle.width / Mathf.Max(1, sizeX), resolvedStyle.height / Mathf.Max(1, sizeZ));
            var terrain = LevelCloner.ExpandTerrain(session.Draft.terrain);
            var painter = ctx.painter2D;
            for (int x = b.min.x; x <= b.max.x; x++)
            for (int z = b.min.z; z <= b.max.z; z++)
            {
                var rect = new Rect((x - b.min.x) * cell, (b.max.z - z) * cell, cell - 1, cell - 1);
                var c = new GridCell(x, session.CurrentY, z);
                Color fill = new Color(0.15f, 0.15f, 0.18f);
                if (terrain.Contains(c)) fill = new Color(0.35f, 0.35f, 0.38f);
                if (session.Draft.entities != null)
                {
                    foreach (var e in session.Draft.entities)
                    {
                        if (!e.cell.Equals(c)) continue;
                        if (e.kind == "Text") fill = new Color(0.9f, 0.85f, 0.5f);
                        else if (e.color == "RED") fill = new Color(0.85f, 0.3f, 0.25f);
                        else if (e.color == "BLUE") fill = new Color(0.3f, 0.45f, 0.9f);
                        else if (e.color == "PINK") fill = new Color(0.9f, 0.45f, 0.75f);
                    }
                }
                painter.fillColor = fill;
                painter.BeginPath();
                painter.MoveTo(rect.min);
                painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                painter.LineTo(rect.max);
                painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
