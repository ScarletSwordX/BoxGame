using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class FixedCameraTests
    {
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
