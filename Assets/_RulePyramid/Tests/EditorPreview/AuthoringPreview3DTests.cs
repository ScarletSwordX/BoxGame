using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public sealed class AuthoringPreview3DTests
    {
        static LevelDefinition Level(params GridCellBox[] terrain)
        {
            return new LevelDefinition
            {
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 3, 4) },
                terrain = terrain,
                entities = Array.Empty<EntityDefinition>()
            };
        }

        [Test]
        public void BlockEdgesUseFaceUvsAndDepthTestedGridMaterial()
        {
            using (var preview = new AuthoringPreview3D())
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = typeof(AuthoringPreview3D);
                var mesh = (Mesh)type.GetField("_cube", flags).GetValue(preview);
                var uv = mesh.uv;
                Assert.AreEqual(24, uv.Length);
                for (int face = 0; face < 6; face++)
                {
                    Assert.AreEqual(Vector2.zero, uv[face * 4]);
                    Assert.AreEqual(Vector2.right, uv[face * 4 + 1]);
                    Assert.AreEqual(Vector2.one, uv[face * 4 + 2]);
                    Assert.AreEqual(Vector2.up, uv[face * 4 + 3]);
                }
                foreach (string field in new[] { "_terrainMaterial", "_glassMaterial", "_objectMaterial", "_textMaterial", "_selectedMaterial", "_validPreviewMaterial", "_invalidPreviewMaterial" })
                {
                    var material = (Material)type.GetField(field, flags).GetValue(preview);
                    Assert.AreEqual("Hidden/RuleWorkshop/AuthoringBlocks", material.shader.name);
                    Assert.IsTrue(material.shader.isSupported);
                    Assert.IsTrue(material.SetPass(0), field);
                    Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(material.shader), field);
                    Assert.AreEqual(field == "_terrainMaterial" || field == "_glassMaterial" ? 1f : 0f, material.GetFloat("_WorldGrid"));
                    bool ghost = field == "_validPreviewMaterial" || field == "_invalidPreviewMaterial";
                    Assert.AreEqual((int)(ghost ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual), material.GetInt("_ZTest"));
                    Assert.AreEqual(field == "_glassMaterial" || ghost ? 0 : 1, material.GetInt("_ZWrite"));
                }
            }
        }

        [Test]
        public void EmptyEntityPickReturnsLayerCellForBoxSelection()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var level = Level();
                preview.Frame(level.bounds);
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(new GridCell(2, 1, 2));
                Assert.IsTrue(preview.TryPick(mouse, true, out var cell, out var id));
                Assert.AreEqual(new GridCell(2, 1, 2), cell);
                Assert.IsNull(id);
            }
        }

        [Test]
        public void SurfaceEraseSelectsTerrainCell()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var terrain = new GridCellBox { min = new GridCell(2, 1, 2), max = new GridCell(2, 1, 2) };
                var level = Level(terrain);
                preview.Focus(terrain.min);
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, true, true);
                var mouse = preview.ProjectCellCenter(terrain.min);
                Assert.IsTrue(preview.TryPick(mouse, true, out var cell, out _));
                Assert.AreEqual(terrain.min, cell);
            }
        }

        [Test]
        public void TerrainPickAndDragPlaneKeepOriginalLayer()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var terrain = new GridCellBox { min = new GridCell(1, 1, 1), max = new GridCell(1, 2, 1) };
                var level = Level(terrain);
                preview.Focus(new GridCell(1, 1, 1));
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(new GridCell(1, 2, 1));
                Assert.IsTrue(preview.TryPickTerrain(mouse, out var picked));
                Assert.AreEqual(1, picked.x);
                Assert.AreEqual(1, picked.z);
                Assert.IsTrue(preview.TryPickDragPlane(mouse, 1, out var dragCell));
                Assert.AreEqual(1, dragCell.y);
                Assert.IsFalse(preview.TryPickDragPlane(new Vector2(-1f, -1f), 1, out _));
            }
        }

        [Test]
        public void DragPreviewDoesNotChangeSourceOrPicking()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var level = Level();
                var source = new EntityState { Id = "rock", Kind = EntityKind.Object, Subject = "ROCK",
                    Cell = new GridCell(1, 1, 1) };
                level.entities = new[] { new EntityDefinition { id = source.Id, kind = "Object",
                    subject = source.Subject, cell = source.Cell } };
                preview.Frame(level.bounds);
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(source.Cell);
                Assert.IsTrue(preview.TryPick(mouse, true, out _, out var beforeId));
                preview.SetSelectionPreview(new[] { source }, new[] { new GridCell(2, 1, 2) }, false);
                source.Cell = new GridCell(3, 1, 3);
                Assert.IsTrue(preview.TryPick(mouse, true, out _, out var afterId));
                Assert.AreEqual("rock", beforeId);
                Assert.AreEqual(beforeId, afterId);
                var field = typeof(AuthoringPreview3D).GetField("_previewEntities",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var cached = (List<EntityState>)field.GetValue(preview);
                Assert.AreEqual(new GridCell(1, 1, 1), cached[0].Cell);
            }
        }

        [Test]
        public void GlassDoesNotOccludeEntityButStoneDoes()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var wall = new GridCellBox { min = new GridCell(0, 1, 0), max = new GridCell(0, 3, 0), appearance = "Stone" };
                var level = Level(wall);
                level.entities = new[] { new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 1) } };
                preview.Focus(new GridCell(1, 1, 1));
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                var mouse = preview.ProjectCellCenter(level.entities[0].cell);
                Assert.IsTrue(preview.TryPick(mouse, true, out _, out var obscuredId));
                Assert.IsNull(obscuredId);

                preview.ThroughSelect = true;
                Assert.IsTrue(preview.TryPick(mouse, true, out _, out var throughId));
                Assert.AreEqual("rock", throughId);

                preview.ThroughSelect = false;
                wall.appearance = "TransparentGlass";
                preview.UpdatePicking(new Rect(0, 0, 500, 400), level, null, 1, false, false, false);
                Assert.IsTrue(preview.TryPick(mouse, true, out _, out var glassId));
                Assert.AreEqual("rock", glassId);
            }
        }

        [Test]
        public void ProjectionAndSegmentClippingStayInsidePreview()
        {
            using (var preview = new AuthoringPreview3D())
            {
                var level = Level();
                preview.Frame(level.bounds);
                preview.UpdatePicking(new Rect(30, 20, 500, 400), level, null, 1, false, false, false);
                var projected = preview.ProjectCellCenter(new GridCell(2, 1, 2));
                Assert.IsTrue(new Rect(30, 20, 500, 400).Contains(projected));
                Assert.IsTrue(preview.TryPick(projected, false, out var cell, out _));
                Assert.AreEqual(new GridCell(2, 1, 2), cell);

                var start = new Vector2(-50, 200);
                var end = new Vector2(550, 200);
                Assert.IsTrue(AuthoringPreview3D.ClipSegment(ref start, ref end, new Rect(0, 0, 500, 400)));
                Assert.AreEqual(0f, start.x, 0.01f);
                Assert.AreEqual(500f, end.x, 0.01f);
            }
        }

        [Test]
        public void CubeTrianglesPointOutward()
        {
            var method = typeof(AuthoringPreview3D).GetMethod("CreateCube", BindingFlags.Static | BindingFlags.NonPublic);
            var mesh = (Mesh)method.Invoke(null, null);
            try
            {
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    Assert.Greater(Vector3.Dot(normal, (a + b + c) / 3f), 0f, "Triangle " + (i / 3));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RebuildingWindowGuiRetainsDraft()
        {
            var window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            try
            {
                var original = window.Session;
                Assert.IsNotNull(original);
                original.Draft.title = "重建仍保留";
                var createGui = typeof(LevelEditorWindow).GetMethod("CreateGUI", BindingFlags.Instance | BindingFlags.NonPublic);
                createGui.Invoke(window, null);
                Assert.AreSame(original, window.Session);
                Assert.AreEqual("重建仍保留", window.Session.Draft.title);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
