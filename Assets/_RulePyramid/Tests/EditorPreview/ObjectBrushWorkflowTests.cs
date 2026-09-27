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
    public class ObjectBrushWorkflowTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        LevelEditorWindow window;
        LevelEditSession session;
        static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Flags).Invoke(target,args);
        HashSet<GridCell> Stroke=>(HashSet<GridCell>)Get(window,"_stroke");
        [SetUp]
        public void SetUp()
        {
            window=ScriptableObject.CreateInstance<LevelEditorWindow>();
            session=new LevelEditSession(new LevelDefinition
            {
                id="brush",schemaVersion=9,mechanicsVersion="RW-v0.9",
                bounds=new GridCellBox {min=new GridCell(0,0,0),max=new GridCell(7,7,7)},
                terrain=new[]{new GridCellBox {id="floor",min=new GridCell(0,0,0),max=new GridCell(7,0,7)}},
                entities=new[]{new EntityDefinition {id="existing",kind="Object",subject="FLAG",cell=new GridCell(4,1,1)}}
            });
            Set(window,"_session",session);
            Set(window,"_brush",EditorBrush.Object);
            Set(window,"_subject","ROCK");
            Set(window,"_dragging",true);
            Call(window,"RefreshInspection");
        }
        [TearDown]
        public void TearDown() { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }

        [Test]
        public void StrokePreviewsEveryCellThenCommitsWithUniqueIdsAndOneUndo()
        {
            Stroke.UnionWith(new[]{new GridCell(1,1,1),new GridCell(2,1,1),new GridCell(3,1,1),new GridCell(2,1,1)});
            Call(window,"UpdateGhost");
            Assert.AreEqual(3,((List<EntityState>)Get(window,"_movePreview")).Count);
            Assert.IsTrue((bool)Get(window,"_dropValid"));
            Assert.IsFalse(session.Dirty);
            Call(window,"CommitGesture",(object)null);
            Assert.AreEqual(4,session.Draft.entities.Length);
            Assert.AreEqual(4,session.Draft.entities.Select(e=>e.id).Distinct().Count());
            Assert.AreEqual(3,session.Draft.entities.Count(e=>e.subject=="ROCK"));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1,session.Draft.entities.Length);
            Assert.IsFalse(session.CanUndo);
            Assert.IsTrue(session.Redo());
            Assert.AreEqual(4,session.Draft.entities.Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OccupiedOrFilteredLockedCellRejectsEntireStroke(bool terrain)
        {
            Set(window,"_categories",EditorCategory.None);
            ((HashSet<string>)Get(window,"_locked")).Add("existing");
            Stroke.Add(new GridCell(1,1,1));
            Stroke.Add(terrain?new GridCell(2,0,1):new GridCell(4,1,1));
            Call(window,"UpdateGhost");
            Assert.IsFalse((bool)Get(window,"_dropValid"));
            Call(window,"CommitGesture",(object)null);
            Assert.AreEqual(1,session.Draft.entities.Length);
            Assert.IsFalse(session.Dirty);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void EscapeDiscardsObjectStroke()
        {
            Stroke.Add(new GridCell(1,1,1));
            Call(window,"UpdateGhost");
            Call(window,"CancelDrag");
            Assert.IsEmpty(Stroke);
            Assert.IsEmpty((List<EntityState>)Get(window,"_movePreview"));
            Assert.IsFalse(session.Dirty);
        }

        [Test]
        public void TextCannotBePlacedOnObject()
        {
            Set(window,"_brush",EditorBrush.Text);
            Set(window,"_hover",new GridCell(4,1,1));
            Call(window,"CommitGesture",(object)null);
            Assert.AreEqual(1,session.Draft.entities.Length);
            Assert.IsFalse(session.CanUndo);
        }

        [Test]
        public void InvalidLoadedOverlapCannotBeSavedButCanBeDeleted()
        {
            session.Draft.entities=new[]{session.Draft.entities[0],new EntityDefinition {id="overlap",kind="Text",token="WIN",cell=new GridCell(4,1,1)}};
            Assert.IsFalse((bool)Call(window,"Save",false));
            Assert.IsFalse(session.Dirty);
            ((HashSet<string>)Get(window,"_selected")).Add("overlap");
            Call(window,"DeleteSelection");
            Assert.AreEqual(1,session.Draft.entities.Length);
            Assert.IsFalse(LevelValidator.ValidateAuthoringOccupancy(session.Draft).HasStructureErrors);
        }
    }
}
