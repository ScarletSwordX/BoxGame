using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class MoveAxisWorkflowTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        LevelEditorWindow window;
        LevelEditSession session;
        static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Flags).Invoke(target,args);
        static object Static(string name,params object[] args)=>typeof(LevelEditorWindow).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
        static readonly GridCell Anchor=new GridCell(1,1,1);
        [SetUp]
        public void SetUp()
        {
            window=ScriptableObject.CreateInstance<LevelEditorWindow>();
            session=new LevelEditSession(new LevelDefinition
            {
                id="axes",schemaVersion=9,mechanicsVersion="RW-v0.9",
                bounds=new GridCellBox {min=new GridCell(0,0,0),max=new GridCell(7,7,7)},
                terrain=new[]{new GridCellBox {id="ground",min=new GridCell(1,0,1),max=new GridCell(1,0,1)}},
                entities=new[]{new EntityDefinition {id="rock",kind="Object",subject="ROCK",cell=Anchor},
                    new EntityDefinition {id="word",kind="Text",token="IS",cell=new GridCell(2,1,1)}}
            });
            Set(window,"_session",session);
            ((HashSet<string>)Get(window,"_selected")).UnionWith(new[]{"rock","word"});
            ((HashSet<GridCell>)Get(window,"_selectedTerrain")).Add(new GridCell(1,0,1));
            Call(window,"RefreshInspection");
        }
        [TearDown]
        public void TearDown() { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }
        void Begin(EditorMoveAxis axis,bool copy=false)
        {
            Call(window,"BeginAxisDrag",axis,new Vector2(100,100),new Vector2(axis==EditorMoveAxis.Y?10:-10,0),new Vector3(2,1,1.5f),Anchor,copy);
        }

        [TestCase(EditorMoveAxis.X)]
        [TestCase(EditorMoveAxis.Y)]
        [TestCase(EditorMoveAxis.Z)]
        public void MixedSelectionMovesOnlyOnChosenAxisAndUndoesOnce(EditorMoveAxis axis)
        {
            Begin(axis);
            Call(window,"UpdateAxisDrag",new Vector2(120,160),true);
            Assert.IsTrue((bool)Get(window,"_dropValid"));
            Assert.IsFalse(session.Dirty);
            var delta=EditorAxisHandleMath.Offset(axis,2);
            Assert.AreEqual(Anchor.Add(delta),((List<EntityState>)Get(window,"_movePreview")).Single(e=>e.Id=="rock").Cell);
            Call(window,"FinishAxisDrag",true);
            Assert.AreEqual(Anchor.Add(delta),session.Draft.entities[0].cell);
            Assert.IsTrue(LevelCloner.ExpandTerrain(session.Draft.terrain).Contains(new GridCell(1,0,1).Add(delta)));
            Assert.AreEqual(axis==EditorMoveAxis.Y?3:1,session.CurrentY);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(Anchor,session.Draft.entities[0].cell);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void PositiveDisplayedXAxisDragMovesLeftAndIncreasesDisplayedX()
        {
            int before = EditorCoordinates.Display(session.Draft.bounds, Anchor).x;
            Begin(EditorMoveAxis.X);
            Call(window,"UpdateAxisDrag",new Vector2(90,100),true);
            Assert.IsTrue((bool)Get(window,"_dropValid"));
            Call(window,"FinishAxisDrag",true);
            Assert.AreEqual(new GridCell(0,1,1),session.Draft.entities[0].cell);
            Assert.AreEqual(before+1,EditorCoordinates.Display(session.Draft.bounds,session.Draft.entities[0].cell).x);
        }

        [Test]
        public void ShiftAxisDragCopiesAndKeepsOriginals()
        {
            Begin(EditorMoveAxis.Y,true);
            Call(window,"UpdateAxisDrag",new Vector2(120,100),true);
            Call(window,"FinishAxisDrag",true);
            Assert.AreEqual(4,session.Draft.entities.Length);
            Assert.AreEqual(4,session.Draft.entities.Select(e=>e.id).Distinct().Count());
            Assert.AreEqual(Anchor,session.Draft.entities[0].cell);
            Assert.IsTrue(session.Draft.entities.Any(e=>e.cell==new GridCell(1,3,1)));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(2,session.Draft.entities.Length);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CancellationOrOutsideReleaseRestoresYWithoutHistory(bool escape)
        {
            Begin(EditorMoveAxis.Y);
            Call(window,"UpdateAxisDrag",new Vector2(120,100),true);
            Assert.AreEqual(3,session.CurrentY);
            if(escape) Call(window,"CancelDrag"); else Call(window,"FinishAxisDrag",false);
            Assert.AreEqual(1,session.CurrentY);
            Assert.AreEqual(EditorMoveAxis.None,Get(window,"_moveAxis"));
            Assert.IsEmpty((List<EntityState>)Get(window,"_movePreview"));
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void OccupiedOrOutOfBoundsDestinationsNeverCommit()
        {
            ((HashSet<string>)Get(window,"_selected")).Remove("word");
            ((HashSet<GridCell>)Get(window,"_selectedTerrain")).Clear();
            Begin(EditorMoveAxis.X);
            Call(window,"UpdateAxisDrag",new Vector2(110,100),true);
            Assert.IsFalse((bool)Get(window,"_dropValid"));
            Call(window,"FinishAxisDrag",true);
            Assert.IsFalse(session.CanUndo);
            Begin(EditorMoveAxis.Y);
            Call(window,"UpdateAxisDrag",new Vector2(300,100),true);
            Assert.IsFalse((bool)Get(window,"_dropValid"));
            Call(window,"FinishAxisDrag",true);
            Assert.AreEqual(1,session.CurrentY);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void ClickingWithoutMovementDoesNotCreateUndoRecord()
        {
            Begin(EditorMoveAxis.Y);
            Call(window,"FinishAxisDrag",true);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void CameraRotationKeepsScreenDraggingOnTheRequestedWorldAxis()
        {
            using(var preview=new AuthoringPreview3D())
            {
                var pivot=new Vector3(1.5f,1.5f,1.5f);
                for(int slot=0;slot<4;slot++)
                {
                    preview.RotateSlot(slot);
                    preview.UpdatePicking(new Rect(200,50,420,360),session.Draft,null,1,false,false,false);
                    var origin=preview.ProjectWorldPoint(pivot);
                    foreach(var axis in new[]{EditorMoveAxis.X,EditorMoveAxis.Y,EditorMoveAxis.Z})
                    {
                        var unit=preview.ProjectWorldPoint(pivot+EditorCoordinates.Direction(EditorAxisHandleMath.Direction(axis)))-origin;
                        Call(window,"BeginAxisDrag",axis,origin,unit,pivot,Anchor,false);
                        Call(window,"UpdateAxisDrag",origin+unit*(axis==EditorMoveAxis.Y?2:-2),true);
                        Assert.IsTrue((bool)Get(window,"_dropValid"));
                        var predicted=((List<EntityState>)Get(window,"_movePreview")).Single(e=>e.Id=="rock").Cell;
                        Assert.AreEqual(Anchor.Add(EditorAxisHandleMath.Offset(axis,2)),predicted);
                        Call(window,"CancelDrag");
                    }
                }
            }
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void ParallelScreenAxesHaveIndividuallyPickableArrowheads()
        {
            var origin=new Vector2(100,100);
            var units=new[]{new Vector2(10,0),new Vector2(0,-10),new Vector2(0,-10)};
            var ends=(Vector2[])Static("MoveAxisEnds",origin,units);
            Assert.Greater(Vector2.Distance(ends[1],ends[2]),20);
            Assert.AreEqual(EditorMoveAxis.Y,Static("PickMoveAxis",ends[1],origin,units,ends));
            Assert.AreEqual(EditorMoveAxis.Z,Static("PickMoveAxis",ends[2],origin,units,ends));
        }
    }
}
