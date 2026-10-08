using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    // 仅用于可重复的 Game View 视觉证据，不保存场景或改变正式地图。
    public static class GamePresentationReview
    {
        const string Output = "docs/ArtReview/VA-v1.2";
        static GameBootstrap _game;
        static Queue<Action> _steps;
        static double _next;
        static int _originalSize;
        static UnityEditor.EditorWindow _view;
        static bool _originalBackground;

        public static void CaptureAll()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("请先进入 Play Mode。");
            _game = UnityEngine.Object.FindObjectOfType<GameBootstrap>();
            if (_game == null) throw new InvalidOperationException("没有运行中的 GameBootstrap。");
            if (_steps != null) throw new InvalidOperationException("视觉采集正在运行。");
            Directory.CreateDirectory(Output);
            _originalBackground = Application.runInBackground; Application.runInBackground = true;
            _view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            _originalSize = (int)_view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_view);
            _view.Show(); _view.Focus();
            _game.ResumeGame(); _game.StartGame();
            _steps = new Queue<Action>();
            foreach (int height in new[] { 720, 1080 })
            {
                int h = height; int w = h * 16 / 9;
                _steps.Enqueue(() => SetSize(w, h));
                for (int c = 0; c < _game.catalog.Count; c++)
                for (int s = 0; s < _game.catalog.StageCount(c); s++)
                {
                    int chapter = c, stage = s;
                    _steps.Enqueue(() => Load(chapter, stage));
                    string file = "L" + (c + 1) + "P" + (s + 1) + "-" + w + "x" + h + ".png";
                    _steps.Enqueue(() => ScreenCapture.CaptureScreenshot(Output + "/" + file));
                }
                _steps.Enqueue(() => LoadShowcase());
                _steps.Enqueue(() => ScreenCapture.CaptureScreenshot(Output + "/objects-and-words-" + w + "x" + h + ".png"));
                _steps.Enqueue(() => Load(0, 0));
                _steps.Enqueue(() => Complete());
                _steps.Enqueue(() => ScreenCapture.CaptureScreenshot(Output + "/stage-complete-" + w + "x" + h + ".png"));
                _steps.Enqueue(() => Load(0, _game.catalog.StageCount(0) - 1));
                _steps.Enqueue(() => Complete());
                _steps.Enqueue(() => ScreenCapture.CaptureScreenshot(Output + "/level-complete-" + w + "x" + h + ".png"));
                _steps.Enqueue(() => Load(_game.catalog.Count - 1, _game.catalog.StageCount(_game.catalog.Count - 1) - 1));
                _steps.Enqueue(() => Complete());
                _steps.Enqueue(() => ScreenCapture.CaptureScreenshot(Output + "/campaign-complete-" + w + "x" + h + ".png"));
            }
            _next = EditorApplication.timeSinceStartup + .8;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (!Application.isPlaying || _game == null) { Finish(); return; }
            if (EditorApplication.timeSinceStartup < _next) return;
            if (_steps.Count == 0) { Load(0, 0); Finish(); Debug.Log("[规则工坊] 视觉采集完成：" + Output); return; }
            try { _steps.Dequeue()(); _view.Repaint(); _next = EditorApplication.timeSinceStartup + .7; }
            catch (Exception ex) { Finish(); Debug.LogException(ex); }
        }
        static void Finish()
        {
            EditorApplication.update -= Tick;
            _steps = null;
            Application.runInBackground = _originalBackground;
            if (_view != null) _view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(_view, _originalSize);
        }
        static void Load(int chapter, int stage)
        {
            var type = typeof(GameBootstrap);
            type.GetField("_index", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, chapter);
            type.GetMethod("ActivateStage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_game,
                new object[] { new GameSession(_game.catalog.LoadStage(chapter, stage)), stage, true });
        }
        static void LoadShowcase()
        {
            var map = LevelCloner.Clone(_game.catalog.LoadStage(0, 0));
            map.id = "VISUAL-REVIEW"; map.title = "Objects and word styles";
            map.bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(15, 4, 7) };
            map.terrain = new[] { new GridCellBox { id = "floor", min = new GridCell(0, 0, 0), max = new GridCell(15, 0, 7) } };
            var entities = new List<EntityDefinition>();
            var subjects = new[] { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA" };
            for (int i = 0; i < subjects.Length; i++)
            {
                entities.Add(new EntityDefinition { id = subjects[i], kind = "Object", subject = subjects[i], cell = new GridCell(1 + i * 2, 1, 3) });
                entities.Add(new EntityDefinition { id = "noun" + i, kind = "Text", token = subjects[i], cell = new GridCell(1 + i * 2, 1, 1) });
            }
            var words = new[] { "ROBOT", "IS", "YOU", "BOUNCY", "HOT", "MELT", "DEFEAT", "AND" };
            for (int i = 0; i < words.Length; i++) entities.Add(new EntityDefinition { id = "word" + i, kind = "Text", token = words[i], cell = new GridCell(i < 3 ? i + 1 : i * 2 - 2, 1, 6) });
            map.entities = entities.ToArray(); map.tutorial = new TutorialData();
            typeof(GameBootstrap).GetMethod("ActivateStage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_game,
                new object[] { new GameSession(map), 0, true });
        }
        static void Complete()
        {
            // UI 样张明确使用合成完成状态；不作为实际地图解法或通关证据。
            _game.Session.World.WonLatched = true;
            _game.hud.Refresh(_game.Session, null, _game.RegionTutorial);
        }
        static void SetSize(int width, int height)
        {
            var assembly = typeof(EditorWindow).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
            string label = "Review " + width + "x" + height;
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                var size = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width").GetValue(size) == width && (int)sizeType.GetProperty("height").GetValue(size) == height) { index = i; break; }
            }
            if (index < 0)
            {
                var ctor = sizeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { kindType, typeof(int), typeof(int), typeof(string) }, null);
                var size = ctor.Invoke(new[] { Enum.Parse(kindType, "FixedResolution"), (object)width, height, label });
                group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size }); index = count;
            }
            _view.GetType().GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(_view, index);
        }
    }
}
