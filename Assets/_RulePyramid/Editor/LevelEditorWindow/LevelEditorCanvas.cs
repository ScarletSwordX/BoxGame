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
        bool _gestureMove, _gestureCopy, _dropValid;
        readonly HashSet<GridCell> _selectedTerrain = new HashSet<GridCell>();
        readonly List<EntityState> _movePreview = new List<EntityState>();
        readonly List<GridCell> _terrainPreview = new List<GridCell>();
        string[] _gestureIds = Array.Empty<string>();
        GridCell[] _gestureTerrain = Array.Empty<GridCell>();
        string _dropError;
        int _gestureControl;
        GridCell _lastStroke;
        IEnumerable<EntityState> VisibleEntities => Playing ? _session.Playtest.World.Entities : _session.Draft.entities.Select(e => new EntityState { Id = e.id, Kind = EntityState.ParseKind(e.kind), Subject = e.subject, Token = e.token, Cell = e.cell });
        GridCellBox ActiveRegion => _regionIndex >= 0 && _regionIndex < (_session.Draft.tutorial?.regions?.Length ?? 0) ? _session.Draft.tutorial.regions[_regionIndex].bounds : null;

        void DrawGrid(Rect rect)
        {
            _gridViewport = new Rect(0,0,rect.width,rect.height);
            var b = Playing ? _session.Playtest.Level.bounds : _session.Draft.bounds;
            if (_autoFit) FitGrid(b);
            else if (_focusPending && _focusBounds!=null) { FitGrid(_focusBounds); _focusPending=false; }
            GUI.Box(rect, GUIContent.none);
            GUI.BeginClip(rect);
            var inner = new Rect(0,0,rect.width,rect.height);
            var origin = new Vector2(24, 26) + _pan;
            Rect CellRect(GridCell c) => new Rect(origin.x + (c.x - b.min.x)*_zoom, origin.y + (b.max.z - c.z)*_zoom, Mathf.Max(.1f,_zoom-1), Mathf.Max(.1f,_zoom-1));
            var terrain = Inspect?.Terrain ?? new HashSet<GridCell>();
            if (Event.current.type == EventType.Repaint)
            {
                int x0 = Math.Max(b.min.x, b.min.x + Mathf.FloorToInt(-origin.x/_zoom));
                int x1 = Math.Min(b.max.x, b.min.x + Mathf.CeilToInt((rect.width-origin.x)/_zoom));
                int z0 = Math.Max(b.min.z, b.max.z - Mathf.CeilToInt((rect.height-origin.y)/_zoom));
                int z1 = Math.Min(b.max.z, b.max.z - Mathf.FloorToInt(-origin.y/_zoom));
                for (int x=x0; x<=x1; x++) for (int z=z0; z<=z1; z++)
                {
                    var cell = new GridCell(x,_session.CurrentY,z); var r = CellRect(cell);
                    Color color = terrain.Contains(cell) ? new Color(.35f,.39f,.43f) : new Color(.12f,.14f,.17f);
                    if (!terrain.Contains(cell) && _adjacent && terrain.Contains(cell.Add(GridCell.Down))) color = new Color(.21f,.23f,.27f);
                    EditorGUI.DrawRect(r,color);
                    if (_selectedTerrain.Contains(cell)) Outline(r,Color.yellow);
                    if (Contains(ActiveRegion,cell)) Outline(r, new Color(.3f,1f,.55f));
                    if (Contains(_ghost,cell)) Outline(r, _subtract ? Color.red : Color.cyan);
                    if (_brush!=EditorBrush.Select && _stroke.Contains(cell)) EditorGUI.DrawRect(r,new Color(.3f,.8f,.8f,.6f));
                }
            }
            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment=TextAnchor.MiddleCenter, clipping=TextClipping.Clip, fontSize=18, padding=new RectOffset(), margin=new RectOffset(), wordWrap=false, normal = { textColor = Color.black } };
            var measurements = new Dictionary<(string, int), Vector2>();
            Vector2 MeasureLabel(string text, int size)
            {
                var key=(text,size);
                if(!measurements.TryGetValue(key,out var measured))
                {
                    labelStyle.fontSize=size; measured=labelStyle.CalcSize(new GUIContent(text));
                    measurements.Add(key,measured);
                }
                return measured;
            }
            void DrawCellLabel(Rect block, string name, string prefix="", string suffix="")
            {
                float inset=Mathf.Min(2f,Mathf.Min(block.width,block.height)*.08f);
                var area=new Rect(block.x+inset,block.y+inset,Mathf.Max(0,block.width-inset*2),Mathf.Max(0,block.height-inset*2));
                var layout=EditorCellLabel.Fit(name,prefix,suffix,area.size,MeasureLabel);
                labelStyle.fontSize=layout.FontSize>0?layout.FontSize:8;
                GUI.Label(area,new GUIContent(layout.Text??"",prefix+name+suffix),labelStyle);
            }
            var cornerLabelStyle = new GUIStyle(EditorStyles.miniLabel) { clipping=TextClipping.Clip, normal = { textColor = Color.black } };
            var headerStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.black } };
            foreach (var group in VisibleEntities.Where(e => e.Cell.y == _session.CurrentY || _adjacent && Math.Abs(e.Cell.y-_session.CurrentY)==1).GroupBy(e=>e.Cell).OrderBy(g=>g.Key.y==_session.CurrentY?1:0))
            {
                var e=group.First(); var r=CellRect(e.Cell); if (!inner.Overlaps(r)) continue;
                bool current=e.Cell.y==_session.CurrentY;
                if (!current && VisibleEntities.Any(o=>o.Cell.x==e.Cell.x && o.Cell.z==e.Cell.z && o.Cell.y==_session.CurrentY)) continue;
                var tint=e.Kind==EntityKind.Text ? new Color(.85f,.72f,.35f) : new Color(.35f,.62f,.78f);
                if (!current) tint.a=.25f;
                var block=new Rect(r.x+r.width*.1f,r.y+r.height*.1f,r.width*.8f,r.height*.8f);
                if (Event.current.type==EventType.Repaint) EditorGUI.DrawRect(block,tint);
                string label=e.Kind==EntityKind.Text ? e.Token : e.Subject;
                DrawCellLabel(block,label,current?"":e.Cell.y>_session.CurrentY?"↑":"↓",group.Count()>1?" ×"+group.Count():"");
                if (group.Any(o=>_selected.Contains(o.Id))) Outline(r,Color.yellow);
                if (_zoom >= 18 && group.Any(o=>_locked.Contains(o.Id))) GUI.Label(block,"锁",cornerLabelStyle);
            }
            if ((_dragging && _gestureMove) || (_brush==EditorBrush.Object && _movePreview.Count>0))
            {
                var tint=_dropValid?new Color(.2f,1f,.85f,.42f):new Color(1f,.2f,.2f,.42f);
                foreach(var cell in _terrainPreview)
                    if(cell.y==_session.CurrentY && inner.Overlaps(CellRect(cell)))
                    { EditorGUI.DrawRect(CellRect(cell),tint); Outline(CellRect(cell),tint); }
                foreach(var e in _movePreview)
                    if(e.Cell.y==_session.CurrentY && inner.Overlaps(CellRect(e.Cell)))
                    {
                        var r=CellRect(e.Cell); EditorGUI.DrawRect(r,tint);
                        string previewLabel=e.Kind==EntityKind.Text?e.Token:e.Subject;
                        DrawCellLabel(r,previewLabel);
                        Outline(r,tint);
                    }
            }
            GUI.Label(new Rect(6,3,rect.width-12,20),"二维 XZ · +Z ↓  +X ← · Y="+_session.CurrentY,headerStyle);
            var corner=CellRect(new GridCell(b.max.x,_session.CurrentY,b.max.z));
            if(inner.Overlaps(corner))
            {
                Outline(corner,Color.cyan);
                GUI.Label(new Rect(corner.xMax-112,corner.yMax+2,112,18),"(0,"+_session.CurrentY+",0)",EditorStyles.miniBoldLabel);
            }
            var ev=Event.current;
            var mouse=ev.mousePosition;
            var picked=new GridCell(b.min.x+Mathf.FloorToInt((mouse.x-origin.x)/_zoom),_session.CurrentY,b.max.z-Mathf.FloorToInt((mouse.y-origin.y)/_zoom));
            bool inside=inner.Contains(mouse);
            if (inside && ev.type==EventType.MouseDown) { GUI.FocusControl(null); _inputFocus=true; }
            if (inside && ev.type==EventType.ScrollWheel)
            {
                _autoFit=false; _focusPending=false; float old=_zoom; _zoom=Mathf.Clamp(_zoom*Mathf.Pow(1.1f,-ev.delta.y),.1f,100);
                _pan=(mouse-new Vector2(24,26))-(mouse-origin)*(_zoom/old); ev.Use(); Repaint();
            }
            if (inside && ev.type==EventType.MouseDrag && ev.button==2) { _autoFit=false; _focusPending=false; _pan+=ev.delta; ev.Use(); Repaint(); }
            string id=VisibleEntities.FirstOrDefault(e=>e.Cell==picked && (Playing || Eligible(e)))?.Id;
            HandleCanvas(ev,inside,picked,id,false);
            GUI.EndClip();
        }

        static void Outline(Rect r,Color color)
        {
            if (Event.current.type!=EventType.Repaint) return;
            EditorGUI.DrawRect(new Rect(r.x,r.y,r.width,2),color); EditorGUI.DrawRect(new Rect(r.x,r.yMax-2,r.width,2),color);
            EditorGUI.DrawRect(new Rect(r.x,r.y,2,r.height),color); EditorGUI.DrawRect(new Rect(r.xMax-2,r.y,2,r.height),color);
        }

        void DrawThree(Rect rect)
        {
            if (rect.Contains(Event.current.mousePosition) && Event.current.type==EventType.MouseDown) { GUI.FocusControl(null); _inputFocus=true; }
            bool navigated=!Playing && !_dragging && _preview.ProcessNavigation(Event.current,rect);
            _preview.SelectedTerrainCells=_selectedTerrain;
            _preview.Draw(rect,Playing ? _session.Playtest.Level : _session.Draft,Playing ? _session.Playtest.World.Entities : null,_selected,ActiveRegion,_ghost,_session.CurrentY,_clip,_surface,_subtract);
            if (navigated) { Repaint(); return; }
            if (DrawAndHandleMoveAxes(rect)) return;
            bool pickEntity=_brush==EditorBrush.Select || _brush==EditorBrush.Erase || _brush==EditorBrush.Eyedropper;
            GridCell cell; string id;
            bool hit=pickEntity && !Playing
                ? _preview.TryPickFiltered(Event.current.mousePosition,_categories,_locked,out cell,out id)
                : _preview.TryPick(Event.current.mousePosition,pickEntity,out cell,out id);
            if (!_dragging && _brush==EditorBrush.Select && id==null && (Event.current.control || Event.current.command))
                hit=_preview.TryPickDragPlane(Event.current.mousePosition,_session.CurrentY,out cell);
            if(_dragging && _brush==EditorBrush.Select)
            {
                hit=_preview.TryPickDragPlane(Event.current.mousePosition,_start.y,out cell);
                if(_gestureMove)id=null;
            }
            HandleCanvas(Event.current,rect.Contains(Event.current.mousePosition)&&hit,cell,id,true);
        }

        void HandleCanvas(Event ev,bool inside,GridCell cell,string id,bool three)
        {
            if (inside && ev.type==EventType.MouseMove)
            {
                _hover=cell;
                _message=DescribeCell(cell);
                if (!Playing && !_dragging && _brush!=EditorBrush.Select && _brush!=EditorBrush.Eyedropper && Contains(_session.Draft.bounds,cell))
                {
                    _start=cell; UpdateGhost();
                    if (LevelCloner.ExpandTerrain(_session.Draft.terrain).Contains(cell) && (_brush==EditorBrush.Object || _brush==EditorBrush.Text || _brush==EditorBrush.Sentence))
                        _message="目标格被地形占据："+DisplayCell(cell);
                    else if ((_brush==EditorBrush.Text || _brush==EditorBrush.Sentence) && Inspect!=null && !Inspect.Terrain.Contains(cell.Add(GridCell.Down)) && !Inspect.Entities.Any(e=>e.Cell==cell.Add(GridCell.Down)&&Inspect.Solid(e)))
                        _message="提示：落点下方缺少实体支撑，保存草稿后仍需修正才能试玩。";
                }
                Repaint();
            }
            if (ev.type==EventType.MouseDown && ev.button==0 && inside && !ev.alt)
            {
                GUI.FocusControl(null); _inputFocus=true;
                if (Playing)
                {
                    if (id!=null) SelectId(id,ev.control||ev.command); ev.Use(); return;
                }
                if (!Contains(_session.Draft.bounds,cell)) return;
                if (RequiresCategories && _categories==EditorCategory.None)
                { _message="未勾选任何类别：请启用地形、物体或词牌。"; ev.Use(); return; }
                if (_brush==EditorBrush.Eyedropper) { SampleCell(cell); ev.Use(); Repaint(); return; }
                _gestureControl=GUIUtility.GetControlID(FocusType.Passive); GUIUtility.hotControl=_gestureControl;
                _dragging=true; _drag3D=three; _start=_hover=_lastStroke=cell; _append=ev.control||ev.command;
                _stroke.Clear(); _stroke.Add(cell);
                _gestureMove=false;
                if (_brush==EditorBrush.Select && !_append)
                {
                    if(id!=null)
                    {
                        if(!_selected.Contains(id))SelectId(id,false);
                        if(!_locked.Contains(id))BeginSelectionDrag(cell,ev.shift);
                    }
                    else if(TerrainEnabled && Inspect?.Terrain.Contains(cell)==true)
                    {
                        if(!_selectedTerrain.Contains(cell)) { _selected.Clear(); _selectedTerrain.Clear(); _selectedTerrain.Add(cell); }
                        _session.CurrentY=cell.y;
                        BeginSelectionDrag(cell,ev.shift);
                    }
                }
                UpdateGhost(); ev.Use(); Repaint();
            }
            if (!_dragging || _drag3D!=three) return;
            if (ev.type==EventType.MouseDrag && ev.button==0)
            {
                if (inside && (_gestureMove || Contains(_session.Draft.bounds,cell)))
                {
                    if(_gestureMove && _hover==cell && _dropValid) { ev.Use(); return; }
                    _hover=cell;
                    int steps=Math.Max(Math.Abs(cell.x-_lastStroke.x),Math.Max(Math.Abs(cell.y-_lastStroke.y),Math.Abs(cell.z-_lastStroke.z)));
                    for (int i=0;i<=steps;i++)
                    {
                        float t=steps==0 ? 0 : (float)i/steps;
                        _stroke.Add(new GridCell(Mathf.RoundToInt(Mathf.Lerp(_lastStroke.x,cell.x,t)),Mathf.RoundToInt(Mathf.Lerp(_lastStroke.y,cell.y,t)),Mathf.RoundToInt(Mathf.Lerp(_lastStroke.z,cell.z,t))));
                    }
                    _lastStroke=cell; UpdateGhost();
                }
                if(_gestureMove && !inside) { _dropValid=false; _dropError="指针在画布外，松开取消"; _preview?.SetSelectionPreview(_movePreview,_terrainPreview,false); }
                ev.Use(); Repaint();
            }
            if (ev.type==EventType.MouseUp && ev.button==0)
            {
                GUIUtility.hotControl=0; _dragging=false;
                if (inside) CommitGesture(id);
                _ghost=null; _stroke.Clear(); ClearDragPreview(); ev.Use(); Repaint();
            }
        }

        void UpdateGhost()
        {
            if(_gestureMove && _dragging) { UpdateDragPreview(); return; }
            if(_brush==EditorBrush.Object) { UpdateObjectBrushPreview(); return; }
            _ghost=Box(_start,_hover);
            if (_brush==EditorBrush.Box || _brush==EditorBrush.Region)
            {
                _ghost.min.y=Math.Min(_start.y,_topY); _ghost.max.y=Math.Max(_start.y,_topY);
            }
            if (_brush==EditorBrush.Sentence)
                _ghost=Box(_hover,_hover.Add((_sentenceAxis==0 ? GridCell.East : GridCell.South).x*Math.Max(0,_sentence.Count-1),0,(_sentenceAxis==1 ? -1 : 0)*Math.Max(0,_sentence.Count-1)));
            _message=DescribeCell(_hover)+" · 预览 "+DisplayCell(_ghost.min)+" → "+DisplayCell(_ghost.max)+" · 松开提交，Esc 取消";
        }

        void CommitGesture(string pickedId)
        {
            if (RequiresCategories && _categories==EditorCategory.None) return;
            var box=_ghost; var cells=_stroke.ToArray();
            if (_brush==EditorBrush.Select)
            {
                if (_gestureMove && _start!=_hover) CommitSelectionDrag();
                else if (_start!=_hover)
                {
                    if (!_append) { _selected.Clear(); _selectedTerrain.Clear(); }
                    foreach(var cell in Inspect?.Terrain ?? new HashSet<GridCell>()) if(TerrainEnabled && Contains(box,cell))_selectedTerrain.Add(cell);
                    foreach (var e in VisibleEntities) if (Contains(box,e.Cell) && Eligible(e)) _selected.Add(e.Id);
                }
                else
                {
                    var choices=VisibleEntities.Where(e=>e.Cell==_hover && Eligible(e)).ToArray();
                    if (choices.Length>1)
                    {
                        var menu=new GenericMenu();
                        foreach(var e in choices) { string selectedId=e.Id; menu.AddItem(new GUIContent(e.Id+" · "+(e.Kind==EntityKind.Text?e.Token:e.Subject)),_selected.Contains(e.Id),()=>SelectId(selectedId,_append)); }
                        menu.ShowAsContext();
                    }
                    else if (pickedId!=null) SelectId(pickedId,_append);
                    else if (_append && TerrainEnabled && Inspect?.Terrain.Contains(_hover)==true) _selectedTerrain.Add(_hover);
                    else if (!_append && !_gestureMove) { _selected.Clear(); _selectedTerrain.Clear(); }
                }
                return;
            }
            if (_brush==EditorBrush.Region)
            {
                if (_regionIndex<0) { _tab=1; _message="先在教学面板添加或选择一条提示，再绘制区域。"; return; }
                Edit("修改教学区域",d=>d.tutorial.regions[_regionIndex].bounds=box); return;
            }
            if (_brush==EditorBrush.Terrain || _brush==EditorBrush.Box)
            {
                if (_subtract && !TerrainEnabled) { _message="挖除地形需要勾选地形类别。"; return; }
                Edit(_subtract?"挖除地形":"绘制地形",d=>
                {
                    var boxes=_brush==EditorBrush.Box ? new[]{box} : cells.Select(c=>Box(c,c)).ToArray();
                    foreach (var b in boxes) { if (_subtract) AuthoringOperations.RemoveTerrain(d,b.min,b.max); else AuthoringOperations.AddTerrain(d,b.min,b.max,_appearance); }
                }); return;
            }
            if (_brush==EditorBrush.Erase)
            {
                EraseCells(cells); return;
            }
            if (_brush==EditorBrush.Sentence)
            {
                Edit("放置空间句子",d=>AuthoringOperations.PlaceSentence(d,_sentence.ToArray(),_hover,_sentenceAxis==0)); return;
            }
            if (_brush==EditorBrush.Object)
            {
                Edit("绘制物体",d=>AuthoringOperations.PlaceObjects(d,cells.Length>0?cells:new[]{_hover},_subject)); return;
            }
            Edit("放置词牌",d=>
            {
                if (LevelCloner.ExpandTerrain(d.terrain).Contains(_hover)) throw new InvalidOperationException("目标格被地形占据。");
                if (d.entities.Any(e=>e.cell==_hover)) throw new InvalidOperationException("该格已有物体或词牌，不能重叠放置："+_hover);
                var list=d.entities.ToList(); list.Add(new EntityDefinition { id=Guid.NewGuid().ToString("N"),kind=_brush==EditorBrush.Text?"Text":"Object",subject=_brush==EditorBrush.Text?"":_subject,token=_brush==EditorBrush.Text?_word:"",cell=_hover }); d.entities=list.ToArray();
            });
        }

        void SelectId(string id,bool append=false)
        {
            if (!append) { _selected.Clear(); _selectedTerrain.Clear(); }
            var entity=VisibleEntities.FirstOrDefault(e=>e.Id==id);
            if (entity==null || (!Playing && !Eligible(entity))) return;
            _selected.Add(id);
            if (entity!=null) _session.CurrentY=entity.Cell.y;
            Repaint();
        }
        void FocusIds(IEnumerable<string> ids)
        {
            _selected.Clear(); _selectedTerrain.Clear();
            foreach(var e in VisibleEntities.Where(e=>ids.Contains(e.Id) && Eligible(e))) _selected.Add(e.Id);
            FocusSelection();
        }
        void BeginSelectionDrag(GridCell cell,bool copy)
        {
            PruneSelection();
            _gestureIds=_selected.Where(id=>!_locked.Contains(id)).ToArray();
            _gestureTerrain=_selectedTerrain.ToArray();
            _gestureMove=_gestureIds.Length+_gestureTerrain.Length>0;
            _gestureCopy=copy; _start=_hover=cell;
            UpdateDragPreview();
        }
        GridCell DragDelta => new GridCell(_hover.x-_start.x,_hover.y-_start.y,_hover.z-_start.z);
        void UpdateDragPreview()
        {
            _ghost=null; _movePreview.Clear(); _terrainPreview.Clear();
            var delta=DragDelta;
            foreach(var e in VisibleEntities.Where(e=>_gestureIds.Contains(e.Id)))
                _movePreview.Add(new EntityState { Id=e.Id,Kind=e.Kind,Token=e.Token,Subject=e.Subject,Cell=e.Cell.Add(delta) });
            foreach(var cell in _gestureTerrain)_terrainPreview.Add(cell.Add(delta));
            try
            {
                SelectionTransform.Apply(LevelCloner.Clone(_session.Draft),_gestureIds,_gestureTerrain,delta,_gestureCopy);
                _dropValid=true; _dropError=null;
            }
            catch(Exception ex) { _dropValid=false; _dropError=ex.Message; }
            _preview?.SetSelectionPreview(_movePreview,_terrainPreview,_dropValid);
            _message=(_gestureCopy?"复制":"移动")+"偏移 "+EditorCoordinates.Delta(delta)+(_dropValid?" · 松开放置，Esc 取消":" · 无法放置："+_dropError);
        }
        void UpdateObjectBrushPreview()
        {
            _ghost=null; _movePreview.Clear(); _terrainPreview.Clear();
            var cells=_dragging?_stroke.ToArray():new[]{_hover};
            var occupied=new HashSet<GridCell>(VisibleEntities.Select(e=>e.Cell));
            if(Inspect!=null) occupied.UnionWith(Inspect.Terrain);
            _dropValid=true; _dropError=null;
            foreach(var cell in cells)
            {
                _movePreview.Add(new EntityState { Id="",Kind=EntityKind.Object,Subject=_subject,Cell=cell });
                if(_dropValid && (!Contains(_session.Draft.bounds,cell) || occupied.Contains(cell)))
                { _dropValid=false; _dropError="目标格已有方块或超出边界："+DisplayCell(cell); }
            }
            _preview?.SetSelectionPreview(_movePreview,_terrainPreview,_dropValid);
            _message=_dropValid?"物体笔刷："+_subject+" · "+cells.Length+" 格 · 拖动连续绘制，松开提交，Esc 取消"
                :"无法放置："+_dropError+"；本次笔画不会提交。";
        }

        void ClearDragPreview()
        {
            _movePreview.Clear(); _terrainPreview.Clear();
            _preview?.SetSelectionPreview(null,null,true);
        }
        void CommitSelectionDrag()
        {
            if(!_dropValid) { _message="未放置："+_dropError; return; }
            TransformSelection(_gestureIds,_gestureTerrain,DragDelta,_gestureCopy);
        }
        void MoveSelection(GridCell delta,bool copy)
        {
            PruneSelection();
            TransformSelection(_selected.Where(id=>!_locked.Contains(id)).ToArray(),_selectedTerrain.ToArray(),delta,copy);
        }
        void TransformSelection(string[] ids,GridCell[] terrain,GridCell delta,bool copy)
        {
            string[] resultingIds=null;
            Edit(copy?"复制选中内容":"移动选中内容",d=>resultingIds=SelectionTransform.Apply(d,ids,terrain,delta,copy));
            if(resultingIds==null)return;
            _selected.Clear(); foreach(var id in resultingIds)_selected.Add(id);
            _selectedTerrain.Clear(); foreach(var cell in terrain)_selectedTerrain.Add(cell.Add(delta));
        }
        void DeleteSelection()
        {
            PruneSelection();
            var ids=_selected.Where(id=>!_locked.Contains(id)).ToArray();
            var terrain=_selectedTerrain.ToArray();
            if (ids.Length+terrain.Length==0) return;
            Edit("删除选中内容",d=>
            {
                d.entities=d.entities.Where(e=>!ids.Contains(e.id)).ToArray();
                foreach(var cell in terrain)AuthoringOperations.RemoveTerrain(d,cell,cell);
            });
        }
        void CancelDrag()
        {
            if(_moveAxis!=EditorMoveAxis.None) _session.CurrentY=Mathf.Clamp(_axisStartY,_session.Draft.bounds.min.y,_session.Draft.bounds.max.y);
            _moveAxis=EditorMoveAxis.None;
            _dragging=false; _gestureMove=false; _ghost=null; _stroke.Clear(); ClearDragPreview(); GUIUtility.hotControl=0; Repaint();
        }
        void HandleKeyboard()
        {
            HandleKeyboardEvent(Event.current);
        }

        void HandleKeyboardEvent(Event ev)
        {
            if (ev==null || ev.type!=EventType.KeyDown) return;
            var operation=EditorCommands.Resolve(ev.keyCode,_inputFocus,EditorGUIUtility.editingTextField,_dragging,Playing,ev.control||ev.command||ev.alt,ev.shift);
            if (operation!=EditorCommand.None) { ExecuteEditorCommand(operation); ev.Use(); return; }
            if (_dragging || EditorGUIUtility.editingTextField) return;
            if (!Playing && (ev.control||ev.command) && !ev.alt)
            {
                if(ev.keyCode==KeyCode.S) { Save(false); ev.Use(); }
                if(ev.keyCode==KeyCode.Z) { if(ev.shift) _session.Redo(); else _session.Undo(); Changed(); ev.Use(); }
                return;
            }
            if (!Playing || !_inputFocus || _replay!=null) return;
            if(ev.keyCode==KeyCode.Z) { UndoPlay(); ev.Use(); return; }
            if(ev.keyCode==KeyCode.R) { RestartPlay(); ev.Use(); return; }
            string command=null;
            int dir=-1;
            if(ev.keyCode==KeyCode.W||ev.keyCode==KeyCode.UpArrow) dir=0;
            if(ev.keyCode==KeyCode.D||ev.keyCode==KeyCode.RightArrow) dir=1;
            if(ev.keyCode==KeyCode.S||ev.keyCode==KeyCode.DownArrow) dir=2;
            if(ev.keyCode==KeyCode.A||ev.keyCode==KeyCode.LeftArrow) dir=3;
            if(dir>=0) { command=new[]{"N","E","S","W"}[dir]; }
            if(ev.keyCode==KeyCode.Space) command=_session.Playtest.Phase==MotionPhase.BounceApex?"WAIT":"J";
            if(command!=null) { ExecutePlay(command); ev.Use(); }
        }
    }
}
