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
    public class SelectionDragWorkflowTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        LevelEditorWindow window;
        LevelEditSession session;
        static object Get(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object value)=>o.GetType().GetField(n,Flags).SetValue(o,value);
        static void Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
        [SetUp]
        public void SetUp()
        {
            window=ScriptableObject.CreateInstance<LevelEditorWindow>();
            session=new LevelEditSession(new LevelDefinition
            {
                id="drag",schemaVersion=9,mechanicsVersion="RW-v0.9",
                bounds=new GridCellBox {min=new GridCell(0,0,0),max=new GridCell(7,7,7)},
                terrain=new[] {new GridCellBox {id="stone",min=new GridCell(1,0,1),max=new GridCell(1,0,1),appearance="Stone"}},
                entities=new[] {
                    new EntityDefinition {id="rock",kind="Object",subject="ROCK",cell=new GridCell(1,1,1)},
                    new EntityDefinition {id="word",kind="Text",token="PUSH",cell=new GridCell(2,1,1)}
                }
            });
            Set(window,"_session",session);
            ((HashSet<string>)Get(window,"_selected")).UnionWith(new[] {"rock","word"});
            ((HashSet<GridCell>)Get(window,"_selectedTerrain")).Add(new GridCell(1,0,1));
            Call(window,"RefreshInspection");
        }
        [TearDown]
        public void TearDown()
        {
            window.DiscardChanges();
            UnityEngine.Object.DestroyImmediate(window);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DragPreviewKeepsDraftUntouchedAndCommitIsOneUndo(bool copy)
        {
            Set(window,"_dragging",true);
            Call(window,"BeginSelectionDrag",new GridCell(1,1,1),copy);
            Set(window,"_hover",new GridCell(3,1,2));
            Call(window,"UpdateDragPreview");
            Assert.IsTrue((bool)Get(window,"_dropValid"));
            var preview=(List<EntityState>)Get(window,"_movePreview");
            Assert.AreEqual(new GridCell(3,1,2),preview.Single(e=>e.Id=="rock").Cell);
            Assert.AreEqual(new GridCell(1,1,1),session.Draft.entities[0].cell);
            Assert.IsFalse(session.Dirty);
            Call(window,"CommitSelectionDrag");
            Assert.AreEqual(copy?4:2,session.Draft.entities.Length);
            Assert.IsTrue(session.Draft.entities.Any(e=>e.subject=="ROCK" && e.cell==new GridCell(3,1,2)));
            var terrain=LevelCloner.ExpandTerrain(session.Draft.terrain);
            Assert.IsTrue(terrain.Contains(new GridCell(3,0,2)));
            Assert.AreEqual(copy,terrain.Contains(new GridCell(1,0,1)));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(2,session.Draft.entities.Length);
            Assert.AreEqual(new GridCell(1,1,1),session.Draft.entities[0].cell);
            Assert.IsFalse(session.CanUndo);
        }
        [Test]
        public void InvalidDropShowsPredictionButDoesNotCommit()
        {
            Set(window,"_dragging",true);
            Call(window,"BeginSelectionDrag",new GridCell(1,1,1),true);
            Set(window,"_hover",new GridCell(7,1,1));
            Call(window,"UpdateDragPreview");
            Assert.IsFalse((bool)Get(window,"_dropValid"));
            Assert.AreEqual(2,((List<EntityState>)Get(window,"_movePreview")).Count);
            Call(window,"CommitSelectionDrag");
            Assert.IsFalse(session.Dirty);
            Assert.IsFalse(session.CanUndo);
        }
        [Test]
        public void EscapeClearsBothPreviewsWithoutEditing()
        {
            Set(window,"_dragging",true);
            Call(window,"BeginSelectionDrag",new GridCell(1,1,1),true);
            Set(window,"_hover",new GridCell(3,1,1));
            Call(window,"UpdateDragPreview");
            Call(window,"CancelDrag");
            Assert.IsFalse((bool)Get(window,"_dragging"));
            Assert.IsEmpty((List<EntityState>)Get(window,"_movePreview"));
            Assert.IsEmpty((List<GridCell>)Get(window,"_terrainPreview"));
            Assert.IsFalse(session.Dirty);
        }
    }
}
