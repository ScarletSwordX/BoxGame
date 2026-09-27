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
        readonly Dictionary<Material, Material> _cutawayMaterials = new Dictionary<Material, Material>();
        MaterialPropertyBlock _cutawayBlock;
        static readonly int CutawayTarget = Shader.PropertyToID("_CutawayTarget");
        static readonly int CutawayMinHeight = Shader.PropertyToID("_CutawayMinHeight");
        static readonly int CutawayRadius = Shader.PropertyToID("_CutawayRadius");
        static readonly int CutawayFeather = Shader.PropertyToID("_CutawayFeather");
        static readonly int CutawayDepthBias = Shader.PropertyToID("_CutawayDepthBias");
        Shader _cutawayShader;
        readonly List<Renderer> _terrain = new List<Renderer>();
        readonly List<ExpansionTile> _expansionTiles = new List<ExpansionTile>();
        WorldModel _world;
        WorldModel _expansionPreviousWorld;
        Material _fallbackMaterial;
        Material _lavaMaterial;
        Mesh _cubeMesh;
        Mesh _wallMesh;
        int _generation;
        int _expansionVersion;

        sealed class ExpansionTile
        {
            public GridCell Cell;
            public bool Glass;
            public bool Structure;
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
            int ground = GroundHeight(world);

            if (world.Spec?.terrain != null)
            {
                foreach (var box in world.Spec.terrain)
                {
                    if (box == null) continue;
                    bool glass = string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase);
                    var renderer = CreateTerrainBox(box, glass ? GlassMat() : TerrainMat(box.min.y > ground));
                    Register(renderer, renderer.sharedMaterial, glass ? 0.28f : 1f);
                    _terrain.Add(renderer);
                }
            }
            else
            {
                foreach (var cell in world.Terrain)
                {
                    var renderer = CreateCube("Terrain " + cell, cell, TerrainMat(cell.y > ground), 1f).GetComponent<Renderer>();
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
                if (Time.timeScale == 0f) { yield return null; continue; }
                int currentRing = Mathf.Min(highestRing, Mathf.FloorToInt(elapsed / batchDuration));
                while (nextTile < added.Count && added[nextTile].Ring <= currentRing)
                {
                    var tile = added[nextTile++];
                    var cube = CreateCube("Expanded Terrain " + tile.Cell,
                        tile.Cell, tile.Glass ? GlassMat() : TerrainMat(tile.Structure), 1f);
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
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (version != _expansionVersion) yield break;
            while (Time.timeScale == 0f) yield return null;
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
            var structure = new HashSet<GridCell>();
            int ground = GroundHeight(next);
            if (next.Spec?.terrain != null)
            foreach (var box in next.Spec.terrain)
            {
                if (box == null) continue;
                bool isGlass = string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase);
                bool isStructure = box.min.y > ground;
                if (!isGlass && !isStructure) continue;
                for (int y = box.min.y; y <= box.max.y; y++)
                for (int z = box.min.z; z <= box.max.z; z++)
                for (int x = box.min.x; x <= box.max.x; x++)
                {
                    var cell = new GridCell(x, y, z);
                    if (isGlass) glass.Add(cell);
                    if (isStructure) structure.Add(cell);
                }
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
                tiles.Add(new ExpansionTile { Cell = cell, Glass = glass.Contains(cell),
                    Structure = next.Spec?.terrain != null ? structure.Contains(cell) : cell.y > ground, Ring = ring });
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
                t.gameObject.SetActive(true);
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

        public void HideEntity(string id)
        {
            if (id != null && _views.TryGetValue(id, out var view) && view != null)
                view.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_world != null) RefreshOcclusion(Camera.main);
        }

        /// <summary>仅剔除 YOU 前方圆形范围内的表面，保留圆外地形和实际碰撞。</summary>
        public void RefreshOcclusion(Camera camera)
        {
            string youId = _world?.ActorId;
            Transform target = null;
            bool hasTarget = isActiveAndEnabled && camera != null && youId != null
                && _views.TryGetValue(youId, out target) && target != null && target.gameObject.activeInHierarchy;
            float radius = (config != null ? config.occlusionRadiusCells : 0.9f) * CellSize;
            hasTarget &= radius > 0f;
            foreach (var renderer in _terrain) ApplyCutaway(renderer, hasTarget, target, radius);
            foreach (var pair in _entityRenderers)
            {
                // 规则词牌始终保留，避免圆形开口隐藏规则来源。
                var entity = _world?.Entity(pair.Key);
                ApplyCutaway(pair.Value, hasTarget && entity?.Kind == EntityKind.Object
                    && !(_world.Props(entity).Contains("YOU")),
                    target, radius);
            }
        }

        void ApplyCutaway(Renderer renderer, bool active, Transform target, float radius)
        {
            if (renderer == null) return;
            var source = _baseMaterials[renderer];
            float alpha = _baseAlpha[renderer];
            if (alpha < 0.999f) source = TransparentMaterial(source, alpha);
            var chosen = source;
            if (active)
            {
                if (_cutawayShader == null)
                    _cutawayShader = Resources.Load<Shader>("RuleWorkshop/PlayerCutaway");
                if (_cutawayShader == null || !_cutawayShader.isSupported) return;
                if (!_cutawayMaterials.TryGetValue(source, out var material))
                {
                    material = new Material(source) { name = source.name + " 玩家局部剖切" };
                    material.shader = _cutawayShader;
                    _cutawayMaterials[source] = material;
                }
                chosen = material;
            }
            if (renderer.sharedMaterial != chosen) renderer.sharedMaterial = chosen;
            if (_cutawayBlock == null) _cutawayBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_cutawayBlock);
            _cutawayBlock.SetVector(CutawayTarget, target != null ? (Vector4)target.position : Vector4.zero);
            // 使用动画中的脚底高度，合并地形盒也只裁切玩家同层及上方部分。
            _cutawayBlock.SetFloat(CutawayMinHeight, target != null ? target.position.y - 0.5f * CellSize : 0f);
            _cutawayBlock.SetFloat(CutawayRadius, active ? radius : 0f);
            _cutawayBlock.SetFloat(CutawayFeather, 0.06f * CellSize);
            _cutawayBlock.SetFloat(CutawayDepthBias, 0.05f * CellSize);
            renderer.SetPropertyBlock(_cutawayBlock);
        }

        void OnDisable() => RefreshOcclusion(null);

        void AddEntity(EntityState e, WorldModel world)
        {
            var t = CreateEntity(e, world);
            _views[e.Id] = t;
            _entityRenderers[e.Id] = t.GetComponent<Renderer>();
            UpdateEntityVisual(e, world, t);
        }

        Transform CreateEntity(EntityState e, WorldModel world)
        {
            var t = CreateCube(e.Id, e.Cell, MaterialFor(e), 1f);
            if (e.Kind == EntityKind.Text)
            {
                // 阴影不应把类别底色压暗到无法衬托正文；物件与地形仍接收阴影。
                t.GetComponent<Renderer>().receiveShadows = false;
                var label = new GameObject("Label");
                label.transform.SetParent(t, false);
                label.transform.localPosition = Vector3.up * 0.55f;
                var tm = label.AddComponent<TextMesh>();
                tm.text = e.Token;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.characterSize = 0.12f;
                tm.fontSize = 32;
                tm.color = WordInk;
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
            var renderer = t.GetComponent<Renderer>();
            Register(renderer, MaterialFor(e), 1f);
            var filter = t.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = e.Kind == EntityKind.Object && e.Subject == "WALL" ? WallMesh() : _cubeMesh;
            t.localScale = Vector3.one * CellSize * (e.Kind == EntityKind.Text ? 0.92f : 1f);
            if (e.Kind == EntityKind.Text)
            {
                var label = t.GetComponentInChildren<TextMesh>();
                if (label != null)
                {
                    if (label.text != e.Token) label.text = e.Token;
                    label.color = WordInk;
                }
            }
            else
            {
                var marker = t.Find("YouMarker");
                bool isYou = world.Props(e).Contains("YOU");
                if (isYou && marker == null)
                {
                    var go = new GameObject("YouMarker");
                    marker = go.transform;
                    marker.SetParent(t, false);
                    marker.localPosition = Vector3.up * 0.7f;
                    var text = go.AddComponent<TextMesh>();
                    text.text = "YOU";
                    text.anchor = TextAnchor.MiddleCenter;
                    text.alignment = TextAlignment.Center;
                    text.characterSize = 0.1f;
                    text.fontSize = 32;
                    go.AddComponent<CameraBillboard>();
                }
                if (marker != null)
                {
                    marker.gameObject.SetActive(isYou);
                    var text = marker.GetComponent<TextMesh>();
                    if (text != null) text.color = config != null ? config.youTint : new Color(1f, 0.35f, 0.25f);
                }
            }
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

        Material LavaMaterial()
        {
            if (config != null && config.lavaMaterial != null) return config.lavaMaterial;
            if (_lavaMaterial == null)
            {
                _lavaMaterial = new Material(FallbackMaterial()) { name = "LAVA 橙红色" };
                _lavaMaterial.color = new Color(0.92f, 0.22f, 0.06f);
                _lavaMaterial.EnableKeyword("_EMISSION");
                _lavaMaterial.SetColor("_EmissionColor", new Color(0.35f, 0.035f, 0f));
            }
            return _lavaMaterial;
        }

        float CellSize => config != null ? config.cellSize : 1f;
        // GUI/Text Shader 直接使用顶点色；仅在这一输入边界转换，配置仍保存 sRGB。
        Color WordInk => config == null ? Color.black : QualitySettings.activeColorSpace == ColorSpace.Linear
            ? config.wordInk.linear : config.wordInk;
        // 只给已有地形盒分配颜色，不拆模型，也不按关卡名或物件属性猜用途。
        static int GroundHeight(WorldModel world)
        {
            int lowest = int.MaxValue;
            foreach (var cell in world.Terrain) lowest = Math.Min(lowest, cell.y);
            return lowest == int.MaxValue ? 0 : lowest;
        }

        Material TerrainMat(bool structure = false) => config == null ? null
            : structure && config.structureMaterial != null ? config.structureMaterial : config.terrainMaterial;
        Material GlassMat() => config != null && config.pinkHollowMaterial != null ? config.pinkHollowMaterial : TerrainMat();

        Material MaterialFor(EntityState e)
        {
            if ((e.Kind == EntityKind.Object && e.Subject == "LAVA")
                || (e.Kind == EntityKind.Text && e.Token == "LAVA")) return LavaMaterial();
            if (config == null) return null;
            if (e.Kind == EntityKind.Text)
            {
                if (Tokens.IsSubject(e.Token)) return SubjectMaterial(e.Token);
                if (Tokens.IsOperator(e.Token)) return config.operatorTextMaterial != null ? config.operatorTextMaterial : config.textMaterial;
                if (Tokens.IsProp(e.Token)) return config.propertyTextMaterial != null ? config.propertyTextMaterial : config.textMaterial;
                return config.textMaterial;
            }
            return SubjectMaterial(e.Subject);
        }

        // 名词词牌与物件共用身份材质，后续调色也保持一致。
        Material SubjectMaterial(string subject)
        {
            switch (subject)
            {
                case "ROBOT": return config.redMaterial;
                case "ROCK": return config.blueMaterial;
                case "FLAG": return config.pinkMaterial;
                case "CLOUD": return config.cloudMaterial != null ? config.cloudMaterial : config.blueMaterial;
                case "SPRING": return config.springMaterial != null ? config.springMaterial : config.pinkMaterial;
                case "WALL": return config.wallMaterial != null ? config.wallMaterial : config.terrainMaterial;
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
            foreach (var material in _cutawayMaterials.Values)
                if (material != null) DestroyMaterial(material);
            _cutawayMaterials.Clear();
            foreach (var levels in _transparentMaterials.Values)
            foreach (var material in levels.Values)
                if (material != null) DestroyMaterial(material);
            _transparentMaterials.Clear();
            if (_fallbackMaterial != null) DestroyMaterial(_fallbackMaterial);
            if (_lavaMaterial != null) DestroyMaterial(_lavaMaterial);
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
