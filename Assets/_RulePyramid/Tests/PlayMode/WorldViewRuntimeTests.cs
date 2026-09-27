using System.Collections;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class WorldViewRuntimeTests
    {
        static LevelDefinition Level()
        {
            return new LevelDefinition
            {
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                id = "world-view",
                title = "渲染测试",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(5, 5, 5) },
                terrain = new[]
                {
                    new GridCellBox { id = "floor", min = new GridCell(0, 0, 0), max = new GridCell(5, 0, 5) }
                },
                entities = new[]
                {
                    new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT", cell = new GridCell(1, 1, 1) },
                    new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 3) },
                    new EntityDefinition { id = "wall", kind = "Object", subject = "WALL", cell = new GridCell(3, 1, 2) },
                    Word("robot-word", "ROBOT", 0, 0),
                    Word("robot-is", "IS", 1, 0),
                    Word("robot-you", "YOU", 2, 0),
                    Word("rock-word", "ROCK", 2, 5),
                    Word("rock-is", "IS", 3, 5),
                    Word("rock-stop", "STOP", 4, 5)
                }
            };
        }

        static EntityDefinition Word(string id, string token, int x, int z) =>
            new EntityDefinition { id = id, kind = "Text", token = token, cell = new GridCell(x, 1, z) };

        static Material Material(Color color)
        {
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            return material;
        }

        [UnityTest]
        public IEnumerator RuleVisualsAndTerrainBoxesUpdateWithoutChangingSourceMaterials()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var red = Material(Color.red);
            var blue = Material(Color.blue);
            var terrain = Material(Color.gray);
            var text = Material(Color.yellow);
            config.redMaterial = red;
            config.blueMaterial = blue;
            config.terrainMaterial = terrain;
            config.textMaterial = text;
            var host = new GameObject("WorldViewRuntimeTests");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var world = WorldModel.FromLevel(Level());
                view.Rebuild(world);
                view.RefreshOcclusion(null);
                Assert.AreEqual(world.Entities.Count + 1, host.transform.childCount,
                    "A whole terrain box should use one render object.");
                var floor = host.transform.Find("Terrain floor");
                Assert.IsNotNull(floor);
                Assert.AreEqual(new Vector3(6f, 1f, 6f), floor.localScale);

                Assert.IsTrue(view.TryGetView("rock", out var rock));
                var rockRenderer = rock.GetComponent<Renderer>();
                Assert.AreSame(blue, rockRenderer.sharedMaterial);
                Assert.IsTrue(world.Solid(world.Entity("rock")));

                Assert.IsTrue(view.TryGetView("wall", out var wall));
                Assert.Greater(wall.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    rock.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    "WALL should have a fence mesh rather than a full block.");

                world.Entity("rock-stop").Cell = new GridCell(4, 1, 4);
                world.Refresh();
                Assert.IsFalse(world.Solid(world.Entity("rock")));
                view.AlignToState(world);
                view.RefreshOcclusion(null);
                Assert.AreSame(blue, rockRenderer.sharedMaterial);
                Assert.AreEqual(Vector3.one, rock.localScale);
                Assert.AreEqual(1f, rockRenderer.sharedMaterial.color.a, 0.001f);
                Assert.AreEqual(1f, blue.color.a, 0.001f, "The shared source asset must stay opaque.");

                world.Entity("rock-stop").Cell = new GridCell(4, 1, 5);
                world.Refresh();
                view.AlignToState(world);
                view.RefreshOcclusion(null);
                Assert.AreSame(blue, rockRenderer.sharedMaterial);
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(config);
                Object.Destroy(red);
                Object.Destroy(blue);
                Object.Destroy(terrain);
                Object.Destroy(text);
            }
            yield return null;
        }

        [TestCase("ROCK")]
        [TestCase("ROBOT")]
        [TestCase("WALL")]
        [TestCase("FLAG")]
        public void RemovingStopPreservesObjectAppearance(string subject)
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var material = Material(Color.blue);
            config.blueMaterial = config.redMaterial = config.terrainMaterial = config.pinkMaterial = material;
            var host = new GameObject("属性不改外观测试");
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var level = Level();
                level.entities[0].subject = "CLOUD";
                level.entities[1].subject = subject;
                level.entities[3].token = "CLOUD";
                level.entities[6].token = subject;
                var world = WorldModel.FromLevel(level);
                view.Rebuild(world);
                view.RefreshOcclusion(null);
                Assert.IsTrue(view.TryGetView("rock", out var target));
                var renderer = target.GetComponent<Renderer>();
                var mesh = target.GetComponent<MeshFilter>().sharedMesh;
                var scale = target.localScale;
                Assert.IsTrue(world.Solid(world.Entity("rock")));
                world.Entity("rock-stop").Cell = new GridCell(4, 1, 4);
                world.Refresh();
                Assert.IsFalse(world.Solid(world.Entity("rock")), "逻辑属性必须实际改变。");
                view.AlignToState(world);
                view.RefreshOcclusion(null);
                Assert.AreSame(material, renderer.sharedMaterial);
                Assert.AreSame(mesh, target.GetComponent<MeshFilter>().sharedMesh);
                Assert.AreEqual(scale, target.localScale);
                Assert.AreEqual(1f, renderer.sharedMaterial.color.a);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(material);
            }
        }

        [UnityTest]
        public IEnumerator CutawayFollowsAnimatedYouAndClearsWhenDisabled()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var source = Material(Color.blue);
            config.blueMaterial = source;
            var host = new GameObject("圆形剖切状态测试");
            var cameraHost = new GameObject("剖切测试相机");
            var camera = cameraHost.AddComponent<Camera>();
            camera.enabled = false;
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var world = WorldModel.FromLevel(Level());
                view.Rebuild(world);
                Assert.IsTrue(view.TryGetView("rock", out var rock));
                Assert.IsTrue(view.TryGetView("robot", out var robot));
                var renderer = rock.GetComponent<Renderer>();
                robot.position += Vector3.right * 0.25f;
                view.RefreshOcclusion(camera);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Assert.AreEqual((Vector4)robot.position, block.GetVector("_CutawayTarget"));
                Assert.AreEqual(0.9f, block.GetFloat("_CutawayRadius"), 0.001f);
                Assert.AreEqual(1f, renderer.sharedMaterial.color.a);
                Assert.AreEqual("Standard", source.shader.name);
                Assert.AreEqual(1f, source.color.a);
                Assert.IsTrue(view.TryGetView("robot-you", out var word));
                word.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0f, block.GetFloat("_CutawayRadius"), "词牌不可被开口裁掉。");
                robot.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreEqual(0f, block.GetFloat("_CutawayRadius"), "YOU 自己不可被裁掉。");
                view.enabled = false;
                Assert.AreSame(source, renderer.sharedMaterial);
                renderer.GetPropertyBlock(block);
                Assert.AreEqual(0f, block.GetFloat("_CutawayRadius"));
                view.enabled = true;
                view.RefreshOcclusion(camera);
                view.RefreshOcclusion(null);
                Assert.AreSame(source, renderer.sharedMaterial);
            }
            finally
            {
                Object.Destroy(cameraHost);
                Object.Destroy(host);
                Object.Destroy(config);
                Object.Destroy(source);
            }
            yield return null;
        }

        [TestCase(true, 256, 128)]
        [TestCase(false, 128, 256)]
        public void RenderedCircleRevealsPlayerPreservesOutsideAndDoesNotCutBehind(bool orthographic, int width, int height)
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var red = Material(Color.black);
            var blue = Material(Color.black);
            red.EnableKeyword("_EMISSION");
            red.SetColor("_EmissionColor", Color.red);
            blue.EnableKeyword("_EMISSION");
            blue.SetColor("_EmissionColor", Color.blue);
            config.redMaterial = red;
            config.blueMaterial = blue;
            var host = new GameObject("圆形剖切像素测试");
            var cameraHost = new GameObject("圆形剖切像素相机");
            var camera = cameraHost.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = orthographic;
            camera.orthographicSize = 3f;
            camera.fieldOfView = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.green;
            camera.cullingMask = 1 << 30;
            camera.transform.position = new Vector3(1f, 1f, 8f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var world = WorldModel.FromLevel(Level());
                view.Rebuild(world);
                Assert.IsTrue(view.TryGetView("rock", out var rock));
                Assert.IsTrue(view.TryGetView("robot", out var robot));
                foreach (Transform child in host.transform)
                    child.gameObject.SetActive(child == rock || child == robot);
                rock.gameObject.layer = robot.gameObject.layer = 30;
                rock.localScale = new Vector3(8f, 8f, 1f);
                view.RefreshOcclusion(camera);
                Capture();
                AssertDominant(Sample(0f, 0f), 0, "中心必须显示红色 YOU。");
                AssertDominant(Sample(0.76f, 0f), 1, "圆内侧向空隙必须剔除。");
                AssertDominant(Sample(0f, 0.76f), 1, "竖向半径必须与横向相同。");
                AssertDominant(Sample(1.18f, 0f), 2, "圆外横向地形必须保留蓝色。");
                AssertDominant(Sample(0f, 1.18f), 2, "圆外竖向地形必须保留蓝色。");
                // 同一投影位置移动到 YOU 后方，圆内原先的空隙应恢复实体表面。
                rock.position = new Vector3(1f, 1f, -1f);
                view.RefreshOcclusion(camera);
                Capture();
                AssertDominant(Sample(0.76f, 0f), 2, "不能剔除玩家后方表面。");
                view.RefreshOcclusion(null);
                rock.position = new Vector3(1f, 1f, 3f);
                Capture();
                AssertDominant(Sample(0f, 0f), 2, "关闭遮罩应恢复完整遮挡。");
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraHost);
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(red);
                Object.DestroyImmediate(blue);
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }

            void Capture()
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
            }
            Color Sample(float x, float y)
            {
                var point = camera.WorldToViewportPoint(new Vector3(1f + x, 1f + y, 1f));
                return pixels.GetPixel(Mathf.FloorToInt(point.x * width), Mathf.FloorToInt(point.y * height));
            }
        }

        static void AssertDominant(Color color, int channel, string message)
        {
            Assert.Greater(color[channel], 0.5f, message + " 实际：" + color);
            Assert.Less(color[(channel + 1) % 3], 0.2f, message + " 实际：" + color);
            Assert.Less(color[(channel + 2) % 3], 0.2f, message + " 实际：" + color);
        }
    }
}
