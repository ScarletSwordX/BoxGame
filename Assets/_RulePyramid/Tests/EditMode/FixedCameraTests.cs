using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class FixedCameraTests
    {
        static void AssertInputAlignment(CameraSlotsController camera, float expectedAngle)
        {
            var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
            var inputs = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            var expected = new[] { forward, -forward, -right, right };
            for (int i = 0; i < inputs.Length; i++)
            {
                var offset = WorldDirections.ToOffset(camera.ScreenToWorld(inputs[i]));
                float angle = Vector3.Angle(new Vector3(offset.x, offset.y, offset.z), expected[i]);
                Assert.LessOrEqual(angle, 20.001f, "四向输入与地平面镜头方向的偏差不得超过 20°。");
                Assert.AreEqual(expectedAngle, angle, .001f);
            }
        }

        [TestCase(15f, 15f)]
        [TestCase(-15f, 15f)]
        [TestCase(0f, 0f)]
        [TestCase(20f, 20f)]
        [TestCase(-20f, 20f)]
        [TestCase(45f, 20f)]
        [TestCase(-45f, 20f)]
        [TestCase(375f, 15f)]
        [TestCase(345f, 15f)]
        [TestCase(360f, 0f)]
        [TestCase(float.NaN, 15f)]
        [TestCase(float.PositiveInfinity, 15f)]
        public void FourDirectionsStayWithinTwentyDegreesOfGroundProjectedCamera(float yaw, float angle)
        {
            var holder = new GameObject("操作与镜头夹角测试");
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            try
            {
                config.cameraYaws[0] = yaw;
                var camera = holder.AddComponent<CameraSlotsController>();
                camera.config = config;
                camera.ConfigureFromLevel(new CameraData { initialSlot = 3 });
                camera.Snap();
                AssertInputAlignment(camera, angle);
            }
            finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(config); }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void MissingOrMalformedYawConfigUsesFifteenDegreeFallback(int length)
        {
            var holder = new GameObject("镜头回退测试");
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            try
            {
                var camera = holder.AddComponent<CameraSlotsController>();
                camera.Snap();
                AssertInputAlignment(camera, 15f);
                config.cameraYaws = length < 0 ? null : new float[length];
                camera.config = config;
                camera.Snap();
                AssertInputAlignment(camera, 15f);
            }
            finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(config); }
        }

        [Test]
        public void PlayerCameraUsesFirstViewEvenWhenLevelRequestsAnotherSlot()
        {
            var holder = new GameObject("Fixed camera test");
            try
            {
                var camera = holder.AddComponent<CameraSlotsController>();
                camera.slot = 3;
                camera.ConfigureFromLevel(new CameraData { initialSlot = 2 });
                Assert.AreEqual(0, camera.Slot);
                Assert.AreEqual(0, camera.slot);
                Assert.AreEqual(WorldDirection.North, camera.ScreenToWorld(Vector2.up));
                Assert.AreEqual(WorldDirection.East, camera.ScreenToWorld(Vector2.right));
                Assert.AreEqual(WorldDirection.South, camera.ScreenToWorld(Vector2.down));
                Assert.AreEqual(WorldDirection.West, camera.ScreenToWorld(Vector2.left));
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void FramingExpandedMapKeepsAllCornersVisibleWithoutRotatingView()
        {
            var holder = new GameObject("阶段取景测试");
            try
            {
                var lens = holder.AddComponent<Camera>();
                lens.aspect = 16f / 9f;
                var camera = holder.AddComponent<CameraSlotsController>();
                camera.FrameLevel(new GridCellBox { min = new GridCell(1, 0, 2), max = new GridCell(7, 1, 4) }, true);
                float firstSize = lens.orthographicSize;
                var rotation = holder.transform.rotation;
                var bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(11, 1, 9) };
                camera.FrameLevel(bounds, true);
                Assert.Greater(lens.orthographicSize, firstSize);
                Assert.AreEqual(rotation, holder.transform.rotation);
                foreach (int x in new[] { bounds.min.x, bounds.max.x })
                foreach (int y in new[] { bounds.min.y, bounds.max.y })
                foreach (int z in new[] { bounds.min.z, bounds.max.z })
                {
                    var point = lens.WorldToViewportPoint(new Vector3(x, y, z));
                    Assert.That(point.x, Is.InRange(0f, 1f));
                    Assert.That(point.y, Is.InRange(0f, 1f));
                    Assert.Greater(point.z, 0f);
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Test]
        public void StageCoordinateShiftPreservesScreenPositionAndScale()
        {
            var holder = new GameObject("右上角过场对齐测试");
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            try
            {
                config.cellSize = 2f;
                config.origin = new Vector3(10f, 3f, -5f);
                var lens = holder.AddComponent<Camera>();
                lens.aspect = 16f / 9f;
                var camera = holder.AddComponent<CameraSlotsController>();
                camera.config = config;
                camera.FrameLevel(new GridCellBox { min = new GridCell(-3, 0, -4), max = new GridCell(7, 5, 8) }, true);
                var point = GridMap.ToWorld(new GridCell(7, 0, 8), config);
                var before = lens.WorldToViewportPoint(point);
                var size = lens.orthographicSize;
                var rotation = holder.transform.rotation;
                var delta = new Vector3(4f, 0f, 3f) * config.cellSize;
                camera.TranslateFrame(delta);
                Assert.Less(Vector3.Distance(before, lens.WorldToViewportPoint(point + delta)), .0001f);
                Assert.AreEqual(size, lens.orthographicSize);
                Assert.AreEqual(rotation, holder.transform.rotation);
            }
            finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(config); }
        }

        [Test]
        public void RotationCommandsAreRejectedWithoutSpendingATurn()
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/L01.json");
            var level = LevelJsonSerializer.FromJson(File.ReadAllText(path));
            level.camera.initialSlot = 2;
            var game = new GameSession(level);

            foreach (var command in new[] { "CAM+", "CAM-" })
            {
                Assert.IsFalse(SimCommand.TryParse(command, out _, out var parseError));
                Assert.AreEqual("Player camera is fixed", parseError);
                Assert.IsFalse(game.TryExecute(command));
                Assert.AreEqual("Player camera is fixed", game.LastRejectReason);
                Assert.AreEqual(0, game.TurnCount);
            }
        }
    }
}
