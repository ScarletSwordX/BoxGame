using System;
using System.Collections.Generic;
using RulePyramid.Core;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    /// <summary>
    /// 独立的关卡预览：所有网格只交给 PreviewRenderUtility，不向用户场景添加对象。
    /// 鼠标坐标使用承载此预览的 IMGUI 局部坐标。
    /// </summary>
    public sealed class AuthoringPreview3D : IDisposable
    {
        [Serializable]
        public struct ViewState
        {
            public Vector3 Target;
            public float Yaw;
            public float Pitch;
            public float Size;
            public int Slot;
            public bool HasFrame;
        }

        struct HitBox
        {
            public Bounds Bounds;
            public GridCell Cell;
            public string EntityId;
            public bool Glass;
            public EditorCategory Category;
        }

        readonly List<HitBox> _hitBoxes = new List<HitBox>();
        readonly List<KeyValuePair<Vector3, string>> _labels = new List<KeyValuePair<Vector3, string>>();
        readonly List<KeyValuePair<Vector3, string>> _previewLabels = new List<KeyValuePair<Vector3, string>>();
        readonly List<EntityState> _previewEntities = new List<EntityState>();
        readonly HashSet<GridCell> _previewTerrain = new HashSet<GridCell>();
        PreviewRenderUtility _preview;
        Mesh _cube;
        Material _terrainMaterial;
        Material _glassMaterial;
        Material _objectMaterial;
        Material _textMaterial;
        Material _selectedMaterial;
        Material _validPreviewMaterial;
        Material _invalidPreviewMaterial;
        Rect _rect;
        GridCellBox _bounds;
        Vector3 _target;
        float _yaw = 45f;
        float _pitch = 35.264f;
        float _size = 10f;
        int _slot;
        int _currentY;
        bool _surfacePlacement;
        bool _erase;
        bool _hasFrame;
        bool _disposed;
        bool _previewValid;

        public int Slot => _slot;
        /// <summary>开启后可以穿透石质地形选取后方实体。</summary>
        public bool ThroughSelect { get; set; }
        public ISet<GridCell> SelectedTerrainCells { get; set; }

        /// <summary>缓存拖动中的落点。预览内容不参与命中，也不改动关卡和传入的实体。</summary>
        public void SetSelectionPreview(IReadOnlyList<EntityState> entities, IEnumerable<GridCell> terrainCells, bool valid)
        {
            _previewEntities.Clear();
            if (entities != null)
                foreach (var entity in entities)
                    if (entity != null) _previewEntities.Add(entity.Clone());
            _previewTerrain.Clear();
            if (terrainCells != null)
                foreach (var cell in terrainCells) _previewTerrain.Add(cell);
            _previewValid = valid;
        }

        public AuthoringPreview3D()
        {
            try
            {
                _preview = new PreviewRenderUtility();
                _preview.camera.orthographic = true;
                _preview.camera.nearClipPlane = 0.01f;
                _preview.camera.farClipPlane = 1000f;
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(0.105f, 0.13f, 0.16f);
                _preview.ambientColor = new Color(0.75f, 0.8f, 0.86f);
                _preview.lights[0].intensity = 1.2f;
                _preview.lights[0].transform.rotation = Quaternion.Euler(45f, -40f, 0f);
                _preview.lights[1].intensity = 0.45f;
                _preview.lights[1].transform.rotation = Quaternion.Euler(340f, 110f, 0f);
                _cube = CreateCube();
                _terrainMaterial = CreateMaterial(new Color(0.48f, 0.54f, 0.6f));
                _terrainMaterial.SetFloat("_WorldGrid", 1f);
                _glassMaterial = CreateTransparentMaterial(new Color(0.35f, 0.9f, 0.95f, 0f));
                _glassMaterial.SetFloat("_WorldGrid", 1f);
                _glassMaterial.SetColor("_EdgeColor", new Color(0.35f, 0.9f, 0.95f, 0.8f));
                _glassMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                _glassMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                _objectMaterial = CreateMaterial(new Color(0.22f, 0.68f, 0.87f));
                _textMaterial = CreateMaterial(new Color(0.84f, 0.57f, 0.27f));
                _selectedMaterial = CreateMaterial(new Color(1f, 0.86f, 0.23f));
                _validPreviewMaterial = CreateTransparentMaterial(new Color(0.2f, 0.96f, 0.88f, 0.42f));
                _invalidPreviewMaterial = CreateTransparentMaterial(new Color(1f, 0.25f, 0.23f, 0.42f));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ViewState CaptureView()
        {
            return new ViewState
            {
                Target = _target,
                Yaw = _yaw,
                Pitch = _pitch,
                Size = _size,
                Slot = _slot,
                HasFrame = _hasFrame
            };
        }

        public void RestoreView(ViewState state)
        {
            _target = state.Target;
            _yaw = state.Yaw;
            _pitch = Mathf.Clamp(state.Pitch, 8f, 82f);
            _size = Mathf.Clamp(state.Size, 1.5f, 200f);
            _slot = ((state.Slot % 4) + 4) % 4;
            _hasFrame = state.HasFrame;
        }

        public void Frame(GridCellBox bounds)
        {
            if (bounds == null) return;
            _bounds = bounds;
            var min = ToMin(bounds.min);
            var max = ToMax(bounds.max);
            _target = (min + max) * 0.5f;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;
            var half = (max - min) * 0.5f;
            float horizontal = Mathf.Abs(right.x) * half.x + Mathf.Abs(right.y) * half.y + Mathf.Abs(right.z) * half.z;
            float vertical = Mathf.Abs(up.x) * half.x + Mathf.Abs(up.y) * half.y + Mathf.Abs(up.z) * half.z;
            float aspect = _rect.height > 0f ? Mathf.Max(0.01f, _rect.width / _rect.height) : 1f;
            _size = Mathf.Max(1.5f, Mathf.Max(vertical, horizontal / aspect) * 1.15f);
            _hasFrame = true;
        }

        public void Focus(GridCell cell)
        {
            _target = Center(cell);
            _hasFrame = true;
        }

        public void RotateSlot(int slot)
        {
            _slot = ((slot % 4) + 4) % 4;
            _yaw = 45f + 90f * _slot;
            _pitch = 35.264f;
        }

        /// <summary>返回 true 表示已消费本次镜头操作。</summary>
        public bool ProcessNavigation(Event e, Rect rect)
        {
            if (e == null || !rect.Contains(e.mousePosition)) return false;
            if (e.type == EventType.ScrollWheel)
            {
                _size = Mathf.Clamp(_size * Mathf.Exp(e.delta.y * 0.08f), 1.5f, 200f);
                e.Use();
                return true;
            }

            if (e.type != EventType.MouseDrag) return false;
            if (e.button == 0 && e.alt)
            {
                _yaw += e.delta.x * 0.55f;
                _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.4f, 8f, 82f);
                _slot = Mathf.RoundToInt((_yaw - 45f) / 90f) & 3;
            }
            else if (e.button == 2)
            {
                var camera = _preview.camera.transform;
                var scale = 2f * _size / Mathf.Max(1f, rect.height);
                _target -= camera.right * (e.delta.x * scale);
                _target += camera.up * (e.delta.y * scale);
            }
            else return false;

            e.Use();
            return true;
        }

        public void Draw(
            Rect rect,
            LevelDefinition level,
            IReadOnlyList<EntityState> liveEntities,
            ISet<string> selectedIds,
            GridCellBox region,
            GridCellBox ghost,
            int currentY,
            bool clipAbove,
            bool surfacePlacement,
            bool erase)
        {
            if (_disposed || level == null || level.bounds == null || rect.width < 2f || rect.height < 2f)
                return;

            UpdatePicking(rect, level, liveEntities, currentY, clipAbove, surfacePlacement, erase);
            if (Event.current.type != EventType.Repaint) return;

            Texture image = null;
            bool began = false;
            try
            {
                _preview.BeginPreview(rect, GUIStyle.none);
                began = true;
                DrawTerrain(level, clipAbove);
                DrawEntities(level, liveEntities, selectedIds, clipAbove);
                DrawSelectionPreview();
                _preview.Render();
            }
            finally
            {
                if (began) image = _preview.EndPreview();
            }
            if (image != null) GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);

            GUI.BeginGroup(rect);
            DrawOverlay(level, region, ghost, currentY, clipAbove, erase);
            DrawLabels();
            DrawPreviewLabels();
            GUI.Label(new Rect(8f, 6f, 210f, 18f), "编辑辅助标签 · 非玩家视角", EditorStyles.miniLabel);
            GUI.EndGroup();
        }

        /// <summary>更新镜头和可选范围；无需 GUI 上下文，供悬停检查及编辑器测试使用。</summary>
        public void UpdatePicking(Rect rect, LevelDefinition level, IReadOnlyList<EntityState> liveEntities,
            int currentY, bool clipAbove, bool surfacePlacement, bool erase)
        {
            if (_disposed || level == null || level.bounds == null) return;
            _rect = rect;
            _bounds = level.bounds;
            _currentY = currentY;
            _surfacePlacement = surfacePlacement;
            _erase = erase;
            if (!_hasFrame) Frame(level.bounds);
            ConfigureCamera(rect);
            BuildHitBoxes(level, liveEntities, clipAbove);
        }

        public Vector2 ProjectCellCenter(GridCell cell) => Project(Center(cell)) + _rect.position;
        public Vector2 ProjectWorldPoint(Vector3 point) => Project(point) + _rect.position;

        /// <summary>
        /// entityPick=true 时优先命中可见实体，空白处回退当前层格。
        /// entityPick=false 时返回绘制格；表面模式返回被命中面的相邻格，擦除时返回命中格。
        /// </summary>
        public bool TryPick(Vector2 mousePosition, bool entityPick, out GridCell cell, out string entityId)
        {
            cell = default(GridCell);
            entityId = null;
            if (_disposed || _bounds == null || !_rect.Contains(mousePosition)) return false;
            Ray ray = MakeRay(mousePosition);
            float nearest = float.PositiveInfinity;
            float nearestStone = float.PositiveInfinity;
            int nearestIndex = -1;
            for (int i = 0; i < _hitBoxes.Count; i++)
            {
                var hit = _hitBoxes[i];
                if (!hit.Bounds.IntersectRay(ray, out float distance)) continue;
                if (entityPick && string.IsNullOrEmpty(hit.EntityId))
                {
                    if (!hit.Glass) nearestStone = Mathf.Min(nearestStone, distance);
                    continue;
                }
                if (distance >= nearest) continue;
                nearest = distance;
                nearestIndex = i;
            }

            if (entityPick)
            {
                if (nearestIndex >= 0 && (ThroughSelect || nearest <= nearestStone + 0.001f))
                {
                    cell = _hitBoxes[nearestIndex].Cell;
                    entityId = _hitBoxes[nearestIndex].EntityId;
                    return true;
                }
                if (_erase && _surfacePlacement)
                    return TryPick(mousePosition, false, out cell, out entityId);
                return TryPickPlane(ray, out cell);
            }

            if (_surfacePlacement && nearestIndex >= 0)
            {
                var box = _hitBoxes[nearestIndex].Bounds;
                var point = ray.GetPoint(nearest);
                var normal = SurfaceNormal(box, point);
                cell = FloorCell(point + normal * (_erase ? -0.02f : 0.02f));
            }
            else return TryPickPlane(ray, out cell);

            return Inside(_bounds, cell);
        }

        /// <summary>按编辑类别拾取；被过滤或锁定的实体不会命中，也不会遮挡后方内容。</summary>
        public bool TryPickFiltered(Vector2 mousePosition, EditorCategory categories, ISet<string> locked,
            out GridCell cell, out string entityId)
        {
            cell = default(GridCell);
            entityId = null;
            if (_disposed || _bounds == null || !_rect.Contains(mousePosition)) return false;

            var ray = MakeRay(mousePosition);
            float nearestEntity = float.PositiveInfinity;
            float nearestTerrain = float.PositiveInfinity;
            float nearestStone = float.PositiveInfinity;
            int entityIndex = -1;
            int terrainIndex = -1;
            for (int i = 0; i < _hitBoxes.Count; i++)
            {
                var hit = _hitBoxes[i];
                if ((categories & hit.Category) == 0
                    || (!string.IsNullOrEmpty(hit.EntityId) && locked != null && locked.Contains(hit.EntityId))
                    || !hit.Bounds.IntersectRay(ray, out float distance)) continue;

                if (hit.Category == EditorCategory.Terrain)
                {
                    if (!hit.Glass) nearestStone = Mathf.Min(nearestStone, distance);
                    if (distance < nearestTerrain) { nearestTerrain = distance; terrainIndex = i; }
                }
                else if (distance < nearestEntity)
                {
                    nearestEntity = distance;
                    entityIndex = i;
                }
            }

            if (entityIndex >= 0 && (ThroughSelect || nearestEntity <= nearestStone + 0.001f))
            {
                var hit = _hitBoxes[entityIndex];
                cell = hit.Cell;
                entityId = hit.EntityId;
                return true;
            }
            if (terrainIndex >= 0)
            {
                var box = _hitBoxes[terrainIndex].Bounds;
                var point = ray.GetPoint(nearestTerrain + 0.001f);
                var surfaceCell = FloorCell(point);
                var min = box.min;
                var max = box.max;
                cell = new GridCell(Mathf.Clamp(surfaceCell.x, Mathf.FloorToInt(min.x), Mathf.CeilToInt(max.x) - 1),
                    Mathf.Clamp(surfaceCell.y, Mathf.FloorToInt(min.y), Mathf.CeilToInt(max.y) - 1),
                    Mathf.Clamp(surfaceCell.z, Mathf.FloorToInt(min.z), Mathf.CeilToInt(max.z) - 1));
                return Inside(_bounds, cell);
            }
            return TryPickPlane(ray, out cell);
        }

        /// <summary>直接命中地形表面所在格，适用于从立方体或地形盒选取地形。</summary>
        public bool TryPickTerrain(Vector2 mousePosition, out GridCell cell)
        {
            cell = default(GridCell);
            if (_disposed || _bounds == null || !_rect.Contains(mousePosition)) return false;
            var ray = MakeRay(mousePosition);
            float nearest = float.PositiveInfinity;
            Bounds nearestBounds = default(Bounds);
            bool found = false;
            foreach (var hit in _hitBoxes)
            {
                if (!string.IsNullOrEmpty(hit.EntityId) || !hit.Bounds.IntersectRay(ray, out float distance)
                    || distance >= nearest) continue;
                nearest = distance;
                nearestBounds = hit.Bounds;
                found = true;
            }
            if (!found) return false;
            var point = ray.GetPoint(nearest + 0.001f);
            cell = FloorCell(point);
            // 光线恰好落在盒面边界时，浮点误差不应把格子推到相邻空格。
            var min = nearestBounds.min;
            var max = nearestBounds.max;
            cell = new GridCell(Mathf.Clamp(cell.x, Mathf.FloorToInt(min.x), Mathf.CeilToInt(max.x) - 1),
                Mathf.Clamp(cell.y, Mathf.FloorToInt(min.y), Mathf.CeilToInt(max.y) - 1),
                Mathf.Clamp(cell.z, Mathf.FloorToInt(min.z), Mathf.CeilToInt(max.z) - 1));
            return Inside(_bounds, cell);
        }

        /// <summary>拖动时固定起点所在的水平层；越界落点照常返回，供无效预览显示。</summary>
        public bool TryPickDragPlane(Vector2 mousePosition, int y, out GridCell cell)
        {
            cell = default(GridCell);
            if (_disposed || _bounds == null || !_rect.Contains(mousePosition)) return false;
            var plane = new Plane(Vector3.up, new Vector3(0f, y + 0.5f, 0f));
            if (!plane.Raycast(MakeRay(mousePosition), out float distance)) return false;
            var point = MakeRay(mousePosition).GetPoint(distance);
            cell = new GridCell(Mathf.FloorToInt(point.x), y, Mathf.FloorToInt(point.z));
            return true;
        }

        bool TryPickPlane(Ray ray, out GridCell cell)
        {
            cell = default(GridCell);
            var plane = new Plane(Vector3.up, new Vector3(0f, _currentY + 0.5f, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;
            var point = ray.GetPoint(distance);
            cell = new GridCell(Mathf.FloorToInt(point.x), _currentY, Mathf.FloorToInt(point.z));
            return Inside(_bounds, cell);
        }

        void ConfigureCamera(Rect rect)
        {
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var camera = _preview.camera;
            camera.transform.rotation = rotation;
            camera.transform.position = _target - rotation * Vector3.forward * Mathf.Max(30f, _size * 4f);
            camera.aspect = rect.width / rect.height;
            camera.orthographicSize = _size;
        }

        Ray MakeRay(Vector2 mouse)
        {
            float u = (mouse.x - _rect.x) / _rect.width;
            float v = (mouse.y - _rect.y) / _rect.height;
            var camera = _preview.camera;
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            var transform = camera.transform;
            var origin = transform.position + transform.right * ((u * 2f - 1f) * halfWidth)
                + transform.up * ((1f - v * 2f) * halfHeight);
            return new Ray(origin, transform.forward);
        }

        void BuildHitBoxes(LevelDefinition level, IReadOnlyList<EntityState> liveEntities, bool clipAbove)
        {
            _hitBoxes.Clear();
            if (level.terrain != null)
            {
                foreach (var terrain in level.terrain)
                {
                    if (terrain == null || !TryVisibleBox(terrain, clipAbove, out Bounds box)) continue;
                    _hitBoxes.Add(new HitBox { Bounds = box, Cell = terrain.min,
                        Glass = IsGlass(terrain), Category = EditorCategory.Terrain });
                }
            }

            if (liveEntities != null)
            {
                foreach (var entity in liveEntities)
                {
                    if (entity == null || (clipAbove && entity.Cell.y > _currentY)) continue;
                    _hitBoxes.Add(new HitBox
                    {
                        Bounds = new Bounds(Center(entity.Cell), Vector3.one * 0.8f),
                        Cell = entity.Cell,
                        EntityId = entity.Id,
                        Category = entity.Kind == EntityKind.Text ? EditorCategory.Text : EditorCategory.Object
                    });
                }
            }
            else if (level.entities != null)
            {
                foreach (var entity in level.entities)
                {
                    if (entity == null || (clipAbove && entity.cell.y > _currentY)) continue;
                    _hitBoxes.Add(new HitBox
                    {
                        Bounds = new Bounds(Center(entity.cell), Vector3.one * 0.8f),
                        Cell = entity.cell,
                        EntityId = entity.id,
                        Category = string.Equals(entity.kind, "Text", StringComparison.OrdinalIgnoreCase)
                            ? EditorCategory.Text : EditorCategory.Object
                    });
                }
            }
        }

        void DrawTerrain(LevelDefinition level, bool clipAbove)
        {
            if (level.terrain == null) return;
            foreach (var terrain in level.terrain)
            {
                if (terrain == null || !TryVisibleBox(terrain, clipAbove, out Bounds box)) continue;
                _preview.DrawMesh(_cube, Matrix4x4.TRS(box.center, Quaternion.identity, box.size), IsGlass(terrain) ? _glassMaterial : _terrainMaterial, 0);
            }
        }

        void DrawEntities(LevelDefinition level, IReadOnlyList<EntityState> liveEntities, ISet<string> selectedIds, bool clipAbove)
        {
            _labels.Clear();
            if (liveEntities != null)
            {
                foreach (var entity in liveEntities)
                {
                    if (entity == null || (clipAbove && entity.Cell.y > _currentY)) continue;
                    bool isText = entity.Kind == EntityKind.Text;
                    DrawEntity(entity.Cell, entity.Id, isText, isText ? entity.Token : entity.Subject, selectedIds);
                }
            }
            else if (level.entities != null)
            {
                foreach (var entity in level.entities)
                {
                    if (entity == null || (clipAbove && entity.cell.y > _currentY)) continue;
                    bool isText = string.Equals(entity.kind, "Text", StringComparison.OrdinalIgnoreCase);
                    DrawEntity(entity.cell, entity.id, isText, isText ? entity.token : entity.subject, selectedIds);
                }
            }
        }

        void DrawSelectionPreview()
        {
            _previewLabels.Clear();
            var material = _previewValid ? _validPreviewMaterial : _invalidPreviewMaterial;
            foreach (var cell in _previewTerrain)
                _preview.DrawMesh(_cube, Matrix4x4.TRS(Center(cell), Quaternion.identity, Vector3.one * 0.96f), material, 0);
            foreach (var entity in _previewEntities)
            {
                bool isText = entity.Kind == EntityKind.Text;
                string label = isText ? entity.Token : entity.Subject;
                DrawEntityShape(entity.Cell, entity.Kind == EntityKind.Text,
                    label, material);
                if (!string.IsNullOrEmpty(label))
                    _previewLabels.Add(new KeyValuePair<Vector3, string>(Center(entity.Cell)
                        + Vector3.up * (isText ? 0.22f : 0.43f), label));
            }
        }

        void DrawEntity(GridCell cell, string id, bool isText, string label, ISet<string> selectedIds)
        {
            var material = selectedIds != null && selectedIds.Contains(id) ? _selectedMaterial
                : isText ? _textMaterial : _objectMaterial;
            DrawEntityShape(cell, isText, label, material);
            if (!string.IsNullOrEmpty(label))
                _labels.Add(new KeyValuePair<Vector3, string>(Center(cell) + Vector3.up * (isText ? 0.22f : 0.43f), label));
        }

        void DrawEntityShape(GridCell cell, bool isText, string label, Material material)
        {
            var size = isText ? new Vector3(0.84f, 0.28f, 0.84f) : Vector3.one * 0.7f;
            if (!isText && string.Equals(label, "WALL", StringComparison.OrdinalIgnoreCase))
            {
                var center = Center(cell);
                _preview.DrawMesh(_cube, Matrix4x4.TRS(center, Quaternion.identity, new Vector3(0.2f, 0.9f, 0.2f)), material, 0);
                foreach (float height in new[] { -0.2f, 0.18f })
                {
                    var railCenter = center + Vector3.up * height;
                    _preview.DrawMesh(_cube, Matrix4x4.TRS(railCenter, Quaternion.identity, new Vector3(0.9f, 0.12f, 0.12f)), material, 0);
                    _preview.DrawMesh(_cube, Matrix4x4.TRS(railCenter, Quaternion.identity, new Vector3(0.12f, 0.12f, 0.9f)), material, 0);
                }
            }
            else
                _preview.DrawMesh(_cube, Matrix4x4.TRS(Center(cell), Quaternion.identity, size), material, 0);
        }

        void DrawOverlay(LevelDefinition level, GridCellBox region, GridCellBox ghost, int currentY, bool clipAbove, bool erase)
        {
            var bounds = level.bounds;
            DrawWire(bounds, new Color(0.55f, 0.66f, 0.74f, 0.65f), 1.3f);
            if (level.terrain != null)
                foreach (var terrain in level.terrain)
                {
                    if (!IsGlass(terrain) || !TryVisibleBox(terrain, clipAbove, out _)) continue;
                    var visible = new GridCellBox
                    {
                        min = terrain.min,
                        max = new GridCell(terrain.max.x, clipAbove ? Mathf.Min(terrain.max.y, currentY) : terrain.max.y, terrain.max.z)
                    };
                    DrawWire(visible, new Color(0.35f, 0.9f, 0.95f, 0.85f), 1.6f);
                }
            if (currentY >= bounds.min.y && currentY <= bounds.max.y)
            {
                var layer = new GridCellBox
                {
                    min = new GridCell(bounds.min.x, currentY, bounds.min.z),
                    max = new GridCell(bounds.max.x, currentY, bounds.max.z)
                };
                DrawWire(layer, new Color(0.3f, 0.8f, 1f, 0.8f), 1.3f);
            }
            DrawWire(region, new Color(0.35f, 0.88f, 0.5f), 2f);
            DrawWire(ghost, erase ? new Color(1f, 0.3f, 0.28f) : new Color(1f, 0.87f, 0.3f), 2.5f);
            if (SelectedTerrainCells != null)
                foreach (var cell in SelectedTerrainCells)
                    DrawWire(new GridCellBox { min = cell, max = cell }, new Color(1f, 0.86f, 0.23f), 2f);
        }

        void DrawWire(GridCellBox box, Color color, float width)
        {
            if (box == null) return;
            var a = ToMin(box.min);
            var b = ToMax(box.max);
            var corners = new[]
            {
                new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z),
                new Vector3(b.x, a.y, b.z), new Vector3(a.x, a.y, b.z),
                new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z),
                new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z)
            };
            var projected = new Vector2[8];
            for (int i = 0; i < corners.Length; i++) projected[i] = Project(corners[i]);
            Handles.BeginGUI();
            var oldColor = Handles.color;
            Handles.color = color;
            for (int i = 0; i < 4; i++)
            {
                DrawClippedLine(projected[i], projected[(i + 1) % 4], width);
                DrawClippedLine(projected[i + 4], projected[(i + 1) % 4 + 4], width);
                DrawClippedLine(projected[i], projected[i + 4], width);
            }
            Handles.color = oldColor;
            Handles.EndGUI();
        }

        void DrawLabels()
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = Color.white }
            };
            foreach (var item in _labels)
            {
                var point = Project(item.Key);
                if (point.x < 0f || point.x > _rect.width || point.y < 0f || point.y > _rect.height) continue;
                if (LabelBehindStone(item.Key)) continue;
                var width = Mathf.Clamp(style.CalcSize(new GUIContent(item.Value)).x + 8f, 24f, 100f);
                GUI.Label(new Rect(point.x - width * 0.5f, point.y - 8f, width, 16f), item.Value, style);
            }
        }



        Vector2 Project(Vector3 point)
        {
            var camera = _preview.camera;
            var relative = point - camera.transform.position;
            float x = Vector3.Dot(relative, camera.transform.right) / (camera.orthographicSize * camera.aspect);
            float y = Vector3.Dot(relative, camera.transform.up) / camera.orthographicSize;
            return new Vector2((x + 1f) * 0.5f * _rect.width,
                (1f - y) * 0.5f * _rect.height);
        }

        bool LabelBehindStone(Vector3 position)
        {
            var ray = MakeRay(Project(position) + _rect.position);
            float labelDistance = Vector3.Dot(position - ray.origin, ray.direction);
            foreach (var hit in _hitBoxes)
                if (string.IsNullOrEmpty(hit.EntityId) && !hit.Glass
                    && hit.Bounds.IntersectRay(ray, out float distance) && distance < labelDistance - 0.02f)
                    return true;
            return false;
        }

        void DrawClippedLine(Vector2 start, Vector2 end, float width)
        {
            if (!ClipSegment(ref start, ref end, new Rect(0f, 0f, _rect.width, _rect.height))) return;
            Handles.DrawAAPolyLine(width, start, end);
        }

        /// <summary>Liang-Barsky 二维线段裁剪，防止长方体线框溢出预览区域。</summary>
        public static bool ClipSegment(ref Vector2 start, ref Vector2 end, Rect rect)
        {
            var delta = end - start;
            float enter = 0f, leave = 1f;
            if (!ClipEdge(-delta.x, start.x - rect.xMin, ref enter, ref leave)
                || !ClipEdge(delta.x, rect.xMax - start.x, ref enter, ref leave)
                || !ClipEdge(-delta.y, start.y - rect.yMin, ref enter, ref leave)
                || !ClipEdge(delta.y, rect.yMax - start.y, ref enter, ref leave)) return false;
            var origin = start;
            start = origin + delta * enter;
            end = origin + delta * leave;
            return true;
        }

        static bool ClipEdge(float p, float q, ref float enter, ref float leave)
        {
            if (Mathf.Approximately(p, 0f)) return q >= 0f;
            float ratio = q / p;
            if (p < 0f) enter = Mathf.Max(enter, ratio);
            else leave = Mathf.Min(leave, ratio);
            return enter <= leave;
        }

        static bool IsGlass(GridCellBox box)
        {
            return box != null && string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase);
        }

        bool TryVisibleBox(GridCellBox box, bool clipAbove, out Bounds result)
        {
            result = default(Bounds);
            if (box.max.x < box.min.x || box.max.y < box.min.y || box.max.z < box.min.z) return false;
            int maxY = clipAbove ? Mathf.Min(box.max.y, _currentY) : box.max.y;
            if (maxY < box.min.y) return false;
            var min = ToMin(box.min);
            var max = ToMax(new GridCell(box.max.x, maxY, box.max.z));
            result = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        static Vector3 SurfaceNormal(Bounds box, Vector3 point)
        {
            var local = point - box.center;
            float dx = Mathf.Abs(Mathf.Abs(local.x) - box.extents.x);
            float dy = Mathf.Abs(Mathf.Abs(local.y) - box.extents.y);
            float dz = Mathf.Abs(Mathf.Abs(local.z) - box.extents.z);
            if (dx < dy && dx < dz) return new Vector3(Mathf.Sign(local.x), 0f, 0f);
            if (dz < dy) return new Vector3(0f, 0f, Mathf.Sign(local.z));
            return new Vector3(0f, Mathf.Sign(local.y), 0f);
        }

        static GridCell FloorCell(Vector3 point)
        {
            return new GridCell(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y), Mathf.FloorToInt(point.z));
        }

        static bool Inside(GridCellBox box, GridCell cell)
        {
            return cell.x >= box.min.x && cell.x <= box.max.x
                && cell.y >= box.min.y && cell.y <= box.max.y
                && cell.z >= box.min.z && cell.z <= box.max.z;
        }

        static Vector3 Center(GridCell cell) => new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
        static Vector3 ToMin(GridCell cell) => new Vector3(cell.x, cell.y, cell.z);
        static Vector3 ToMax(GridCell cell) => new Vector3(cell.x + 1f, cell.y + 1f, cell.z + 1f);

        static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Hidden/RuleWorkshop/AuthoringBlocks");
            if (shader == null) throw new InvalidOperationException("找不到关卡编辑器方块描边 Shader。");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = color };
            return material;
        }

        void DrawPreviewLabels()
        {
            if (_previewLabels.Count == 0) return;
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = _previewValid ? new Color(0.68f, 1f, 0.96f, 0.7f)
                    : new Color(1f, 0.64f, 0.62f, 0.7f) }
            };
            foreach (var item in _previewLabels)
            {
                var point = Project(item.Key);
                if (point.x < 0f || point.x > _rect.width || point.y < 0f || point.y > _rect.height) continue;
                var width = Mathf.Clamp(style.CalcSize(new GUIContent(item.Value)).x + 8f, 24f, 100f);
                GUI.Label(new Rect(point.x - width * 0.5f, point.y - 8f, width, 16f), item.Value, style);
            }
        }

        static Material CreateTransparentMaterial(Color color)
        {
            var shader = Shader.Find("Hidden/RuleWorkshop/AuthoringBlocks");
            if (shader == null) throw new InvalidOperationException("找不到关卡编辑器方块描边 Shader。");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = color,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent };
            material.SetColor("_EdgeColor", new Color(0.08f, 0.15f, 0.18f, 0.65f));
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return material;
        }

        static Mesh CreateCube()
        {
            var mesh = new Mesh { name = "关卡编辑器预览立方体", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f),
                new Vector3(.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,-.5f), new Vector3(-.5f,.5f,-.5f), new Vector3(.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(-.5f,.5f,.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(.5f,-.5f,.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(.5f,.5f,.5f),
                new Vector3(-.5f,.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f)
            };
            var uv = new Vector2[24];
            for (int side = 0; side < 6; side++)
            {
                int first = side * 4;
                uv[first] = new Vector2(0f, 0f); uv[first + 1] = new Vector2(1f, 0f);
                uv[first + 2] = new Vector2(1f, 1f); uv[first + 3] = new Vector2(0f, 1f);
            }
            mesh.uv = uv;
            var triangles = new int[36];
            for (int side = 0; side < 6; side++)
            {
                int v = side * 4;
                int t = side * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _preview?.Cleanup();
            if (_cube != null) UnityEngine.Object.DestroyImmediate(_cube);
            if (_glassMaterial != null) UnityEngine.Object.DestroyImmediate(_glassMaterial);
            if (_terrainMaterial != null) UnityEngine.Object.DestroyImmediate(_terrainMaterial);
            if (_objectMaterial != null) UnityEngine.Object.DestroyImmediate(_objectMaterial);
            if (_textMaterial != null) UnityEngine.Object.DestroyImmediate(_textMaterial);
            if (_selectedMaterial != null) UnityEngine.Object.DestroyImmediate(_selectedMaterial);
            if (_validPreviewMaterial != null) UnityEngine.Object.DestroyImmediate(_validPreviewMaterial);
            if (_invalidPreviewMaterial != null) UnityEngine.Object.DestroyImmediate(_invalidPreviewMaterial);
        }
    }
}
