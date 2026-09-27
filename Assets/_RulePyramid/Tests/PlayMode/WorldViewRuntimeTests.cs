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
                Assert.AreNotSame(blue, rockRenderer.sharedMaterial);
                Assert.AreEqual(3f, rockRenderer.sharedMaterial.GetFloat("_Mode"));
                Assert.AreEqual(0.22f, rockRenderer.sharedMaterial.color.a, 0.001f);
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

        [UnityTest]
        public IEnumerator OrthographicOcclusionUsesThePlayersParallelCameraRay()
        {
            var config = ScriptableObject.CreateInstance<VisualConfig>();
            var red = Material(Color.red);
            var blue = Material(Color.blue);
            config.redMaterial = red;
            config.blueMaterial = blue;
            var host = new GameObject("WorldViewOcclusionTest");
            var cameraHost = new GameObject("WorldViewOcclusionCamera");
            var camera = cameraHost.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            var view = host.AddComponent<WorldView>();
            view.config = config;
            try
            {
                var world = WorldModel.FromLevel(Level());
                view.Rebuild(world);
                camera.transform.position = new Vector3(6f, 1f, 8f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.back);
                view.RefreshOcclusion(camera);
                Assert.IsTrue(view.TryGetView("rock", out var rock));
                var renderer = rock.GetComponent<Renderer>();
                Assert.AreEqual(0.22f, renderer.sharedMaterial.color.a, 0.001f,
                    "Off-axis YOU still needs the parallel orthographic ray.");

                camera.transform.position = new Vector3(6f, 1f, -8f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                view.RefreshOcclusion(camera);
                Assert.AreSame(blue, renderer.sharedMaterial,
                    "Moving the camera past the occluder should restore opacity.");
            }
            finally
            {
                Object.Destroy(cameraHost);
                Object.Destroy(host);
                Object.Destroy(config);
                Object.Destroy(red);
                Object.Destroy(blue);
            }
            yield return null;
        }
    }
}
