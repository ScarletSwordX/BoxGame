using System;
using System.Collections;
using System.Collections.Generic;
using RulePyramid.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RulePyramid.Runtime
{
    public class WorldView : MonoBehaviour
    {
        public VisualConfig config;

        readonly Dictionary<string, Transform> _views = new Dictionary<string, Transform>();
        readonly Dictionary<string, Renderer> _entityRenderers = new Dictionary<string, Renderer>();
        readonly Dictionary<Renderer, Material> _baseMaterials = new Dictionary<Renderer, Material>();
        readonly Dictionary<Renderer, float> _baseAlpha = new Dictionary<Renderer, float>();
        readonly Dictionary<Material, Dictionary<int, Material>> _transparentMaterials =
            new Dictionary<Material, Dictionary<int, Material>>();
        readonly List<Renderer> _terrain = new List<Renderer>();
        readonly List<ExpansionTile> _expansionTiles = new List<ExpansionTile>();
        WorldModel _world;
        WorldModel _expansionPreviousWorld;
        Material _fallbackMaterial;
        Mesh _cubeMesh;
        Mesh _wallMesh;
        int _generation;
        int _expansionVersion;

        sealed class ExpansionTile
        {
            public GridCell Cell;
            public bool Glass;
            public int Ring;
            public float StartedAt;
            public Vector3 Target;
            public Transform Transform;
            public Renderer Renderer;
        }

        public int Generation => _generation;

        public void Rebuild(WorldModel world)
        {
            CancelExpansion();
            Clear();
            _world = world;
            if (world == null) return;

            if (world.Spec?.terrain != null)
            {
                foreach (var box in world.Spec.terrain)
                {
                    if (box == null) continue;
                    bool glass = string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase);
                    var renderer = CreateTerrainBox(box, glass ? GlassMat() : TerrainMat());
                    Register(renderer, renderer.sharedMaterial, glass ? 0.28f : 1f);
                    _terrain.Add(renderer);
                }
            }
            else
            {
                foreach (var cell in world.Terrain)
                {
                    var renderer = CreateCube("Terrain " + cell, cell, TerrainMat(), 1f).GetComponent<Renderer>();
                    Register(renderer, renderer.sharedMaterial, 1f);
                    _terrain.Add(renderer);
                }
            }

            foreach (var e in world.Entities)
                AddEntity(e, world);
            _generation++;
            RefreshOcclusion(Camera.main);
        }

        /// <summary>Reveal terrain cells added by the next stage, then replace all stage entities with its authored initial state.</summary>
        public IEnumerator ExpandTo(WorldModel next, float duration)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            CancelExpansion();
            if (_world == null || duration <= 0f)
            {
                Rebuild(next);
                yield break;
            }

            var previous = _world;
            var added = AddedTerrain(previous, next);
            if (added.Count == 0)
            {
                Rebuild(next);
                yield break;
            }

            int version = ++_expansionVersion;
            _expansionPreviousWorld = previous;
            RefreshOcclusion(null);
            foreach (var view in _views.Values)
                if (view != null) view.gameObject.SetActive(false);
            _world = null;

            int highestRing = added[added.Count - 1].Ring;
            float batchDuration = duration / (highestRing + 1);
            float riseDuration = Mathf.Min(0.28f, batchDuration * 0.8f);
            int nextTile = 0;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (version != _expansionVersion) yield break;
                int currentRing = Mathf.Min(highestRing, Mathf.FloorToInt(elapsed / batchDuration));
                while (nextTile < added.Count && added[nextTile].Ring <= currentRing)
                {
                    var tile = added[nextTile++];
                    var cube = CreateCube("Expanded Terrain " + tile.Cell,
                        tile.Cell, tile.Glass ? GlassMat() : TerrainMat(), 1f);
                    tile.Transform = cube;
                    tile.Renderer = cube.GetComponent<Renderer>();
                    tile.Target = cube.position;
                    tile.StartedAt = elapsed;
                    cube.position = tile.Target + Vector3.down * CellSize;
                    Register(tile.Renderer, tile.Renderer.sharedMaterial, tile.Glass ? 0.28f : 1f);
                    _expansionTiles.Add(tile);
                }
                for (int i = 0; i < _expansionTiles.Count; i++)
                {
                    var tile = _expansionTiles[i];
                    if (tile.Transform == null) continue;
                    float rise = Mathf.Clamp01((elapsed - tile.StartedAt) / Mathf.Max(0.001f, riseDuration));
                    tile.Transform.position = tile.Target + Vector3.down * (CellSize * (1f - Mathf.SmoothStep(0f, 1f, rise)));
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (version != _expansionVersion) yield break;
            _expansionPreviousWorld = null;
            Rebuild(next);
        }

        /// <summary>Stop an in-progress reveal and restore the previous stage's visual state.</summary>
        public void CancelExpansion()
        {
            _expansionVersion++;
            for (int i = 0; i < _expansionTiles.Count; i++)
            {
                var tile = _expansionTiles[i];
                if (tile.Renderer != null)
                {
                    _baseMaterials.Remove(tile.Renderer);
                    _baseAlpha.Remove(tile.Renderer);
                }
                if (tile.Transform == null) continue;
                tile.Transform.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(tile.Transform.gameObject);
                else DestroyImmediate(tile.Transform.gameObject);
            }
            _expansionTiles.Clear();
            if (_expansionPreviousWorld == null) return;
            _world = _expansionPreviousWorld;
            _expansionPreviousWorld = null;
            foreach (var view in _views.Values)
                if (view != null) view.gameObject.SetActive(true);
            RefreshOcclusion(Camera.main);
        }

        static List<ExpansionTile> AddedTerrain(WorldModel previous, WorldModel next)
        {
            var tiles = new List<ExpansionTile>();
            var glass = new HashSet<GridCell>();
            if (next.Spec?.terrain != null)
            foreach (var box in next.Spec.terrain)
            {
                if (box == null || !string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase))
                    continue;
                for (int y = box.min.y; y <= box.max.y; y++)
                for (int z = box.min.z; z <= box.max.z; z++)
                for (int x = box.min.x; x <= box.max.x; x++)
                    glass.Add(new GridCell(x, y, z));
            }

            bool hasOld = previous.Terrain.Count > 0;
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (var cell in previous.Terrain)
            {
                minX = Math.Min(minX, cell.x); maxX = Math.Max(maxX, cell.x);
                minY = Math.Min(minY, cell.y); maxY = Math.Max(maxY, cell.y);
                minZ = Math.Min(minZ, cell.z); maxZ = Math.Max(maxZ, cell.z);
            }
            int minimumRing = int.MaxValue;
            foreach (var cell in next.Terrain)
            {
                if (previous.Terrain.Contains(cell)) continue;
                int ring = hasOld
                    ? Math.Max(Math.Max(Math.Max(minX - cell.x, cell.x - maxX), Math.Max(minY - cell.y, cell.y - maxY)),
                        Math.Max(Math.Max(minZ - cell.z, cell.z - maxZ), 0))
                    : 0;
                minimumRing = Math.Min(minimumRing, ring);
                tiles.Add(new ExpansionTile { Cell = cell, Glass = glass.Contains(cell), Ring = ring });
            }
            foreach (var tile in tiles) tile.Ring -= minimumRing;
            tiles.Sort((a, b) =>
            {
                int cmp = a.Ring.CompareTo(b.Ring);
                if (cmp != 0) return cmp;
                cmp = a.Cell.z.CompareTo(b.Cell.z);
                if (cmp != 0) return cmp;
                cmp = a.Cell.x.CompareTo(b.Cell.x);
                return cmp != 0 ? cmp : a.Cell.y.CompareTo(b.Cell.y);
            });
            return tiles;
        }

        public void AlignToState(WorldModel world)
        {
            if (world == null) return;
            _world = world;
            var currentIds = new HashSet<string>();
            foreach (var e in world.Entities)
            {
                currentIds.Add(e.Id);
                if (!_views.TryGetValue(e.Id, out var t) || t == null)
                {
                    AddEntity(e, world);
                    continue;
                }
                t.position = GridMap.ToWorld(e.Cell, config);
                UpdateEntityVisual(e, world, t);
            }

            var removed = new List<string>();
            foreach (var pair in _views)
                if (!currentIds.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (var id in removed)
            {
                var t = _views[id];
                if (_entityRenderers.TryGetValue(id, out var renderer))
                {
                    _baseMaterials.Remove(renderer);
                    _baseAlpha.Remove(renderer);
                }
                _entityRenderers.Remove(id);
                _views.Remove(id);
                if (t != null)
                {
                    if (Application.isPlaying) Destroy(t.gameObject);
                    else DestroyImmediate(t.gameObject);
                }
            }
            RefreshOcclusion(Camera.main);
        }

        public bool TryGetView(string id, out Transform t) => _views.TryGetValue(id, out t);

        void LateUpdate()
        {
            if (_world != null) RefreshOcclusion(Camera.main);
        }

        /// <summary>Applies camera-relative fading without changing the simulation collision state.</summary>
        public void RefreshOcclusion(Camera camera)
        {
            string youId = _world?.ActorId;
            Transform target = null;
            bool hasTarget = camera != null && youId != null
                && _views.TryGetValue(youId, out target) && target != null;
            Ray ray = default;
            float targetDistance = 0f;
            if (hasTarget)
            {
                var viewport = camera.WorldToViewportPoint(target.position);
                ray = camera.ViewportPointToRay(viewport);
                targetDistance = Vector3.Dot(target.position - ray.origin, ray.direction);
                hasTarget = targetDistance > 0.001f;
            }

            foreach (var renderer in _terrain)
                ApplyAlpha(renderer, IsOccluding(renderer) ? Mathf.Min(_baseAlpha[renderer], 0.22f) : _baseAlpha[renderer]);
            foreach (var pair in _entityRenderers)
            {
                var renderer = pair.Value;
                float alpha = _baseAlpha[renderer];
                if (pair.Key != youId && IsOccluding(renderer)) alpha = Mathf.Min(alpha, 0.22f);
                ApplyAlpha(renderer, alpha);
            }

            bool IsOccluding(Renderer renderer)
            {
                if (!hasTarget || renderer == null || !renderer.enabled) return false;
                if (!renderer.bounds.IntersectRay(ray, out var entry)) return false;
                return entry >= 0f && entry < targetDistance - 0.05f;
            }
        }

        void AddEntity(EntityState e, WorldModel world)
        {
            var t = CreateEntity(e, world);
            _views[e.Id] = t;
            _entityRenderers[e.Id] = t.GetComponent<Renderer>();
            UpdateEntityVisual(e, world, t);
        }

        Transform CreateEntity(EntityState e, WorldModel world)
        {
            bool solid = PropertyResolver.IsSolid(e, world.Rules);
            bool win = PropertyResolver.HasWin(e, world.Rules);
            var t = CreateCube(e.Id, e.Cell, MaterialFor(e, solid, win), 1f);
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
                DestroyCollider(peg);
            }
            return t;
        }

        void UpdateEntityVisual(EntityState e, WorldModel world, Transform t)
        {
            bool solid = PropertyResolver.IsSolid(e, world.Rules);
            bool win = PropertyResolver.HasWin(e, world.Rules);
            var renderer = t.GetComponent<Renderer>();
            float alpha = e.Kind == EntityKind.Object && !solid ? (win ? 0.45f : 0.22f) : 1f;
            Register(renderer, MaterialFor(e, solid, win), alpha);
            var filter = t.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = e.Kind == EntityKind.Object && e.Subject == "WALL" ? WallMesh() : _cubeMesh;
            t.localScale = Vector3.one * CellSize * (e.Kind == EntityKind.Text || !solid ? 0.92f : 1f);
        }

        Renderer CreateTerrainBox(GridCellBox box, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Terrain " + (box.id ?? "Box");
            go.transform.SetParent(transform, false);
            var center = new Vector3(
                ((float)box.min.x + box.max.x) * 0.5f,
                ((float)box.min.y + box.max.y) * 0.5f,
                ((float)box.min.z + box.max.z) * 0.5f);
            go.transform.position = (config != null ? config.origin : Vector3.zero) + center * CellSize;
            go.transform.localScale = new Vector3(
                box.max.x - box.min.x + 1,
                box.max.y - box.min.y + 1,
                box.max.z - box.min.z + 1) * CellSize;
            DestroyCollider(go);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat != null ? mat : FallbackMaterial();
            return renderer;
        }

        Transform CreateCube(string name, GridCell cell, Material mat, float scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = GridMap.ToWorld(cell, config);
            go.transform.localScale = Vector3.one * CellSize * scale;
            DestroyCollider(go);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat != null ? mat : FallbackMaterial();
            if (_cubeMesh == null) _cubeMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go.transform;
        }

        Mesh WallMesh()
        {
            if (_wallMesh != null) return _wallMesh;
            var pieces = new[]
            {
                new Vector3(0.18f, 1f, 0.18f),
                new Vector3(1f, 0.13f, 0.12f),
                new Vector3(1f, 0.13f, 0.12f),
                new Vector3(0.12f, 0.13f, 1f),
                new Vector3(0.12f, 0.13f, 1f)
            };
            var positions = new[]
            {
                Vector3.zero,
                new Vector3(0f, 0.18f, 0f),
                new Vector3(0f, -0.22f, 0f),
                new Vector3(0f, 0.18f, 0f),
                new Vector3(0f, -0.22f, 0f)
            };
            var combines = new CombineInstance[pieces.Length];
            for (int i = 0; i < combines.Length; i++)
                combines[i] = new CombineInstance
                {
                    mesh = _cubeMesh,
                    transform = Matrix4x4.TRS(positions[i], Quaternion.identity, pieces[i])
                };
            _wallMesh = new Mesh { name = "WorldView Wall Fence" };
            _wallMesh.CombineMeshes(combines, true, true);
            return _wallMesh;
        }

        static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
            else UnityEngine.Object.DestroyImmediate(collider);
        }

        void Register(Renderer renderer, Material material, float alpha)
        {
            if (renderer == null) return;
            _baseMaterials[renderer] = material != null ? material : FallbackMaterial();
            _baseAlpha[renderer] = alpha;
            ApplyAlpha(renderer, alpha);
        }

        void ApplyAlpha(Renderer renderer, float alpha)
        {
            if (renderer == null || !_baseMaterials.TryGetValue(renderer, out var material)) return;
            var chosen = alpha >= 0.999f ? material : TransparentMaterial(material, alpha);
            if (renderer.sharedMaterial != chosen) renderer.sharedMaterial = chosen;
        }

        Material TransparentMaterial(Material source, float alpha)
        {
            if (!_transparentMaterials.TryGetValue(source, out var levels))
                _transparentMaterials[source] = levels = new Dictionary<int, Material>();
            int level = Mathf.Clamp(Mathf.RoundToInt(alpha * 1000f), 0, 999);
            if (levels.TryGetValue(level, out var existing)) return existing;
            var material = new Material(source) { name = source.name + " Runtime Fade " + level };
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            if (material.HasProperty("_Color"))
            {
                var color = source.color;
                color.a = level / 1000f;
                material.color = color;
            }
            levels[level] = material;
            return material;
        }

        Material FallbackMaterial()
        {
            if (_fallbackMaterial == null)
                _fallbackMaterial = new Material(Shader.Find("Standard")) { name = "WorldView Runtime Fallback" };
            return _fallbackMaterial;
        }

        float CellSize => config != null ? config.cellSize : 1f;
        Material TerrainMat() => config != null ? config.terrainMaterial : null;
        Material GlassMat() => config != null && config.pinkHollowMaterial != null ? config.pinkHollowMaterial : TerrainMat();

        Material MaterialFor(EntityState e, bool solid, bool win)
        {
            if (config == null) return null;
            if (e.Kind == EntityKind.Text)
                return e.Anchored && config.anchoredTextMaterial != null ? config.anchoredTextMaterial : config.textMaterial;
            switch (e.Subject)
            {
                case "ROBOT": return config.redMaterial;
                case "ROCK": return config.blueMaterial;
                case "FLAG": return !solid && config.pinkHollowMaterial != null ? config.pinkHollowMaterial : config.pinkMaterial;
                case "CLOUD": return config.blueMaterial != null ? config.blueMaterial : config.textMaterial;
                case "SPRING": return config.pinkMaterial != null ? config.pinkMaterial : config.textMaterial;
                case "WALL": return config.terrainMaterial != null ? config.terrainMaterial : config.textMaterial;
                default: return config.textMaterial;
            }
        }

        public void Clear()
        {
            CancelExpansion();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _views.Clear();
            _entityRenderers.Clear();
            _baseMaterials.Clear();
            _baseAlpha.Clear();
            _terrain.Clear();
            _world = null;
        }

        void OnDestroy()
        {
            foreach (var levels in _transparentMaterials.Values)
            foreach (var material in levels.Values)
                if (material != null) DestroyMaterial(material);
            _transparentMaterials.Clear();
            if (_fallbackMaterial != null) DestroyMaterial(_fallbackMaterial);
            if (_wallMesh != null)
            {
                if (Application.isPlaying) Destroy(_wallMesh);
                else DestroyImmediate(_wallMesh);
            }
        }

        static void DestroyMaterial(Material material)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
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
