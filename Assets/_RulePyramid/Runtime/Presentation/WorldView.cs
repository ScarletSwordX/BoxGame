using System.Collections.Generic;
using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public class WorldView : MonoBehaviour
    {
        public VisualConfig config;
        readonly Dictionary<string, Transform> _views = new Dictionary<string, Transform>();
        readonly List<Transform> _terrain = new List<Transform>();
        int _generation;

        public int Generation => _generation;

        public void Rebuild(WorldModel world)
        {
            Clear();
            if (world == null) return;
            foreach (var cell in world.Terrain)
                _terrain.Add(CreateCube("Terrain " + cell, cell, TerrainMat(), 1f));
            foreach (var e in world.Entities)
                _views[e.Id] = CreateEntity(e, world);
            _generation++;
        }

        public void AlignToState(WorldModel world)
        {
            if (world == null) return;
            foreach (var e in world.Entities)
            {
                if (_views.TryGetValue(e.Id, out var t) && t != null)
                    t.position = GridMap.ToWorld(e.Cell, config);
            }
        }

        public bool TryGetView(string id, out Transform t) => _views.TryGetValue(id, out t);

        Transform CreateEntity(EntityState e, WorldModel world)
        {
            bool solid = PropertyResolver.IsSolid(e, world.Rules);
            bool win = PropertyResolver.HasWin(e, world.Rules);
            var mat = MaterialFor(e, solid, win);
            float scale = e.Kind == EntityKind.Text ? 0.92f : (solid ? 1f : 0.92f);
            var t = CreateCube(e.Id, e.Cell, mat, scale);
            if (!solid && e.Kind == EntityKind.Color)
            {
                var rend = t.GetComponent<Renderer>();
                if (rend != null)
                {
                    var c = rend.material.color;
                    c.a = win ? 0.45f : 0.22f;
                    rend.material.color = c;
                }
            }
            if (e.Kind == EntityKind.Text)
            {
                var label = new GameObject("Label");
                label.transform.SetParent(t, false);
                label.transform.localPosition = Vector3.up * 0.55f;
                var tm = label.AddComponent<TextMesh>();
                tm.text = e.Token;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.12f;
                tm.fontSize = 32;
                tm.color = Color.black;
                label.AddComponent<CameraBillboard>();
            }
            if (e.Anchored)
            {
                var peg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                peg.name = "Anchor";
                peg.transform.SetParent(t, false);
                peg.transform.localScale = new Vector3(0.12f, 0.2f, 0.12f);
                peg.transform.localPosition = Vector3.down * 0.55f;
                Object.Destroy(peg.GetComponent<Collider>());
            }
            return t;
        }

        Transform CreateCube(string name, GridCell cell, Material mat, float scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = GridMap.ToWorld(cell, config);
            go.transform.localScale = Vector3.one * (config != null ? config.cellSize * scale : scale);
            Object.Destroy(go.GetComponent<Collider>());
            var rend = go.GetComponent<Renderer>();
            if (mat != null) rend.sharedMaterial = mat;
            return go.transform;
        }

        Material TerrainMat() => config != null ? config.terrainMaterial : null;

        Material MaterialFor(EntityState e, bool solid, bool win)
        {
            if (config == null) return null;
            if (e.Kind == EntityKind.Text)
                return e.Anchored && config.anchoredTextMaterial != null ? config.anchoredTextMaterial : config.textMaterial;
            switch (e.Color)
            {
                case "RED": return config.redMaterial;
                case "BLUE": return config.blueMaterial;
                case "PINK": return (!solid && config.pinkHollowMaterial != null) ? config.pinkHollowMaterial : config.pinkMaterial;
                default: return config.textMaterial;
            }
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject);
            else DestroyImmediate(transform.GetChild(i).gameObject);
            _views.Clear();
            _terrain.Clear();
        }
    }

    public class CameraBillboard : MonoBehaviour
    {
        void LateUpdate()
        {
            if (Camera.main == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
        }
    }
}
