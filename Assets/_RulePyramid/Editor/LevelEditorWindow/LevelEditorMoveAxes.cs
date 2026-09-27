using System;
using System.Linq;
using RulePyramid.Core;
using UnityEditor;
using UnityEngine;

namespace RulePyramid.Editor
{
    public partial class LevelEditorWindow
    {
        EditorMoveAxis _moveAxis = EditorMoveAxis.None;
        Vector2 _axisStartMouse, _axisProjectedUnit;
        Vector3 _axisPivot;
        int _axisStartY;
        const float AxisLength = 72f;

        bool TryGetMovePivot(out Vector3 pivot, out GridCell anchor)
        {
            var cells = VisibleEntities.Where(e=>_selected.Contains(e.Id) && Eligible(e)).Select(e=>e.Cell)
                .Concat(TerrainEnabled?_selectedTerrain.Where(c=>Inspect?.Terrain.Contains(c)==true):Enumerable.Empty<GridCell>()).ToArray();
            pivot=Vector3.zero; anchor=default;
            if(cells.Length==0) return false;
            anchor=cells[0];
            var min=new Vector3(cells.Min(c=>c.x),cells.Min(c=>c.y),cells.Min(c=>c.z));
            var max=new Vector3(cells.Max(c=>c.x)+1f,cells.Max(c=>c.y)+1f,cells.Max(c=>c.z)+1f);
            pivot=(min+max)*.5f;
            return true;
        }

        bool DrawAndHandleMoveAxes(Rect rect)
        {
            if(Playing || _brush!=EditorBrush.Select || (_dragging && _moveAxis==EditorMoveAxis.None)) return false;
            if(!TryGetMovePivot(out var pivot,out var anchor)) return false;
            bool active=_moveAxis!=EditorMoveAxis.None;
            if(active) pivot=_axisPivot+new Vector3(DragDelta.x,DragDelta.y,DragDelta.z);
            var origin=_preview.ProjectWorldPoint(pivot);
            var units=new Vector2[3];
            for(int i=0;i<3;i++)
                units[i]=_preview.ProjectWorldPoint(pivot+EditorCoordinates.Direction(EditorAxisHandleMath.Direction((EditorMoveAxis)i)))-origin;
            var ends=MoveAxisEnds(origin,units);
            var ev=Event.current;
            var hovered=rect.Contains(ev.mousePosition)?PickMoveAxis(ev.mousePosition,origin,units,ends):EditorMoveAxis.None;
            if(ev.type==EventType.Repaint)
                DrawAxisOverlay(rect,origin,units,ends,active?_moveAxis:hovered);
            if(active)
            {
                if(ev.type==EventType.MouseDrag && ev.button==0)
                {
                    UpdateAxisDrag(ev.mousePosition,rect.Contains(ev.mousePosition));
                    ev.Use(); Repaint();
                }
                else if(ev.type==EventType.MouseUp && ev.button==0)
                {
                    UpdateAxisDrag(ev.mousePosition,rect.Contains(ev.mousePosition));
                    FinishAxisDrag(rect.Contains(ev.mousePosition));
                    ev.Use(); Repaint();
                }
                return true;
            }
            if(hovered==EditorMoveAxis.None) return false;
            if(ev.type==EventType.MouseDown && ev.button==0 && !ev.alt)
            {
                GUI.FocusControl(null); _inputFocus=true;
                _gestureControl=GUIUtility.GetControlID(FocusType.Passive); GUIUtility.hotControl=_gestureControl;
                BeginAxisDrag(hovered,ev.mousePosition,units[(int)hovered],pivot,anchor,ev.shift);
                ev.Use(); Repaint(); return true;
            }
            if(ev.type==EventType.MouseMove)
            {
                _message=hovered+" 轴移动 · 沿箭头拖动按整数格吸附 · Shift 拖动复制 · Esc 取消";
                Repaint(); return true;
            }
            return false;
        }

        static Vector2[] MoveAxisEnds(Vector2 origin,Vector2[] units)
        {
            var ends=new Vector2[3];
            float largest=Mathf.Max(.001f,units.Max(v=>v.magnitude));
            for(int i=0;i<3;i++)
            {
                float length=Mathf.Clamp(AxisLength*units[i].magnitude/largest,28f,AxisLength);
                // 接近侧视时两根轴可能同向投影；错开箭头端点仍可单独拾取。
                for(int j=0;j<i;j++)
                    if(Vector2.Dot(units[i].normalized,units[j].normalized)>.96f
                        && Mathf.Abs(length-(ends[j]-origin).magnitude)<20f)
                    {
                        float previous=(ends[j]-origin).magnitude;
                        length=previous>=50f?previous-22f:previous+22f;
                    }
                ends[i]=origin+units[i].normalized*length;
            }
            return ends;
        }

        static EditorMoveAxis PickMoveAxis(Vector2 mouse,Vector2 origin,Vector2[] units,Vector2[] ends)
        {
            var result=EditorMoveAxis.None;
            float best=10f;
            for(int i=0;i<3;i++)
            {
                if(units[i].sqrMagnitude<EditorAxisHandleMath.MinimumProjectedLengthSqr) continue;
                float tip=Vector2.Distance(mouse,ends[i]);
                if(tip<best) { best=tip; result=(EditorMoveAxis)i; }
            }
            if(result!=EditorMoveAxis.None) return result;
            best=8f;
            for(int i=0;i<3;i++)
            {
                if(units[i].sqrMagnitude<EditorAxisHandleMath.MinimumProjectedLengthSqr) continue;
                float distance=EditorAxisHandleMath.SegmentDistance(mouse,origin+units[i].normalized*12f,ends[i]);
                if(distance<best) { best=distance; result=(EditorMoveAxis)i; }
            }
            return result;
        }

        void DrawAxisOverlay(Rect rect,Vector2 origin,Vector2[] units,Vector2[] ends,EditorMoveAxis highlight)
        {
            var colors=new[]{new Color(1f,.28f,.22f),new Color(.3f,1f,.4f),new Color(.3f,.62f,1f)};
            GUI.BeginGroup(rect);
            Handles.BeginGUI();
            var oldColor=Handles.color;
            try
            {
                var clip=new Rect(0,0,rect.width,rect.height);
                void Line(Vector2 a,Vector2 b,Color color,float width)
                {
                    a-=rect.position; b-=rect.position;
                    if(!AuthoringPreview3D.ClipSegment(ref a,ref b,clip)) return;
                    Handles.color=color; Handles.DrawAAPolyLine(width,a,b);
                }
                for(int i=0;i<3;i++)
                {
                    if(units[i].sqrMagnitude<EditorAxisHandleMath.MinimumProjectedLengthSqr) continue;
                    var direction=units[i].normalized;
                    var across=new Vector2(-direction.y,direction.x);
                    var color=(int)highlight==i?Color.yellow:colors[i];
                    var end=ends[i];
                    Line(origin,end,Color.black,5f); Line(origin,end,color,3f);
                    Line(end,end-direction*11f+across*5f,color,3f);
                    Line(end,end-direction*11f-across*5f,color,3f);
                    var label=end+direction*10f-rect.position;
                    var style=new GUIStyle(EditorStyles.boldLabel) { alignment=TextAnchor.MiddleCenter,normal={textColor=color} };
                    GUI.Label(new Rect(label.x-10,label.y-10,20,20),((EditorMoveAxis)i).ToString(),style);
                }
            }
            finally { Handles.color=oldColor; Handles.EndGUI(); GUI.EndGroup(); }
        }

        void BeginAxisDrag(EditorMoveAxis axis,Vector2 mouse,Vector2 projectedUnit,Vector3 pivot,GridCell anchor,bool copy)
        {
            _axisStartY=_session.CurrentY; _axisStartMouse=mouse; _axisProjectedUnit=projectedUnit;
            _axisPivot=pivot; _moveAxis=axis; _dragging=true; _drag3D=true;
            _stroke.Clear();
            BeginSelectionDrag(anchor,copy);
        }

        void UpdateAxisDrag(Vector2 mouse,bool inside)
        {
            int steps=EditorAxisHandleMath.DragSteps(mouse-_axisStartMouse,_axisProjectedUnit);
            var offset=EditorCoordinates.Delta(EditorAxisHandleMath.Offset(_moveAxis,steps));
            _hover=_start.Add(offset);
            UpdateDragPreview();
            if(!inside)
            {
                _dropValid=false; _dropError="指针在三维画布外，松开取消";
                _preview?.SetSelectionPreview(_movePreview,_terrainPreview,false);
                _message=_dropError;
            }
            _session.CurrentY=Mathf.Clamp(_axisStartY+offset.y,_session.Draft.bounds.min.y,_session.Draft.bounds.max.y);
            if(inside) _message=_moveAxis+" 轴 · "+_message;
        }

        void FinishAxisDrag(bool inside)
        {
            var before=_session.Draft;
            int y=_session.CurrentY;
            if(inside && _start!=_hover) CommitSelectionDrag();
            bool committed=!ReferenceEquals(before,_session.Draft);
            CancelDrag();
            if(committed) _session.CurrentY=y;
        }
    }
}
