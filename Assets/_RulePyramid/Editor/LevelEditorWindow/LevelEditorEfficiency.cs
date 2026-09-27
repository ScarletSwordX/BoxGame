using System;
using System.Collections.Generic;
using System.Linq;
using RulePyramid.Core;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        bool TerrainEnabled => (_categories & EditorCategory.Terrain)!=0;
        bool RequiresCategories => _brush==EditorBrush.Select || _brush==EditorBrush.Erase || _brush==EditorBrush.Eyedropper;
        bool Eligible(EntityState entity) => EditorPicking.Eligible(_categories,entity.Kind,entity.Id,_locked);

        void DrawCategoryToolbar()
        {
            using(new EditorGUI.DisabledScope(Playing || _dragging))
            using(new EditorGUILayout.HorizontalScope())
            {
                var mask=_categories;
                var categories=new[]{EditorCategory.Terrain,EditorCategory.Object,EditorCategory.Text};
                var labels=new[]{"地形","物体","词牌"};
                for(int i=0;i<categories.Length;i++)
                {
                    bool enabled=GUILayout.Toggle((mask & categories[i])!=0,labels[i]);
                    mask=enabled?mask|categories[i]:mask & ~categories[i];
                    if(GUILayout.Button(new GUIContent("仅此","仅选择"+labels[i]),GUILayout.Width(38))) mask=categories[i];
                }
                if(GUILayout.Button("全部",GUILayout.Width(42))) mask=EditorCategory.All;
                if(mask!=_categories) SetCategories(mask);
                if(GUILayout.Button(new GUIContent("全选地块","选中当前阶段地图所有高度层的地形、物体和词牌（跳过已锁定物体），并切换到选择工具。"),GUILayout.Width(76))) SelectAllMap();
            }
        }

        void SelectAllMap()
        {
            if(Playing || _dragging || _session==null) return;
            SetBrush(EditorBrush.Select);
            _categories=EditorCategory.All;
            _selected.Clear();
            _selected.UnionWith(VisibleEntities.Where(Eligible).Select(e=>e.Id));
            _selectedTerrain.Clear();
            _selectedTerrain.UnionWith(LevelCloner.ExpandTerrain(_session.Draft.terrain));
            int lockedCount=VisibleEntities.Count(e=>_locked.Contains(e.Id));
            _message="已全选当前地图：地形 "+_selectedTerrain.Count+" 格，物体 / 词牌 "+_selected.Count+" 个。可拖动三维坐标轴，或使用右侧整组偏移移动。";
            if(lockedCount>0) _message+=" 已跳过 "+lockedCount+" 个锁定对象。";
            Repaint();
        }
        void SetCategories(EditorCategory mask)
        {
            _categories=mask & EditorCategory.All;
            PruneSelection();
            _message=_categories==EditorCategory.None?"未勾选任何类别：选取、吸管和擦除停用；放置笔刷仍可使用。":"选择过滤："+_categories;
            Repaint();
        }

        void PruneSelection()
        {
            var valid=new HashSet<string>(VisibleEntities.Where(Eligible).Select(e=>e.Id));
            _selected.RemoveWhere(id=>!valid.Contains(id));
            if(!TerrainEnabled) _selectedTerrain.Clear();
        }

        void SetBrush(EditorBrush brush)
        {
            EditorCommand command;
            switch(brush)
            {
                case EditorBrush.Select: command=EditorCommand.Select; break;
                case EditorBrush.Terrain: command=EditorCommand.Terrain; break;
                case EditorBrush.Box: command=EditorCommand.Box; break;
                case EditorBrush.Object: command=EditorCommand.Object; break;
                case EditorBrush.Text: command=EditorCommand.Text; break;
                case EditorBrush.Erase: command=EditorCommand.Erase; break;
                case EditorBrush.Eyedropper: command=EditorCommand.Eyedropper; break;
                default: if(!Playing && !_dragging) { _brush=brush; _ghost=null; ClearDragPreview(); } return;
            }
            ExecuteEditorCommand(command);
        }

        void ExecuteEditorCommand(EditorCommand command)
        {
            if(Playing || (_dragging && command!=EditorCommand.Cancel)) return;
            switch(command)
            {
                case EditorCommand.Cancel: CancelDrag(); return;
                case EditorCommand.Delete: DeleteSelection(); return;
                case EditorCommand.Focus: FocusSelection(); return;
                case EditorCommand.FrameAll: FrameAll(); return;
                case EditorCommand.Select: _brush=EditorBrush.Select; break;
                case EditorCommand.Terrain: _brush=EditorBrush.Terrain; break;
                case EditorCommand.Box: _brush=EditorBrush.Box; break;
                case EditorCommand.Object: _brush=EditorBrush.Object; break;
                case EditorCommand.Text: _brush=EditorBrush.Text; break;
                case EditorCommand.Erase: _brush=EditorBrush.Erase; break;
                case EditorCommand.Eyedropper: _brush=EditorBrush.Eyedropper; break;
                default: return;
            }
            _ghost=null; ClearDragPreview();
            _message=_brush==EditorBrush.Eyedropper?"吸管 [R]：点击单格取样；空白保留当前笔刷参数。":"当前工具："+_brush;
            Repaint();
        }

        void SampleCell(GridCell cell)
        {
            var candidates=EditorPicking.Candidates(_session.Draft,cell,_categories,_locked).ToArray();
            if(candidates.Length==0) { _message="此格没有可取样内容："+DisplayCell(cell); return; }
            if(candidates.Length==1) { ApplySample(candidates[0]); return; }
            var menu=new GenericMenu();
            foreach(var candidate in candidates)
            {
                var sample=candidate;
                menu.AddItem(new GUIContent(sample.Category+" · "+sample.Value+" · "+sample.EntityId),false,()=>ApplySample(sample));
            }
            menu.ShowAsContext();
        }

        void ApplySample(EditorSample sample)
        {
            if(Playing) return;
            if(sample.Category==EditorCategory.Terrain) { _appearance=sample.Value; _subtract=false; SetBrush(EditorBrush.Terrain); }
            else if(sample.Category==EditorCategory.Object) { _subject=sample.Value; SetBrush(EditorBrush.Object); }
            else if(sample.Category==EditorCategory.Text) { _word=sample.Value; SetBrush(EditorBrush.Text); }
            _message="已取样："+sample.Category+" · "+sample.Value;
            Repaint();
        }

        void EraseCells(IEnumerable<GridCell> cells)
        {
            var hit=new HashSet<GridCell>(cells);
            var ids=VisibleEntities.Where(e=>hit.Contains(e.Cell) && Eligible(e)).Select(e=>e.Id).ToArray();
            var terrain=TerrainEnabled?hit.Where(c=>Inspect?.Terrain.Contains(c)==true).ToArray():Array.Empty<GridCell>();
            if(ids.Length+terrain.Length==0) return;
            Edit("擦除",d=>
            {
                d.entities=d.entities.Where(e=>!ids.Contains(e.id)).ToArray();
                foreach(var cell in terrain) AuthoringOperations.RemoveTerrain(d,cell,cell);
            });
        }

        string DescribeCell(GridCell cell)
        {
            var parts=VisibleEntities.Where(e=>e.Cell==cell).Select(e=>(e.Kind==EntityKind.Text?e.Token:e.Subject)+(_locked.Contains(e.Id)?"（锁定）":"")).ToList();
            if(Inspect?.Terrain.Contains(cell)==true) parts.Insert(0,"地形");
            return DisplayCell(cell)+" · "+(parts.Count==0?"空白":string.Join(" / ",parts));
        }

        void FitGrid(GridCellBox target)
        {
            if(_gridViewport.width<=0 || _gridViewport.height<=0) return;
            var fit=EditorCanvasMath.Fit(_gridViewport,_session.Draft.bounds,target);
            _zoom=fit.Zoom; _pan=fit.Pan;
        }

        void FocusSelection()
        {
            PruneSelection();
            var cells=VisibleEntities.Where(e=>_selected.Contains(e.Id)).Select(e=>e.Cell).Concat(_selectedTerrain).ToArray();
            if(cells.Length==0) { _message="先选择需要聚焦的内容。"; return; }
            var bounds=Box(cells[0],cells[0]);
            foreach(var cell in cells) { bounds=Box(new GridCell(Math.Min(bounds.min.x,cell.x),Math.Min(bounds.min.y,cell.y),Math.Min(bounds.min.z,cell.z)),new GridCell(Math.Max(bounds.max.x,cell.x),Math.Max(bounds.max.y,cell.y),Math.Max(bounds.max.z,cell.z))); }
            if(!cells.Any(c=>c.y==_session.CurrentY)) _session.CurrentY=bounds.min.y;
            _autoFit=false; _focusBounds=bounds; _focusPending=true; FitGrid(bounds); _preview?.Frame(bounds); Repaint();
        }
    }
}
