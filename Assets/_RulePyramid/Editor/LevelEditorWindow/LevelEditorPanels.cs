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
        void DrawInspector()
        {
            GUILayout.Label("选择详情 · " + (_selected.Count + _selectedTerrain.Count),EditorStyles.boldLabel);
            var selected=VisibleEntities.Where(e=>_selected.Contains(e.Id)).ToArray();
            if(_selectedTerrain.Count>0) GUILayout.Label("地形格："+_selectedTerrain.Count+" 个",EditorStyles.miniLabel);
            foreach(var entity in selected)
            {
                GUILayout.Label(entity.Id,EditorStyles.miniLabel);
                GUILayout.Label((entity.Kind==EntityKind.Text?"词牌 "+entity.Token:"物体 "+entity.Subject)+"  "+entity.Cell);
                if(entity.Kind==EntityKind.Object && Inspect!=null)
                    GUILayout.Label("当前性质："+string.Join(" / ",Inspect.Props(entity)),EditorStyles.wordWrappedLabel);
            }
            using(new EditorGUI.DisabledScope(Playing || selected.Length+_selectedTerrain.Count==0))
            {
                if(selected.Length==1 && _selectedTerrain.Count==0)
                {
                    var e=selected[0];
                    EditorGUI.BeginChangeCheck();
                    var pos=EditorGUILayout.Vector3IntField("整数格坐标",Vec(e.Cell));
                    if(EditorGUI.EndChangeCheck()) MoveSelection(new GridCell(pos.x-e.Cell.x,pos.y-e.Cell.y,pos.z-e.Cell.z),false);
                    string value=e.Kind==EntityKind.Text?e.Token:e.Subject;
                    string[] choices=e.Kind==EntityKind.Text?Words:Subjects;
                    int current=Math.Max(0,Array.IndexOf(choices,value));
                    int next=EditorGUILayout.Popup(e.Kind==EntityKind.Text?"文字内容":"初始身份",current,choices);
                    if(next!=current) Edit("修改选中对象",d=> { var target=d.entities.First(x=>x.id==e.Id); if(e.Kind==EntityKind.Text) target.token=choices[next]; else target.subject=choices[next]; });
                }
                _moveOffset=Cell(EditorGUILayout.Vector3IntField("整组偏移 / 复制",Vec(_moveOffset)));
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("移动")) MoveSelection(_moveOffset,false);
                    if(GUILayout.Button("复制")) MoveSelection(_moveOffset,true);
                    if(GUILayout.Button(new GUIContent("删除 [Shift+E]", "删除选区：Shift+E / Delete / Backspace"))) ExecuteEditorCommand(EditorCommand.Delete);
                }
                if(selected.Length>0 && GUILayout.Button("锁定选择（仅编辑）")) foreach(var id in _selected) _locked.Add(id);
            }
            if(_locked.Count>0 && GUILayout.Button("解除全部编辑锁定（"+_locked.Count+"）")) _locked.Clear();
            if(selected.Length+_selectedTerrain.Count>0 && GUILayout.Button("定位两侧画布 [F]")) ExecuteEditorCommand(EditorCommand.Focus);
            GUILayout.Space(10); GUILayout.Label("空间规则与来源",EditorStyles.boldLabel);
            if(_inspectionError!=null && !Playing) EditorGUILayout.HelpBox(_inspectionError,MessageType.Warning);
            if(Inspect!=null)
            {
                foreach(var rule in Inspect.PropertySources)
                {
                    if(selected.Length>0 && !selected.Any(e=>e.Subject==rule.Subject || rule.TextIds.Contains(e.Id))) continue;
                    if(GUILayout.Button(rule.Subject+" IS "+rule.Property+"  ↗")) FocusIds(rule.TextIds);
                }
                foreach(var rule in Inspect.TransformSources)
                {
                    var ids=rule.Origin as IEnumerable<string>;
                    if(selected.Length>0 && !selected.Any(e=>e.Subject==rule.Source || ids!=null&&ids.Contains(e.Id))) continue;
                    if(GUILayout.Button(rule.Source+" → "+rule.Target+"  ↗") && ids!=null) FocusIds(ids);
                }
            }
            if(Playing)
            {
                GUILayout.Space(8);
                GUILayout.Label("回合 "+_session.Playtest.TurnCount+" · "+_session.Playtest.Phase);
                GUILayout.Label("点击画布后：WASD / 方向键移动与推动\nSpace 跳\nZ 撤销 · R 重开",EditorStyles.wordWrappedMiniLabel);
                using(new EditorGUI.DisabledScope(_replay!=null))
                {
                    using(new EditorGUILayout.HorizontalScope())
                        foreach(var cmd in new[]{"N","W","S","E","J","WAIT"}) if(GUILayout.Button(cmd)) ExecutePlay(cmd);
                }
                GUILayout.Label("按钮使用世界方向；键盘采用固定第 1 视角。",EditorStyles.wordWrappedMiniLabel);
            }
        }

        void DrawDiagnostics()
        {
            var report=_validation??_session.Validate();
            if(report.Issues.Count==0) GUILayout.Label("初态校验通过。参考解与教学展示可分别试玩验证。");
            foreach(var issue in report.Issues)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(issue.Code+" · "+issue.Message,EditorStyles.wordWrappedLabel);
                    var e=_session.Draft.entities.FirstOrDefault(x=>issue.Message.Contains(x.id));
                    if(e!=null && GUILayout.Button("定位",GUILayout.Width(50))) FocusIds(new[]{e.id});
                }
            }
            if(Inspect!=null)
            {
                foreach(var entity in Inspect.Entities)
                {
                    if(Inspect.Gravity(entity)!=GravityMode.Down) continue;
                    var below=entity.Cell.Add(GridCell.Down);
                    bool support=Inspect.Terrain.Contains(below)||Inspect.Entities.Any(e=>e.Cell==below && e.Id!=entity.Id && Inspect.Solid(e)&&Inspect.Solid(entity));
                    if(!support && GUILayout.Button("缺少支撑："+entity.Id+" "+entity.Cell+" · 点击定位")) FocusIds(new[]{entity.Id});
                }
            }
            var regions=_session.Draft.tutorial?.regions??Array.Empty<RegionTutorialData>();
            for(int i=0;i<regions.Length;i++)
            {
                var r=regions[i];
                if(r.durationSeconds<=0 || !Contains(_session.Draft.bounds,r.bounds.min) || !Contains(_session.Draft.bounds,r.bounds.max))
                    GUILayout.Label("教学提示 "+r.name+"：检查时长与区域范围。",EditorStyles.wordWrappedLabel);
            }
            if(Playing)
            {
                GUILayout.Label("最近事件：");
                foreach(var ev in _session.Playtest.World.Log.Skip(Math.Max(0,_session.Playtest.World.Log.Count-12)))
                    GUILayout.Label(ev.Kind+" · "+ev.EntityId+" "+ev.From+" → "+ev.To+" "+ev.Message,EditorStyles.miniLabel);
            }
        }

        void DrawTutorial()
        {
            var tutorial=_session.Draft.tutorial;
            var regions=tutorial?.regions??Array.Empty<RegionTutorialData>();
            using(new EditorGUI.DisabledScope(Playing))
            {
                EditorGUI.BeginChangeCheck();
                string objective=EditorGUILayout.DelayedTextField("教学目标",tutorial?.objective??"");
                if(EditorGUI.EndChangeCheck()) Edit("修改教学目标",d=> { if(d.tutorial==null)d.tutorial=new TutorialData(); d.tutorial.objective=objective; });
            }
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    for(int i=0;i<regions.Length;i++)
                    {
                        string state=Playing?" · "+new[]{"未触发","排队中","展示中","已结束"}[(int)_tutorial.StateAt(i)]:"";
                        if(GUILayout.Toggle(_regionIndex==i,(regions[i].enabled?"":"[停用] ")+regions[i].name+state,"Button")) _regionIndex=i;
                    }
                    using(new EditorGUI.DisabledScope(Playing))
                        if(GUILayout.Button("添加区域提示"))
                        {
                            Edit("添加教学提示",d=>
                            {
                                if(d.tutorial==null)d.tutorial=new TutorialData();
                                var list=(d.tutorial.regions??Array.Empty<RegionTutorialData>()).ToList();
                                list.Add(new RegionTutorialData { id=Guid.NewGuid().ToString("N"),name="新提示",text="",durationSeconds=5,enabled=true,bounds=Box(new GridCell(d.bounds.min.x,_session.CurrentY,d.bounds.min.z),new GridCell(d.bounds.min.x,_session.CurrentY,d.bounds.min.z)) });
                                d.tutorial.regions=list.ToArray();
                            }); _regionIndex=_session.Draft.tutorial.regions.Length-1;
                        }
                }
                if(_regionIndex>=0 && _regionIndex<regions.Length)
                {
                    int index=_regionIndex; var r=regions[index];
                    using(new EditorGUILayout.VerticalScope())
                    using(new EditorGUI.DisabledScope(Playing))
                    {
                        EditorGUI.BeginChangeCheck();
                        string name=EditorGUILayout.DelayedTextField("名称",r.name);
                        string text=EditorGUILayout.TextArea(r.text??"",GUILayout.Height(45));
                        float seconds=EditorGUILayout.DelayedFloatField("展示秒数",r.durationSeconds);
                        bool enabled=EditorGUILayout.Toggle("启用",r.enabled);
                        var min=EditorGUILayout.Vector3IntField("区域最小格",Vec(r.bounds.min));
                        var max=EditorGUILayout.Vector3IntField("区域最大格",Vec(r.bounds.max));
                        if(EditorGUI.EndChangeCheck()) Edit("修改教学提示",d=>
                        {
                            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))throw new InvalidOperationException("展示秒数必须大于零。");
                            var target=d.tutorial.regions[index]; target.name=name; target.text=text; target.durationSeconds=seconds; target.enabled=enabled; target.bounds=Box(Cell(min),Cell(max));
                        });
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            if(GUILayout.Button("在画布绘制区域")) { _brush=EditorBrush.Region; _session.CurrentY=r.bounds.min.y; _topY=r.bounds.max.y; _preview.Focus(r.bounds.min); }
                            if(GUILayout.Button("上移") && index>0) { Edit("调整提示顺序",d=> { var a=d.tutorial.regions; var t=a[index-1]; a[index-1]=a[index]; a[index]=t; }); _regionIndex--; }
                            if(GUILayout.Button("下移") && index<regions.Length-1) { Edit("调整提示顺序",d=> { var a=d.tutorial.regions; var t=a[index+1]; a[index+1]=a[index]; a[index]=t; }); _regionIndex++; }
                            if(GUILayout.Button("删除提示")) { Edit("删除教学提示",d=>d.tutorial.regions=d.tutorial.regions.Where((_,i)=>i!=index).ToArray()); _regionIndex=-1; }
                        }
                    }
                }
            }
            GUILayout.Label("仅当前 YOU；初态/换控在区域内立即触发；依次排队。撤销与重开不清已触发记录，退出试玩再进入才重置。",EditorStyles.wordWrappedMiniLabel);
            if(Playing) GUILayout.Label("剩余 "+_tutorial.RemainingSeconds.ToString("0.0")+" 秒 · 等待 "+_tutorial.QueuedCount+" 条");
            if((tutorial?.hints?.Length??0)>0) GUILayout.Label("旧版手动提示已保留在文件中，不自动转换为区域提示。",EditorStyles.miniLabel);
        }

        void DrawSolutions()
        {
            var solutions=_session.Draft.referenceSolutions??Array.Empty<ReferenceSolutionData>();
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
                {
                    for(int i=0;i<solutions.Length;i++)
                    {
                        var sol=solutions[i];
                        if(Playing) GUILayout.Label(sol.name);
                        if(!Playing && GUILayout.Toggle(_solutionIndex==i,sol.name+" · "+(_replayResults.TryGetValue(sol.id,out var status)?status:"待验证"),"Button")) _solutionIndex=i;
                    }
                    if(!Playing && GUILayout.Button("录制新参考解")) { _solutionIndex=-1; _recordName="新参考解"; _recordFamily="recorded"; StartPlay(true); }
                }
                using(new EditorGUILayout.VerticalScope())
                {
                    if(_recording)
                    {
                        _recordName=EditorGUILayout.TextField("解法名称",_recordName);
                        _recordFamily=EditorGUILayout.TextField("策略类别",_recordFamily);
                        GUILayout.Label("已录制 "+_recorder.CommandCount+" 步；获胜后可以保存。旧参考解会保留到新录制保存成功。");
                        using(new EditorGUI.DisabledScope(!_session.Playtest.Won)) if(GUILayout.Button("保存录制并返回编辑")) FinishRecording();
                    }
                    else if(_replay!=null)
                    {
                        GUILayout.Label(_replay.name+" · "+_replayStep+" / "+_replay.commands.Length);
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            if(GUILayout.Button(_autoReplay?"暂停":"播放")) _autoReplay=!_autoReplay;
                            if(GUILayout.Button("上一步")) { _autoReplay=false; if(_recorder.Undo())_replayStep=Math.Max(0,_replayStep-1); _tutorial.Observe(_session.Playtest); }
                            if(GUILayout.Button("下一步")) { _autoReplay=false; StepReplay(); }
                        }
                        GUILayout.Label(string.Join(" ",_replay.commands),EditorStyles.wordWrappedLabel);
                    }
                    else if(_solutionIndex>=0 && _solutionIndex<solutions.Length)
                    {
                        var sol=solutions[_solutionIndex];
                        GUILayout.Label(sol.commands.Length+" 步 · "+sol.family);
                        GUILayout.Label(string.Join(" ",sol.commands),EditorStyles.wordWrappedLabel);
                        using(new EditorGUI.DisabledScope(Playing))
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            if(GUILayout.Button("逐步回放")) StartReplay(sol);
                            if(GUILayout.Button("验证")) VerifySolution(sol);
                            if(GUILayout.Button("重录")) { _recordName=sol.name; _recordFamily=sol.family; StartPlay(true); }
                            if(GUILayout.Button("删除")) { int index=_solutionIndex; Edit("删除参考解",d=>d.referenceSolutions=d.referenceSolutions.Where((_,i)=>i!=index).ToArray()); _solutionIndex=-1; }
                        }
                    }
                    if(!Playing && GUILayout.Button("验证全部参考解")) foreach(var sol in solutions) VerifySolution(sol);
                }
            }
        }

        void DrawMapSize()
        {
            using(new EditorGUI.DisabledScope(Playing))
            {
                var d=_session.Draft;
                if(!_resizeEditing)
                {
                    _resizeOrigin=Vec(d.bounds.min);
                    _resizeSize=new Vector3Int(d.bounds.max.x-d.bounds.min.x+1,d.bounds.max.y-d.bounds.min.y+1,d.bounds.max.z-d.bounds.min.z+1);
                }
                GUILayout.Label("地图大小",EditorStyles.boldLabel);
                GUILayout.Label("拖动滑块，或在滑块右侧直接输入格数。每个方向最多 64 格。",EditorStyles.wordWrappedMiniLabel);
                EditorGUI.BeginChangeCheck();
                int x=EditorGUILayout.IntSlider("长度 X",_resizeSize.x,1,MapResize.MaxAxisSize);
                int y=EditorGUILayout.IntSlider("高度 Y",_resizeSize.y,1,MapResize.MaxAxisSize);
                int z=EditorGUILayout.IntSlider("宽度 Z",_resizeSize.z,1,MapResize.MaxAxisSize);
                _resizeSize=new Vector3Int(x,y,z);
                _resizeOrigin=EditorGUILayout.Vector3IntField("最小格坐标",_resizeOrigin);
                if(EditorGUI.EndChangeCheck())_resizeEditing=true;
                GUILayout.Label("待应用："+_resizeSize.x+" × "+_resizeSize.y+" × "+_resizeSize.z+" 格；扩展后可继续绘制地形。缩小时检查实体、地形与教学区域。",EditorStyles.wordWrappedMiniLabel);
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(!_resizeEditing))
                    {
                        if(GUILayout.Button("应用地图大小"))
                        {
                            bool applied=false;
                            Edit("调整地图大小",draft=> { MapResize.Resize(draft,Cell(_resizeOrigin),Cell(_resizeSize)); applied=true; });
                            if(applied)
                            {
                                _resizeEditing=false;
                                _session.CurrentY=Mathf.Clamp(_session.CurrentY,_session.Draft.bounds.min.y,_session.Draft.bounds.max.y);
                                FrameAll();
                            }
                        }
                        if(GUILayout.Button("取消修改"))_resizeEditing=false;
                    }
                }
                using(new EditorGUI.DisabledScope(_resizeEditing))
                    if(GUILayout.Button("自动填满底层地板")) Edit("填满底层地板",AuthoringOperations.FillFloor);
                GUILayout.Label(_resizeEditing ? "先应用或取消尺寸修改，再填满地板。" : "填充当前地图最低层 Y="+d.bounds.min.y+" 的空格，使用石质地形；保留已有地形，遇到实体则提示冲突。",EditorStyles.wordWrappedMiniLabel);
            }
        }

        void DrawSettings()
        {
            using(new EditorGUI.DisabledScope(Playing))
            {
                var d=_session.Draft;
                EditorGUI.BeginChangeCheck();
                var id=EditorGUILayout.DelayedTextField("关卡 ID",d.id);
                var title=EditorGUILayout.DelayedTextField("标题",d.title);
                if(EditorGUI.EndChangeCheck()) Edit("修改关卡信息",draft=>
                {
                    if(string.IsNullOrWhiteSpace(id))throw new InvalidOperationException("关卡 ID 不能为空。");
                    draft.id=id; draft.title=title;
                });
                GUILayout.Label("玩家固定第 1 视角；搭建时可自由旋转检查。",EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label("RW-v0.9 · 三维整数格 · 单 YOU · 全部空间词牌可移动 · 固定地形",EditorStyles.wordWrappedLabel);
            }
        }

        void StartPlay(bool recording)
        {
            CancelDrag();
            if(Playing)return;
            if(!_session.TryStartPlaytest(out var game,out var error)) { _message=error; _tab=0; return; }
            _editView=_preview.CaptureView(); _editY=_session.CurrentY; _editSelection=_selected.ToArray();
            _recorder=new PlaytestRecorder(game); _tutorial=new RegionTutorialSession(game.Level.tutorial); _tutorial.Observe(game);
            _recordTargetIndex=recording?_solutionIndex:-1;
            _recording=recording; _replay=null; _replayStep=0; _autoReplay=false; _inputFocus=true;
            _preview.RotateSlot(0); _lastTick=EditorApplication.timeSinceStartup;
            _message="试玩已开始；点击画布后使用 WASD / 方向键移动与推动，Space 跳跃。退出恢复原草稿。";
            if(recording)_tab=2;
        }
        void StopPlay()
        {
            if(_recording && _recorder.CommandCount>0 && !EditorUtility.DisplayDialog("结束录制","放弃这次尚未保存的录制？原参考解不会改变。","放弃录制","继续录制"))return;
            _session.StopPlaytest(); _recording=false; _replay=null; _autoReplay=false; _recorder=null; _tutorial=null; _inputFocus=false;
            _preview.RestoreView(_editView); _session.CurrentY=_editY; _selected.Clear(); foreach(var id in _editSelection??Array.Empty<string>())_selected.Add(id);
            _message="已返回作者草稿。"; Repaint();
        }
        void ExecutePlay(string command)
        {
            if(!Playing)return;
            if(_recorder.TryExecute(command)) { _tutorial.Observe(_session.Playtest); _message="执行 "+command+" · "+_session.Playtest.Phase; }
            else _message="操作未执行："+_session.Playtest.LastRejectReason;
            Repaint();
        }
        void UndoPlay()
        {
            if(!Playing)return;
            _autoReplay=false;
            if(_recorder.Undo()) { if(_replay!=null)_replayStep=Math.Max(0,_replayStep-1); _tutorial.Observe(_session.Playtest); }
            Repaint();
        }
        void RestartPlay()
        {
            if(!Playing)return;
            _recorder.Restart(); _replayStep=0; _autoReplay=false; _tutorial.Observe(_session.Playtest); Repaint();
        }
        void FinishRecording()
        {
            int index=_recordTargetIndex;
            string id=index>=0?_session.Draft.referenceSolutions[index].id:Guid.NewGuid().ToString("N");
            var sol=_recorder.ToSolution(id,_recordName,_recordFamily);
            _recording=false; StopPlay();
            Edit(index>=0?"替换参考解录制":"保存参考解录制",d=> { var list=d.referenceSolutions.ToList(); if(index>=0)list[index]=sol; else list.Add(sol); d.referenceSolutions=list.ToArray(); });
            _solutionIndex=index>=0?index:_session.Draft.referenceSolutions.Length-1;
            VerifySolution(sol);
        }
        void StartReplay(ReferenceSolutionData solution)
        {
            StartPlay(false); if(!Playing)return;
            _replay=solution; _replayStep=0; _tab=2;
        }
        void StepReplay()
        {
            if(_replay==null || !Playing)return;
            if(_replayStep>=_replay.commands.Length) { _autoReplay=false; VerifySolution(_replay); return; }
            string cmd=_replay.commands[_replayStep];
            if(!_recorder.TryExecute(cmd)) { _autoReplay=false; _message="回放第 "+(_replayStep+1)+" 步 "+cmd+" 失败："+_session.Playtest.LastRejectReason; return; }
            _replayStep++; _tutorial.Observe(_session.Playtest); Repaint();
            if(_replayStep==_replay.commands.Length) { _autoReplay=false; VerifySolution(_replay); }
        }
        void VerifySolution(ReferenceSolutionData sol)
        {
            var report = _session.Validate();
            if (!report.CanPlaytest)
            {
                _replayResults[sol.id] = "失败";
                _message = report.ToString();
                return;
            }
            try { var result=ReplayRunner.Run(_session.BuildPlaytestLevel(),sol); _replayResults[sol.id]=result.Won?"通过":"失败"; _message=sol.name+"："+(result.Won?"回放通过，共 "+result.Turns+" 步":result.FailReason); }
            catch(Exception ex) { _replayResults[sol.id]="失败"; _message=ex.Message; }
        }
    }
}
