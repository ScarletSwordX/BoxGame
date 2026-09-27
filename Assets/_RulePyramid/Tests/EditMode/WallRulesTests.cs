using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;

namespace RulePyramid.Tests.EditMode
{
    public class WallRulesTests
    {
        static WorldModel World(string wallProperty)
        {
            var entities = new List<EntityDefinition>
            {
                new EntityDefinition { id="player", kind="Object", subject="ROBOT", cell=new GridCell(0,1,0) },
                new EntityDefinition { id="wall", kind="Object", subject="WALL", cell=new GridCell(1,1,0) }
            };
            AddSentence(entities,"control",new[] {"ROBOT","IS","YOU"},-3);
            if(wallProperty!=null)AddSentence(entities,"wall_rule",new[] {"WALL","IS",wallProperty},-2);
            return WorldModel.FromLevel(new LevelDefinition
            {
                schemaVersion=9,mechanicsVersion="RW-v0.9",id="wall_fixture",
                bounds=new GridCellBox { min=new GridCell(-3,0,-3),max=new GridCell(5,5,3) },
                terrain=new[] { new GridCellBox { min=new GridCell(-3,0,-3),max=new GridCell(5,0,3) } },
                entities=entities.ToArray(), options=new OptionsData { bounceRiseCells=3 }
            });
        }
        static void AddSentence(List<EntityDefinition> entities,string id,string[] tokens,int z)
        {
            for(int i=0;i<tokens.Length;i++)entities.Add(new EntityDefinition { id=id+i,kind="Text",token=tokens[i],cell=new GridCell(-3+i,1,z) });
        }
        [Test]
        public void StopWallBlocksPushingAndPushRuleMakesItMovable()
        {
            var world=World("STOP");
            Assert.IsTrue(world.Solid(world.Entities.First(e=>e.Id=="wall")));
            Assert.IsFalse(world.TryCommand("PE",out _));
            Assert.AreEqual(new GridCell(1,1,0),world.Entities.First(e=>e.Id=="wall").Cell);
            world.Entities.First(e=>e.Id=="wall_rule2").Token="PUSH";
            world.Refresh();
            Assert.IsTrue(world.TryCommand("PE",out _));
            Assert.AreEqual(new GridCell(2,1,0),world.Entities.First(e=>e.Id=="wall").Cell);
        }
        [Test]
        public void WallWithoutRuleHasNoHiddenCollision()
        {
            var world=World(null);
            Assert.IsFalse(world.Solid(world.Entities.First(e=>e.Id=="wall")));
            Assert.IsTrue(world.TryCommand("E",out _));
            Assert.AreEqual(new GridCell(1,1,0),world.Actor().Cell);
        }
        [Test]
        public void WallIsLegalAsObjectAndNounWord()
        {
            Assert.IsTrue(Tokens.IsSubject("WALL"));
            Assert.IsTrue(Tokens.IsLegalWord("WALL"));
            var world=World("STOP");
            Assert.That(world.PropertySources.Any(r=>r.Subject=="WALL" && r.Property=="STOP"));
        }
    }
}
